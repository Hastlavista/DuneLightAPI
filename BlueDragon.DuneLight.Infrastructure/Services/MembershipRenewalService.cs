using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi <see cref="IMembershipRenewalService"/>.</summary>
public class MembershipRenewalService : IMembershipRenewalService
{
    private readonly IClientMembershipHandler _handler;
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<MembershipRenewalService> _logger;
    private readonly IMembershipCoverageService _coverage;
    private readonly IGroupMembershipSkipService _membershipSkips;
    private readonly TimeProvider _timeProvider;

    public MembershipRenewalService(
        IClientMembershipHandler handler,
        IOrganizationCalendarService organizationCalendarService,
        IOrganizationSettingsService organizationSettingsService,
        IUnitOfWorkFactory unitOfWorkFactory,
        ILogger<MembershipRenewalService> logger,
        IMembershipCoverageService coverage,
        IGroupMembershipSkipService membershipSkips,
        TimeProvider timeProvider)
    {
        _membershipSkips = membershipSkips;
        _handler = handler;
        _organizationCalendarService = organizationCalendarService;
        _organizationSettingsService = organizationSettingsService;
        _unitOfWorkFactory = unitOfWorkFactory;
        _logger = logger;
        _coverage = coverage;
        _timeProvider = timeProvider;
    }

    public async Task<MembershipDebtRules> GetDebtRules(Guid organizationId)
    {
        OrganizationSettingsDto settings = await _organizationSettingsService.GetSettings(organizationId);
        return new MembershipDebtRules(settings.MembershipGraceDays, settings.MembershipDebtBehavior, settings.MembershipAutoEndAfterUnpaidPeriods);
    }

