using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// P2 (faza 2B, docs/p2) — članstva klijenata i njihov lifecycle. Svaka naredba je jedna transakcija pod zaključanim retkom
/// članstva; provjera preklapanja (Q10/Q46) i kapaciteta plana serijalizira se lockom klijenta odnosno plana.
/// Lock redoslijed: klijent → članstvo → plan (prodaja: klijent → plan; objava plana: plan → članstva).
/// "Danas" je lokalni datum zone organizacije (Q22). Zaduženja (2C) i claimovi pokrića (2D) se ovdje još ne diraju; njihovi
/// uvjeti (npr. poništavanje samo bez aktivnih alokacija i claimova, pauza ne uz dug nakon grace) dolaze s tim fazama.
/// </summary>
public class ClientMembershipService : IClientMembershipService
{
    private readonly IClientMembershipHandler _handler;
    private readonly IMembershipPlanHandler _planHandler;
    private readonly IClientHandler _clientHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly IMembershipRenewalService _renewalService;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IMembershipCoverageService _coverage;
    private readonly IGroupMembershipSkipService _membershipSkips;
    private readonly ICommissionLedgerService _commissionLedger;

    public ClientMembershipService(
        IClientMembershipHandler handler,
        IMembershipPlanHandler planHandler,
        IClientHandler clientHandler,
        ICompanyHandler companyHandler,
        IEmployeeHandler employeeHandler,
        IOrganizationCalendarService organizationCalendarService,
        IMembershipRenewalService renewalService,
        IUnitOfWorkFactory unitOfWorkFactory,
        IMembershipCoverageService coverage,
        IGroupMembershipSkipService membershipSkips,
        ICommissionLedgerService commissionLedger)
    {
        _membershipSkips = membershipSkips;
        _commissionLedger = commissionLedger;
        _handler = handler;
        _planHandler = planHandler;
        _clientHandler = clientHandler;
        _companyHandler = companyHandler;
        _employeeHandler = employeeHandler;
        _organizationCalendarService = organizationCalendarService;
        _renewalService = renewalService;
        _unitOfWorkFactory = unitOfWorkFactory;
        _coverage = coverage;
    }

    #region Čitanje

    public async Task<List<ClientMembershipDto>> GetByClient(Guid organizationId, Guid clientId)
    {
        if (await _clientHandler.GetByIdLight(organizationId, clientId) == null)
            throw new NotFoundAppException("Client", clientId);
        DateOnly today = await Today(organizationId);
        int graceDays = (await _renewalService.GetDebtRules(organizationId)).GraceDays;
        List<ClientMembership> memberships = await _handler.GetByClient(organizationId, clientId);
        return memberships.Select(m => ToDto(m, today, graceDays)).ToList();
    }

    public async Task<ClientMembershipDto> GetById(Guid organizationId, Guid id)
    {
        ClientMembership membership = await _handler.GetById(organizationId, id)
            ?? throw new NotFoundAppException("ClientMembership", id);
        return ToDto(membership, await Today(organizationId), (await _renewalService.GetDebtRules(organizationId)).GraceDays);
    }

    public async Task<List<ClientMembershipDto>> GetPlanUpdateNotApplied(Guid organizationId, Guid? membershipPlanId)
    {
        DateOnly today = await Today(organizationId);
        int graceDays = (await _renewalService.GetDebtRules(organizationId)).GraceDays;
        List<ClientMembership> memberships = await _handler.GetPlanUpdateNotApplied(organizationId, membershipPlanId, today);
        return memberships.Select(m => ToDto(m, today, graceDays)).ToList();
    }

    public async Task<List<ClientMembershipDto>> GetEndingDueToPlanDeactivation(Guid organizationId, Guid membershipPlanId)
    {
        MembershipPlan plan = await _planHandler.GetById(organizationId, membershipPlanId)
            ?? throw new NotFoundAppException("MembershipPlan", membershipPlanId);
        if (plan.IsActive)
            return new List<ClientMembershipDto>();

        DateOnly today = await Today(organizationId);
        int graceDays = (await _renewalService.GetDebtRules(organizationId)).GraceDays;
        List<ClientMembership> memberships = await _handler.GetActiveForPlan(organizationId, membershipPlanId, today);
        return memberships.Where(WillEndDueToDeactivation).Select(m => ToDto(m, today, graceDays)).ToList();
    }

    /// <summary>2C — članstvo neaktivnog plana završava na sljedećoj obnovi, osim ako tada prelazi na aktivan plan ili već
    /// ima zakazan završetak.</summary>
    public static bool WillEndDueToDeactivation(ClientMembership membership) =>
        membership.EndsOn == null
        && !(membership.PendingSource == MembershipPendingChangeSource.ClientPlanChange && membership.PendingPlanVersion?.Plan?.IsActive == true);

    public async Task<List<MembershipPeriodDto>> GetPeriods(Guid organizationId, Guid id)
    {
        ClientMembership membership = await _handler.GetById(organizationId, id)
            ?? throw new NotFoundAppException("ClientMembership", id);
        return membership.Periods
            .OrderByDescending(p => p.StartsOn)
            .Select((p, i) => new MembershipPeriodDto { Sequence = membership.Periods.Count - 1 - i, StartsOn = p.StartsOn, EndsOn = p.EndsOn })
            .ToList();
    }

    public async Task<List<MembershipChargeDto>> GetCharges(Guid organizationId, Guid id)
    {
        ClientMembership membership = await _handler.GetById(organizationId, id)
            ?? throw new NotFoundAppException("ClientMembership", id);
        return membership.Charges.OrderByDescending(c => c.DueOn).ThenBy(c => c.Kind).Select(ToDto).ToList();
    }

