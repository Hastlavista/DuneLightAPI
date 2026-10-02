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
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi IWaitlistService (kontroler-okrenute metode) i IWaitlistPromotionService (uow-metode pozivane iz
/// tuđe transakcije) za domenske napomene — ova klasa implementira oba sučelja.
/// </summary>
public class WaitlistService : IWaitlistService, IWaitlistPromotionService
{
    private const string NotOwnerMessage = "Trener smije upravljati samo listom čekanja na svojim vlastitim terminima.";

    private readonly IWaitlistHandler _waitlistHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly ISchedulingOccupancyHandler _schedulingOccupancyHandler;
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
        ISchedulingOccupancyHandler schedulingOccupancyHandler,
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
        _schedulingOccupancyHandler = schedulingOccupancyHandler;
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
    /// <remarks>Phase D3B2: vraća cijelo razrješavanje (Price + Source) — Source je istinit snapshot za
    /// BookingSegmentParticipation.BaseAmountSource (vidi BookingPricing.FromResolution).</remarks>
    private Task<ResolvePriceResponse> ResolveServicePrice(Guid organizationId, Guid serviceId, Guid companyId, DateTimeOffset date)
    {
        return _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = serviceId,
            CompanyId = companyId,
            Date = date
        });
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

        // Phase M1E: lista čekanja je samo za grupe (individualni/višesegmentni termin: WAITLIST_NOT_AVAILABLE, ne greška
        // integriteta) — provjera forme PRIJE razrješavanja grupnog segmenta.
        if (appointment.Form != AppointmentForm.Group)
            throw new BusinessRuleException(ErrorCodes.WaitlistNotAvailable, "Lista čekanja nije dostupna za ovaj termin.");

        // Lista čekanja je po (jedinom grupnom) segmentu occurrencea — razriješen na ovoj granici; vlasništvo, početak i
        // kapacitet slijede TAJ segment.
        AppointmentSegment segment = SingleGroupSegment.Of(appointment);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, new[] { segment }, NotOwnerMessage);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        // Phase M1A/M1A.1: lista čekanja je po occurrenceu/segmentu. Nedostupna je za EKSPLICITNO otkazanu sesiju
        // (Cancelled); occurrence čiji su članovi pojedinačno otkazali ostaje Scheduled, a onaj čiji je jedini član izostao
        // (Closed) i dalje odlučuje provjera kapaciteta segmenta niže (CAPACITY_AVAILABLE) — kao i prije.
        if (appointment.Form != AppointmentForm.Group || appointment.Status == AppointmentStatus.Cancelled ||
            segment.PlannedStart <= now)
            throw new BusinessRuleException(ErrorCodes.WaitlistNotAvailable, "Lista čekanja nije dostupna za ovaj termin.");

        Client client = await _clientHandler.GetByIdLight(organizationId, request.ClientId);
        if (client == null)
            throw new NotFoundAppException("Client", request.ClientId);
        if (!client.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveClient, "Klijent nije aktivan.");
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran.");

        // Phase M0: "već rezerviran" = klijentovo sudjelovanje na (jedinom grupnom) segmentu zauzima raspored.
        Booking existingBooking = await _appointmentHandler.GetBooking(organizationId, appointmentId, request.ClientId);
        if (existingBooking != null && existingBooking.Participations.Any(p =>
                p.AppointmentSegmentId == segment.Id && ParticipationOccupancy.Occupies(p.Status)))
            throw new BusinessRuleException(ErrorCodes.AlreadyBooked, "Klijent već ima rezervaciju na ovom terminu.");

        WaitlistEntry activeEntry = await _waitlistHandler.GetActiveForClient(organizationId, appointmentId, request.ClientId);
        if (activeEntry != null)
            throw new BusinessRuleException(ErrorCodes.AlreadyWaitlisted, "Klijent je već na listi čekanja za ovaj termin.");

        Group group = appointment.GroupId.HasValue ? await _groupHandler.GetByIdLight(organizationId, appointment.GroupId.Value) : null;
        if (group == null)
            throw new NotFoundAppException("Group", appointment.GroupId.GetValueOrDefault());

        int confirmedCount = await _appointmentHandler.CountConfirmedOnSegment(organizationId, segment.Id.GetValueOrDefault());
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
        if (appointment.Form != AppointmentForm.Group)
            throw new BusinessRuleException(ErrorCodes.WaitlistNotAvailable, "Lista čekanja nije dostupna za ovaj termin.");

        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope,
            new[] { SingleGroupSegment.Of(appointment) }, NotOwnerMessage);

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
        // Phase M1A: Scheduled = termin ima barem jedno Confirmed sudjelovanje ILI se tek izvodi — pozivatelji (otkazivanje
        // sudjelovanja, uklanjanje člana) pozivaju promociju PRIJE ponovnog izvođenja statusa, pa occurrence čije je zadnje
        // aktivno sudjelovanje upravo otkazano ovdje još vidi Scheduled i promovira (isto kao prije).
        AppointmentSegment segment = SingleGroupSegment.Of(appointment); // grupni occurrence: jedan segment
        if (appointment.Status != AppointmentStatus.Scheduled || segment.PlannedStart <= now)
            return;

        int confirmedCount = await _appointmentHandler.CountConfirmedOnSegment(uow, organizationId, segment.Id.GetValueOrDefault());
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

            // Phase M1C: promocija se izvršava POD Appointment lockom (kasnije u globalnom redoslijedu od subjekata rasporeda),
            // pa klijenta zaključava samo NEBLOKIRAJUĆE — nikad ne čeka, pa ne može zatvoriti ciklus. Ako klijentov raspored
            // upravo mijenja druga transakcija, promocija se odgađa (FIFO: ni kasniji čekatelji ne preskaču ovog) — čekatelj
            // ostaje Waiting za sljedeću priliku (sljedeće oslobađanje mjesta).
            if (!await _schedulingOccupancyHandler.TryLockClientSchedule(uow, entry.ClientId))
                break;
            // Phase M1D: prostorija — isto neblokirajuće; zauzeta prostorija odgađa promociju jednako kao zauzet klijent.
            if (segment.RoomId.HasValue && !await _schedulingOccupancyHandler.TryLockRoomSchedule(uow, segment.RoomId.Value))
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

            // Phase M1D: fizički kapacitet prostorije (osobe) je TVRD — bez mjesta u prostoriji nema promocije (čekatelj ostaje
            // Waiting; ni kasniji ne preskaču red). Nije isto što i Group.Capacity (poslovni broj mjesta, provjeren gore).
            if (segment.RoomId.HasValue && (await SchedulingConflictGuard.FindCapacityViolations(
                    _schedulingOccupancyHandler, uow, organizationId,
                    new[] { SegmentClaim.ForParticipationActivation(appointment, segment, entry.ClientId, Array.Empty<ResourceClaim>()) })).Count > 0)
                break;

            SegmentExecutionContext execution = ExecutionContextResolver.ForSegment(appointment, segment);
            ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, execution.ServiceId, execution.CompanyId, execution.StartsAt);

            // Obična Confirmed rezervacija od trenutka nastanka — bez paketa/plaćanja (spec section 13/45): klijent/
            // osoblje to razrješava naknadno kroz uobičajeni check-in tok (BookingService.ResolveCoverage), isto
            // kao svaki drugi Booking. Ne koristi se "slaba" posebna vrsta bookinga za promovirane retke.
            Booking booking = BookingFactory.CreateConfirmed(
                organizationId, segment, entry.ClientId, BookingPricing.AtSuggested(resolvedPrice), now);
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
    /// zaključani Appointment redak (i zaključan klijentov raspored), tako da odluka odražava stvarno trenutno stanje.
    /// Phase M1C: sudar se traži nad SEGMENTIMA (uključivo sestrinske segmente istog termina), unutar ove transakcije.</summary>
    private async Task<string> FindIneligibilityReason(IUnitOfWork uow, Guid organizationId, Appointment appointment, WaitlistEntry entry)
    {
        Client client = await uow.Context.Clients.SingleOrDefaultAsync(c => c.Id == entry.ClientId && c.OrganizationId == organizationId);
        if (client == null || !client.IsActive)
            return WaitlistExpiredReasons.ClientInactive;
        if (client.IsAnonymized)
            return WaitlistExpiredReasons.ClientAnonymized;

        AppointmentSegment segment = SingleGroupSegment.Of(appointment); // grupni occurrence: jedan segment
        Guid segmentId = segment.Id.GetValueOrDefault();
        bool alreadyBooked = await uow.Context.BookingSegmentParticipations
            .Where(p => p.AppointmentSegmentId == segmentId && p.Booking.ClientId == entry.ClientId)
            .AnyAsync(ParticipationOccupancy.OccupiesSchedule);
        if (alreadyBooked)
            return WaitlistExpiredReasons.AppointmentNoLongerAvailable;

        List<OccupancySlot> overlapping = await _schedulingOccupancyHandler.GetOverlappingForClients(
            uow, organizationId, new[] { entry.ClientId }, segment.PlannedStart, segment.PlannedEnd, excludedSegmentIds: null);
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