    public async Task<int> RunForOrganization(Guid organizationId)
    {
        DateOnly day = (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(_timeProvider.GetUtcNow());
        MembershipDebtRules rules = await GetDebtRules(organizationId);
        int processed = 0;
        foreach (Guid id in await _handler.GetRenewalCandidateIds(organizationId, day))
        {
            try
            {
                await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
                ClientMembership membership = await _handler.GetForUpdate(uow, organizationId, id);
                if (membership == null)
                    continue;
                await CatchUp(uow, membership, day, rules, userId: null);
                await uow.Context.SaveChangesAsync();
                // 2D (Q27, Q15): dnevni prolaz pomiče horizont (oznake čekanja ulaze u evaluaciju) i provodi stanje duga
                // (istek grace perioda oslobađa buduće claimove; plaćen dug ih vraća). Idempotentno.
                await _coverage.ReconcileMembership(uow, organizationId, id, MembershipCoverageEvent.Renewal, null, day);
                await uow.CommitAsync();
                processed++;
            }
            catch (Exception ex)
            {
                // Jedno članstvo ne smije zaustaviti obnovu ostalih; idempotentno, pa sljedeći prolaz pokušava ponovno.
                _logger.LogError(ex, "Obnova članstva {MembershipId} (organizacija {OrganizationId}) nije uspjela.", id, organizationId);
            }
        }

        // P2 (Q53): klijenti čiji dug je prestao dobivaju preskočene buduće termine grupe (vlastite transakcije).
        await _membershipSkips.BackfillForOrganization(organizationId);
        // P2 (pregled 2E #4): jamstvo usklađivanja — zastarjele cijene (PriceStale) se usklađuju u svakom prolazu.
        await _coverage.RefreshStalePrices(organizationId);
        return processed;
    }

    public async Task CatchUp(IUnitOfWork uow, ClientMembership membership, DateOnly today, MembershipDebtRules rules, Guid? userId)
    {
        if (membership.VoidedAt != null)
            return;

        DateOnly horizon = today > membership.StartsOn ? today : membership.StartsOn;
        // K1-8 (bug b): članstvo "stoji" dok su sve poslovnice opsega plana neaktivne; ponovna aktivacija ga pokreće od danas.
        bool scopeActive = await ScopeHasActiveCompany(uow, membership);
        if (scopeActive && MembershipTimelines.OpenCompanyClosure(membership) is MembershipPause standstill)
            EndCompanyClosure(uow, membership, standstill, today, userId);
        SyncCurrentPeriodEnd(membership);

        for (int guard = 0; guard < 600; guard++)
        {
            ClientMembershipPeriod latest = membership.Periods.OrderByDescending(p => p.StartsOn).FirstOrDefault();
            MembershipPeriod? next = MembershipPeriodCalendar.Periods(MembershipTimelines.WithPending(membership))
                .Where(p => !p.Skipped)
                .TakeWhile(p => p.StartsOn <= horizon)
                .Where(p => latest == null || p.StartsOn > latest.StartsOn)
                .Select(p => (MembershipPeriod?)p)
                .FirstOrDefault();
            if (next == null || (membership.EndsOn.HasValue && next.Value.StartsOn > membership.EndsOn.Value))
                break;

            // K1-8: dok članstvo stoji obnova ne otvara periode ni zaduženja; na granici obnove, kad su sve poslovnice opsega
            // neaktivne, stajanje počinje (sustavna pauza od te granice, tekući period ostaje kakav jest).
            if (MembershipTimelines.OpenCompanyClosure(membership) != null)
                break;
            if (latest != null && !scopeActive)
            {
                StartCompanyClosure(uow, membership, next.Value.StartsOn, userId);
                break;
            }

            if (latest != null && !Renew(uow, membership, next.Value, today, rules, userId))
                break;

            OpenPeriod(uow, membership, MembershipTimelines.Current(membership), next.Value.StartsOn, userId);
        }
    }

    /// <summary>K1-8 — ima li opseg poslovnica verzije plana koja vrijedi za članstvo barem jednu aktivnu poslovnicu ("Sve
    /// poslovnice" = bilo koja aktivna poslovnica organizacije).</summary>
    private async Task<bool> ScopeHasActiveCompany(IUnitOfWork uow, ClientMembership membership) =>
        membership.PlanVersion.CompanyScope == MembershipCompanyScope.AllCompanies
            ? await _handler.AnyActiveCompany(uow, membership.OrganizationId)
            : membership.PlanVersion.Companies.Any(c => c.Company?.IsActive == true);

    /// <summary>K1-8 — početak stajanja: sustavna pauza od granice obnove, bez kraja (kalendarski plan: preskočeni periodi; od
    /// datuma kupnje: dani koji pomiču granice). Ne troši klijentove limite pauza.</summary>
    private void StartCompanyClosure(IUnitOfWork uow, ClientMembership membership, DateOnly boundary, Guid? userId)
    {
        MembershipPause pause = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = membership.OrganizationId,
            ClientMembershipId = membership.Id,
            Source = MembershipPauseSource.CompanyClosure,
            Kind = MembershipPlanReadModel.PeriodTerms(membership.PlanVersion).Anchor == MembershipRenewalAnchor.CalendarMonth
                ? MembershipPauseKind.SkipPeriods
                : MembershipPauseKind.Days,
            StartsOn = boundary,
            PlannedEndsOn = null,
            Reason = "Sve poslovnice plana su neaktivne.",
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedBy = userId
        };
        membership.Pauses.Add(pause);
        uow.Context.MembershipPauses.Add(pause);
        Touch(membership, userId, _timeProvider.GetUtcNow());
        _handler.AddAudit(uow, Audit(membership, userId, "CompanyClosureStarted", null, $"{pause.Id};{boundary:yyyy-MM-dd}", _timeProvider.GetUtcNow()));
    }

    /// <summary>K1-8 — kraj stajanja na dan ponovne aktivacije (today), bez naknadnog zaduživanja propuštenih perioda. Od datuma
    /// kupnje: novi period počinje danas. Kalendarski: nastavlja od 1. sljedećeg mjeseca bez zaduženja za ostatak tekućeg (osim
    /// kad je danas 1. u mjesecu); recepcija može ručno otvoriti period od danas kroz Q47 (raniji povratak). Stajanje istog dana
    /// kad je počelo se poništava.</summary>
    private void EndCompanyClosure(IUnitOfWork uow, ClientMembership membership, MembershipPause pause, DateOnly today, Guid? userId)
    {
        DateOnly lastDay = pause.Kind == MembershipPauseKind.Days || today.Day == 1
            ? today.AddDays(-1)
            : new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (lastDay < pause.StartsOn)
        {
            pause.CancelledAt = now;
            pause.CancelledBy = userId;
            pause.CancellationReason = MembershipPauseCancellationReason.Withdrawn;
        }
        else
        {
            pause.ActualEndsOn = lastDay;
        }

        pause.UpdatedAt = now;
        pause.UpdatedBy = userId;
        Touch(membership, userId, now);
        _handler.AddAudit(uow, Audit(membership, userId, "CompanyClosureEnded", $"{pause.Id};{pause.StartsOn:yyyy-MM-dd}",
            pause.ActualEndsOn.HasValue ? $"{pause.ActualEndsOn:yyyy-MM-dd}" : "Withdrawn", now));
    }

