using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi <see cref="IMembershipCoverageService"/>; čista pravila su u <see cref="MembershipCoverageRules"/>.</summary>
public class MembershipCoverageService : IMembershipCoverageService
{
    private readonly IClientMembershipHandler _membershipHandler;
    private readonly IOrganizationCalendarService _calendars;
    private readonly IOrganizationSettingsService _settings;
    private readonly IGrantResolver _grantResolver;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;

    public MembershipCoverageService(
        IClientMembershipHandler membershipHandler, IOrganizationCalendarService calendars, IOrganizationSettingsService settings,
        IGrantResolver grantResolver,
        IAppointmentAuditLogHandler auditLogHandler,
        IUnitOfWorkFactory unitOfWorkFactory,
        TimeProvider timeProvider)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _auditLogHandler = auditLogHandler;
        _grantResolver = grantResolver;
        _membershipHandler = membershipHandler;
        _calendars = calendars;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    /// <summary>Kontekst jedne operacije: trenutak, "danas" u zoni organizacije, kalendari i postavke.</summary>
    private sealed class Run
    {
        public Guid OrganizationId { get; init; }
        public Guid? UserId { get; init; }
        public DateTimeOffset Now { get; init; }
        public DateOnly Today { get; init; }
        public OrganizationCalendar OrganizationCalendar { get; init; }
        public Dictionary<Guid, OrganizationCalendar> CompanyCalendars { get; } = new();
        public int GraceDays { get; init; }
        public MembershipDebtBehavior DebtBehavior { get; init; }
        public MembershipLimitExceededBehavior LimitBehavior { get; init; }
    }