    public async Task<MembershipChargeDto> WriteOffCharge(Guid organizationId, Guid userId, Guid chargeId, MembershipChargeWriteOffRequest request)
    {
        string reason = string.IsNullOrWhiteSpace(request?.Reason) ? throw new ValidationAppException("Razlog otpisa je obavezan.") : request.Reason.Trim();
        MembershipCharge charge;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            charge = await _handler.GetChargeForUpdate(uow, organizationId, chargeId)
                ?? throw new NotFoundAppException("MembershipCharge", chargeId);
            if (charge.Lifecycle != MembershipChargeLifecycle.Open || MembershipChargeSettlement.IsFinal(charge))
                throw new BusinessRuleException(ErrorCodes.MembershipChargeNotOpen, "Zaduženje nije otvoreno ili je već plaćeno.");
            if (charge.CheckoutItems.Any(i => i.LocksMembershipCharge))
                throw new BusinessRuleException(ErrorCodes.MembershipChargeInOpenCheckout,
                    "Zaduženje je stavka otvorenog checkouta; uklonite stavku prije otpisa.");

            // Q16/Q42 — otpisuje se preostali dug; plaćeni dio ostaje plaćen. Otpisano zaduženje je konačno.
            decimal writtenOff = MembershipChargeSettlement.Outstanding(charge);
            charge.Lifecycle = MembershipChargeLifecycle.WrittenOff;
            charge.WrittenOffAt = DateTimeOffset.UtcNow;
            charge.WrittenOffBy = userId;
            charge.WriteOffReason = reason;
            _handler.AddAudit(uow, new ClientMembershipAuditLog
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ClientMembershipId = charge.ClientMembershipId,
                ChangeType = "ChargeWrittenOff",
                OldValue = charge.Id.ToString(),
                NewValue = writtenOff.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Reason = reason,
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
            await uow.Context.SaveChangesAsync();
            // 2F (Q42): otpis može učiniti zaduženja prve prodaje konačnima — provizija na prvu prodaju (otpisano = 0 u osnovici).
            await _commissionLedger.EvaluateMembershipFirstSale(uow, organizationId, userId, charge.ClientMembershipId);
            // 2D (Q15.2): otpis može okončati dug — pokriće se ponovno primjenjuje na buduće nepokrivene termine.
            await _coverage.ReconcileMembership(uow, organizationId, charge.ClientMembershipId, MembershipCoverageEvent.DebtChanged, userId);
            await uow.CommitAsync();
        }

        // P2 (Q53): otpis može okončati dug — preskočeni budući termini grupe se popunjavaju.
        await _membershipSkips.BackfillForClient(organizationId, charge.ClientId, userId);

        return (await GetCharges(organizationId, charge.ClientMembershipId)).Single(c => c.Id == chargeId);
    }

    private static MembershipChargeDto ToDto(MembershipCharge charge) => new()
    {
        Id = charge.Id,
        ClientMembershipId = charge.ClientMembershipId,
        ClientId = charge.ClientId,
        Kind = charge.Kind,
        Description = charge.Description,
        Amount = charge.Amount,
        DueOn = charge.DueOn,
        Period = charge.Period == null ? null : new MembershipPeriodDto { StartsOn = charge.Period.StartsOn, EndsOn = charge.Period.EndsOn },
        Status = MembershipChargeSettlement.StatusOf(charge),
        SettledAmount = MembershipChargeSettlement.Settled(charge),
        OutstandingAmount = MembershipChargeSettlement.Outstanding(charge),
        InOpenCheckout = charge.CheckoutItems.Any(i => i.LocksMembershipCharge),
        WrittenOffAt = charge.WrittenOffAt,
        WrittenOffBy = charge.WrittenOffBy,
        WriteOffReason = charge.WriteOffReason,
        VoidedAt = charge.VoidedAt,
        CreatedAt = charge.CreatedAt
    };

    public async Task<MembershipCancellationPreviewDto> PreviewCancellation(Guid organizationId, Guid id)
    {
        ClientMembership membership = await _handler.GetById(organizationId, id)
            ?? throw new NotFoundAppException("ClientMembership", id);
        (DateOnly effectiveOn, MembershipEndEffectiveReason reason) = CancellationEffective(membership, await Today(organizationId));
        return new MembershipCancellationPreviewDto { EffectiveOn = effectiveOn, Reason = reason };
    }

    #endregion

    #region Prodaja

    public async Task<ClientMembershipDto> Sell(Guid organizationId, Guid userId, Guid clientId, ClientMembershipSellRequest request)
    {
        Guid planId = request?.MembershipPlanId ?? throw new ValidationAppException("MembershipPlanId je obavezan.");
        Guid soldCompanyId = request.SoldCompanyId ?? throw new ValidationAppException("SoldCompanyId je obavezan.");

        Client client = await _clientHandler.GetByIdLight(organizationId, clientId) ?? throw new NotFoundAppException("Client", clientId);
        if (!client.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveClient, "Klijent nije aktivan.");
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran.");

        Company soldCompany = await _companyHandler.GetById(organizationId, soldCompanyId) ?? throw new NotFoundAppException("Company", soldCompanyId);
        if (!soldCompany.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Poslovnica '{soldCompany.Name}' nije aktivna.");

        DateOnly today = await Today(organizationId);
        DateOnly startsOn = request.StartsOn ?? today;
        MembershipLifecycleRules.EnsureStartDate(startsOn, today);
        Guid? proposedCommissionEmployeeId = await ResolveProposedCommissionEmployee(organizationId, userId, request.ProposedSaleCommissionEmployeeId);

        List<WarningDto> warnings = new();
        Guid membershipId = Guid.NewGuid();
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _handler.LockClient(uow, organizationId, clientId);
            MembershipPlan plan = await _planHandler.GetForUpdate(uow, organizationId, planId)
                ?? throw new NotFoundAppException("MembershipPlan", planId);
            if (!plan.IsActive)
                throw new BusinessRuleException(ErrorCodes.MembershipPlanInactive, $"Plan '{plan.Name}' nije aktivan i ne može se prodati.");

            MembershipPlanVersion version = await _planHandler.GetLatestVersion(uow, planId);
            await EnsureSellableCompanies(organizationId, version, soldCompanyId, warnings);

            await EnsurePlanCapacity(uow, organizationId, plan, startsOn, today, excludeMembershipId: null);

            EnsureNoOverlap(
                MembershipTimelines.Scope(membershipId, plan.Name, startsOn, null, version),
                await _handler.GetCoverageCandidates(uow, organizationId, clientId));

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ClientMembership membership = new()
            {
                Id = membershipId,
                OrganizationId = organizationId,
                ClientId = clientId,
                MembershipPlanId = planId,
                PlanVersionId = version.Id,
                StartsOn = startsOn,
                TermsAnchorOn = startsOn,
                CommitmentFromOn = startsOn,
                AnchorDay = startsOn.Day,
                SoldCompanyId = soldCompanyId,
                SoldVia = MembershipSaleChannel.Staff,
                SoldBy = userId,
                SaleCommissionEmployeeId = proposedCommissionEmployeeId,
                CreatedAt = now,
                CreatedBy = userId
            };
            uow.Context.ClientMemberships.Add(membership);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "MembershipSold", null,
                $"plan={planId};version={version.Version};startsOn={startsOn:yyyy-MM-dd};soldCompany={soldCompanyId}"));
            await uow.Context.SaveChangesAsync();

