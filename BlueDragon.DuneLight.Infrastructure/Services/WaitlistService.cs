using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Outbox;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi IWaitlistService (kontroler-okrenute metode) i IWaitlistPromotionService (uow-metode pozivane iz
/// tuđe transakcije) za domenske napomene — ova klasa implementira oba sučelja.
/// </summary>
public class WaitlistService : IWaitlistService, IWaitlistPromotionService
{
    private readonly IWaitlistHandler _waitlistHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IClientHandler _clientHandler;
    private readonly IGroupHandler _groupHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IPricingService _pricingService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public WaitlistService(
        IWaitlistHandler waitlistHandler,
        IAppointmentHandler appointmentHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientHandler clientHandler,
        IGroupHandler groupHandler,
        IEmployeeHandler employeeHandler,
        IPricingService pricingService,
        IOutboxWriter outboxWriter,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _waitlistHandler = waitlistHandler;
        _appointmentHandler = appointmentHandler;
        _auditLogHandler = auditLogHandler;
        _clientHandler = clientHandler;
        _groupHandler = groupHandler;
        _employeeHandler = employeeHandler;
        _pricingService = pricingService;
        _outboxWriter = outboxWriter;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    /// <summary>Isti IPricingService poziv kao BookingService/AppointmentService/GroupService — centralni
    /// resolver, ne duplicira logiku razrješavanja cijene (vidi spec section 44).</summary>
    private async Task<decimal> ResolveSuggestedAmount(Guid organizationId, Guid serviceId, Guid companyId, DateTimeOffset date)
    {
        ResolvePriceResponse resolved = await _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = serviceId,
            CompanyId = companyId,
            Date = date
        });
        return resolved.Price;
    }

    public async Task<List<WaitlistEntryDto>> GetForAppointment(Guid organizationId, Guid appointmentId)
    {
        List<WaitlistEntry> entries = await _waitlistHandler.GetForAppointment(organizationId, appointmentId);

        List<WaitlistEntry> ordered = entries
            .OrderBy(e => e.Status == WaitlistEntryStatus.Waiting ? 0 : 1)
            .ThenBy(e => e.JoinedAt)
            .ThenBy(e => e.Id)
            .ToList();

        List<WaitlistEntryDto> result = new List<WaitlistEntryDto>();
        int position = 0;
        foreach (WaitlistEntry entry in ordered)
        {
            int? entryPosition = null;
            if (entry.Status == WaitlistEntryStatus.Waiting)
            {
                position++;
                entryPosition = position;
            }

            result.Add(ToDto(entry, entryPosition));
        }

        return result;
    }

    public async Task<WaitlistEntryDto> Join(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, WaitlistJoinRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (appointment.Form != AppointmentForm.Group || appointment.Status != AppointmentStatus.Scheduled || appointment.StartsAt <= now)
            throw new BusinessRuleException(ErrorCodes.WaitlistNotAvailable, "Lista čekanja nije dostupna za ovaj termin.");

        Client client = await _clientHandler.GetByIdLight(organizationId, request.ClientId);
        if (client == null)
            throw new NotFoundAppException("Client", request.ClientId);
        if (!client.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveClient, "Klijent nije aktivan.");
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran.");

        Booking existingBooking = await _appointmentHandler.GetBooking(organizationId, appointmentId, request.ClientId);
        if (existingBooking != null && (existingBooking.Status == BookingStatus.Confirmed || existingBooking.Status == BookingStatus.Completed))
            throw new BusinessRuleException(ErrorCodes.AlreadyBooked, "Klijent već ima rezervaciju na ovom terminu.");

        WaitlistEntry activeEntry = await _waitlistHandler.GetActiveForClient(organizationId, appointmentId, request.ClientId);
        if (activeEntry != null)
            throw new BusinessRuleException(ErrorCodes.AlreadyWaitlisted, "Klijent je već na listi čekanja za ovaj termin.");

        Group group = appointment.GroupId.HasValue ? await _groupHandler.GetByIdLight(organizationId, appointment.GroupId.Value) : null;
        if (group == null)
            throw new NotFoundAppException("Group", appointment.GroupId.GetValueOrDefault());

        int confirmedCount = await _appointmentHandler.CountConfirmedBookings(organizationId, appointmentId);
        if (confirmedCount < group.Capacity)
            throw new BusinessRuleException(
                ErrorCodes.CapacityAvailable,
                "Termin ima slobodna mjesta — koristite rezervaciju umjesto liste čekanja.",
                new { capacity = group.Capacity, confirmedCount });

        WaitlistEntry entry = new WaitlistEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppointmentId = appointmentId,
            ClientId = request.ClientId,
            Status = WaitlistEntryStatus.Waiting,
            JoinedAt = now,
            CreatedAt = now
        };

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _waitlistHandler.Add(uow, entry);

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                WaitlistEntryId = entry.Id,
                ChangeType = "WaitlistJoined",
                OldValue = null,
                NewValue = request.ClientId.ToString(),
                ChangedAt = now,
                ChangedBy = userId
            });

            await uow.CommitAsync();
        }

        List<WaitlistEntryDto> all = await GetForAppointment(organizationId, appointmentId);
        return all.First(e => e.Id == entry.Id);
    }

    public async Task<WaitlistEntryDto> Cancel(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId);

        WaitlistEntry entry = await _waitlistHandler.GetMostRecentForClient(organizationId, appointmentId, clientId);
        if (entry == null)
            throw new NotFoundAppException("WaitlistEntry", clientId);

        // Idempotentno — Cancel na već terminalnom retku (Cancelled/Promoted/Expired) samo vraća trenutno stanje,
        // ne diže grešku i ne mijenja ništa (vidi spec section 18/33 po analogiji s Booking idempotencijom).
        if (entry.Status == WaitlistEntryStatus.Waiting)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
            {
                entry.Status = WaitlistEntryStatus.Cancelled;
                entry.CancelledAt = now;
                entry.UpdatedAt = now;
                await _waitlistHandler.Update(uow, entry);

                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointmentId,
                    WaitlistEntryId = entry.Id,
                    ChangeType = "WaitlistCancelled",
                    OldValue = "Waiting",
                    NewValue = "Cancelled",
                    ChangedAt = now,
                    ChangedBy = userId
                });

                await uow.CommitAsync();
            }
        }

        List<WaitlistEntryDto> all = await GetForAppointment(organizationId, appointmentId);
        return all.FirstOrDefault(e => e.Id == entry.Id) ?? ToDto(entry, null);
    }

    public async Task PromoteEligibleWaiters(IUnitOfWork uow, Guid organizationId, Guid appointmentId, Guid userId)
    {
        Appointment appointment = await _appointmentHandler.GetForUpdateWithGroup(uow, organizationId, appointmentId);
        if (appointment == null || appointment.Form != AppointmentForm.Group || appointment.Group == null)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (appointment.Status != AppointmentStatus.Scheduled || appointment.StartsAt <= now)
            return;

        int confirmedCount = await _appointmentHandler.CountConfirmedBookings(uow, organizationId, appointmentId);
        int freeSeats = appointment.Group.Capacity - confirmedCount;
        if (freeSeats <= 0)
            return;

        List<WaitlistEntry> waiting = await uow.Context.WaitlistEntries
            .Where(w => w.AppointmentId == appointmentId && w.Status == WaitlistEntryStatus.Waiting)
            .OrderBy(w => w.JoinedAt).ThenBy(w => w.Id)
            .ToListAsync();

        foreach (WaitlistEntry entry in waiting)
        {
            if (freeSeats <= 0)
                break;

            string ineligibleReason = await FindIneligibilityReason(uow, organizationId, appointment, entry);

            if (ineligibleReason != null)
            {
                entry.Status = WaitlistEntryStatus.Expired;
                entry.ExpiredReason = ineligibleReason;
                entry.UpdatedAt = now;
                await uow.Context.SaveChangesAsync();

                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointmentId,
                    WaitlistEntryId = entry.Id,
                    ChangeType = "WaitlistExpired",
                    OldValue = "Waiting",
                    NewValue = "Expired",
                    ChangedAt = now,
                    ChangedBy = userId
                });

                continue;
            }

            decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, appointment.ServiceId, appointment.CompanyId, appointment.StartsAt);

            // Obična Confirmed rezervacija od trenutka nastanka — bez paketa/plaćanja (spec section 13/45): klijent/
            // osoblje to razrješava naknadno kroz uobičajeni check-in tok (BookingService.ResolveCoverage), isto
            // kao svaki drugi Booking. Ne koristi se "slaba" posebna vrsta bookinga za promovirane retke.
            Booking booking = new Booking
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                AppointmentId = appointmentId,
                ClientId = entry.ClientId,
                Status = BookingStatus.Confirmed,
                Amount = suggestedAmount,
                SuggestedAmount = suggestedAmount,
                CreatedAt = now
            };
            uow.Context.Bookings.Add(booking);
            await uow.Context.SaveChangesAsync();

            entry.Status = WaitlistEntryStatus.Promoted;
            entry.PromotedAt = now;
            entry.PromotedBookingId = booking.Id;
            entry.UpdatedAt = now;
            await uow.Context.SaveChangesAsync();

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                BookingId = booking.Id,
                WaitlistEntryId = entry.Id,
                ChangeType = "WaitlistPromoted",
                OldValue = "Waiting",
                NewValue = "Promoted",
                ChangedAt = now,
                ChangedBy = userId
            });

            // Ista transakcija kao promocija + stvaranje Bookinga (vidi spec section 35) — rollback (npr.
            // konkurentna revalidacija koja baci iznimku niže u petlji) briše i ovaj redak zajedno s promocijom.
            await _outboxWriter.Add(
                uow, organizationId, OutboxEventTypes.WaitlistPromotedV1,
                new WaitlistPromotedEvent
                {
                    OrganizationId = organizationId,
                    WaitlistEntryId = entry.Id.GetValueOrDefault(),
                    BookingId = booking.Id.GetValueOrDefault(),
                    AppointmentId = appointmentId,
                    ClientId = entry.ClientId,
                    CompanyId = appointment.CompanyId,
                    OccurredAt = now
                },
                now,
                idempotencyKey: $"waitlist-promoted:{entry.Id.GetValueOrDefault()}");

            freeSeats--;
        }
    }

    /// <summary>Revalidacija PRIJE promocije (spec section 13/51) — sve unutar ISTE transakcije/uow.Context kao
    /// zaključani Appointment redak, tako da odluka odražava stvarno trenutno stanje. GetOverlappingForClients
    /// namjerno koristi handler (zaseban DbContext) jer provjerava DRUGE termine klijenta, ne stanje koje ova
    /// transakcija mijenja — reuse postojeće infrastrukture preklapanja umjesto duplicirane logike (section 51).</summary>
    private async Task<string> FindIneligibilityReason(IUnitOfWork uow, Guid organizationId, Appointment appointment, WaitlistEntry entry)
    {
        Client client = await uow.Context.Clients.SingleOrDefaultAsync(c => c.Id == entry.ClientId && c.OrganizationId == organizationId);
        if (client == null || !client.IsActive)
            return WaitlistExpiredReasons.ClientInactive;
        if (client.IsAnonymized)
            return WaitlistExpiredReasons.ClientAnonymized;

        bool alreadyBooked = await uow.Context.Bookings.AnyAsync(b =>
            b.AppointmentId == appointment.Id && b.ClientId == entry.ClientId &&
            b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow);
        if (alreadyBooked)
            return WaitlistExpiredReasons.AppointmentNoLongerAvailable;

        List<Appointment> overlapping = await _appointmentHandler.GetOverlappingForClients(
            organizationId, new List<Guid> { entry.ClientId }, appointment.StartsAt, appointment.DurationMinutes, excludeId: appointment.Id);
        if (overlapping.Count > 0)
            return WaitlistExpiredReasons.ClientScheduleConflict;

        return null;
    }

    public async Task ExpireWaitingForAppointment(IUnitOfWork uow, Guid organizationId, Guid appointmentId, Guid? userId, string reason)
    {
        List<WaitlistEntry> waiting = await uow.Context.WaitlistEntries
            .Where(w => w.OrganizationId == organizationId && w.AppointmentId == appointmentId && w.Status == WaitlistEntryStatus.Waiting)
            .ToListAsync();

        if (waiting.Count == 0)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (WaitlistEntry entry in waiting)
        {
            entry.Status = WaitlistEntryStatus.Expired;
            entry.ExpiredReason = reason;
            entry.UpdatedAt = now;
            await uow.Context.SaveChangesAsync();

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                WaitlistEntryId = entry.Id,
                ChangeType = "WaitlistExpired",
                OldValue = "Waiting",
                NewValue = "Expired",
                ChangedAt = now,
                ChangedBy = userId
            });
        }
    }

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool hasFullScope, Guid? appointmentEmployeeId)
    {
        if (hasFullScope)
            return;

        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || !appointmentEmployeeId.HasValue || employee.Id != appointmentEmployeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, "Trener smije upravljati samo listom čekanja na svojim vlastitim terminima.");
    }

    private static WaitlistEntryDto ToDto(WaitlistEntry entry, int? position)
    {
        return new WaitlistEntryDto
        {
            Id = entry.Id.GetValueOrDefault(),
            AppointmentId = entry.AppointmentId,
            ClientId = entry.ClientId,
            ClientName = entry.Client != null ? $"{entry.Client.FirstName} {entry.Client.LastName}" : null,
            Status = entry.Status,
            Position = position,
            JoinedAt = entry.JoinedAt,
            PromotedAt = entry.PromotedAt,
            PromotedBookingId = entry.PromotedBookingId,
            CancelledAt = entry.CancelledAt,
            ExpiredReason = entry.ExpiredReason
        };
    }
}