    private async Task<Run> Start(Guid organizationId, Guid? userId, DateOnly? today = null)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        OrganizationCalendar calendar = await _calendars.GetCalendar(organizationId);
        OrganizationSettingsDto settings = await _settings.GetSettings(organizationId);
        return new Run
        {
            OrganizationId = organizationId,
            UserId = userId,
            Now = now,
            Today = today ?? calendar.LocalDate(now),
            OrganizationCalendar = calendar,
            GraceDays = settings.MembershipGraceDays,
            DebtBehavior = settings.MembershipDebtBehavior,
            LimitBehavior = settings.MembershipLimitExceededBehavior
        };
    }

    #region Strana sudjelovanja

    public Task<MembershipCoverageDecision> SyncParticipation(
        IUnitOfWork uow, Guid organizationId, Guid? userId, Appointment appointment, Booking booking, BookingSegmentParticipation participation,
        MembershipCoverageEvent @event, MembershipCoverageMode mode) =>
        Sync(uow, organizationId, userId, ViewOf(appointment, booking, participation), participation, @event, mode, reevaluate: false);

    public Task<MembershipCoverageDecision> ReevaluateParticipation(
        IUnitOfWork uow, Guid organizationId, Guid? userId, Appointment appointment, Booking booking, BookingSegmentParticipation participation) =>
        Sync(uow, organizationId, userId, ViewOf(appointment, booking, participation), participation,
            MembershipCoverageEvent.Rescheduled, MembershipCoverageMode.Automatic, reevaluate: true);

    private async Task<MembershipCoverageDecision> Sync(
        IUnitOfWork uow, Guid organizationId, Guid? userId, ParticipationExecutionContextView view, BookingSegmentParticipation participation,
        MembershipCoverageEvent @event, MembershipCoverageMode mode, bool reevaluate)
    {
        Guid participationId = view.ParticipationId;

        // No-op bez članarine (naglasak 1): bez claima, bez projekcije i bez relevantnog članstva ništa se ne čita dalje ni piše.
        MembershipUsage claim = await ActiveClaimOf(uow, participationId);
        bool hasProjection = participation.MembershipCoverage != null
                             || await uow.Context.ParticipationMembershipCoverages.AnyAsync(c => c.ParticipationId == participationId);
        if (claim == null && !hasProjection && !await HasRelevantMembership(uow, organizationId, view.ClientId, _timeProvider.GetUtcNow()))
            return null;

        Run run = await Start(organizationId, userId);
        MembershipCoverageSubject subject = await SubjectOf(uow, run, view, participationId);
        List<ClientMembership> memberships = await _membershipHandler.GetCoverageCandidates(uow, organizationId, view.ClientId);
        ClientMembership responsibleView = MembershipCoverageRules.Responsible(
            memberships, subject.ServiceId, subject.CompanyId, subject.PeriodDate, run.Today);

        // Istovremenost (naglasak 2): lock odgovornog članstva (i članstva postojećeg claima) PRIJE čitanja brojača.
        SortedSet<Guid> lockIds = new();
        if (responsibleView != null)
            lockIds.Add(responsibleView.Id);
        if (claim != null)
            lockIds.Add(claim.ClientMembershipId);
        Dictionary<Guid, ClientMembership> locked = new();
        foreach (Guid id in lockIds)
            locked[id] = await _membershipHandler.GetForUpdate(uow, organizationId, id);
        claim = await ActiveClaimOf(uow, participationId);
        ClientMembership responsible = responsibleView == null ? null : locked[responsibleView.Id];
        HashSet<Guid> freed = new();

        if (reevaluate && claim != null)
        {
            Release(uow, run, claim, MembershipUsageReleaseReason.Rescheduled);
            freed.Add(claim.ClientMembershipId);
            claim = null;
        }

        MembershipCoverageDecision decision;
        if (!ParticipationOccupancy.Occupies(participation.Status))
        {
            decision = await SyncInactive(uow, run, participation, claim, freed);
        }
        else
        {
            if (claim != null && claim.ClientMembershipId != responsible?.Id)
            {
                Release(uow, run, claim, MembershipUsageReleaseReason.TermsChanged);
                freed.Add(claim.ClientMembershipId);
                claim = null;
            }

            if (responsible == null)
            {
                // Nijedno članstvo nije relevantno za termin (npr. pomaknut izvan važenja) — kao bez članarine.
                await RemoveProjection(uow, participationId);
                decision = null;
            }
            else
            {
                decision = await SyncActive(uow, run, responsible, subject, claim, @event, freed);
                // Q15.4/Q54: nova rezervacija sesije koju bi pokrila članarina u dugu uz "blokiraj rezervaciju" — samo uz grant
                // (tada bez pokrića, DebtNotCovered).
                if (decision.Reason == MembershipCoverageReason.DebtNotCovered && mode == MembershipCoverageMode.Interactive
                    && @event == MembershipCoverageEvent.Booking && run.DebtBehavior == MembershipDebtBehavior.BlockBooking
                    && !(userId.HasValue && (await _grantResolver.Resolve(organizationId, userId.Value)).Has(Grants.AppointmentsMembershipBlockOverride)))
                {
                    throw new BusinessRuleException(ErrorCodes.MembershipBookingBlocked,
                        "Članarina klijenta je u dugu nakon grace perioda, a postavka organizacije blokira rezervaciju.",
                        new { clientMembershipId = responsible.Id, plannedStart = view.StartsAt });
                }

                if (decision.Reason == MembershipCoverageReason.LimitReached && mode == MembershipCoverageMode.Interactive
                    && run.LimitBehavior == MembershipLimitExceededBehavior.Reject)
                {
                    MembershipExhaustedLimit limit = decision.Limit.GetValueOrDefault();
                    throw new BusinessRuleException(ErrorCodes.MembershipLimitExceeded,
                        "Limit članarine je iskorišten, a postavka organizacije je odbijanje rezervacije.",
                        new
                        {
                            clientMembershipId = responsible.Id,
                            window = limit.Window,
                            serviceId = limit.ServiceId,
                            maxUses = limit.MaxUses,
                            used = limit.Used,
                            plannedStart = view.StartsAt
                        });
                }
            }
        }

        // 2E: cijena aktivne rezervacije prati odluku o pokriću i pogodnost (pokriveno = cjenik; nepokriveno = najbolja cijena, Q1).
        // Pregled 2E #4: zastarjela cijena (PriceStale) se uskladi pri svakoj obradi zauzimajućeg sudjelovanja (i pri odrađivanju).
        bool stale = ParticipationOccupancy.Occupies(participation.Status)
                     && await uow.Context.ParticipationMembershipCoverages.AnyAsync(c => c.ParticipationId == participationId && c.PriceStale);
        if (participation.Status == ParticipationStatus.Confirmed || stale)
            await PriceParticipation(uow, run, participation, view.AppointmentId, subject,
                Views(run, memberships.Select(m => locked.TryGetValue(m.Id, out ClientMembership l) ? l : m)),
                covered: decision?.IsCovered == true, packageCovered: false, @event);

        await uow.Context.SaveChangesAsync();
        await FillFreed(uow, run, freed, locked, participationId);
        return decision;
    }

    /// <summary>Zauzimajuće sudjelovanje: postojeći claim odgovornog članstva ostaje dok je prihvatljiv; inače evaluacija.</summary>
    private async Task<MembershipCoverageDecision> SyncActive(
        IUnitOfWork uow, Run run, ClientMembership membership, MembershipCoverageSubject subject, MembershipUsage claim,
        MembershipCoverageEvent @event, HashSet<Guid> freed)
    {
        MembershipStanding standing = Standing(run, membership);
        if (claim != null)
        {
            MembershipCoverageReason? ineligible = MembershipCoverageRules.Ineligibility(membership, subject, standing, run.DebtBehavior);
            if (ineligible == null)
            {
                MembershipCoverageDecision kept = new(MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
                await SetProjection(uow, run, subject.ParticipationId, membership.Id, kept, claim.Id, @event);
                return kept;
            }

            Release(uow, run, claim, MembershipCoverageRules.ReleaseReasonFor(ineligible.Value));
            freed.Add(membership.Id);
            await uow.Context.SaveChangesAsync();
        }

        List<MembershipUsage> claims = await ActiveClaimsOf(uow, membership.Id);
        MembershipCoverageDecision decision = MembershipCoverageRules.Evaluate(
            membership, subject, claims.Select(ViewOf).ToList(), run.Today, standing, run.DebtBehavior);
        Guid? usageId = decision.IsCovered ? Claim(uow, run, membership, subject).Id : null;
        await SetProjection(uow, run, subject.ParticipationId, membership.Id, decision, usageId, @event);
        return decision;
    }

    /// <summary>Otkazano/izostalo sudjelovanje: claim ostaje samo kad ga zadržava aktivna posljedica politike (ForfeitCredit,
    /// Q26/Q31), inače se vraća s razlogom (otkaz na vrijeme, poslovni/sustavski otkaz, otpis, ReturnCreditChargeFee).</summary>
    private async Task<MembershipCoverageDecision> SyncInactive(
        IUnitOfWork uow, Run run, BookingSegmentParticipation participation, MembershipUsage claim, HashSet<Guid> freed)
    {
        if (claim == null)
            return null;

        Guid participationId = participation.Id.GetValueOrDefault();
        ParticipationPolicyConsequence active = PolicyConsequences.ActiveOf(participation);
        if (active != null && active.MembershipUsageId == claim.Id)
        {
            MembershipCoverageDecision retained = new(MembershipCoverageStatus.Covered,
                active.MembershipCreditForfeited ? MembershipCoverageReason.CreditForfeited : MembershipCoverageReason.SlotUsedFeeCharged);
            await SetProjection(uow, run, participationId, claim.ClientMembershipId, retained, claim.Id, MembershipCoverageEvent.PolicyEvent);
            return retained;
        }

        (MembershipUsageReleaseReason releaseReason, MembershipCoverageReason reason, MembershipCoverageEvent @event) = InactiveReleaseReason(participation, claim);
        Release(uow, run, claim, releaseReason);
        freed.Add(claim.ClientMembershipId);
        MembershipCoverageDecision released = new(MembershipCoverageStatus.Released, reason);
        await SetProjection(uow, run, participationId, claim.ClientMembershipId, released, null, @event);
        return released;
    }

    private static (MembershipUsageReleaseReason, MembershipCoverageReason, MembershipCoverageEvent) InactiveReleaseReason(
        BookingSegmentParticipation participation, MembershipUsage claim)
    {
        ParticipationPolicyConsequence current = participation.PolicyConsequences
            .FirstOrDefault(c => c.SourceVersion == participation.StatusVersion && c.MembershipAction != null);
        bool waivedAfter = participation.PolicyConsequences.Any(c => c.MembershipUsageId == claim.Id && c.Status == PolicyConsequenceStatus.Waived);
        if (current?.Status == PolicyConsequenceStatus.Waived || waivedAfter)
            return (MembershipUsageReleaseReason.PolicyWaived, MembershipCoverageReason.PolicyWaived, MembershipCoverageEvent.PolicyEvent);
        if (current?.MembershipAction == CancellationMembershipAction.ReturnCreditChargeFee)
            return current.CalculatedFeeAmount > 0m
                ? (MembershipUsageReleaseReason.CreditReturnedWithFee, MembershipCoverageReason.CreditReturnedWithFee, MembershipCoverageEvent.PolicyEvent)
                : (MembershipUsageReleaseReason.CreditReturned, MembershipCoverageReason.CreditReturned, MembershipCoverageEvent.PolicyEvent);

        return participation.CancellationInitiator switch
        {
            CancellationInitiator.Client => (MembershipUsageReleaseReason.CancelledOnTime, MembershipCoverageReason.CancelledOnTime,
                MembershipCoverageEvent.ParticipationCancelled),
            CancellationInitiator.Business => (MembershipUsageReleaseReason.CancelledByBusiness, MembershipCoverageReason.CancelledByBusiness,
                MembershipCoverageEvent.ParticipationCancelled),
            _ => (MembershipUsageReleaseReason.CancelledBySystem, MembershipCoverageReason.CancelledBySystem,
                MembershipCoverageEvent.ParticipationCancelled)
        };
    }

    public async Task<MembershipActiveClaim> GetActiveClaim(
        IUnitOfWork uow, Guid organizationId, BookingSegmentParticipation participation, ParticipationExecutionContextView execution)
    {
        MembershipUsage claim = await ActiveClaimOf(uow, execution.ParticipationId);
        if (claim == null)
            return null;

        ClientMembership membership = await _membershipHandler.GetForUpdate(uow, organizationId, claim.ClientMembershipId);
        claim = await ActiveClaimOf(uow, execution.ParticipationId);
        if (claim == null || membership == null)
            return null;
        Domain.Models.Catalog.MembershipPlanVersion terms = MembershipCoverageRules.TermsOn(membership, claim.PeriodDate);
        return new MembershipActiveClaim(claim, membership, MembershipCoverageRules.HasPeriodCredits(terms, claim.ServiceId));
    }


    public async Task ReleaseForRemoval(
        IUnitOfWork uow, Guid organizationId, Guid? userId, IReadOnlyCollection<BookingSegmentParticipation> participations)
    {
        List<Guid> ids = participations.Select(p => p.Id.GetValueOrDefault()).ToList();
        List<Guid> membershipIds = await uow.Context.MembershipUsages
            .Where(u => ids.Contains(u.ParticipationId) && u.EntryType == MembershipUsageEntryType.Claim && u.IsActive)
            .Select(u => u.ClientMembershipId).Distinct().ToListAsync();
        List<ParticipationMembershipCoverage> projections = await uow.Context.ParticipationMembershipCoverages
            .Where(c => ids.Contains(c.ParticipationId)).ToListAsync();
        if (membershipIds.Count == 0 && projections.Count == 0)
            return;

        Run run = await Start(organizationId, userId);
        Dictionary<Guid, ClientMembership> locked = new();
        foreach (Guid id in membershipIds.OrderBy(id => id))
            locked[id] = await _membershipHandler.GetForUpdate(uow, organizationId, id);

        HashSet<Guid> freed = new();
        foreach (Guid participationId in ids)
        {
            MembershipUsage claim = await ActiveClaimOf(uow, participationId);
            if (claim == null)
                continue;
            Release(uow, run, claim, MembershipUsageReleaseReason.ParticipationRemoved);
            freed.Add(claim.ClientMembershipId);
        }

        uow.Context.ParticipationMembershipCoverages.RemoveRange(projections);
        foreach (BookingSegmentParticipation participation in participations)
            participation.MembershipCoverage = null;
        await uow.Context.SaveChangesAsync();
        await FillFreed(uow, run, freed, locked, excludeParticipationIds: ids);
    }

    #endregion

    #region Strana članstva

    public async Task ReconcileMembership(
        IUnitOfWork uow, Guid organizationId, Guid membershipId, MembershipCoverageEvent @event, Guid? userId, DateOnly? today = null)
    {
        Run run = await Start(organizationId, userId, today);
        ClientMembership membership = await _membershipHandler.GetForUpdate(uow, organizationId, membershipId);
        if (membership == null || membership.VoidedAt != null)
            return;
        await Reconcile(uow, run, membership, @event, Array.Empty<Guid>());
    }

    public async Task ReconcileClient(IUnitOfWork uow, Guid organizationId, Guid clientId, MembershipCoverageEvent @event, Guid? userId)
    {
        List<Guid> ids = await uow.Context.ClientMemberships
            .Where(m => m.OrganizationId == organizationId && m.ClientId == clientId && m.VoidedAt == null)
            .OrderBy(m => m.Id).Select(m => m.Id).ToListAsync();
        foreach (Guid id in ids)
            await ReconcileMembership(uow, organizationId, id, @event, userId);
    }

    public async Task<List<WarningMembershipSession>> ReleaseForVoid(IUnitOfWork uow, Guid organizationId, Guid membershipId, Guid? userId)
    {
        Run run = await Start(organizationId, userId);
        ClientMembership membership = await _membershipHandler.GetForUpdate(uow, organizationId, membershipId);
        List<MembershipUsage> claims = await ActiveClaimsOf(uow, membershipId);
        foreach (MembershipUsage claim in claims)
            Release(uow, run, claim, MembershipUsageReleaseReason.MembershipVoided);

        // Pokriveni (vraćen claim) i oni koji su čekali evaluaciju su "pogođeni"; ostale projekcije članstva samo dobivaju razlog.
        List<ParticipationMembershipCoverage> rows = await uow.Context.ParticipationMembershipCoverages
            .Where(c => c.OrganizationId == organizationId && c.ClientMembershipId == membershipId)
            .ToListAsync();
        HashSet<Guid> affected = rows
            .Where(r => r.Status is MembershipCoverageStatus.Covered or MembershipCoverageStatus.PendingEvaluation)
            .Select(r => r.ParticipationId)
            .Concat(claims.Select(c => c.ParticipationId))
            .ToHashSet();
        foreach (ParticipationMembershipCoverage row in rows.Where(r => r.Status != MembershipCoverageStatus.Released))
            Apply(row, run, membershipId, new MembershipCoverageDecision(MembershipCoverageStatus.NotCovered, MembershipCoverageReason.MembershipVoided),
                null, MembershipCoverageEvent.MembershipChanged);
        await uow.Context.SaveChangesAsync();

        // Kao da članarine nije bilo: druga (neponištena) članstva klijenta mogu preuzeti termine.
        await ReconcileClient(uow, organizationId, membership.ClientId, MembershipCoverageEvent.MembershipChanged, userId);
        // 2E: i kad klijentu ne ostane nijedno članstvo, pogodnost poništenog se uklanja s budućih neplaćenih rezervacija.
        await RepriceFuture(uow, run, await FutureConfirmedOf(uow, run, membership.ClientId),
            Views(run, await _membershipHandler.GetCoverageCandidates(uow, organizationId, membership.ClientId)), MembershipCoverageEvent.MembershipChanged);

        var sessions = await uow.Context.BookingSegmentParticipations
            .Where(p => affected.Contains(p.Id.Value))
            .Select(p => new { Id = p.Id.Value, p.Booking.AppointmentId, p.Segment.PlannedStart })
            .ToListAsync();
        return sessions.OrderBy(s => s.PlannedStart)
            .Select(s => new WarningMembershipSession { ParticipationId = s.Id, AppointmentId = s.AppointmentId, PlannedStart = s.PlannedStart })
            .ToList();
    }

    public async Task<Guid?> BlockingMembership(
        IUnitOfWork uow, Guid organizationId, Guid clientId, Guid serviceId, Guid companyId, DateTimeOffset startsAt)
    {
        if (!await HasRelevantMembership(uow, organizationId, clientId, _timeProvider.GetUtcNow()))
            return null;
        Run run = await Start(organizationId, null);
        if (run.DebtBehavior != MembershipDebtBehavior.BlockBooking)
            return null;

        DateOnly periodDate = run.OrganizationCalendar.LocalDate(startsAt);
        ClientMembership responsible = MembershipCoverageRules.Responsible(
            await _membershipHandler.GetCoverageCandidates(uow, organizationId, clientId), serviceId, companyId, periodDate, run.Today);
        if (responsible == null || !MembershipCoverageRules.IsValidOn(responsible, periodDate))
            return null;
        Domain.Models.Catalog.MembershipPlanVersion terms = MembershipCoverageRules.TermsOn(responsible, periodDate);
        bool covers = MembershipCoverageRules.CoversService(terms, serviceId) && MembershipCoverageRules.CoversCompany(terms, companyId);
        return covers && Standing(run, responsible) == MembershipStanding.Delinquent ? responsible.Id : null;
    }

    public async Task<int> CountUsage(IUnitOfWork uow, Guid membershipId)
    {
        List<Guid> claimed = await uow.Context.MembershipUsages
            .Where(u => u.ClientMembershipId == membershipId && u.EntryType == MembershipUsageEntryType.Claim && u.IsActive)
            .Select(u => u.ParticipationId)
            .ToListAsync();
        if (claimed.Count == 0)
            return 0;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        return await uow.Context.BookingSegmentParticipations.CountAsync(p =>
            claimed.Contains(p.Id.Value) && (p.Status != ParticipationStatus.Confirmed || p.Segment.PlannedStart <= now));
    }

    /// <summary>Oslobođena mjesta (2D): za svako članstvo čiji je claim vraćen, jedan prolaz ponovne evaluacije.</summary>
    private async Task FillFreed(
        IUnitOfWork uow, Run run, HashSet<Guid> freed, Dictionary<Guid, ClientMembership> locked, Guid excludeParticipationId) =>
        await FillFreed(uow, run, freed, locked, new[] { excludeParticipationId });

    private async Task FillFreed(
        IUnitOfWork uow, Run run, HashSet<Guid> freed, Dictionary<Guid, ClientMembership> locked, IReadOnlyCollection<Guid> excludeParticipationIds)
    {
        foreach (Guid membershipId in freed.OrderBy(id => id))
        {
            if (!locked.TryGetValue(membershipId, out ClientMembership membership) || membership == null || membership.VoidedAt != null)
                continue;
            await Reconcile(uow, run, membership, MembershipCoverageEvent.SlotFreed, excludeParticipationIds);
        }
    }

    /// <summary>
    /// Jedan prolaz pod lockom članstva nad BUDUĆIM potvrđenim sudjelovanjima klijenta (prošli termini se ne diraju):
    /// (1) claimovi ovog članstva koji više nisu prihvatljivi (pauza, kraj, dug, uvjeti) se storniraju; (2) nepokrivena
    /// sudjelovanja za koja je ovo članstvo odgovorno evaluiraju se redom po početku termina, pa vremenu rezervacije, pa Id-u
    /// (Q27) — dok limiti dopuštaju. Postojeći claimovi se nikad ne preraspodjeljuju (nema "otimanja" mjesta).
    /// </summary>
    private async Task Reconcile(
        IUnitOfWork uow, Run run, ClientMembership membership, MembershipCoverageEvent @event, IReadOnlyCollection<Guid> excludeParticipationIds)
    {
        await uow.Context.SaveChangesAsync();
        List<FutureParticipation> participations = await FutureConfirmedOf(uow, run, membership.ClientId);
        participations = participations.Where(p => !excludeParticipationIds.Contains(p.Subject.ParticipationId)).ToList();
        if (participations.Count == 0)
            return;

        List<ClientMembership> all = (await _membershipHandler.GetCoverageCandidates(uow, run.OrganizationId, membership.ClientId))
            .Where(m => m.Id != membership.Id).Append(membership).ToList();
        MembershipStanding standing = Standing(run, membership);
        List<MembershipUsage> claims = await ActiveClaimsOf(uow, membership.Id);
        List<Guid> participationIds = participations.Select(p => p.Subject.ParticipationId).ToList();
        HashSet<Guid> claimedElsewhere = (await uow.Context.MembershipUsages
                .Where(u => participationIds.Contains(u.ParticipationId) && u.EntryType == MembershipUsageEntryType.Claim && u.IsActive
                            && u.ClientMembershipId != membership.Id)
                .Select(u => u.ParticipationId).ToListAsync())
            .ToHashSet();

        // (1) Storno claimova koji više nisu prihvatljivi.
        foreach (FutureParticipation participation in participations)
        {
            MembershipUsage claim = claims.FirstOrDefault(c => c.ParticipationId == participation.Subject.ParticipationId);
            if (claim == null)
                continue;
            ClientMembership responsible = MembershipCoverageRules.Responsible(
                all, participation.Subject.ServiceId, participation.Subject.CompanyId, participation.Subject.PeriodDate, run.Today);
            MembershipCoverageReason? ineligible = MembershipCoverageRules.Ineligibility(membership, participation.Subject, standing, run.DebtBehavior)
                ?? (responsible?.Id != membership.Id ? MembershipCoverageReason.ServiceNotCovered : null);
            if (ineligible == null)
                continue;

            Release(uow, run, claim, MembershipCoverageRules.ReleaseReasonFor(ineligible.Value));
            claims.Remove(claim);
            await SetProjection(uow, run, participation.Subject.ParticipationId, membership.Id,
                new MembershipCoverageDecision(MembershipCoverageStatus.NotCovered, ineligible.Value), null, @event);
        }

        // (2) Evaluacija nepokrivenih redom po vremenu termina.
        foreach (FutureParticipation participation in participations)
        {
            Guid participationId = participation.Subject.ParticipationId;
            if (claimedElsewhere.Contains(participationId) || claims.Any(c => c.ParticipationId == participationId))
                continue;
            ClientMembership responsible = MembershipCoverageRules.Responsible(
                all, participation.Subject.ServiceId, participation.Subject.CompanyId, participation.Subject.PeriodDate, run.Today);
            if (responsible?.Id != membership.Id)
                continue;

            MembershipCoverageDecision decision = MembershipCoverageRules.Evaluate(
                membership, participation.Subject, claims.Select(ViewOf).ToList(), run.Today, standing, run.DebtBehavior);
            Guid? usageId = null;
            if (decision.IsCovered)
            {
                MembershipUsage claim = Claim(uow, run, membership, participation.Subject);
                claims.Add(claim);
                usageId = claim.Id;
            }

            await SetProjection(uow, run, participationId, membership.Id, decision, usageId, @event);
        }

        await uow.Context.SaveChangesAsync();
        // 2E: cijene budućih neplaćenih rezervacija klijenta prate novo pokriće i pogodnost (svih njegovih članstava).
        await RepriceFuture(uow, run, participations, Views(run, all), @event);
    }

    #endregion

    #region Cijena (2E)

    private static readonly JsonSerializerOptions AdjustmentJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task PriceOnCompletion(
        IUnitOfWork uow, Guid organizationId, Guid? userId, Appointment appointment, Booking booking, BookingSegmentParticipation participation,
        bool packageCovered)
    {
        ParticipationExecutionContextView view = ViewOf(appointment, booking, participation);
        bool relevant = participation.MembershipCoverage != null || participation.AdjustmentType != null
                        || await HasRelevantMembership(uow, organizationId, view.ClientId, _timeProvider.GetUtcNow());
        if (!relevant)
            return;

        Run run = await Start(organizationId, userId);
        MembershipCoverageSubject subject = await SubjectOf(uow, run, view, view.ParticipationId);
        await PriceParticipation(uow, run, participation, view.AppointmentId, subject,
            Views(run, await _membershipHandler.GetCoverageCandidates(uow, organizationId, view.ClientId)),
            covered: MembershipCoverages.CoversService(participation), packageCovered, MembershipCoverageEvent.Booking);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<bool> EnsurePriceCurrent(IUnitOfWork uow, Guid organizationId, Guid? userId, Guid participationId)
    {
        bool stale = await uow.Context.ParticipationMembershipCoverages.AsNoTracking()
            .AnyAsync(c => c.OrganizationId == organizationId && c.ParticipationId == participationId && c.PriceStale);
        if (!stale)
            return false;

        var row = await uow.Context.BookingSegmentParticipations
            .Where(p => p.OrganizationId == organizationId && p.Id == participationId)
            .Select(p => new { p.Booking.ClientId, p.Booking.AppointmentId, p.Segment.ServiceId, p.Segment.Appointment.CompanyId, p.Segment.PlannedStart })
            .SingleAsync();
        BookingSegmentParticipation participation = await uow.Context.BookingSegmentParticipations
            .SingleAsync(p => p.OrganizationId == organizationId && p.Id == participationId);
        // Pozivatelj drži lock sudjelovanja; vrijednosti se čitaju svježe (instanca je mogla biti učitana prije locka).
        if (uow.Context.Entry(participation).State == EntityState.Unchanged)
            await uow.Context.Entry(participation).ReloadAsync();
        ParticipationMembershipCoverage projection = await uow.Context.ParticipationMembershipCoverages.FindAsync(participationId);
        if (uow.Context.Entry(projection).State == EntityState.Unchanged)
            await uow.Context.Entry(projection).ReloadAsync();

        Run run = await Start(organizationId, userId);
        if (!ParticipationOccupancy.Occupies(participation.Status))
        {
            projection.PriceStale = false;
            await uow.Context.SaveChangesAsync();
            return true;
        }

        ParticipationExecutionContextView view = new(participationId, row.ClientId, row.ServiceId, row.CompanyId, row.PlannedStart, row.AppointmentId);
        MembershipCoverageSubject subject = await SubjectOf(uow, run, view, participationId);
        await PriceParticipation(uow, run, participation, row.AppointmentId, subject,
            Views(run, await _membershipHandler.GetCoverageCandidates(uow, organizationId, row.ClientId)),
            covered: projection.Status == MembershipCoverageStatus.Covered, packageCovered: PackageConsumptions.IsSettledByPackage(participation),
            MembershipCoverageEvent.PriceRefresh);
        projection.PriceStale = false;
        await uow.Context.SaveChangesAsync();
        return true;
    }

    public async Task<int> RefreshStalePrices(Guid organizationId)
    {
        List<Guid> stale;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            stale = await uow.Context.ParticipationMembershipCoverages.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && c.PriceStale)
                .Select(c => c.ParticipationId).ToListAsync();
        }

        int refreshed = 0;
        foreach (Guid participationId in stale)
        {
            // Vlastita transakcija po sudjelovanju; lock sudjelovanja (čeka, ne preskače) pa usklađivanje — isti redoslijed kao naplata.
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
            await uow.Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM dunelight.booking_segment_participations WHERE organization_id = {organizationId} AND id = {participationId} FOR UPDATE");
            if (await EnsurePriceCurrent(uow, organizationId, null, participationId))
                refreshed++;
            await uow.CommitAsync();
        }

        return refreshed;
    }

    /// <summary>Članstva klijenta kao izvori pogodnosti: redoslijed izvora (početak, Id) je tie-breaker kod iste cijene.</summary>
    private List<ClientMembershipView> Views(Run run, IEnumerable<ClientMembership> memberships) => memberships
        .Where(m => m.VoidedAt == null)
        .OrderBy(m => m.StartsOn).ThenBy(m => m.Id)
        .Select((m, index) => new ClientMembershipView(m, index, Standing(run, m), run.DebtBehavior))
        .ToList();

    /// <summary>
    /// 2E — JEDINO mjesto automatske cijene sudjelovanja s obzirom na članarinu (pozivatelj drži lock sudjelovanja): pokrivena
    /// sesija (članarina ili paket) = cjenik (Q9, Q2), nepokrivena = najbolja cijena među kandidatima (Q1). Zaštićeno od
    /// automatske promjene: ručni iznos i aktivna novčana alokacija (razlog na projekciji). Svaka promjena iznosa ide u audit
    /// (stara, nova) i na projekciju (stara, nova, događaj, vrijeme); evaluacija svih kandidata se snapshotira na sudjelovanju.
    /// </summary>
    private async Task PriceParticipation(
        IUnitOfWork uow, Run run, BookingSegmentParticipation participation, Guid appointmentId, MembershipCoverageSubject subject,
        IReadOnlyCollection<ClientMembershipView> memberships, bool covered, bool packageCovered, MembershipCoverageEvent @event)
    {
        ParticipationMembershipCoverage projection = await uow.Context.ParticipationMembershipCoverages.FindAsync(subject.ParticipationId);
        PriceProtectionReason? protectedBy = participation.IsAmountManuallyOverridden ? PriceProtectionReason.ManualAmount
            : subject.ActiveMonetarySettlement > 0m ? PriceProtectionReason.AlreadyPaid
            : null;
        if (projection != null)
        {
            projection.PriceProtectedReason = protectedBy;
            projection.PriceStale = false;
        }

        if (protectedBy != null || participation.BaseAmount == null)
            return;

        decimal basePrice = participation.BaseAmount.Value;
        PriceAdjustmentResult result = PriceAdjustmentResolver.Resolve(basePrice, memberships
            .Select(m => MembershipPriceBenefitRules.Candidate(m, subject, basePrice, covered, packageCovered))
            .ToList());
        PriceAdjustmentCandidate applied = result.Applied;
        decimal suggested = applied?.ResultingPrice ?? basePrice;
        decimal? adjustmentAmount = applied == null ? null : suggested - basePrice;
        string evaluation = memberships.Count == 0 ? null : JsonSerializer.Serialize(result.Evaluation, AdjustmentJson);
        string snapshot = applied == null
            ? null
            : JsonSerializer.Serialize(new
            {
                clientMembershipId = applied.SourceId,
                planVersionId = applied.Rule.MembershipPlanVersionId,
                scope = applied.Rule.Scope,
                serviceId = applied.Rule.ServiceId,
                type = applied.Rule.Type,
                value = applied.Rule.Value
            }, AdjustmentJson);

        bool unchanged = participation.Amount == suggested && participation.SuggestedAmount == suggested
                         && participation.AdjustmentType == applied?.Type && participation.AdjustmentSourceId == applied?.SourceId
                         && participation.AdjustmentAmount == adjustmentAmount
                         && NormalizedEvaluation(participation.AdjustmentEvaluation) == evaluation;
        if (unchanged)
            return;

        decimal oldAmount = participation.Amount;
        ParticipationPrice.Apply(participation, new BookingPricing(
            suggested, suggested, false, participation.BaseAmount, participation.BaseAmountSource, participation.PricingMode,
            participation.PricingEmployeeId), run.Now);
        participation.AdjustmentAmount = adjustmentAmount;
        participation.AdjustmentType = applied?.Type;
        participation.AdjustmentSourceId = applied?.SourceId;
        participation.AdjustmentRuleSnapshot = snapshot;
        participation.AdjustmentEvaluation = evaluation;
        if (oldAmount == suggested)
            return;

        if (projection != null)
        {
            projection.LastPriceChangeOldAmount = oldAmount;
            projection.LastPriceChangeNewAmount = suggested;
            projection.LastPriceChangeEvent = @event;
            projection.LastPriceChangeAt = run.Now;
        }

        await _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            BookingId = participation.BookingId,
            BookingSegmentParticipationId = participation.Id,
            ChangeType = "AmountRepricedByMembership",
            OldValue = oldAmount.ToString(CultureInfo.InvariantCulture),
            NewValue = $"{suggested.ToString(CultureInfo.InvariantCulture)};{@event}",
            ChangedAt = run.Now,
            ChangedBy = run.UserId
        });
    }

    private static string NormalizedEvaluation(string stored) => stored == null
        ? null
        : JsonSerializer.Serialize(JsonSerializer.Deserialize<List<Core.DTOs.Appointments.PriceAdjustmentCandidateDto>>(stored, AdjustmentJson), AdjustmentJson);

    /// <summary>Strana članstva: cijene budućih potvrđenih rezervacija klijenta. Sudjelovanje zaključano drugom naredbom se
    /// preskače (SKIP LOCKED, bez deadlocka s prijelazom) i dobiva oznaku PriceStale — cijena se uskladi pri sljedećoj obradi.</summary>
    private async Task RepriceFuture(
        IUnitOfWork uow, Run run, List<FutureParticipation> participations, IReadOnlyCollection<ClientMembershipView> memberships,
        MembershipCoverageEvent @event)
    {
        if (participations.Count == 0)
            return;
        await uow.Context.SaveChangesAsync();

        Guid[] ids = participations.Select(p => p.Subject.ParticipationId).ToArray();
        List<Guid> lockedIds = await uow.Context.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM dunelight.booking_segment_participations WHERE id = ANY({ids}) FOR UPDATE SKIP LOCKED")
            .ToListAsync();
        Dictionary<Guid, BookingSegmentParticipation> tracked = (await uow.Context.BookingSegmentParticipations
                .Include(p => p.Booking)
                .Where(p => lockedIds.Contains(p.Id.Value))
                .ToListAsync())
            .ToDictionary(p => p.Id.Value);

        foreach (FutureParticipation future in participations)
        {
            Guid participationId = future.Subject.ParticipationId;
            ParticipationMembershipCoverage projection = await uow.Context.ParticipationMembershipCoverages.FindAsync(participationId);
            if (!tracked.TryGetValue(participationId, out BookingSegmentParticipation participation))
            {
                if (projection != null)
                    projection.PriceStale = true;
                continue;
            }

            if (participation.Status != ParticipationStatus.Confirmed)
                continue;
            await PriceParticipation(uow, run, participation, participation.Booking.AppointmentId, future.Subject, memberships,
                covered: projection?.Status == MembershipCoverageStatus.Covered, packageCovered: false, @event);
        }

        await uow.Context.SaveChangesAsync();
    }

    #endregion

    #region Ledger i projekcija

    private static MembershipUsage Claim(IUnitOfWork uow, Run run, ClientMembership membership, MembershipCoverageSubject subject)
    {
        MembershipUsage usage = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = run.OrganizationId,
            ClientMembershipId = membership.Id,
            ParticipationId = subject.ParticipationId,
            ServiceId = subject.ServiceId,
            CompanyId = subject.CompanyId,
            ServiceDate = subject.ServiceDate,
            PeriodDate = subject.PeriodDate,
            EntryType = MembershipUsageEntryType.Claim,
            Units = -1,
            IsActive = true,
            CreatedAt = run.Now,
            CreatedBy = run.UserId
        };
        uow.Context.MembershipUsages.Add(usage);
        return usage;
    }

    /// <summary>Storno (+1): claim postaje neaktivan, a storno je zaseban redak s referencom (nikad brisanje).</summary>
    private static void Release(IUnitOfWork uow, Run run, MembershipUsage claim, MembershipUsageReleaseReason reason)
    {
        claim.IsActive = false;
        claim.ReleasedAt = run.Now;
        claim.ReleaseReason = reason;
        uow.Context.MembershipUsages.Add(new MembershipUsage
        {
            Id = Guid.NewGuid(),
            OrganizationId = claim.OrganizationId,
            ClientMembershipId = claim.ClientMembershipId,
            ParticipationId = claim.ParticipationId,
            ServiceId = claim.ServiceId,
            CompanyId = claim.CompanyId,
            ServiceDate = claim.ServiceDate,
            PeriodDate = claim.PeriodDate,
            EntryType = MembershipUsageEntryType.Release,
            Units = 1,
            ReversesUsageId = claim.Id,
            IsActive = false,
            ReleasedAt = run.Now,
            ReleaseReason = reason,
            CreatedAt = run.Now,
            CreatedBy = run.UserId
        });
    }

    /// <summary>Jedini pisac projekcije; nepromijenjena odluka se ne prepisuje (događaj ostaje onaj koji ju je promijenio).</summary>
    private static async Task SetProjection(
        IUnitOfWork uow, Run run, Guid participationId, Guid? membershipId, MembershipCoverageDecision decision, Guid? usageId,
        MembershipCoverageEvent @event)
    {
        ParticipationMembershipCoverage row = await uow.Context.ParticipationMembershipCoverages.FindAsync(participationId);
        if (row == null)
        {
            row = new ParticipationMembershipCoverage { ParticipationId = participationId, OrganizationId = run.OrganizationId };
            uow.Context.ParticipationMembershipCoverages.Add(row);
        }
        else if (row.ClientMembershipId == membershipId && row.Status == decision.Status && row.Reason == decision.Reason
                 && row.ActiveUsageId == usageId && row.LimitWindow == decision.Limit?.Window && row.LimitServiceId == decision.Limit?.ServiceId
                 && row.LimitMaxUses == decision.Limit?.MaxUses && row.LimitUsed == decision.Limit?.Used
                 && row.ExpectedPeriodStartsOn == decision.ExpectedPeriodStartsOn)
        {
            return;
        }

        Apply(row, run, membershipId, decision, usageId, @event);
    }

    private static void Apply(
        ParticipationMembershipCoverage row, Run run, Guid? membershipId, MembershipCoverageDecision decision, Guid? usageId,
        MembershipCoverageEvent @event)
    {
        row.ClientMembershipId = membershipId;
        row.Status = decision.Status;
        row.Reason = decision.Reason;
        row.ChangedByEvent = @event;
        row.ActiveUsageId = usageId;
        row.LimitWindow = decision.Limit?.Window;
        row.LimitServiceId = decision.Limit?.ServiceId;
        row.LimitMaxUses = decision.Limit?.MaxUses;
        row.LimitUsed = decision.Limit?.Used;
        row.ExpectedPeriodStartsOn = decision.ExpectedPeriodStartsOn;
        row.EvaluatedAt = run.Now;
        row.EvaluatedBy = run.UserId;
    }

    private static async Task RemoveProjection(IUnitOfWork uow, Guid participationId)
    {
        ParticipationMembershipCoverage row = await uow.Context.ParticipationMembershipCoverages.FindAsync(participationId);
        if (row != null)
            uow.Context.ParticipationMembershipCoverages.Remove(row);
    }

    #endregion

    #region Čitanje

    private static async Task<MembershipUsage> ActiveClaimOf(IUnitOfWork uow, Guid participationId)
    {
        List<MembershipUsage> claims = await uow.Context.MembershipUsages
            .Where(u => u.ParticipationId == participationId && u.EntryType == MembershipUsageEntryType.Claim && u.IsActive)
            .ToListAsync();
        return claims.SingleOrDefault(u => u.IsActive);
    }

    private static async Task<List<MembershipUsage>> ActiveClaimsOf(IUnitOfWork uow, Guid membershipId)
    {
        List<MembershipUsage> claims = await uow.Context.MembershipUsages
            .Where(u => u.ClientMembershipId == membershipId && u.EntryType == MembershipUsageEntryType.Claim && u.IsActive)
            .ToListAsync();
        return claims.Where(u => u.IsActive).ToList();
    }

    private static MembershipClaimView ViewOf(MembershipUsage usage) =>
        new(usage.Id, usage.ParticipationId, usage.ServiceId, usage.ServiceDate, usage.PeriodDate);

    private static Task<bool> HasRelevantMembership(IUnitOfWork uow, Guid organizationId, Guid clientId, DateTimeOffset now)
    {
        // Dan ranije kao zaštita od razlike UTC datuma i zone organizacije (točna provjera slijedi pod "danas" organizacije).
        DateOnly floor = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        return uow.Context.ClientMemberships.AnyAsync(m => m.OrganizationId == organizationId && m.ClientId == clientId
                                                           && m.VoidedAt == null && (m.EndsOn == null || m.EndsOn >= floor));
    }

    private MembershipStanding Standing(Run run, ClientMembership membership) =>
        MembershipChargeSettlement.Standing(membership.Charges, run.Today, run.GraceDays);

    private static ParticipationExecutionContextView ViewOf(Appointment appointment, Booking booking, BookingSegmentParticipation participation)
    {
        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, booking, participation);
        return new ParticipationExecutionContextView(
            participation.Id.GetValueOrDefault(), execution.ClientId, execution.ServiceId, execution.CompanyId, execution.StartsAt,
            appointment.Id.GetValueOrDefault());
    }

    private async Task<OrganizationCalendar> CompanyCalendar(Run run, Guid companyId)
    {
        if (!run.CompanyCalendars.TryGetValue(companyId, out OrganizationCalendar calendar))
        {
            calendar = await _calendars.GetCompanyCalendar(run.OrganizationId, companyId);
            run.CompanyCalendars[companyId] = calendar;
        }

        return calendar;
    }

    private async Task<MembershipCoverageSubject> SubjectOf(IUnitOfWork uow, Run run, ParticipationExecutionContextView view, Guid participationId)
    {
        List<CheckoutItem> items = await uow.Context.CheckoutItems
            .Include(i => i.Allocations).ThenInclude(a => a.Payment)
            .Where(i => i.BookingSegmentParticipationId == participationId)
            .ToListAsync();
        return new MembershipCoverageSubject(
            participationId, view.ServiceId, view.CompanyId,
            PackageValidity.ServiceDate(await CompanyCalendar(run, view.CompanyId), view.StartsAt),
            run.OrganizationCalendar.LocalDate(view.StartsAt),
            ParticipationSettlement.SettledAmountOf(items));
    }

    private sealed record FutureParticipation(MembershipCoverageSubject Subject, DateTimeOffset StartsAt, DateTimeOffset CreatedAt);

    /// <summary>Buduća potvrđena sudjelovanja klijenta (početak segmenta nakon "sada"), redom po početku, vremenu rezervacije i Id-u.</summary>
    private async Task<List<FutureParticipation>> FutureConfirmedOf(IUnitOfWork uow, Run run, Guid clientId)
    {
        var rows = await uow.Context.BookingSegmentParticipations
            .Where(p => p.OrganizationId == run.OrganizationId && p.Booking.ClientId == clientId
                        && p.Status == ParticipationStatus.Confirmed && p.Segment.PlannedStart > run.Now)
            .Select(p => new
            {
                Id = p.Id.Value,
                p.Segment.ServiceId,
                p.Segment.Appointment.CompanyId,
                p.Segment.PlannedStart,
                p.CreatedAt
            })
            .ToListAsync();
        if (rows.Count == 0)
            return new List<FutureParticipation>();

        List<Guid> ids = rows.Select(r => r.Id).ToList();
        Dictionary<Guid, decimal> settled = (await uow.Context.CheckoutItems
                .Include(i => i.Allocations).ThenInclude(a => a.Payment)
                .Where(i => i.BookingSegmentParticipationId != null && ids.Contains(i.BookingSegmentParticipationId.Value))
                .ToListAsync())
            .GroupBy(i => i.BookingSegmentParticipationId.Value)
            .ToDictionary(g => g.Key, g => ParticipationSettlement.SettledAmountOf(g));

        List<FutureParticipation> result = new();
        foreach (var row in rows.OrderBy(r => r.PlannedStart).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id))
        {
            OrganizationCalendar companyCalendar = await CompanyCalendar(run, row.CompanyId);
            result.Add(new FutureParticipation(
                new MembershipCoverageSubject(row.Id, row.ServiceId, row.CompanyId,
                    PackageValidity.ServiceDate(companyCalendar, row.PlannedStart), run.OrganizationCalendar.LocalDate(row.PlannedStart),
                    settled.GetValueOrDefault(row.Id)),
                row.PlannedStart, row.CreatedAt));
        }

        return result;
    }

    #endregion
}
