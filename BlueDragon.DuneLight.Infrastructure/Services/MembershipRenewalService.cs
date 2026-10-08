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

    public MembershipRenewalService(
        IClientMembershipHandler handler,
        IOrganizationCalendarService organizationCalendarService,
        IOrganizationSettingsService organizationSettingsService,
        IUnitOfWorkFactory unitOfWorkFactory,
        ILogger<MembershipRenewalService> logger,
        IMembershipCoverageService coverage,
        IGroupMembershipSkipService membershipSkips)
    {
        _membershipSkips = membershipSkips;
        _handler = handler;
        _organizationCalendarService = organizationCalendarService;
        _organizationSettingsService = organizationSettingsService;
        _unitOfWorkFactory = unitOfWorkFactory;
        _logger = logger;
        _coverage = coverage;
    }

    public async Task<MembershipDebtRules> GetDebtRules(Guid organizationId)
    {
        OrganizationSettingsDto settings = await _organizationSettingsService.GetSettings(organizationId);
        return new MembershipDebtRules(settings.MembershipGraceDays, settings.MembershipDebtBehavior, settings.MembershipAutoEndAfterUnpaidPeriods);
    }

    public async Task<int> RunForOrganization(Guid organizationId, DateOnly? today = null)
    {
        DateOnly day = today ?? (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(DateTimeOffset.UtcNow);
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

    public Task CatchUp(IUnitOfWork uow, ClientMembership membership, DateOnly today, MembershipDebtRules rules, Guid? userId)
    {
        if (membership.VoidedAt != null)
            return Task.CompletedTask;

        DateOnly horizon = today > membership.StartsOn ? today : membership.StartsOn;
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

            if (latest != null && !Renew(uow, membership, next.Value, today, rules, userId))
                break;

            OpenPeriod(uow, membership, MembershipTimelines.Current(membership), next.Value.StartsOn, userId);
        }

        return Task.CompletedTask;
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
                MembershipTimelines.MarkPlanUpdateNotApplied(membership, notApplied, ErrorCodes.MembershipPlanInactive, DateTimeOffset.UtcNow);
                _handler.AddAudit(uow, Audit(membership, userId, "PlanChangeNotApplied", notApplied.ToString(), null, ErrorCodes.MembershipPlanInactive));
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

        Touch(membership, userId);
        _handler.AddAudit(uow, Audit(membership, userId, "TermsApplied", old,
            $"{membership.MembershipPlanId}:{membership.PlanVersionId}@{boundary:yyyy-MM-dd}", source.ToString()));
    }

    private void OpenPeriod(IUnitOfWork uow, ClientMembership membership, MembershipTimeline timeline, DateOnly startsOn, Guid? userId)
    {
        MembershipPeriod period = MembershipPeriodCalendar.Periods(timeline).First(p => p.StartsOn == startsOn);
        DateTimeOffset now = DateTimeOffset.UtcNow;
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

        _handler.AddAudit(uow, Audit(membership, userId, "PeriodOpened", null, $"{row.StartsOn:yyyy-MM-dd}..{row.EndsOn:yyyy-MM-dd};{row.Price}"));
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
        Touch(membership, userId);
        _handler.AddAudit(uow, Audit(membership, userId, "MembershipEnded", null, $"{endsOn:yyyy-MM-dd}", reason.ToString()));
    }

    private static void ClearPending(ClientMembership membership)
    {
        membership.PendingPlanVersionId = null;
        membership.PendingPlanVersion = null;
        membership.PendingEffectiveOn = null;
        membership.PendingSource = null;
    }

    private static void Touch(ClientMembership membership, Guid? userId)
    {
        membership.UpdatedAt = DateTimeOffset.UtcNow;
        membership.UpdatedBy = userId;
    }

    private static ClientMembershipAuditLog Audit(ClientMembership membership, Guid? userId, string type, string oldValue, string newValue, string reason = null)
    {
        ClientMembershipAuditLog entry = MembershipTimelines.Audit(membership, userId.GetValueOrDefault(), type, oldValue, newValue, reason);
        entry.ChangedBy = userId;
        return entry;
    }
}