            // 2C (Q20/Q45.1) — prvi period i njegovo zaduženje nastaju pri prodaji (i kad članstvo počinje kasnije), dospijeće je
            // datum početka; početna naknada je zasebno zaduženje.
            ClientMembership tracked = await _handler.GetForUpdate(uow, organizationId, membershipId);
            await _renewalService.CatchUp(uow, tracked, today, await _renewalService.GetDebtRules(organizationId), userId);
            if (version.StartFee > 0m)
                AddStartFeeCharge(uow, tracked, version.StartFee, userId);
            await uow.Context.SaveChangesAsync();
            // 2D: postojeće buduće rezervacije klijenta dobivaju pokriće redom po vremenu do limita (već plaćene ne — AlreadyPaid).
            await _coverage.ReconcileMembership(uow, organizationId, membershipId, MembershipCoverageEvent.MembershipSale, userId);
            await uow.CommitAsync();
        }

        return await WithWarnings(organizationId, membershipId, warnings);
    }

    private static void AddStartFeeCharge(IUnitOfWork uow, ClientMembership membership, decimal amount, Guid userId)
    {
        MembershipCharge charge = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = membership.OrganizationId,
            ClientId = membership.ClientId,
            ClientMembershipId = membership.Id,
            Kind = MembershipChargeKind.StartFee,
            Description = $"Početna naknada {membership.Plan?.Name}",
            Amount = amount,
            DueOn = membership.StartsOn,
            Lifecycle = MembershipChargeLifecycle.Open,
            SettlementStatus = MembershipChargeSettlementStatus.Unpaid,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };
        membership.Charges.Add(charge);
        uow.Context.MembershipCharges.Add(charge);
    }

    /// <summary>Q29.3 — plan s odabranim poslovnicama koje su sve neaktivne se ne prodaje; prodaja na poslovnici u kojoj plan ne
    /// vrijedi je dopuštena uz upozorenje (2B, P2 dnevnik).</summary>
    private async Task EnsureSellableCompanies(Guid organizationId, MembershipPlanVersion version, Guid soldCompanyId, List<WarningDto> warnings)
    {
        if (version.CompanyScope != MembershipCompanyScope.SelectedCompanies)
            return;

        List<Guid> validIds = version.Companies.Select(c => c.CompanyId).ToList();
        List<Company> companies = await _companyHandler.GetByIds(organizationId, validIds);
        if (!companies.Any(c => c.IsActive))
            throw new BusinessRuleException(ErrorCodes.MembershipPlanNoActiveCompany,
                "Nijedna poslovnica u kojoj plan vrijedi nije aktivna; plan se ne može prodati.");
        if (!validIds.Contains(soldCompanyId))
            warnings.Add(new WarningDto(WarningCodes.MembershipPlanNotValidAtSaleCompany, new WarningMembershipPlanCompaniesDetails
            {
                CompanyIds = validIds
            }));
    }

    private async Task<Guid?> ResolveProposedCommissionEmployee(Guid organizationId, Guid userId, Guid? requested)
    {
        // 2F: odabrani korisnik provizije mora biti aktivan zaposlenik; default (prodavač) samo ako je aktivan.
        if (requested.HasValue)
        {
            await _commissionLedger.EnsureSelectableEmployee(organizationId, requested.Value);
            return requested.Value;
        }

        Employee seller = await _employeeHandler.GetByUserId(organizationId, userId);
        return seller is { IsActive: true } ? seller.Id : null;
    }

    #endregion

    #region Otkaz

    public async Task<ClientMembershipDto> RequestCancellation(Guid organizationId, Guid userId, Guid id, ClientMembershipCancelRequest request)
    {
        DateOnly today = await Today(organizationId);
        List<WarningDto> warnings = new();
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            ClientMembership membership = await LockActive(uow, organizationId, id, today);
            if (membership.EndsOn.HasValue)
                throw new BusinessRuleException(ErrorCodes.MembershipEndAlreadyScheduled, $"Članstvo već završava {membership.EndsOn:dd.MM.yyyy.}.");

            (DateOnly effectiveOn, MembershipEndEffectiveReason reason) = CancellationEffective(membership, today);
            membership.CancellationRequestedAt = DateTimeOffset.UtcNow;
            membership.CancellationRequestedBy = userId;
            membership.CancellationReason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
            membership.EndsOn = effectiveOn;
            membership.EndReason = MembershipEndReason.Cancelled;
            Touch(membership, userId);

            // 2B (P2 dnevnik) — zahtjev za otkaz poništava zakazane pauze koje još nisu počele.
            CancelFuturePauses(uow, membership, userId, today, after: today, MembershipPauseCancellationReason.MembershipCancellation, warnings);
            DropPendingAfterEnd(uow, membership, userId);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "CancellationRequested", null,
                $"{effectiveOn:yyyy-MM-dd};{reason}", membership.CancellationReason));
            await uow.Context.SaveChangesAsync();
            // 2D (Q27): termini nakon datuma kraja gube pokriće (AfterMembershipEnd), bez automatskog otkazivanja.
            await _coverage.ReconcileMembership(uow, organizationId, membership.Id, MembershipCoverageEvent.MembershipChanged, userId);
            await uow.CommitAsync();
        }

        return await WithWarnings(organizationId, id, warnings);
    }

    public async Task<ClientMembershipDto> WithdrawCancellation(Guid organizationId, Guid userId, Guid id)
    {
        DateOnly today = await Today(organizationId);
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            ClientMembership membership = await LockActive(uow, organizationId, id, today);
            if (membership.EndReason != MembershipEndReason.Cancelled)
                throw new BusinessRuleException(ErrorCodes.MembershipNoScheduledCancellation, "Članstvo nema zakazan otkaz koji bi se mogao povući.");

            string old = $"{membership.EndsOn:yyyy-MM-dd}";
            membership.EndsOn = null;
            membership.EndReason = null;
            membership.CancellationRequestedAt = null;
            membership.CancellationRequestedBy = null;
            membership.CancellationReason = null;
            Touch(membership, userId);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "CancellationWithdrawn", old, null));
            await uow.Context.SaveChangesAsync();
            await _coverage.ReconcileMembership(uow, organizationId, membership.Id, MembershipCoverageEvent.MembershipChanged, userId);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    #endregion

    #region Pauza

    public async Task<ClientMembershipDto> Pause(Guid organizationId, Guid userId, Guid id, ClientMembershipPauseRequest request)
    {
        DateOnly startsOn = request?.StartsOn ?? throw new ValidationAppException("StartsOn je obavezan.");
        DateOnly today = await Today(organizationId);
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            ClientMembership membership = await LockActive(uow, organizationId, id, today);
            MembershipPlanVersion terms = membership.PlanVersion;
            MembershipTimeline timeline = MembershipTimelines.Current(membership);
            MembershipPauseSpan candidate = Candidate(terms, timeline, startsOn, request);

            MembershipLifecycleRules.EnsurePauseAllowed(timeline,
                new MembershipPauseRules(terms.PauseAllowed, terms.MaxPauseDays, terms.MaxPausePeriods, terms.MaxPausesPer12Months),
                candidate, today, endScheduled: membership.EndsOn.HasValue);

            // Q5.3 — pauza nije dopuštena dok članstvo ima dug nakon isteka grace perioda (iz alokacija, ne iz projekcije).
            MembershipDebtRules rules = await _renewalService.GetDebtRules(organizationId);
            if (MembershipChargeSettlement.Standing(membership.Charges, today, rules.GraceDays) == MembershipStanding.Delinquent)
                throw new BusinessRuleException(ErrorCodes.MembershipDelinquent, "Članstvo ima dug nakon isteka grace perioda; pauza nije dopuštena.");

            DateTimeOffset now = DateTimeOffset.UtcNow;
            MembershipPause pause = new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ClientMembershipId = membership.Id,
                Kind = candidate.Kind,
                StartsOn = candidate.StartsOn,
                PlannedEndsOn = candidate.EndsOn,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                CreatedAt = now,
                CreatedBy = userId
            };
            membership.Pauses.Add(pause);
            uow.Context.MembershipPauses.Add(pause);
            Touch(membership, userId);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PauseScheduled", null,
                $"{pause.Id};{pause.Kind};{pause.StartsOn:yyyy-MM-dd}..{pause.PlannedEndsOn:yyyy-MM-dd}", pause.Reason));
            await _renewalService.CatchUp(uow, membership, today, rules, userId); // pauza produljuje tekući period
            await uow.Context.SaveChangesAsync();
            // 2D (Q5.5): termini u pauzi gube pokriće (storno claima), bez automatskog otkazivanja; granice perioda se pomiču.
            await _coverage.ReconcileMembership(uow, organizationId, membership.Id, MembershipCoverageEvent.MembershipChanged, userId);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    /// <summary>Days: zadani kraj; SkipPeriods: N cijelih perioda od prvog dana perioda (Q5.1).</summary>
    private static MembershipPauseSpan Candidate(MembershipPlanVersion terms, MembershipTimeline timeline, DateOnly startsOn, ClientMembershipPauseRequest request)
    {
        if (terms.RenewalAnchor == MembershipRenewalAnchor.PurchaseDate)
        {
            if (request.Periods.HasValue)
                throw new ValidationAppException("Plan od datuma kupnje pauzira po danima (EndsOn), ne po periodima.");
            DateOnly endsOn = request.EndsOn ?? throw new ValidationAppException("EndsOn je obavezan za pauzu po danima.");
            return new MembershipPauseSpan(MembershipPauseKind.Days, startsOn, endsOn);
        }

        if (request.EndsOn.HasValue)
            throw new ValidationAppException("Kalendarski plan pauzira u cijelim periodima (Periods), ne do datuma.");
        int periods = request.Periods ?? throw new ValidationAppException("Periods je obavezan za kalendarsku pauzu.");
        if (periods < 1)
            throw new ValidationAppException("Pauza traje barem jedan period.");

        List<MembershipPeriod> covered = MembershipPeriodCalendar.Periods(timeline)
            .SkipWhile(p => p.StartsOn < startsOn)
            .Where(p => !p.Skipped)
            .Take(periods)
            .ToList();
        DateOnly end = covered.Count == periods ? covered[^1].EndsOn : startsOn;
        return new MembershipPauseSpan(MembershipPauseKind.SkipPeriods, startsOn, end);
    }

    public async Task<ClientMembershipPauseEndEarlyResultDto> EndPauseEarly(
        Guid organizationId, Guid userId, Guid id, Guid pauseId, ClientMembershipPauseEndEarlyRequest request)
    {
        DateOnly today = await Today(organizationId);
        ClientMembershipPauseEndEarlyResultDto result = new();
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            ClientMembership membership = await LockActive(uow, organizationId, id, today);
            MembershipPause pause = membership.Pauses.SingleOrDefault(p => p.Id == pauseId)
                ?? throw new NotFoundAppException("MembershipPause", pauseId);
            if (pause.CancelledAt != null || pause.StartsOn > today || pause.EffectiveEndsOn < today)
                throw new BusinessRuleException(ErrorCodes.MembershipPauseNotPending, "Raniji povratak je moguć samo iz pauze koja je u tijeku.");

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (pause.StartsOn == today)
            {
                // Povratak na prvi dan pauze: pauza se ne koristi — poništava se bez trošenja limita.
                CancelPause(pause, userId, now, MembershipPauseCancellationReason.Withdrawn);
                _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PauseWithdrawn", pause.Id.ToString(), null));
                result.Applied = true;
            }
            else if (pause.Kind == MembershipPauseKind.SkipPeriods)
            {
                // Q47 — otvara se period od dana povratka uz puni iznos; bez potvrde samo pregled.
                MembershipTimeline after = MembershipTimelines.Current(membership) with
                {
                    Pauses = MembershipTimelines.PauseSpans(membership)
                        .Select(s => s.StartsOn == pause.StartsOn ? s with { EndsOn = today.AddDays(-1) } : s).ToList()
                };
                MembershipPeriod opens = MembershipPeriodCalendar.PeriodContaining(after, today);
                result.OpensPeriod = new MembershipPeriodDto { Sequence = opens.Sequence, StartsOn = opens.StartsOn, EndsOn = opens.EndsOn };
                result.OpensPeriodAmount = membership.PlanVersion.Price;
                if (request?.Confirm == true)
                {
                    pause.ActualEndsOn = today.AddDays(-1);
                    pause.UpdatedAt = now;
                    pause.UpdatedBy = userId;
                    _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PauseEndedEarly", $"{pause.PlannedEndsOn:yyyy-MM-dd}",
                        $"{pause.ActualEndsOn:yyyy-MM-dd};opens={opens.StartsOn:yyyy-MM-dd}..{opens.EndsOn:yyyy-MM-dd}"));
                    result.Applied = true;
                }
            }
            else
            {
                // Q5.6 — iskorišteni dani ostaju u limitu, produljenje se skraćuje na stvarno trajanje. Zakazani otkaz prati isti
                // period (kraj perioda se pomiče s produljenjem).
                int? cancelledPeriod = membership.EndReason == MembershipEndReason.Cancelled ? SequenceEndingOn(membership) : null;
                pause.ActualEndsOn = today.AddDays(-1);
                pause.UpdatedAt = now;
                pause.UpdatedBy = userId;
                if (cancelledPeriod.HasValue)
                    membership.EndsOn = MembershipPeriodCalendar.Periods(MembershipTimelines.Current(membership)).First(p => p.Sequence == cancelledPeriod.Value).EndsOn;
                _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PauseEndedEarly", $"{pause.PlannedEndsOn:yyyy-MM-dd}",
                    $"{pause.ActualEndsOn:yyyy-MM-dd}"));
                result.Applied = true;
            }

            if (result.Applied)
            {
                Touch(membership, userId);
                // Days: tekući period se skraćuje; kalendarski (Q47): otvara se period od dana povratka s punim zaduženjem.
                await _renewalService.CatchUp(uow, membership, today, await _renewalService.GetDebtRules(organizationId), userId);
                await uow.Context.SaveChangesAsync();
                // 2D (Q5.6): termini od povratka ponovno dobivaju pokriće (redom, do limita).
                await _coverage.ReconcileMembership(uow, organizationId, membership.Id, MembershipCoverageEvent.MembershipChanged, userId);
                await uow.CommitAsync();
            }
        }

        result.Membership = await GetById(organizationId, id);
        return result;
    }

    public async Task<ClientMembershipDto> CancelPause(Guid organizationId, Guid userId, Guid id, Guid pauseId)
    {
        DateOnly today = await Today(organizationId);
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            ClientMembership membership = await LockActive(uow, organizationId, id, today);
            MembershipPause pause = membership.Pauses.SingleOrDefault(p => p.Id == pauseId)
                ?? throw new NotFoundAppException("MembershipPause", pauseId);
            if (pause.CancelledAt != null || pause.StartsOn <= today)
                throw new BusinessRuleException(ErrorCodes.MembershipPauseNotPending, "Otkazati se može samo pauza koja još nije počela.");

            CancelPause(pause, userId, DateTimeOffset.UtcNow, MembershipPauseCancellationReason.Withdrawn);
            Touch(membership, userId);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PauseWithdrawn", pause.Id.ToString(), null));
            await _renewalService.CatchUp(uow, membership, today, await _renewalService.GetDebtRules(organizationId), userId);
            await uow.Context.SaveChangesAsync();
            await _coverage.ReconcileMembership(uow, organizationId, membership.Id, MembershipCoverageEvent.MembershipChanged, userId);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    #endregion

    #region Promjena plana

    public async Task<ClientMembershipDto> ChangePlan(Guid organizationId, Guid userId, Guid id, ClientMembershipPlanChangeRequest request)
    {
        Guid planId = request?.MembershipPlanId ?? throw new ValidationAppException("MembershipPlanId je obavezan.");
        DateOnly today = await Today(organizationId);
        ClientMembership snapshot = await _handler.GetById(organizationId, id) ?? throw new NotFoundAppException("ClientMembership", id);

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _handler.LockClient(uow, organizationId, snapshot.ClientId);
            ClientMembership membership = await LockActive(uow, organizationId, id, today);
            if (planId == membership.MembershipPlanId)
                throw new BusinessRuleException(ErrorCodes.MembershipPlanChangeNotAllowed, "Članstvo je već na tom planu.");

            // Lock ciljnog plana (redoslijed klijent → članstvo → plan) serijalizira provjeru kapaciteta s prodajom (pregled 2B, #8).
            MembershipPlan plan = await _planHandler.GetForUpdate(uow, organizationId, planId) ?? throw new NotFoundAppException("MembershipPlan", planId);
            if (!plan.IsActive)
                throw new BusinessRuleException(ErrorCodes.MembershipPlanInactive, $"Plan '{plan.Name}' nije aktivan.");
            MembershipPlanVersion version = await _planHandler.GetLatestVersion(uow, planId);

            // Pravilo 6 — od sljedećeg ciklusa: prva obnova nakon danas (ili nakon početka za članstvo koje još nije počelo).
            DateOnly after = (today > membership.StartsOn ? today : membership.StartsOn).AddDays(1);
            DateOnly effectiveOn = MembershipPeriodCalendar.FirstPeriodStartingOnOrAfter(MembershipTimelines.Current(membership), after).StartsOn;
            if (membership.EndsOn.HasValue && effectiveOn > membership.EndsOn.Value)
                throw new BusinessRuleException(ErrorCodes.MembershipPlanChangeNotAllowed,
                    $"Članstvo završava {membership.EndsOn:dd.MM.yyyy.}, prije nego bi promjena plana stupila na snagu.");

            await EnsurePlanCapacity(uow, organizationId, plan, effectiveOn, today, excludeMembershipId: membership.Id);

            // Q46 — vremenska provjera preklapanja s drugim članarinama klijenta od stupanja promjene na snagu.
            EnsureNoOverlap(
                MembershipTimelines.Scope(membership.Id, plan.Name, effectiveOn, membership.EndsOn, version),
                await _handler.GetCoverageCandidates(uow, organizationId, membership.ClientId));

            string old = membership.PendingPlanVersionId == null ? null : $"{membership.PendingSource}:{membership.PendingPlanVersionId}";

            // Pregled 2B — istisnuta izmjena plana se pamti s IZVORNIM datumom (donja granica); povlačenje promjene je vraća.
            if (membership.PendingSource == MembershipPendingChangeSource.PlanUpdate)
            {
                membership.DisplacedPlanVersionId = membership.PendingPlanVersionId;
                membership.DisplacedEffectiveOn = membership.PendingEffectiveOn;
            }

            membership.PendingPlanVersionId = version.Id;
            membership.PendingEffectiveOn = effectiveOn;
            membership.PendingSource = MembershipPendingChangeSource.ClientPlanChange;
            Touch(membership, userId);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PlanChangeScheduled", old,
                $"plan={planId};version={version.Version};effectiveOn={effectiveOn:yyyy-MM-dd}"));
            await uow.Context.SaveChangesAsync();
            // 2D: od datuma promjene vrijede novi uvjeti (usluge, poslovnice, limiti, granice perioda).
            await _coverage.ReconcileMembership(uow, organizationId, membership.Id, MembershipCoverageEvent.MembershipChanged, userId);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    public async Task<ClientMembershipDto> WithdrawPlanChange(Guid organizationId, Guid userId, Guid id)
    {
        DateOnly today = await Today(organizationId);
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            ClientMembership membership = await LockActive(uow, organizationId, id, today);
            if (membership.PendingSource != MembershipPendingChangeSource.ClientPlanChange)
                throw new BusinessRuleException(ErrorCodes.MembershipNoScheduledPlanChange, "Članstvo nema zakazanu promjenu plana.");

            string old = $"{membership.PendingPlanVersionId}@{membership.PendingEffectiveOn:yyyy-MM-dd}";
            ClearPending(membership);
            Touch(membership, userId);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PlanChangeWithdrawn", old, null));
            await RestoreDisplacedPlanUpdate(uow, organizationId, userId, membership, today);
            await uow.Context.SaveChangesAsync();
            await _coverage.ReconcileMembership(uow, organizationId, membership.Id, MembershipCoverageEvent.MembershipChanged, userId);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    /// <summary>Pregled 2B — nakon povlačenja klijentove promjene članstvo dobiva stanje kao da promjene nije bilo: istisnuta
    /// izmjena plana se vraća s IZVORNIM datumom (rok najave od objave verzije); ako je ta obnova u međuvremenu prošla, stupa na
    /// snagu pri prvoj sljedećoj obnovi. Ako bi vraćena izmjena stvorila preklapanje (Q10), članstvo dobiva trajnu oznaku.
    /// Izmjena koja bi stupila tek nakon zakazanog završetka se ne vraća.</summary>
    private async Task RestoreDisplacedPlanUpdate(IUnitOfWork uow, Guid organizationId, Guid userId, ClientMembership membership, DateOnly today)
    {
        if (membership.DisplacedPlanVersionId == null)
            return;

        MembershipPlanVersion displaced = membership.DisplacedPlanVersion;
        DateOnly notBefore = membership.DisplacedEffectiveOn.Value;
        if (MembershipPeriodCalendar.FirstPeriodStartingOnOrAfter(MembershipTimelines.Current(membership), notBefore).StartsOn <= today)
            notBefore = today.AddDays(1);
        DateOnly effectiveOn = MembershipPeriodCalendar.FirstPeriodStartingOnOrAfter(MembershipTimelines.Current(membership), notBefore).StartsOn;

        membership.DisplacedPlanVersionId = null;
        membership.DisplacedPlanVersion = null;
        membership.DisplacedEffectiveOn = null;
        if (membership.EndsOn.HasValue && effectiveOn > membership.EndsOn.Value)
            return;

        MembershipCoverageConflict conflict = MembershipCoverageOverlap.FindConflict(
            MembershipTimelines.Scope(membership.Id, membership.Plan?.Name, effectiveOn, membership.EndsOn, displaced),
            (await _handler.GetCoverageCandidates(uow, organizationId, membership.ClientId)).SelectMany(MembershipTimelines.Scopes));
        if (conflict != null)
        {
            MembershipTimelines.MarkPlanUpdateNotApplied(membership, displaced.Id, ErrorCodes.MembershipOverlappingCoverage, DateTimeOffset.UtcNow);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PlanUpdateNotApplied", null, displaced.Id.ToString(),
                ErrorCodes.MembershipOverlappingCoverage));
            return;
        }

        membership.PendingPlanVersionId = displaced.Id;
        membership.PendingEffectiveOn = effectiveOn;
        membership.PendingSource = MembershipPendingChangeSource.PlanUpdate;
        _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PlanUpdateRestored", null, $"{displaced.Id}@{effectiveOn:yyyy-MM-dd}"));
    }

    #endregion

    #region Raniji izlazak i poništavanje

    public async Task<ClientMembershipDto> EndEarly(Guid organizationId, Guid userId, Guid id, ClientMembershipEndRequest request)
    {
        DateOnly endsOn = request?.EndsOn ?? throw new ValidationAppException("EndsOn je obavezan.");
        string reason = string.IsNullOrWhiteSpace(request.Reason) ? throw new ValidationAppException("Razlog je obavezan.") : request.Reason.Trim();
        DateOnly today = await Today(organizationId);
        List<WarningDto> warnings = new();
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            ClientMembership membership = await LockActive(uow, organizationId, id, today);

            // Samo ranije od izračunatog datuma otkaza (ili već zakazanog završetka), nikad u prošlosti ni prije početka.
            DateOnly latest = membership.EndsOn ?? CancellationEffective(membership, today).EffectiveOn;
            DateOnly earliest = today > membership.StartsOn ? today : membership.StartsOn;
            if (endsOn < earliest || endsOn > latest)
                throw new ValidationAppException(ErrorCodes.MembershipEndDateOutOfRange,
                    $"Datum izlaska mora biti od {earliest:dd.MM.yyyy.} do {latest:dd.MM.yyyy.}.");

            string old = membership.EndsOn == null ? null : $"{membership.EndsOn:yyyy-MM-dd};{membership.EndReason}";
            membership.EndsOn = endsOn;
            membership.EndReason = MembershipEndReason.EndOverride;
            Touch(membership, userId);

            // Zakazane pauze nakon izlaska se poništavaju; pauza u tijeku završava s članstvom.
            CancelFuturePauses(uow, membership, userId, today, after: endsOn, MembershipPauseCancellationReason.MembershipEnded, warnings);
            foreach (MembershipPause running in membership.Pauses.Where(p => p.CancelledAt == null && p.StartsOn <= endsOn && p.EffectiveEndsOn > endsOn))
            {
                running.ActualEndsOn = endsOn;
                running.UpdatedAt = DateTimeOffset.UtcNow;
                running.UpdatedBy = userId;
            }

            DropPendingAfterEnd(uow, membership, userId);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "EndOverridden", old, $"{endsOn:yyyy-MM-dd}", reason));
            await uow.Context.SaveChangesAsync();
            // 2B/2D: claimovi nakon izlaska se oslobađaju (AfterMembershipEnd), bez automatskog otkazivanja termina.
            await _coverage.ReconcileMembership(uow, organizationId, membership.Id, MembershipCoverageEvent.MembershipChanged, userId);
            await uow.CommitAsync();
        }

        return await WithWarnings(organizationId, id, warnings);
    }

    /// <summary>2F (§16.3) — korisnik provizije na prvu prodaju, izravno na članstvu, dok provizija nije nastala (nakon nastanka
    /// COMMISSION_SALE_ALREADY_EARNED — korekcija uz commissions.manage, Q50). Promjena je događaj u povijesti članstva.</summary>
    public async Task<ClientMembershipDto> SetSaleCommissionEmployee(
        Guid organizationId, Guid userId, Guid id, Core.DTOs.Commissions.SaleCommissionEmployeeRequest request)
    {
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _commissionLedger.SetMembershipSaleCommissionEmployee(uow, organizationId, userId, id, request?.EmployeeId, "Membership");
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    public async Task<ClientMembershipDto> VoidSale(Guid organizationId, Guid userId, Guid id, ClientMembershipVoidRequest request)
    {
        string reason = string.IsNullOrWhiteSpace(request?.Reason) ? throw new ValidationAppException("Razlog je obavezan.") : request.Reason.Trim();
        DateOnly today = await Today(organizationId);
        List<WarningDto> warnings = new();
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            ClientMembership membership = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("ClientMembership", id);
            if (membership.VoidedAt != null)
                throw new BusinessRuleException(ErrorCodes.MembershipNotActive, "Prodaja je već poništena.");

            // Q24.4/Q51 — poništavanje samo bez AKTIVNIH alokacija plaćanja (voidane ostaju u povijesti naplate) i bez zaduženja u
            // otvorenom checkoutu; 2D dodaje "nema claimova".
            if (membership.Charges.Any(c => MembershipChargeSettlement.Settled(c) > 0m))
                throw new BusinessRuleException(ErrorCodes.MembershipSaleHasPayments,
                    "Prodaja ima aktivna plaćanja; stornirajte ih ili koristite otkaz (Q51).");
            if (membership.Charges.Any(c => c.CheckoutItems.Any(i => i.LocksMembershipCharge)))
                throw new BusinessRuleException(ErrorCodes.MembershipChargeInOpenCheckout,
                    "Zaduženje članarine je stavka otvorenog checkouta; uklonite stavku prije poništavanja.");
            // 2D (Q24.4 precizirano): blokira samo stvarno korištenje — sesija koja je počela ili je odrađena uz claim, ili propali
            // kredit (kasni otkaz / izostanak uz ForfeitCredit). Claimovi budućih termina se vraćaju niže.
            int usage = await _coverage.CountUsage(uow, membership.Id);
            if (usage > 0)
                throw new BusinessRuleException(ErrorCodes.MembershipSaleHasUsage,
                    "Članarina je već korištena (sesija je počela ili je kredit potrošen); poništavanje nije moguće — koristite otkaz.",
                    new { usedSessions = usage });

            DateTimeOffset now = DateTimeOffset.UtcNow;
            membership.VoidedAt = now;
            membership.VoidedBy = userId;
            membership.VoidReason = reason;
            // Pregled 2C (#6): SVA zaduženja (i otpisana) postaju Voided; zapis otpisa ostaje u povijesti zaduženja, a izvještaj
            // otpisa broji samo lifecycle WrittenOff, pa poništena prodaja ne pokazuje gubitak.
            foreach (MembershipCharge charge in membership.Charges.Where(c => c.Lifecycle != MembershipChargeLifecycle.Voided))
            {
                charge.Lifecycle = MembershipChargeLifecycle.Voided;
                charge.VoidedAt = now;
                charge.VoidedBy = userId;
                charge.VoidReason = reason;
            }
            Touch(membership, userId);
            CancelFuturePauses(uow, membership, userId, today, after: today, MembershipPauseCancellationReason.MembershipVoided, warnings);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "SaleVoided", null, null, reason));
            await uow.Context.SaveChangesAsync();
            List<WarningMembershipSession> uncovered = await _coverage.ReleaseForVoid(uow, organizationId, membership.Id, userId);
            if (uncovered.Count > 0)
                warnings.Add(new WarningDto(WarningCodes.MembershipVoidedSessionsUncovered, new WarningMembershipSessionsDetails { Sessions = uncovered }));
            await uow.CommitAsync();
        }

        return await WithWarnings(organizationId, id, warnings);
    }

    #endregion

    #region Pomoćno

    private async Task<DateOnly> Today(Guid organizationId) =>
        (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(DateTimeOffset.UtcNow);

    /// <summary>Zaključano članstvo koje nije poništeno ni završilo.</summary>
    private async Task<ClientMembership> LockActive(IUnitOfWork uow, Guid organizationId, Guid id, DateOnly today)
    {
        ClientMembership membership = await _handler.GetForUpdate(uow, organizationId, id)
            ?? throw new NotFoundAppException("ClientMembership", id);
        MembershipState state = StateOf(membership, today);
        if (state is MembershipState.Voided or MembershipState.Ended)
            throw new BusinessRuleException(ErrorCodes.MembershipNotActive, "Članstvo je završilo ili je poništeno.");
        return membership;
    }

    /// <summary>Kapacitet plana (prodaja i promjena plana, pregled 2B #8): članstva na planu na datum (bez odlazaka do tog datuma)
    /// i sva zakazana dolaženja klijentovom promjenom; pun plan → MEMBERSHIP_PLAN_FULL. Poziva se pod lockom plana.</summary>
    private async Task EnsurePlanCapacity(IUnitOfWork uow, Guid organizationId, MembershipPlan plan, DateOnly onDate, DateOnly today, Guid? excludeMembershipId)
    {
        if (!plan.MaxActiveMemberships.HasValue)
            return;

        List<ClientMembership> candidates = await _handler.GetCapacityCandidates(uow, organizationId, plan.Id, today);
        if (MembershipPlanCapacity.CountOnPlanAt(plan.Id, onDate, candidates, excludeMembershipId) >= plan.MaxActiveMemberships.Value)
            throw new BusinessRuleException(ErrorCodes.MembershipPlanFull, $"Plan '{plan.Name}' je dosegnuo najveći broj aktivnih članstava.");
    }

    private static MembershipState StateOf(ClientMembership membership, DateOnly today) => MembershipLifecycleRules.State(
        today, membership.StartsOn, membership.EndsOn, membership.VoidedAt != null, MembershipTimelines.PauseSpans(membership));

    /// <summary>Datum od kad bi otkaz zatražen danas djelovao. Pauze koje još nisu počele se ne računaju: zahtjev za otkaz ih
    /// poništava (2B), pa ne smiju produljiti period ni obvezu; pauza u tijeku se računa (otkaz na kraju produljenog perioda).</summary>
    private static (DateOnly EffectiveOn, MembershipEndEffectiveReason Reason) CancellationEffective(ClientMembership membership, DateOnly today)
    {
        MembershipTimeline current = MembershipTimelines.Current(membership);
        MembershipTimeline withoutFuturePauses = current with { Pauses = current.Pauses.Where(p => p.StartsOn <= today).ToList() };
        return MembershipLifecycleRules.CancellationEffective(
            withoutFuturePauses, today, membership.PlanVersion.MinimumCommitmentPeriods, membership.PlanVersion.CancellationNoticeDays,
            membership.CommitmentFromOn, membership.CommitmentFloorOn);
    }

    private static void EnsureNoOverlap(MembershipCoverageScope candidate, List<ClientMembership> existing)
    {
        MembershipCoverageConflict conflict = MembershipCoverageOverlap.FindConflict(candidate, existing.SelectMany(MembershipTimelines.Scopes));
        if (conflict != null)
            throw new BusinessRuleException(ErrorCodes.MembershipOverlappingCoverage,
                $"Članarina se preklapa s aktivnom članarinom '{conflict.Existing.PlanName}' (iste usluge u istoj poslovnici).",
                new
                {
                    conflictingMembershipId = conflict.Existing.MembershipId,
                    planName = conflict.Existing.PlanName,
                    sharedServiceIds = conflict.SharedServiceIds,
                    sharedCompanyIds = conflict.SharedCompanyIds,
                    sharedAllCompanies = conflict.SharedAllCompanies
                });
    }

    private void CancelFuturePauses(
        IUnitOfWork uow, ClientMembership membership, Guid userId, DateOnly today, DateOnly after,
        MembershipPauseCancellationReason reason, List<WarningDto> warnings)
    {
        List<MembershipPause> cancelled = membership.Pauses
            .Where(p => p.CancelledAt == null && p.StartsOn > today && p.StartsOn > after)
            .ToList();
        if (cancelled.Count == 0)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (MembershipPause pause in cancelled)
        {
            CancelPause(pause, userId, now, reason);
            _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "ScheduledPauseCancelled", pause.Id.ToString(), null, reason.ToString()));
        }

        warnings.Add(new WarningDto(WarningCodes.MembershipScheduledPauseCancelled, new WarningMembershipPausesDetails
        {
            PauseIds = cancelled.Select(p => p.Id).ToList()
        }));
    }

    private static void CancelPause(MembershipPause pause, Guid userId, DateTimeOffset now, MembershipPauseCancellationReason reason)
    {
        pause.CancelledAt = now;
        pause.CancelledBy = userId;
        pause.CancellationReason = reason;
        pause.UpdatedAt = now;
        pause.UpdatedBy = userId;
    }

    /// <summary>Zakazana promjena uvjeta koja bi stupila na snagu nakon završetka se uklanja (nikad se ne bi primijenila).</summary>
    private void DropPendingAfterEnd(IUnitOfWork uow, ClientMembership membership, Guid userId)
    {
        DateOnly? effectiveOn = MembershipTimelines.PendingEffectiveOn(membership);
        if (effectiveOn == null || membership.EndsOn == null || effectiveOn.Value <= membership.EndsOn.Value)
            return;

        string old = $"{membership.PendingSource}:{membership.PendingPlanVersionId}@{effectiveOn:yyyy-MM-dd}";
        ClearPending(membership);
        _handler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PendingChangeDropped", old, null));
    }

    private static void ClearPending(ClientMembership membership)
    {
        membership.PendingPlanVersionId = null;
        membership.PendingPlanVersion = null;
        membership.PendingEffectiveOn = null;
        membership.PendingSource = null;
    }

    /// <summary>Redni broj perioda koji završava na zakazanom datumu otkaza (prije promjene pauze).</summary>
    private static int? SequenceEndingOn(ClientMembership membership) => MembershipPeriodCalendar
        .Periods(MembershipTimelines.Current(membership))
        .TakeWhile(p => p.StartsOn <= membership.EndsOn.Value)
        .Where(p => p.EndsOn == membership.EndsOn.Value)
        .Select(p => (int?)p.Sequence)
        .FirstOrDefault();

    private static void Touch(ClientMembership membership, Guid userId)
    {
        membership.UpdatedAt = DateTimeOffset.UtcNow;
        membership.UpdatedBy = userId;
    }

    private async Task<ClientMembershipDto> WithWarnings(Guid organizationId, Guid id, List<WarningDto> warnings)
    {
        ClientMembershipDto dto = await GetById(organizationId, id);
        dto.Warnings = warnings;
        return dto;
    }

    private static ClientMembershipDto ToDto(ClientMembership membership, DateOnly today, int graceDays)
    {
        MembershipState state = StateOf(membership, today);
        MembershipTimeline timeline = MembershipTimelines.Current(membership);
        MembershipPeriod? period = state switch
        {
            MembershipState.Ended or MembershipState.Voided => null,
            MembershipState.Scheduled => MembershipPeriodCalendar.Periods(timeline).First(),
            _ => MembershipPeriodCalendar.PeriodContaining(timeline, today)
        };
        DateOnly? pendingOn = MembershipTimelines.PendingEffectiveOn(membership);

        return new ClientMembershipDto
        {
            Id = membership.Id,
            ClientId = membership.ClientId,
            MembershipPlanId = membership.MembershipPlanId,
            MembershipPlanName = membership.Plan?.Name,
            Terms = MembershipPlanReadModel.ToDto(membership.PlanVersion),
            State = state,
            Standing = MembershipChargeSettlement.Standing(membership.Charges, today, graceDays),
            OutstandingAmount = membership.Charges.Sum(MembershipChargeSettlement.Outstanding),
            StartsOn = membership.StartsOn,
            CurrentPeriod = period == null ? null : new MembershipPeriodDto
            {
                Sequence = period.Value.Sequence, StartsOn = period.Value.StartsOn, EndsOn = period.Value.EndsOn
            },
            EndsOn = membership.EndsOn,
            EndReason = membership.EndReason,
            CancellationRequestedAt = membership.CancellationRequestedAt,
            CancellationRequestedBy = membership.CancellationRequestedBy,
            CancellationReason = membership.CancellationReason,
            PendingChange = membership.PendingPlanVersion == null ? null : new MembershipPendingChangeDto
            {
                Source = membership.PendingSource.GetValueOrDefault(),
                MembershipPlanId = membership.PendingPlanVersion.MembershipPlanId,
                MembershipPlanName = membership.PendingPlanVersion.Plan?.Name,
                PlanVersionId = membership.PendingPlanVersion.Id,
                PlanVersion = membership.PendingPlanVersion.Version,
                EffectiveOn = pendingOn.GetValueOrDefault()
            },
            DisplacedPlanUpdate = membership.DisplacedPlanVersion == null ? null : new MembershipPendingChangeDto
            {
                Source = MembershipPendingChangeSource.PlanUpdate,
                MembershipPlanId = membership.DisplacedPlanVersion.MembershipPlanId,
                MembershipPlanName = membership.DisplacedPlanVersion.Plan?.Name,
                PlanVersionId = membership.DisplacedPlanVersion.Id,
                PlanVersion = membership.DisplacedPlanVersion.Version,
                EffectiveOn = membership.DisplacedEffectiveOn.GetValueOrDefault()
            },
            PlanUpdateNotApplied = membership.PlanUpdateSkippedVersionId == null ? null : new MembershipPlanUpdateNotAppliedDto
            {
                PlanVersionId = membership.PlanUpdateSkippedVersionId.Value,
                PlanVersion = membership.PlanUpdateSkippedVersion?.Version ?? 0,
                Reason = membership.PlanUpdateSkippedReason,
                SkippedAt = membership.PlanUpdateSkippedAt.GetValueOrDefault()
            },
            Pauses = membership.Pauses.OrderBy(p => p.StartsOn).Select(p => new MembershipPauseDto
            {
                Id = p.Id,
                Kind = p.Kind,
                StartsOn = p.StartsOn,
                PlannedEndsOn = p.PlannedEndsOn,
                ActualEndsOn = p.ActualEndsOn,
                Reason = p.Reason,
                CancelledAt = p.CancelledAt,
                CancelledBy = p.CancelledBy,
                CancellationReason = p.CancellationReason,
                CreatedAt = p.CreatedAt,
                CreatedBy = p.CreatedBy
            }).ToList(),
            PauseAllowance = PauseAllowance(membership, timeline, today),
            SoldCompanyId = membership.SoldCompanyId,
            SoldVia = membership.SoldVia,
            SoldBy = membership.SoldBy,
            SaleCommissionEmployeeId = membership.SaleCommissionEmployeeId,
            FirstSaleSettledAt = membership.FirstSaleSettledAt,
            VoidedAt = membership.VoidedAt,
            VoidedBy = membership.VoidedBy,
            VoidReason = membership.VoidReason,
            CreatedAt = membership.CreatedAt,
            CreatedBy = membership.CreatedBy,
            UpdatedAt = membership.UpdatedAt,
            UpdatedBy = membership.UpdatedBy
        };
    }

    /// <summary>Q5.4 — preostalo u tekućem 12-mjesečnom prozoru od početka članstva.</summary>
    private static MembershipPauseAllowanceDto PauseAllowance(ClientMembership membership, MembershipTimeline timeline, DateOnly today)
    {
        MembershipPlanVersion terms = membership.PlanVersion;
        if (!terms.PauseAllowed)
            return null;

        MembershipPauseUsage usage = MembershipLifecycleRules.PauseUsage(timeline, today > membership.StartsOn ? today : membership.StartsOn);
        return new MembershipPauseAllowanceDto
        {
            WindowStartsOn = usage.WindowStartsOn,
            WindowEndsOn = usage.WindowEndsOn,
            RemainingDays = terms.MaxPauseDays.HasValue ? Math.Max(terms.MaxPauseDays.Value - usage.UsedDays, 0) : null,
            RemainingPeriods = terms.MaxPausePeriods.HasValue ? Math.Max(terms.MaxPausePeriods.Value - usage.UsedPeriods, 0) : null,
            RemainingPauses = terms.MaxPausesPer12Months.HasValue ? Math.Max(terms.MaxPausesPer12Months.Value - usage.UsedPauses, 0) : null
        };
    }

    #endregion
}