    /// <summary>Granica obnove. Vraća false kad članstvo završava (ne otvara se novi period); true kad se period otvara (s
    /// trenutnim ili upravo primijenjenim uvjetima).</summary>
    private bool Renew(IUnitOfWork uow, ClientMembership membership, MembershipPeriod next, DateOnly today, MembershipDebtRules rules, Guid? userId)
    {
        // 2C — automatski završetak nakon N neplaćenih perioda (default isključeno); dug ostaje.
        if (rules.AutoEndAfterUnpaidPeriods.HasValue
            && MembershipChargeSettlement.UnpaidPeriodsAfterGrace(membership.Charges, today, rules.GraceDays) >= rules.AutoEndAfterUnpaidPeriods.Value)
        {
            End(uow, membership, next.StartsOn.AddDays(-1), MembershipEndReason.NonPayment, userId);
            return false;
        }

        DateOnly? pendingOn = MembershipTimelines.PendingEffectiveOn(membership);
        if (pendingOn.HasValue && next.StartsOn >= pendingOn.Value)
        {
            if (membership.PendingSource == MembershipPendingChangeSource.ClientPlanChange && membership.PendingPlanVersion.Plan?.IsActive != true)
            {
                // 2C — prelazak na plan deaktiviran prije stupanja na snagu se ne primjenjuje; članstvo ostaje na starom planu.
                Guid notApplied = membership.PendingPlanVersionId.Value;
                ClearPending(membership);
                MembershipTimelines.MarkPlanUpdateNotApplied(membership, notApplied, ErrorCodes.MembershipPlanInactive, _timeProvider.GetUtcNow());
                _handler.AddAudit(uow, Audit(membership, userId, "PlanChangeNotApplied", notApplied.ToString(), null, _timeProvider.GetUtcNow(), ErrorCodes.MembershipPlanInactive));
            }
            else
            {
                ApplyPending(uow, membership, next.StartsOn, userId);
            }
        }

        // 2C — plan deaktiviran i još neaktivan na dan obnove: članstvo završava krajem tekućeg perioda.
        if (membership.Plan?.IsActive != true)
        {
            End(uow, membership, next.StartsOn.AddDays(-1), MembershipEndReason.PlanDeactivated, userId);
            return false;
        }

        return true;
    }

    private void ApplyPending(IUnitOfWork uow, ClientMembership membership, DateOnly boundary, Guid? userId)
    {
        MembershipPeriodTerms current = MembershipPlanReadModel.PeriodTerms(membership.PlanVersion);
        MembershipPeriodTerms pending = MembershipPlanReadModel.PeriodTerms(membership.PendingPlanVersion);
        bool keepAnchor = current.Anchor == MembershipRenewalAnchor.PurchaseDate && pending.Anchor == MembershipRenewalAnchor.PurchaseDate
                          && current.Interval == pending.Interval;

        string old = $"{membership.MembershipPlanId}:{membership.PlanVersionId}";
        MembershipPendingChangeSource source = membership.PendingSource.Value;

        // Pregled 2C (#8) — promjenom uvjeta se obveza ne izbjegava: dosadašnji kraj obveze postaje donja granica, a obveza novih
        // uvjeta broji se od datuma promjene. Kraj obveze = kasniji od ta dva.
        int? oldCommitment = membership.PlanVersion.MinimumCommitmentPeriods;
        if (oldCommitment.HasValue)
        {
            DateOnly oldEnd = MembershipPeriodCalendar.EndOfNthUnskippedPeriod(
                MembershipTimelines.Current(membership), oldCommitment.Value, membership.CommitmentFromOn);
            if (membership.CommitmentFloorOn == null || oldEnd > membership.CommitmentFloorOn.Value)
                membership.CommitmentFloorOn = oldEnd;
        }

        membership.CommitmentFromOn = boundary;
        membership.PlanVersion = membership.PendingPlanVersion;
        membership.PlanVersionId = membership.PendingPlanVersionId.Value;
        membership.Plan = membership.PendingPlanVersion.Plan;
        membership.MembershipPlanId = membership.PendingPlanVersion.MembershipPlanId;
        if (!keepAnchor)
            membership.TermsAnchorOn = boundary;
        ClearPending(membership);

        if (source == MembershipPendingChangeSource.ClientPlanChange)
        {
            // Novi plan: istisnuta izmjena i oznaka starog plana više nisu relevantne.
            membership.DisplacedPlanVersionId = null;
            membership.DisplacedPlanVersion = null;
            membership.DisplacedEffectiveOn = null;
            MembershipTimelines.ClearPlanUpdateNotApplied(membership);
        }

        Touch(membership, userId, _timeProvider.GetUtcNow());
        _handler.AddAudit(uow, Audit(membership, userId, "TermsApplied", old,
            $"{membership.MembershipPlanId}:{membership.PlanVersionId}@{boundary:yyyy-MM-dd}", _timeProvider.GetUtcNow(), source.ToString()));
    }

