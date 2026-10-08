using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi <see cref="IGroupMembershipSkipService"/>.</summary>
public class GroupMembershipSkipService : IGroupMembershipSkipService
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly ISchedulingOccupancyHandler _schedulingOccupancyHandler;
    private readonly IPricingService _pricingService;
    private readonly IMembershipCoverageService _membershipCoverage;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<GroupMembershipSkipService> _logger;

    public GroupMembershipSkipService(
        IAppointmentHandler appointmentHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        ISchedulingOccupancyHandler schedulingOccupancyHandler,
        IPricingService pricingService,
        IMembershipCoverageService membershipCoverage,
        IUnitOfWorkFactory unitOfWorkFactory,
        ILogger<GroupMembershipSkipService> logger)
    {
        _appointmentHandler = appointmentHandler;
        _auditLogHandler = auditLogHandler;
        _schedulingOccupancyHandler = schedulingOccupancyHandler;
        _pricingService = pricingService;
        _membershipCoverage = membershipCoverage;
        _unitOfWorkFactory = unitOfWorkFactory;
        _logger = logger;
    }

    public async Task<List<GroupMembershipSkipDto>> GetForGroup(Guid organizationId, Guid groupId)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        var rows = await (from skip in uow.Context.GroupOccurrenceMembershipSkips.AsNoTracking()
                          join segment in uow.Context.AppointmentSegments.AsNoTracking() on skip.AppointmentSegmentId equals segment.Id
                          where skip.OrganizationId == organizationId && skip.GroupId == groupId
                          select new { skip, segment.PlannedStart })
            .ToListAsync();
        return rows.OrderBy(r => r.PlannedStart).ThenBy(r => r.skip.ClientId)
            .Select(r => GroupMembershipSkips.ToDto(r.skip, r.PlannedStart)).ToList();
    }

    public async Task<int> BackfillForOrganization(Guid organizationId)
    {
        List<Guid> clientIds;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            clientIds = await uow.Context.GroupOccurrenceMembershipSkips
                .Where(s => s.OrganizationId == organizationId && s.Resolution == null)
                .Select(s => s.ClientId).Distinct().ToListAsync();
        }

        int added = 0;
        foreach (Guid clientId in clientIds)
        {
            try
            {
                added += await BackfillForClient(organizationId, clientId, null);
            }
            catch (Exception ex)
            {
                // Jedan klijent ne smije zaustaviti ostale; idempotentno, sljedeći prolaz pokušava ponovno.
                _logger.LogError(ex, "Naknadno dodavanje preskočenih termina klijenta {ClientId} nije uspjelo.", clientId);
            }
        }

        return added;
    }

    public async Task<int> BackfillForClient(Guid organizationId, Guid clientId, Guid? userId)
    {
        List<(Guid SkipId, DateTimeOffset PlannedStart)> open;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            open = (await (from skip in uow.Context.GroupOccurrenceMembershipSkips
                           join segment in uow.Context.AppointmentSegments on skip.AppointmentSegmentId equals segment.Id
                           where skip.OrganizationId == organizationId && skip.ClientId == clientId && skip.Resolution == null
                           select new { skip.Id, segment.PlannedStart })
                    .ToListAsync())
                .OrderBy(r => r.PlannedStart).ThenBy(r => r.Id)
                .Select(r => (r.Id, r.PlannedStart))
                .ToList();
        }

        int added = 0;
        foreach ((Guid skipId, _) in open)
        {
            SkipOutcome outcome = await TryAdd(organizationId, skipId, userId);
            if (outcome == SkipOutcome.StillBlocked)
                break;
            if (outcome == SkipOutcome.Added)
                added++;
        }

        return added;
    }

    private enum SkipOutcome
    {
        Added,
        Resolved,
        StillBlocked
    }

    private async Task<SkipOutcome> TryAdd(Guid organizationId, Guid skipId, Guid? userId)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        GroupOccurrenceMembershipSkip skip = await uow.Context.GroupOccurrenceMembershipSkips.SingleAsync(s => s.Id == skipId);
        if (skip.Resolution != null)
            return SkipOutcome.Resolved;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Appointment preloaded = await _appointmentHandler.GetWithBookingsForMutation(organizationId, skip.AppointmentId);
        AppointmentSegment segment = preloaded?.Segments.SingleOrDefault(s => s.Id == skip.AppointmentSegmentId);
        bool stillMember = await uow.Context.GroupMembers.AnyAsync(m => m.GroupId == skip.GroupId && m.ClientId == skip.ClientId && m.IsActive);
        if (segment == null || preloaded.Status == AppointmentStatus.Cancelled || segment.PlannedStart <= now || !stillMember)
            return await Resolve(uow, skip, GroupMembershipSkipResolution.NotApplicable, null);

        if (await _membershipCoverage.BlockingMembership(uow, organizationId, skip.ClientId, segment.ServiceId, preloaded.CompanyId, segment.PlannedStart) != null)
            return SkipOutcome.StillBlocked;

        if (preloaded.Bookings.Any(b => b.ClientId == skip.ClientId && b.Participations.Any(p => p.AppointmentSegmentId == segment.Id)))
            return await Resolve(uow, skip, GroupMembershipSkipResolution.AlreadyParticipating, null);

        try
        {
            // Isti redoslijed lockova kao rezervacija: subjekti rasporeda (klijent, prostorija) → termin → članstvo.
            await SchedulingConflictGuard.ClaimParticipationActivation(_schedulingOccupancyHandler, uow, organizationId,
                SegmentClaim.ForParticipationActivation(preloaded, segment, skip.ClientId, Array.Empty<ResourceClaim>()));
        }
        catch (BusinessRuleException)
        {
            return await Resolve(uow, skip, GroupMembershipSkipResolution.Conflict, null);
        }

        try
        {
            await GroupCapacityGuard.EnsureAvailable(_appointmentHandler, uow, organizationId, skip.AppointmentId, segment.Id.GetValueOrDefault(), false);
        }
        catch (BusinessRuleException)
        {
            return await Resolve(uow, skip, GroupMembershipSkipResolution.CapacityFull, null);
        }

        Appointment locked = await _appointmentHandler.GetForSegmentMutation(uow, organizationId, skip.AppointmentId)
            ?? throw new NotFoundAppException("Appointment", skip.AppointmentId);
        AppointmentSegment lockedSegment = locked.Segments.Single(s => s.Id == segment.Id);
        ResolvePriceResponse price = await _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = lockedSegment.ServiceId,
            CompanyId = locked.CompanyId,
            EmployeeId = SegmentPricingSource.PricingEmployeeOf(lockedSegment),
            Date = lockedSegment.PlannedStart
        });

        Booking booking = locked.Bookings.FirstOrDefault(b => b.ClientId == skip.ClientId);
        BookingSegmentParticipation participation;
        if (booking == null)
        {
            booking = BookingFactory.CreateConfirmed(organizationId, lockedSegment, skip.ClientId, BookingPricing.AtSuggested(price), now);
            uow.Context.Bookings.Add(booking);
            participation = booking.Participations.Single();
        }
        else
        {
            participation = BookingFactory.AddParticipation(booking, lockedSegment, ParticipationStatus.Confirmed, BookingPricing.AtSuggested(price), now);
            uow.Context.BookingSegmentParticipations.Add(participation);
        }
        await uow.Context.SaveChangesAsync();
        if (!locked.Bookings.Contains(booking))
            locked.Bookings.Add(booking);

        await _membershipCoverage.SyncParticipation(uow, organizationId, userId, locked, booking, participation,
            MembershipCoverageEvent.DebtChanged, MembershipCoverageMode.Automatic);
        await _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(), AppointmentId = skip.AppointmentId, BookingId = booking.Id, BookingSegmentParticipationId = participation.Id,
            ChangeType = "MembershipSkipBackfilled", OldValue = skip.Id.ToString(), ChangedAt = now, ChangedBy = userId
        });
        await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, skip.AppointmentId, userId ?? Guid.Empty);
        await Resolve(uow, skip, GroupMembershipSkipResolution.Added, participation.Id);
        return SkipOutcome.Added;
    }

    private static async Task<SkipOutcome> Resolve(
        IUnitOfWork uow, GroupOccurrenceMembershipSkip skip, GroupMembershipSkipResolution resolution, Guid? participationId)
    {
        skip.Resolution = resolution;
        skip.ResolvedAt = DateTimeOffset.UtcNow;
        skip.ParticipationId = participationId;
        await uow.Context.SaveChangesAsync();
        await uow.CommitAsync();
        return resolution == GroupMembershipSkipResolution.Added ? SkipOutcome.Added : SkipOutcome.Resolved;
    }
}