    private void OpenPeriod(IUnitOfWork uow, ClientMembership membership, MembershipTimeline timeline, DateOnly startsOn, Guid? userId)
    {
        MembershipPeriod period = MembershipPeriodCalendar.Periods(timeline).First(p => p.StartsOn == startsOn);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        ClientMembershipPeriod row = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = membership.OrganizationId,
            ClientMembershipId = membership.Id,
            StartsOn = period.StartsOn,
            EndsOn = period.EndsOn,
            PlanVersionId = membership.PlanVersionId,
            Price = membership.PlanVersion.Price,
            CreatedAt = now,
            CreatedBy = userId
        };
        membership.Periods.Add(row);
        uow.Context.ClientMembershipPeriods.Add(row);

        if (row.Price > 0m)
        {
            MembershipCharge charge = new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = membership.OrganizationId,
                ClientId = membership.ClientId,
                ClientMembershipId = membership.Id,
                PeriodId = row.Id,
                Kind = MembershipChargeKind.Period,
                Description = $"Članarina {membership.Plan?.Name} {row.StartsOn:dd.MM.yyyy.}–{row.EndsOn:dd.MM.yyyy.}",
                Amount = row.Price,
                DueOn = row.StartsOn,
                Lifecycle = MembershipChargeLifecycle.Open,
                SettlementStatus = MembershipChargeSettlementStatus.Unpaid,
                CreatedAt = now,
                CreatedBy = userId
            };
            membership.Charges.Add(charge);
            uow.Context.MembershipCharges.Add(charge);
        }

        _handler.AddAudit(uow, Audit(membership, userId, "PeriodOpened", null, $"{row.StartsOn:yyyy-MM-dd}..{row.EndsOn:yyyy-MM-dd};{row.Price}", now));
    }

    /// <summary>Kraj tekućeg (zadnjeg otvorenog) perioda prati izračun — pauza produljuje ili skraćuje period.</summary>
    private static void SyncCurrentPeriodEnd(ClientMembership membership)
    {
        ClientMembershipPeriod latest = membership.Periods.OrderByDescending(p => p.StartsOn).FirstOrDefault();
        if (latest == null || latest.StartsOn < membership.TermsAnchorOn)
            return;

        MembershipPeriod? computed = MembershipPeriodCalendar.Periods(MembershipTimelines.Current(membership))
            .TakeWhile(p => p.StartsOn <= latest.StartsOn)
            .Where(p => p.StartsOn == latest.StartsOn)
            .Select(p => (MembershipPeriod?)p)
            .FirstOrDefault();
        if (computed.HasValue && computed.Value.EndsOn != latest.EndsOn)
            latest.EndsOn = computed.Value.EndsOn;
    }

    private void End(IUnitOfWork uow, ClientMembership membership, DateOnly endsOn, MembershipEndReason reason, Guid? userId)
    {
        membership.EndsOn = endsOn;
        membership.EndReason = reason;
        Touch(membership, userId, _timeProvider.GetUtcNow());
        _handler.AddAudit(uow, Audit(membership, userId, "MembershipEnded", null, $"{endsOn:yyyy-MM-dd}", _timeProvider.GetUtcNow(), reason.ToString()));
    }

    private static void ClearPending(ClientMembership membership)
    {
        membership.PendingPlanVersionId = null;
        membership.PendingPlanVersion = null;
        membership.PendingEffectiveOn = null;
        membership.PendingSource = null;
    }

    private static void Touch(ClientMembership membership, Guid? userId, DateTimeOffset now)
    {
        membership.UpdatedAt = now;
        membership.UpdatedBy = userId;
    }

    private static ClientMembershipAuditLog Audit(ClientMembership membership, Guid? userId, string type, string oldValue, string newValue, DateTimeOffset now, string reason = null)
    {
        ClientMembershipAuditLog entry = MembershipTimelines.Audit(membership, userId.GetValueOrDefault(), type, oldValue, newValue, now, reason);
        entry.ChangedBy = userId;
        return entry;
    }
}
