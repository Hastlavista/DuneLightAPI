using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Outbox;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class GroupService : IGroupService
{
    private readonly IGroupHandler _groupHandler;
    private readonly IGroupAuditLogHandler _auditLogHandler;
    private readonly IAppointmentAuditLogHandler _appointmentAuditLogHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly IServiceAvailabilityService _serviceAvailabilityService;
    private readonly IPricingService _pricingService;
    private readonly ICompanyHandler _companyHandler;
    private readonly IRoomHandler _roomHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IClientHandler _clientHandler;
    private readonly ICompanyHolidayHandler _companyHolidayHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly IWorkingHoursTemplateHandler _workingHoursTemplateHandler;
    private readonly IScheduleBreakHandler _scheduleBreakHandler;
    private readonly IWaitlistPromotionService _waitlistPromotionService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public GroupService(
        IGroupHandler groupHandler,
        IGroupAuditLogHandler auditLogHandler,
        IAppointmentAuditLogHandler appointmentAuditLogHandler,
        IServiceHandler serviceHandler,
        IServiceAvailabilityService serviceAvailabilityService,
        IPricingService pricingService,
        ICompanyHandler companyHandler,
        IRoomHandler roomHandler,
        IEmployeeHandler employeeHandler,
        IClientHandler clientHandler,
        ICompanyHolidayHandler companyHolidayHandler,
        IAppointmentHandler appointmentHandler,
        IRosterEntryHandler rosterEntryHandler,
        IWorkingHoursTemplateHandler workingHoursTemplateHandler,
        IScheduleBreakHandler scheduleBreakHandler,
        IWaitlistPromotionService waitlistPromotionService,
        IOutboxWriter outboxWriter,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _groupHandler = groupHandler;
        _auditLogHandler = auditLogHandler;
        _appointmentAuditLogHandler = appointmentAuditLogHandler;
        _serviceHandler = serviceHandler;
        _serviceAvailabilityService = serviceAvailabilityService;
        _pricingService = pricingService;
        _companyHandler = companyHandler;
        _roomHandler = roomHandler;
        _employeeHandler = employeeHandler;
        _clientHandler = clientHandler;
        _companyHolidayHandler = companyHolidayHandler;
        _appointmentHandler = appointmentHandler;
        _rosterEntryHandler = rosterEntryHandler;
        _workingHoursTemplateHandler = workingHoursTemplateHandler;
        _scheduleBreakHandler = scheduleBreakHandler;
        _waitlistPromotionService = waitlistPromotionService;
        _outboxWriter = outboxWriter;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    /// <summary>Isti IPricingService poziv kao AppointmentService.ResolveSuggestedAmount — centralni resolver,
    /// ne duplicira logiku razrješavanja cijene.</summary>
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

    public async Task<GroupDto> Create(Guid organizationId, Guid userId, GroupCreateRequest request)
    {
        if (request.Slots == null || request.Slots.Count == 0)
            throw new ValidationAppException("Grupa mora imati barem jedan slot.");

        foreach (GroupSlotCreateRequest slot in request.Slots)
            ValidateSlotTime(slot.StartTime);

        ServiceEntity service = await EnsureServiceExists(organizationId, request.ServiceId);
        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.DefaultTrainerId);
        await EnsureRoomExists(organizationId, request.CompanyId, request.DefaultRoomId);

        List<(DayOfWeek DayOfWeek, TimeSpan StartTime)> slotTuples =
            request.Slots.Select(s => (s.DayOfWeek, s.StartTime)).ToList();

        await EnsureNoRoomSlotConflict(
            organizationId, excludeGroupId: null, request.DefaultRoomId, service.DefaultDurationMinutes, slotTuples);
        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, request.CompanyId, request.DefaultTrainerId, service.DefaultDurationMinutes, slotTuples);

        Guid groupId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Group group = new Group
        {
            Id = groupId,
            OrganizationId = organizationId,
            Name = request.Name,
            ServiceId = request.ServiceId,
            CompanyId = request.CompanyId,
            Capacity = request.Capacity,
            DefaultTrainerId = request.DefaultTrainerId,
            DefaultRoomId = request.DefaultRoomId,
            IsActive = true,
            Note = request.Note,
            CreatedAt = now,
            CreatedBy = userId
        };

        group.Slots = request.Slots.Select(s => new GroupSlot
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            DayOfWeek = s.DayOfWeek,
            StartTime = s.StartTime,
            IsActive = true,
            CreatedAt = now
        }).ToList();

        await _groupHandler.Add(group);
        GroupDto dto = await GetDtoById(organizationId, groupId);
        dto.Warnings.AddRange(warnings);
        return dto;
    }

    public async Task<GroupDto> Update(Guid organizationId, Guid userId, Guid id, GroupUpdateRequest request)
    {
        Group existing = await _groupHandler.GetByIdLight(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Group", id);

        ServiceEntity service = await EnsureServiceExists(organizationId, request.ServiceId);
        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.DefaultTrainerId);
        await EnsureRoomExists(organizationId, request.CompanyId, request.DefaultRoomId);

        Group fullExisting = await _groupHandler.GetById(organizationId, id);
        List<(DayOfWeek DayOfWeek, TimeSpan StartTime)> slotTuples = fullExisting.Slots
            .Where(s => s.IsActive).Select(s => (s.DayOfWeek, s.StartTime)).ToList();

        await EnsureNoRoomSlotConflict(
            organizationId, excludeGroupId: id, request.DefaultRoomId, service.DefaultDurationMinutes, slotTuples);
        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, request.CompanyId, request.DefaultTrainerId, service.DefaultDurationMinutes, slotTuples);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        bool trainerChanged = existing.DefaultTrainerId != request.DefaultTrainerId;
        bool capacityChanged = existing.Capacity != request.Capacity;
        string oldTrainerId = existing.DefaultTrainerId?.ToString();
        string oldCapacity = existing.Capacity.ToString();

        existing.Name = request.Name;
        existing.ServiceId = request.ServiceId;
        existing.CompanyId = request.CompanyId;
        existing.Capacity = request.Capacity;
        existing.DefaultTrainerId = request.DefaultTrainerId;
        existing.DefaultRoomId = request.DefaultRoomId;
        existing.Note = request.Note;
        existing.UpdatedAt = now;
        existing.UpdatedBy = userId;

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _groupHandler.UpdateScalar(uow, existing);

            if (trainerChanged)
            {
                await _auditLogHandler.Add(uow, new GroupAuditLog
                {
                    Id = Guid.NewGuid(),
                    GroupId = id,
                    ChangeType = "DefaultTrainer",
                    OldValue = oldTrainerId,
                    NewValue = request.DefaultTrainerId?.ToString(),
                    ChangedAt = now,
                    ChangedBy = userId
                });
            }

            if (capacityChanged)
            {
                await _auditLogHandler.Add(uow, new GroupAuditLog
                {
                    Id = Guid.NewGuid(),
                    GroupId = id,
                    ChangeType = "Capacity",
                    OldValue = oldCapacity,
                    NewValue = request.Capacity.ToString(),
                    ChangedAt = now,
                    ChangedBy = userId
                });
            }

            await uow.CommitAsync();
        }

        GroupDto dto = await GetDtoById(organizationId, id);
        dto.Warnings.AddRange(warnings);
        return dto;
    }

    public async Task<GroupDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Group existing = await _groupHandler.GetByIdLight(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Group", id);

        if (existing.IsActive == isActive)
            return await GetDtoById(organizationId, id);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool wasActive = existing.IsActive;
        existing.IsActive = isActive;
        existing.UpdatedAt = now;
        existing.UpdatedBy = userId;

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _groupHandler.UpdateScalar(uow, existing);

            await _auditLogHandler.Add(uow, new GroupAuditLog
            {
                Id = Guid.NewGuid(),
                GroupId = id,
                ChangeType = "Active",
                OldValue = wasActive ? "true" : "false",
                NewValue = isActive ? "true" : "false",
                ChangedAt = now,
                ChangedBy = userId
            });

            // Deaktivacija grupe NE dira već generirane termine niti njihove Bookinge (spec: generirani
            // Appointment je samostalan snapshot/kalendarska instanca čim je materijaliziran) — samo
            // zaustavlja buduće generiranje (GenerateAppointments preskače neaktivne grupe) i blokira nove
            // GroupMembere. Ako osoblje želi ukloniti buduće termine s kalendara, to ide kroz eksplicitan
            // Appointment cancel/delete (AppointmentService.Cancel/Delete), ne automatski ovdje.
            await uow.CommitAsync();
        }

        return await GetDtoById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Group existing = await _groupHandler.GetByIdLight(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Group", id);

        bool isReferenced = await _groupHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(
                ErrorCodes.ReferencedCannotDelete,
                "Grupa je generirala termine ili ima članove i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _groupHandler.Delete(existing);
    }

    public async Task<GroupDetailDto> GetById(Guid organizationId, Guid id)
    {
        Group group = await _groupHandler.GetById(organizationId, id);
        if (group == null)
            throw new NotFoundAppException("Group", id);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<Appointment> appointments = await _groupHandler.GetAppointmentsForGroup(
            organizationId, id, now.AddMonths(-3), now.AddMonths(3));

        GroupDetailDto dto = new GroupDetailDto();
        MapGroup(group, dto);
        dto.Members = group.Members.Where(m => m.IsActive).Select(ToMemberDto).ToList();
        int expectedCount = group.Members.Count(m => m.IsActive);
        dto.UpcomingAppointments = appointments.Where(a => a.StartsAt >= now)
            .OrderBy(a => a.StartsAt).Select(a => ToScheduleCellDto(a, group.Name, expectedCount)).ToList();
        dto.PastAppointments = appointments.Where(a => a.StartsAt < now)
            .OrderByDescending(a => a.StartsAt).Select(a => ToScheduleCellDto(a, group.Name, expectedCount)).ToList();

        return dto;
    }

    public async Task<List<GroupDto>> GetAll(Guid organizationId, bool? isActive)
    {
        List<Group> groups = await _groupHandler.GetAll(organizationId, isActive);
        return groups.Select(g =>
        {
            GroupDto dto = new GroupDto();
            MapGroup(g, dto);
            return dto;
        }).ToList();
    }

    public async Task<GroupDto> AddSlot(Guid organizationId, Guid userId, Guid groupId, GroupSlotCreateRequest request)
    {
        Group group = await _groupHandler.GetByIdLight(organizationId, groupId);
        if (group == null)
            throw new NotFoundAppException("Group", groupId);

        ValidateSlotTime(request.StartTime);

        ServiceEntity service = await _serviceHandler.GetById(organizationId, group.ServiceId);
        List<(DayOfWeek DayOfWeek, TimeSpan StartTime)> slotTuple =
            new() { (request.DayOfWeek, request.StartTime) };

        await EnsureNoRoomSlotConflict(
            organizationId, excludeGroupId: groupId, group.DefaultRoomId, service.DefaultDurationMinutes, slotTuple);
        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, group.CompanyId, group.DefaultTrainerId, service.DefaultDurationMinutes, slotTuple);

        await _groupHandler.AddSlot(new GroupSlot
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });

        GroupDto dto = await GetDtoById(organizationId, groupId);
        dto.Warnings.AddRange(warnings);
        return dto;
    }

    public async Task<GroupDto> UpdateSlot(Guid organizationId, Guid userId, Guid groupId, Guid slotId, GroupSlotUpdateRequest request)
    {
        Group group = await _groupHandler.GetByIdLight(organizationId, groupId);
        if (group == null)
            throw new NotFoundAppException("Group", groupId);

        GroupSlot slot = await _groupHandler.GetSlotById(organizationId, groupId, slotId);
        if (slot == null)
            throw new NotFoundAppException("GroupSlot", slotId);

        ValidateSlotTime(request.StartTime);

        ServiceEntity service = await _serviceHandler.GetById(organizationId, group.ServiceId);
        List<(DayOfWeek DayOfWeek, TimeSpan StartTime)> slotTuple =
            new() { (request.DayOfWeek, request.StartTime) };

        await EnsureNoRoomSlotConflict(
            organizationId, excludeGroupId: groupId, group.DefaultRoomId, service.DefaultDurationMinutes, slotTuple);
        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, group.CompanyId, group.DefaultTrainerId, service.DefaultDurationMinutes, slotTuple);

        // Izmjena ne dira već generirane termine (GroupSlotId ostaje isti) — utječe samo na sljedeća generiranja.
        slot.DayOfWeek = request.DayOfWeek;
        slot.StartTime = request.StartTime;
        await _groupHandler.UpdateSlot(slot);

        GroupDto dto = await GetDtoById(organizationId, groupId);
        dto.Warnings.AddRange(warnings);
        return dto;
    }

    public async Task<GroupDto> RemoveSlot(Guid organizationId, Guid userId, Guid groupId, Guid slotId)
    {
        await EnsureGroupExists(organizationId, groupId);

        GroupSlot slot = await _groupHandler.GetSlotById(organizationId, groupId, slotId);
        if (slot == null)
            throw new NotFoundAppException("GroupSlot", slotId);

        if (!slot.IsActive)
            return await GetDtoById(organizationId, groupId);

        int activeSlots = await _groupHandler.CountActiveSlots(groupId);
        if (activeSlots <= 1)
            throw new BusinessRuleException(ErrorCodes.LastActiveSlot, "Grupa mora imati barem jedan aktivan slot.");

        slot.IsActive = false;
        await _groupHandler.UpdateSlot(slot);

        return await GetDtoById(organizationId, groupId);
    }

    public async Task<GroupDto> AddMember(Guid organizationId, Guid userId, Guid groupId, GroupMemberAddRequest request)
    {
        Group group = await _groupHandler.GetByIdLight(organizationId, groupId);
        if (group == null)
            throw new NotFoundAppException("Group", groupId);

        Client client = await _clientHandler.GetByIdLight(organizationId, request.ClientId);
        if (client == null)
            throw new NotFoundAppException("Client", request.ClientId);

        // Isti obrazac kao BookingService/CheckoutService/ClientPackageService/WaitlistService — anonimizirani
        // ili neaktivni klijent ne smije ući u novu aktivnu poslovnu relaciju (ovdje: grupno članstvo).
        if (!client.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveClient, "Klijent nije aktivan.");
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran.");

        GroupMember existingActive = await _groupHandler.GetActiveMember(organizationId, groupId, request.ClientId);
        if (existingActive != null)
            throw new BusinessRuleException(ErrorCodes.AlreadyMember, "Klijent je već aktivan član ove grupe.");

        // Tvrda blokada kapaciteta (spec section 18/19) — zamjenjuje staro ponašanje "upozorenje nakon upisa".
        // Nema override-a u ovoj fazi (section 19 dopušta odgoditi eksplicitni admin override).
        int activeMembersBefore = await _groupHandler.CountActiveMembers(groupId);
        if (activeMembersBefore >= group.Capacity)
            throw new BusinessRuleException(
                ErrorCodes.GroupCapacityReached, "Grupa je popunjena — kapacitet je dosegnut.",
                new { capacity = group.Capacity, activeMemberCount = activeMembersBefore });

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _groupHandler.AddMember(uow, new GroupMember
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                ClientId = request.ClientId,
                JoinedAt = now,
                IsActive = true,
                CreatedAt = now
            });

            await _auditLogHandler.Add(uow, new GroupAuditLog
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                ChangeType = "MemberAdded",
                OldValue = null,
                NewValue = request.ClientId.ToString(),
                ChangedAt = now,
                ChangedBy = userId
            });

            // Novi član odmah dobiva Confirmed Booking na SVIM već generiranim budućim terminima grupe — bez
            // ovoga bi ostao "nevidljiv" na terminima generiranim prije nego se pridružio (vidi spec section 11/37).
            List<Appointment> futureAppointments = await _appointmentHandler.GetFutureScheduledForGroup(uow, organizationId, groupId);

            // Klijent ne smije završiti dvostruko zakazan (spec section 17) — provjera PRIJE ijednog Bookinga,
            // cijela operacija (uklj. samo članstvo) abortira atomično (uow se baca bez commit-a = rollback) ako
            // ijedan budući occurrence sudara s postojećim aktivnim Bookingom klijenta na BILO KOJEM terminu.
            List<RecurringConflictDetail> conflicts = new List<RecurringConflictDetail>();
            foreach (Appointment futureAppointment in futureAppointments)
            {
                if (futureAppointment.Bookings.Any(b => b.ClientId == request.ClientId))
                    continue;

                List<Appointment> overlapping = await _appointmentHandler.GetOverlappingForClients(
                    organizationId, new List<Guid> { request.ClientId },
                    futureAppointment.StartsAt, futureAppointment.DurationMinutes, excludeId: futureAppointment.Id);

                if (overlapping.Count > 0)
                    conflicts.Add(new RecurringConflictDetail
                    {
                        Date = futureAppointment.StartsAt,
                        Reason = ErrorCodes.RecurringConflictReasonAppointment
                    });
            }

            if (conflicts.Count > 0)
                throw new BusinessRuleException(
                    ErrorCodes.RecurringConflict,
                    "Klijent je već zakazan u vrijeme jednog ili više budućih termina ove grupe.",
                    new { conflicts });

            foreach (Appointment futureAppointment in futureAppointments)
            {
                if (futureAppointment.Bookings.Any(b => b.ClientId == request.ClientId))
                    continue;

                // Roster-provjera iznad (activeMembersBefore >= group.Capacity) NE vidi Confirmed Bookinge gostiju
                // koji nisu (više) aktivni članovi — npr. klijent promoviran s liste čekanja ili dodan izravno
                // preko AddBooking (spec section 3/4 P1 hardening). Bez ove per-occurrence provjere AddMember bi
                // mogao stvoriti Confirmed Booking na terminu koji je za TAJ occurrence već pun, oversubscribing
                // ga bez obzira na rosterski broj. Isti dijeljeni guard (GroupCapacityGuard) i isti Appointment
                // FOR UPDATE lock kao BookingService/WaitlistService.PromoteEligibleWaiters — jedan izvor istine za
                // "confirmedCount < capacity" umjesto duplicirane aritmetike. GetFutureScheduledForGroup vraća
                // occurrencee determinističkim redoslijedom (StartsAt, Id) baš zato da dva konkurentna
                // AddMember/RemoveMember poziva preko istog skupa termina zaključavaju istim redoslijedom (nema
                // obrnutog Appointment<->Appointment lock ordera koji bi mogao deadlockati). Baca PRIJE ijednog
                // Bookinga za ovaj occurrence — iznimka izlazi iz uow bloka bez CommitAsync pa cijela operacija
                // (članstvo + audit + sve dosad dodane Bookinge) rollback-a atomično (sve-ili-ništa, isto kao
                // postojeći RecurringConflict abort iznad).
                await GroupCapacityGuard.EnsureAvailable(_appointmentHandler, uow, organizationId, futureAppointment.Id.GetValueOrDefault());

                decimal suggestedAmount = await ResolveSuggestedAmount(
                    organizationId, futureAppointment.ServiceId, futureAppointment.CompanyId, futureAppointment.StartsAt);

                await _appointmentHandler.AddBooking(uow, new Booking
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    AppointmentId = futureAppointment.Id.GetValueOrDefault(),
                    ClientId = request.ClientId,
                    Status = BookingStatus.Confirmed,
                    Amount = suggestedAmount,
                    SuggestedAmount = suggestedAmount,
                    CreatedAt = now
                });
            }

            await uow.CommitAsync();
        }

        return await GetDtoById(organizationId, groupId);
    }

    public async Task<GroupDto> RemoveMember(Guid organizationId, Guid userId, Guid groupId, Guid memberId)
    {
        await EnsureGroupExists(organizationId, groupId);

        GroupMember member = await _groupHandler.GetMemberById(organizationId, groupId, memberId);
        if (member == null)
            throw new NotFoundAppException("GroupMember", memberId);

        if (!member.IsActive)
            return await GetDtoById(organizationId, groupId);

        member.IsActive = false;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _groupHandler.UpdateMember(uow, member);

            await _auditLogHandler.Add(uow, new GroupAuditLog
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                ChangeType = "MemberRemoved",
                OldValue = member.ClientId.ToString(),
                NewValue = null,
                ChangedAt = now,
                ChangedBy = userId
            });

            // Napuštanje grupe otkazuje Booking na SVIM već generiranim budućim terminima grupe — bivši član
            // više ne bi trebao ostati Confirmed na satima koji dolaze (vidi AddMember za suprotni smjer).
            // Ne dira prošle/odrađene termine.
            List<Appointment> futureAppointments = await _appointmentHandler.GetFutureScheduledForGroup(uow, organizationId, groupId);
            foreach (Appointment futureAppointment in futureAppointments)
            {
                Booking booking = futureAppointment.Bookings.FirstOrDefault(b => b.ClientId == member.ClientId && b.Status == BookingStatus.Confirmed);
                if (booking == null)
                    continue;

                BookingStatus oldBookingStatus = booking.Status;
                BookingStatusVersioning.TrySetStatus(booking, BookingStatus.Cancelled);
                booking.CancellationReason = "Klijent uklonjen iz grupe";
                booking.UpdatedAt = now;
                booking.UpdatedBy = userId;
                await _appointmentHandler.UpdateBooking(uow, booking);

                // Isti obrazac kao BookingService.SetStatus/AppointmentService.ChangeToTerminalStatus — bez ovoga
                // bi ovaj SUSTAVOM izveden (iz GroupMember odjave) Booking prijelaz ostao bez traga tko/kada/zašto
                // (vidi audit-cleanup spec section 20/60, "GroupService.RemoveMember bypassing history").
                await _appointmentAuditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = futureAppointment.Id.GetValueOrDefault(),
                    BookingId = booking.Id,
                    ChangeType = "BookingStatus",
                    OldValue = oldBookingStatus.ToString(),
                    NewValue = booking.Status.ToString(),
                    StatusVersion = booking.StatusVersion,
                    ChangedAt = now,
                    ChangedBy = userId
                });

                // Isti booking.cancelled.v1 event kao izravno otkazivanje Bookinga — izvor (GroupMember odjava)
                // ne mijenja event-tip/handler (vidi spec section 34). Ista uow transakcija kao mutacija iznad.
                await _outboxWriter.Add(
                    uow, organizationId, OutboxEventTypes.BookingCancelledV1,
                    new BookingCancelledEvent
                    {
                        OrganizationId = organizationId,
                        BookingId = booking.Id.GetValueOrDefault(),
                        AppointmentId = futureAppointment.Id.GetValueOrDefault(),
                        ClientId = booking.ClientId,
                        CompanyId = futureAppointment.CompanyId,
                        StatusVersion = booking.StatusVersion,
                        OccurredAt = now
                    },
                    now,
                    idempotencyKey: $"booking-cancelled:{booking.Id.GetValueOrDefault()}:{booking.StatusVersion}");

                // Oslobođeno mjesto -> pokušaj promocije liste čekanja za OVAJ occurrence (spec section 39) —
                // ista promocijska logika kao izravno otkazivanje Bookinga (BookingService), ne duplicirana ovdje.
                await _waitlistPromotionService.PromoteEligibleWaiters(
                    uow, organizationId, futureAppointment.Id.GetValueOrDefault(), userId);
            }

            await uow.CommitAsync();
        }

        return await GetDtoById(organizationId, groupId);
    }

    public async Task<GenerateGroupAppointmentsResult> GenerateAppointments(
        Guid organizationId, Guid userId, GenerateGroupAppointmentsRequest request)
    {
        if (request.ToDate < request.FromDate)
            throw new ValidationAppException("Datum kraja ne smije biti prije datuma početka.");

        List<Group> candidateGroups;
        if (request.GroupId.HasValue)
        {
            Group group = await _groupHandler.GetById(organizationId, request.GroupId.Value);
            if (group == null)
                throw new NotFoundAppException("Group", request.GroupId.Value);

            candidateGroups = group.IsActive ? new List<Group> { group } : new List<Group>();
        }
        else
        {
            candidateGroups = await _groupHandler.GetAll(organizationId, isActive: true);
        }

        DateTimeOffset offset = request.FromDate;
        DateTime fromDate = request.FromDate.Date;
        DateTime toDate = request.ToDate.Date;

        List<GroupSlot> activeSlots = candidateGroups.SelectMany(g => g.Slots.Where(s => s.IsActive)).ToList();
        List<Guid> activeSlotIds = activeSlots.Select(s => s.Id.GetValueOrDefault()).ToList();

        HashSet<(Guid GroupSlotId, DateTimeOffset StartsAt)> existing = activeSlotIds.Count == 0
            ? new HashSet<(Guid, DateTimeOffset)>()
            : await _groupHandler.GetExistingSlotOccurrences(
                activeSlotIds, ComputeStartsAt(fromDate, TimeSpan.Zero, offset), ComputeStartsAt(toDate, new TimeSpan(23, 59, 59), offset));

        List<Guid> candidateCompanyIds = candidateGroups.Select(g => g.CompanyId).Distinct().ToList();
        List<CompanyHoliday> holidaysForCompanies = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, candidateCompanyIds, fromDate, toDate);

        List<GroupOccurrenceCandidate> candidates = new List<GroupOccurrenceCandidate>();
        int skipped = 0;

        foreach (Group group in candidateGroups)
        {
            foreach (GroupSlot slot in group.Slots.Where(s => s.IsActive))
            {
                for (DateTime date = fromDate; date <= toDate; date = date.AddDays(1))
                {
                    if (date.DayOfWeek != slot.DayOfWeek)
                        continue;

                    if (holidaysForCompanies.Any(h => h.CompanyId == group.CompanyId && h.Date.Date == date))
                    {
                        skipped++;
                        continue;
                    }

                    DateTimeOffset startsAt = ComputeStartsAt(date, slot.StartTime, offset);
                    (Guid, DateTimeOffset) key = (slot.Id.GetValueOrDefault(), startsAt);

                    if (existing.Contains(key))
                    {
                        skipped++;
                        continue;
                    }

                    existing.Add(key);
                    candidates.Add(new GroupOccurrenceCandidate { Group = group, Slot = slot, StartsAt = startsAt });
                }
            }
        }

        // Batch validacija PRIJE ijednog upisa (candidate vs persisted state + candidate vs candidate u istom
        // batchu) — candidates se namjerno NE perzistiraju inkrementalno da bi ih DB upiti "vidjeli", cijela
        // provjera je u memoriji nad ovom listom, isti obrazac kao AppointmentService.EnsureNoRecurringConflicts.
        EnsureNoDuplicateOccurrences(candidates);

        // Section 37 — grupa čiji aktivni broj članova premašuje kapacitet ne smije generirati nove occurrencee
        // (nemoguć broj sudionika). Provjerava se samo nad grupama koje stvarno imaju kandidata u ovom rasponu.
        EnsureCapacityNotExceeded(candidates.Select(c => c.Group).GroupBy(g => g.Id).Select(g => g.First()).ToList());

        Dictionary<(Guid SlotId, DateTimeOffset StartsAt), List<WarningDto>> warningsByCandidate =
            await EnsureNoTrainerConflicts(organizationId, candidates, request.OverrideAvailability);
        await EnsureNoRoomConflicts(organizationId, candidates);
        await EnsureNoMemberConflicts(organizationId, candidates);

        List<Appointment> toCreate = new List<Appointment>();
        List<AppointmentScheduleCellDto> createdDtos = new List<AppointmentScheduleCellDto>();

        foreach (GroupOccurrenceCandidate candidate in candidates)
        {
            Group group = candidate.Group;
            GroupSlot slot = candidate.Slot;
            DateTimeOffset startsAt = candidate.StartsAt;
            Guid appointmentId = Guid.NewGuid();

            // Navigacijska svojstva se namjerno NE postavljaju ovdje — appointment ide u
            // AddAppointments preko svježeg DbContext-a, a Service/Company/DefaultTrainer su
            // materijalizirani u kontekstu GetAll/GetById poziva pa bi ih EF pokušao ponovno umetnuti.
            Appointment appointment = new Appointment
            {
                Id = appointmentId,
                OrganizationId = organizationId,
                Form = AppointmentForm.Group,
                StartsAt = startsAt,
                DurationMinutes = group.Service.DefaultDurationMinutes,
                ServiceId = group.ServiceId,
                EmployeeId = group.DefaultTrainerId,
                CompanyId = group.CompanyId,
                RoomId = group.DefaultRoomId,
                Status = AppointmentStatus.Scheduled,
                GroupId = group.Id,
                GroupSlotId = slot.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            };

            // Predložena cijena se snapshotta ODMAH po članu (isti IPricingService poziv kao za Individual) —
            // grupni termin prije ovog zahvata nikad nije imao cijenu (uvijek 0 na Appointment). Amount ostaje
            // jednak SuggestedAmount i booking ostaje financijski neplaćen do stvarnog check-ina
            // (BookingService.ResolveCoverage), koji po potrebi razrješava paket/naplatu.
            decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, group.ServiceId, group.CompanyId, startsAt);

            // Booking (Status=Confirmed) se stvara ODMAH za svakog aktivnog člana grupe u trenutku generiranja
            // occurrencea — preferirani model iz spec section 11: daje eksplicitnu po-terminsku evidenciju
            // sudjelovanja prije nego se itko čekira, čime otkazivanje/no-show jednog člana unaprijed postaje
            // moguće (vidi BookingService.SetStatus). Gost izvan popisa članova i dalje dobiva ad-hoc Booking
            // tek na check-inu (BookingService.AddBooking / SetStatus s nepostojećim bookingom).
            foreach (GroupMember member in group.Members.Where(m => m.IsActive))
            {
                appointment.Bookings.Add(new Booking
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    AppointmentId = appointmentId,
                    ClientId = member.ClientId,
                    Status = BookingStatus.Confirmed,
                    Amount = suggestedAmount,
                    SuggestedAmount = suggestedAmount,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            toCreate.Add(appointment);

            warningsByCandidate.TryGetValue((slot.Id.GetValueOrDefault(), startsAt), out List<WarningDto> occurrenceWarnings);

            createdDtos.Add(new AppointmentScheduleCellDto
            {
                Id = appointmentId,
                StartsAt = startsAt,
                DurationMinutes = group.Service.DefaultDurationMinutes,
                ServiceId = group.ServiceId,
                ServiceName = group.Service?.Name,
                ServiceCategoryColorHex = group.Service?.ColorHex,
                EmployeeId = group.DefaultTrainerId,
                EmployeeName = group.DefaultTrainer != null ? $"{group.DefaultTrainer.FirstName} {group.DefaultTrainer.LastName}" : null,
                CompanyId = group.CompanyId,
                CompanyName = group.Company?.Name,
                RoomId = group.DefaultRoomId,
                RoomName = group.DefaultRoom?.Name,
                Status = AppointmentStatus.Scheduled,
                IsCancelled = false,
                Form = AppointmentForm.Group,
                GroupId = group.Id,
                GroupName = group.Name,
                AttendanceCount = 0,
                ExpectedCount = group.Members.Count(m => m.IsActive),
                Warnings = occurrenceWarnings ?? new List<WarningDto>()
            });
        }

        try
        {
            await _groupHandler.AddAppointments(toCreate);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgresException &&
                                           postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                                           postgresException.ConstraintName == "ux_appointments_group_slot_startsat")
        {
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Jedan ili više termina grupe već je generiran u međuvremenu.",
                new
                {
                    conflicts = candidates.Select(candidate => new RecurringConflictDetail
                    {
                        Date = candidate.StartsAt,
                        Reason = ErrorCodes.RecurringConflictReasonDuplicateOccurrence
                    }).ToList()
                });
        }

        return new GenerateGroupAppointmentsResult
        {
            CreatedCount = toCreate.Count,
            SkippedCount = skipped,
            Created = createdDtos.OrderBy(a => a.StartsAt).ToList()
        };
    }

    public async Task<List<ClientGroupMembershipDto>> GetMembershipsByClient(Guid organizationId, Guid clientId)
    {
        List<GroupMember> memberships = await _groupHandler.GetMembershipsByClient(organizationId, clientId);
        return memberships.Select(m => new ClientGroupMembershipDto
        {
            GroupId = m.GroupId,
            GroupName = m.Group?.Name,
            ServiceName = m.Group?.Service?.Name,
            CompanyName = m.Group?.Company?.Name,
            Slots = m.Group?.Slots.Where(s => s.IsActive)
                .OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
                .Select(s => new GroupSlotDto
                {
                    Id = s.Id.GetValueOrDefault(),
                    DayOfWeek = s.DayOfWeek,
                    StartTime = s.StartTime,
                    IsActive = s.IsActive
                }).ToList() ?? new List<GroupSlotDto>(),
            JoinedAt = m.JoinedAt,
            IsActive = m.IsActive
        }).ToList();
    }

    private readonly struct GroupOccurrenceCandidate
    {
        public Group Group { get; init; }
        public GroupSlot Slot { get; init; }
        public DateTimeOffset StartsAt { get; init; }
    }

    /// <summary>Za GenerateAppointments — STVARNI sudar (trener već ima termin/grupu u to vrijeme) uvijek baca
    /// RECURRING_CONFLICT (409) prije nego se bilo što spremi, cijeli batch abortira (isti obrazac kao
    /// AppointmentService.EnsureNoRecurringConflicts). Trener odsutan (roster), izvan radnog vremena, na praznik,
    /// ili na pauzi (ScheduleBreak) su TAKOĐER tvrda blokada za cijeli batch OSIM kad je overrideAvailability=true
    /// (request.OverrideAvailability — nema posebnog grant zahtjeva jer je groups.manage već jedini grant ovog
    /// endpointa), kad se umjesto blokade vraćaju kao upozorenje po kandidatu (ključ (GroupSlotId, StartsAt), isti
    /// ključ kao dedup-set u GenerateAppointments) koje pozivatelj upisuje u AppointmentScheduleCellDto.Warnings.
    /// Grupe bez dodijeljenog trenera (DefaultTrainerId == null) se preskaču — nema koga provjeriti. Kandidati se
    /// grupiraju po treneru kako bi se termini/roster/pauze/predlošci dohvatili JEDNOM po treneru za cijeli raspon,
    /// umjesto po occurrenceu.</summary>
    private async Task<Dictionary<(Guid SlotId, DateTimeOffset StartsAt), List<WarningDto>>> EnsureNoTrainerConflicts(
        Guid organizationId, List<GroupOccurrenceCandidate> candidates, bool overrideAvailability)
    {
        List<RecurringConflictDetail> hardConflicts = new List<RecurringConflictDetail>();
        Dictionary<(Guid, DateTimeOffset), List<WarningDto>> warningsByCandidate = new Dictionary<(Guid, DateTimeOffset), List<WarningDto>>();

        IEnumerable<IGrouping<Guid, GroupOccurrenceCandidate>> byEmployee = candidates
            .Where(c => c.Group.DefaultTrainerId.HasValue)
            .GroupBy(c => c.Group.DefaultTrainerId.GetValueOrDefault());

        foreach (IGrouping<Guid, GroupOccurrenceCandidate> employeeCandidates in byEmployee)
        {
            Guid employeeId = employeeCandidates.Key;
            List<GroupOccurrenceCandidate> ordered = employeeCandidates.OrderBy(c => c.StartsAt).ToList();

            DateTimeOffset rangeFrom = ordered[0].StartsAt.AddDays(-1);
            DateTimeOffset rangeTo = ordered[^1].StartsAt.AddDays(1);

            List<Appointment> candidateAppointments = await _appointmentHandler.GetForEmployeeInRange(
                organizationId, employeeId, rangeFrom, rangeTo);

            List<ScheduleBreak> candidateBreaks = await _scheduleBreakHandler.GetForEmployeeInRange(
                organizationId, employeeId, rangeFrom, rangeTo);

            List<RosterEntry> rosterEntriesInRange = await _rosterEntryHandler.GetForPeriod(
                organizationId, new List<Guid> { employeeId }, rangeFrom, rangeTo);

            List<RosterEntry> absences = rosterEntriesInRange.Where(e => e.RosterType.IsAbsence).ToList();

            WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);

            Dictionary<Guid, WorkingHoursTemplate> companyTemplatesById = new Dictionary<Guid, WorkingHoursTemplate>();
            foreach (Guid companyId in ordered.Select(c => c.Group.CompanyId).Distinct())
                companyTemplatesById[companyId] = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);

            for (int i = 0; i < ordered.Count; i++)
            {
                GroupOccurrenceCandidate candidate = ordered[i];
                int durationMinutes = candidate.Group.Service.DefaultDurationMinutes;
                DateTimeOffset startsAt = candidate.StartsAt;
                DateTimeOffset occurrenceEnd = startsAt.AddMinutes(durationMinutes);
                (Guid, DateTimeOffset) key = (candidate.Slot.Id.GetValueOrDefault(), startsAt);

                bool appointmentHit = candidateAppointments.Any(a =>
                    a.StartsAt < occurrenceEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes));

                // Candidate vs candidate u istom batchu (spec zahtjev #2) — isti trener predložen za dvije
                // različite grupe/slotove čiji generirani occurrenceи se preklapaju, iako nijedan još ne postoji
                // u bazi. Tvrda blokada bez override-a, isto kao appointmentHit.
                bool batchEmployeeHit = !appointmentHit && HasBatchOverlap(ordered, i, startsAt, durationMinutes);

                if (appointmentHit || batchEmployeeHit)
                {
                    hardConflicts.Add(new RecurringConflictDetail { Date = startsAt, Reason = ErrorCodes.RecurringConflictReasonAppointment });
                    continue;
                }

                bool breakHit = candidateBreaks.Any(b =>
                    b.StartsAt < occurrenceEnd && startsAt < b.StartsAt.AddMinutes(b.DurationMinutes));

                bool absenceHit = absences.Any(a =>
                    a.DateFrom.Date <= startsAt.Date && (a.DateTo == null || startsAt.Date <= a.DateTo.Value.Date));

                List<RosterEntry> rosterEntriesForOccurrence = rosterEntriesInRange
                    .Where(e => !e.RosterType.IsAbsence && e.DateFrom.Date == startsAt.Date)
                    .ToList();

                WorkingHoursTemplate companyTemplate = companyTemplatesById[candidate.Group.CompanyId];

                // Praznik je za ovu granu već obrađen ranije u GenerateAppointments (tiho preskačanje kandidata
                // na dan praznika) — holidayHit je uvijek false ovdje, vidi IsWithinWorkingHours doc-komentar.
                bool withinHours = absenceHit || IsWithinWorkingHours(employeeTemplate, companyTemplate, rosterEntriesForOccurrence, startsAt, durationMinutes);

                AppointmentEligibilityHelper.WorkforceViolation violation = AppointmentEligibilityHelper.Classify(
                    absenceHit, breakHit, holidayHit: false, withinWorkingHours: withinHours);

                if (violation == AppointmentEligibilityHelper.WorkforceViolation.None)
                    continue;

                if (!overrideAvailability)
                {
                    hardConflicts.Add(new RecurringConflictDetail { Date = startsAt, Reason = ToRecurringConflictReason(violation) });
                    continue;
                }

                List<WarningDto> warnings = new List<WarningDto>();
                AppointmentEligibilityHelper.ThrowOrWarn(violation, overrideAvailability: true, warnings);
                warningsByCandidate[key] = warnings;
            }
        }

        if (hardConflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Neki termini u nizu se sudaraju s postojećim obavezama.",
                new { conflicts = hardConflicts });

        return warningsByCandidate;
    }

    /// <summary>Isti obrazac kao EnsureNoTrainerConflicts, ali po prostoriji — grupe bez DefaultRoomId ili čija
    /// prostorija ima AllowConcurrentBookings=true se preskaču (nema tvrde blokade).</summary>
    private async Task EnsureNoRoomConflicts(Guid organizationId, List<GroupOccurrenceCandidate> candidates)
    {
        List<RecurringConflictDetail> conflicts = new List<RecurringConflictDetail>();

        IEnumerable<IGrouping<Guid, GroupOccurrenceCandidate>> byRoom = candidates
            .Where(c => c.Group.DefaultRoomId.HasValue && c.Group.DefaultRoom?.AllowConcurrentBookings != true)
            .GroupBy(c => c.Group.DefaultRoomId.GetValueOrDefault());

        foreach (IGrouping<Guid, GroupOccurrenceCandidate> roomCandidates in byRoom)
        {
            Guid roomId = roomCandidates.Key;
            List<GroupOccurrenceCandidate> ordered = roomCandidates.OrderBy(c => c.StartsAt).ToList();

            DateTimeOffset rangeFrom = ordered[0].StartsAt.AddDays(-1);
            DateTimeOffset rangeTo = ordered[^1].StartsAt.AddDays(1);

            List<Appointment> candidateAppointments = await _appointmentHandler.GetForRoomInRange(
                organizationId, roomId, rangeFrom, rangeTo);

            for (int i = 0; i < ordered.Count; i++)
            {
                GroupOccurrenceCandidate candidate = ordered[i];
                int durationMinutes = candidate.Group.Service.DefaultDurationMinutes;
                DateTimeOffset startsAt = candidate.StartsAt;
                DateTimeOffset occurrenceEnd = startsAt.AddMinutes(durationMinutes);

                bool roomHit = candidateAppointments.Any(a =>
                    a.StartsAt < occurrenceEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes));

                // Candidate vs candidate u istom batchu (spec zahtjev #2) — ista soba predložena za dvije
                // različite grupe/slotove čiji generirani occurrenceи se preklapaju. AllowConcurrentBookings=true
                // je već filtriran iz byRoom grupiranja iznad, pa se batch provjera nikad ne primjenjuje na njih.
                bool batchRoomHit = !roomHit && HasBatchOverlap(ordered, i, startsAt, durationMinutes);

                if (roomHit || batchRoomHit)
                    conflicts.Add(new RecurringConflictDetail { Date = startsAt, Reason = ErrorCodes.RecurringConflictReasonRoom });
            }
        }

        if (conflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Neki termini u nizu se sudaraju s postojećom zauzetošću prostorije.",
                new { conflicts });
    }

    /// <summary>Standardno pravilo preklapanja (newStart &lt; existingEnd &amp;&amp; newEnd &gt; existingStart) —
    /// susjedni intervali (kraj jednog = početak drugog) NISU sudar. Dijeli ga svaka batch-vs-batch provjera
    /// u ovoj klasi (trener/soba/član) umjesto da svaka duplicira vlastitu formulu preklapanja.</summary>
    private static bool IntervalsOverlap(DateTimeOffset aStart, int aDurationMinutes, DateTimeOffset bStart, int bDurationMinutes)
    {
        DateTimeOffset aEnd = aStart.AddMinutes(aDurationMinutes);
        DateTimeOffset bEnd = bStart.AddMinutes(bDurationMinutes);
        return aStart < bEnd && aEnd > bStart;
    }

    /// <summary>Ima li kandidat na poziciji <paramref name="excludeIndex"/> preklapanje s BILO KOJIM drugim
    /// kandidatom iz iste liste (već filtrirane po zajedničkom resursu — trener ili soba) — koristi ga
    /// EnsureNoTrainerConflicts/EnsureNoRoomConflicts za candidate-vs-candidate provjeru istog batcha.</summary>
    private static bool HasBatchOverlap(List<GroupOccurrenceCandidate> group, int excludeIndex, DateTimeOffset startsAt, int durationMinutes)
    {
        for (int j = 0; j < group.Count; j++)
        {
            if (j == excludeIndex)
                continue;

            GroupOccurrenceCandidate other = group[j];
            if (IntervalsOverlap(startsAt, durationMinutes, other.StartsAt, other.Group.Service.DefaultDurationMinutes))
                return true;
        }

        return false;
    }

    /// <summary>Spec zahtjev #2 — ista grupa ne smije generirati dva kandidata na potpuno isti (GroupId,
    /// StartsAt) par unutar jednog batcha. Dedup-set u GenerateAppointments (ključ (GroupSlotId, StartsAt))
    /// ovo NE hvata kad dva RAZLIČITA aktivna slota iste grupe slučajno računaju isti StartsAt (npr. dva slota
    /// oba na ponedjeljak 18:00) — svaki slot ima svoj ključ pa dedup-set ne prepoznaje takav par kao duplikat.</summary>
    private static void EnsureNoDuplicateOccurrences(List<GroupOccurrenceCandidate> candidates)
    {
        List<RecurringConflictDetail> duplicates = candidates
            .GroupBy(c => (c.Group.Id, c.StartsAt))
            .Where(g => g.Count() > 1)
            .Select(g => new RecurringConflictDetail { Date = g.Key.StartsAt, Reason = ErrorCodes.RecurringConflictReasonDuplicateOccurrence })
            .ToList();

        if (duplicates.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Ista grupa bi generirala dva termina u potpuno isto vrijeme u ovom zahtjevu.",
                new { conflicts = duplicates });
    }

    /// <summary>Section 37 — tvrda blokada (409) ako bilo koja kandidatna grupa ima više aktivnih članova nego
    /// dopušta Capacity. Grupe se ne mijenjaju između AddMember (koji sam hard-blokira na kapacitetu) i ovog
    /// poziva u istoj transakciji generiranja, ali batch i dalje revalidira jer je Capacity mogla biti smanjena
    /// nakon što su članovi već upisani (Update ne provjerava kapacitet retroaktivno).</summary>
    private static void EnsureCapacityNotExceeded(List<Group> groups)
    {
        List<object> violations = new List<object>();

        foreach (Group group in groups)
        {
            int activeMemberCount = group.Members.Count(m => m.IsActive);
            if (activeMemberCount > group.Capacity)
                violations.Add(new { groupId = group.Id, groupName = group.Name, capacity = group.Capacity, activeMemberCount });
        }

        if (violations.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.GroupCapacityReached,
                "Jedna ili više grupa ima više aktivnih članova nego što kapacitet dopušta — generiranje termina je blokirano.",
                new { groups = violations });
    }

    /// <summary>Section 36 — aktivni GroupMember ne smije završiti dvostruko zakazan generiranjem novog
    /// occurrencea (bilo koji Form termina se računa, ne samo grupni). Tvrda blokada za cijeli batch, bez
    /// override-a (isto kao EnsureNoRoomConflicts — nema poslovnog razloga zaobići sudar klijenta). Details
    /// namjerno ne nose ClientId/ime (izbjegava nepotreban PII u error payloadu, vidi spec section 36).</summary>
    private async Task EnsureNoMemberConflicts(Guid organizationId, List<GroupOccurrenceCandidate> candidates)
    {
        List<Guid> allMemberClientIds = candidates
            .SelectMany(c => c.Group.Members.Where(m => m.IsActive).Select(m => m.ClientId))
            .Distinct()
            .ToList();

        if (allMemberClientIds.Count == 0)
            return;

        DateTimeOffset rangeFrom = candidates.Min(c => c.StartsAt).AddDays(-1);
        DateTimeOffset rangeTo = candidates.Max(c => c.StartsAt).AddDays(1);

        List<Appointment> candidateAppointments = await _appointmentHandler.GetForClientsInRange(
            organizationId, allMemberClientIds, rangeFrom, rangeTo);

        List<RecurringConflictDetail> conflicts = new List<RecurringConflictDetail>();

        for (int i = 0; i < candidates.Count; i++)
        {
            GroupOccurrenceCandidate candidate = candidates[i];
            List<Guid> memberClientIds = candidate.Group.Members.Where(m => m.IsActive).Select(m => m.ClientId).ToList();
            if (memberClientIds.Count == 0)
                continue;

            int durationMinutes = candidate.Group.Service.DefaultDurationMinutes;
            DateTimeOffset startsAt = candidate.StartsAt;
            DateTimeOffset occurrenceEnd = startsAt.AddMinutes(durationMinutes);

            bool memberConflict = candidateAppointments.Any(a =>
                a.StartsAt < occurrenceEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes) &&
                a.Bookings.Any(b => memberClientIds.Contains(b.ClientId) &&
                    b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow));

            // Candidate vs candidate u istom batchu (spec zahtjev #2) — isti aktivni član pripada dvjema
            // različitim grupama/slotovima čiji generirani occurrenceи se preklapaju, iako nijedan Booking još
            // ne postoji u bazi za bilo koji od njih.
            if (!memberConflict)
            {
                for (int j = 0; j < candidates.Count && !memberConflict; j++)
                {
                    if (j == i)
                        continue;

                    GroupOccurrenceCandidate other = candidates[j];
                    if (!IntervalsOverlap(startsAt, durationMinutes, other.StartsAt, other.Group.Service.DefaultDurationMinutes))
                        continue;

                    List<Guid> otherMemberClientIds = other.Group.Members.Where(m => m.IsActive).Select(m => m.ClientId).ToList();
                    memberConflict = memberClientIds.Any(otherMemberClientIds.Contains);
                }
            }

            if (memberConflict)
                conflicts.Add(new RecurringConflictDetail { Date = startsAt, Reason = ErrorCodes.RecurringConflictReasonMemberConflict });
        }

        if (conflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Jedan ili više aktivnih članova grupe već ima zakazan termin u vrijeme generiranog termina.",
                new { conflicts });
    }

    private static string ToRecurringConflictReason(AppointmentEligibilityHelper.WorkforceViolation violation) => violation switch
    {
        AppointmentEligibilityHelper.WorkforceViolation.EmployeeAbsent => ErrorCodes.RecurringConflictReasonRosterAbsence,
        AppointmentEligibilityHelper.WorkforceViolation.EmployeeOnBreak => ErrorCodes.RecurringConflictReasonScheduleBreak,
        AppointmentEligibilityHelper.WorkforceViolation.CompanyClosedHoliday => ErrorCodes.RecurringConflictReasonHoliday,
        _ => ErrorCodes.RecurringConflictReasonOutsideWorkingHours
    };

    /// <summary>Isto pravilo kao AppointmentService.IsWithinWorkingHours, bez holiday parametra — holiday je za ovaj
    /// poziv već obrađen ranije u GenerateAppointments (tiho preskačanje), pa kandidati koji stignu ovamo po
    /// definiciji nisu na praznik.</summary>
    private static bool IsWithinWorkingHours(
        WorkingHoursTemplate employeeTemplate, WorkingHoursTemplate companyTemplate, List<RosterEntry> rosterEntriesForDate,
        DateTimeOffset startsAt, int durationMinutes)
    {
        TimeSpan start = startsAt.TimeOfDay;
        TimeSpan end = start + TimeSpan.FromMinutes(durationMinutes);

        (List<WorkingHoursCalculator.Interval> employeeIntervals, _) =
            WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterEntriesForDate, startsAt);
        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, new List<CompanyHoliday>(), startsAt);

        return WorkingHoursCalculator.IsWithinIntervals(employeeIntervals, start, end)
            && WorkingHoursCalculator.IsWithinIntervals(companyIntervals, start, end);
    }

    private static DateTimeOffset ComputeStartsAt(DateTime date, TimeSpan timeOfDay, DateTimeOffset offsetSource)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, offsetSource.Offset).Add(timeOfDay);
    }

    /// <summary>Radno vrijeme (trener/poslovnica) za definiciju grupe (Create/Update/AddSlot/UpdateSlot) je
    /// UPOZORENJE, ne blokada — isti obrazac kao AppointmentService za pojedinačni termin. Provjerava se prema
    /// predlošku (bez roster odsutnosti/override-a — nema konkretnog datuma na razini definicije grupe, samo
    /// dan-u-tjednu + vrijeme), pa se za svaki slot uzima najbliži budući datum tog dana kao reprezentativni.</summary>
    private async Task<List<WarningDto>> ComputeWorkingHoursWarnings(
        Guid organizationId, Guid companyId, Guid? trainerId, int durationMinutes,
        List<(DayOfWeek DayOfWeek, TimeSpan StartTime)> slots)
    {
        List<WarningDto> warnings = new List<WarningDto>();
        if (!trainerId.HasValue)
            return warnings;

        WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, trainerId.Value);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach ((DayOfWeek dayOfWeek, TimeSpan startTime) in slots)
        {
            DateTime representativeDate = NextOccurrenceDate(now, dayOfWeek);
            DateTimeOffset startsAt = ComputeStartsAt(representativeDate, startTime, now);

            if (!IsWithinWorkingHours(employeeTemplate, companyTemplate, new List<RosterEntry>(), startsAt, durationMinutes))
            {
                warnings.Add(new WarningDto(WarningCodes.OutsideWorkingHours, new WarningSlotDetails
                {
                    DayOfWeek = dayOfWeek,
                    StartTime = startTime
                }));
            }
        }

        return warnings;
    }

    private static DateTime NextOccurrenceDate(DateTimeOffset from, DayOfWeek dayOfWeek)
    {
        int diff = ((int)dayOfWeek - (int)from.DayOfWeek + 7) % 7;
        return from.Date.AddDays(diff);
    }

    /// <summary>Tvrda blokada (409) — druga aktivna grupa već koristi istu prostoriju u preklapajućem
    /// danu-u-tjednu/vremenu, a prostorija ne dopušta paralelne rezervacije (AllowConcurrentBookings=false).
    /// excludeGroupId isključuje grupu koja se upravo ažurira (ne sudara se sama sa sobom).</summary>
    private async Task EnsureNoRoomSlotConflict(
        Guid organizationId, Guid? excludeGroupId, Guid? roomId, int durationMinutes,
        List<(DayOfWeek DayOfWeek, TimeSpan StartTime)> slots)
    {
        if (!roomId.HasValue)
            return;

        Room room = await _roomHandler.GetById(organizationId, roomId.Value);
        if (room == null || room.AllowConcurrentBookings)
            return;

        List<Group> otherGroups = await _groupHandler.GetAll(organizationId, isActive: true);

        foreach (Group other in otherGroups)
        {
            if (other.Id == excludeGroupId || other.DefaultRoomId != roomId)
                continue;

            int otherDuration = other.Service?.DefaultDurationMinutes ?? 0;

            foreach (GroupSlot otherSlot in other.Slots.Where(s => s.IsActive))
            {
                TimeSpan otherStart = otherSlot.StartTime;
                TimeSpan otherEnd = otherStart + TimeSpan.FromMinutes(otherDuration);

                foreach ((DayOfWeek dayOfWeek, TimeSpan startTime) in slots)
                {
                    if (dayOfWeek != otherSlot.DayOfWeek)
                        continue;

                    TimeSpan end = startTime + TimeSpan.FromMinutes(durationMinutes);
                    if (startTime < otherEnd && otherStart < end)
                    {
                        throw new BusinessRuleException(
                            ErrorCodes.AppointmentOverlap,
                            $"Prostorija je već zauzeta u ovom terminu (grupa \"{other.Name}\").");
                    }
                }
            }
        }
    }

    private static void ValidateSlotTime(TimeSpan startTime)
    {
        if (startTime < TimeSpan.Zero || startTime >= TimeSpan.FromDays(1))
            throw new ValidationAppException("Vrijeme slota mora biti između 00:00 i 23:59.");
    }

    private async Task EnsureGroupExists(Guid organizationId, Guid groupId)
    {
        Group group = await _groupHandler.GetByIdLight(organizationId, groupId);
        if (group == null)
            throw new NotFoundAppException("Group", groupId);
    }

    private async Task<ServiceEntity> EnsureServiceExists(Guid organizationId, Guid serviceId)
    {
        ServiceEntity service = await _serviceHandler.GetById(organizationId, serviceId);
        if (service == null)
            throw new NotFoundAppException("Service", serviceId);

        if (service.ExecutionMode != ServiceExecutionMode.Group)
            throw new BusinessRuleException(
                ErrorCodes.ServiceNotGroupMode, "Grupa može koristiti samo uslugu s grupnim načinom izvođenja.");

        return service;
    }

    /// <summary>Puni strukturni lanac (FAZA 3, isti obrazac kao AppointmentService.EnsureStructuralEligibility):
    /// Company/Service aktivni, Service stvarno ponuđen u toj Company, DefaultTrainer (ako je zadan) aktivan i
    /// dodijeljen i toj Company i toj usluzi. Poziva se iz Create/Update — GenerateAppointments/AddSlot/UpdateSlot
    /// ne mijenjaju Service/Company/DefaultTrainer pa im ovo nije potrebno.</summary>
    private async Task<Company> EnsureStructuralEligibility(Guid organizationId, ServiceEntity service, Guid companyId, Guid? trainerId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);
        if (!company.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Poslovnica '{company.Name}' nije aktivna.");

        if (!await _serviceAvailabilityService.IsServiceAvailableAtCompany(organizationId, service.Id.GetValueOrDefault(), companyId))
            throw new BusinessRuleException(
                ErrorCodes.ServiceNotAvailableAtCompany, $"Usluga '{service.Name}' nije dostupna u poslovnici '{company.Name}'.");

        if (trainerId.HasValue)
        {
            Employee employee = await _employeeHandler.GetById(organizationId, trainerId.Value);
            if (employee == null)
                throw new NotFoundAppException("Employee", trainerId.Value);
            if (!employee.IsActive)
                throw new BusinessRuleException(ErrorCodes.InactiveEmployee, $"Zaposlenik '{employee.FirstName} {employee.LastName}' nije aktivan.");
            if (!await _employeeHandler.IsEmployeeAssignedToCompany(organizationId, trainerId.Value, companyId))
                throw new BusinessRuleException(ErrorCodes.EmployeeNotAssignedToCompany, $"Zaposlenik nije dodijeljen poslovnici '{company.Name}'.");
            if (!await _employeeHandler.CanEmployeePerformService(organizationId, trainerId.Value, service.Id.GetValueOrDefault()))
                throw new BusinessRuleException(ErrorCodes.EmployeeNotAssignedToService, $"Zaposlenik nije ovlašten izvoditi uslugu '{service.Name}'.");
        }

        return company;
    }

    /// <summary>Isti oblik kao EnsureStructuralEligibility — prostorija je opcionalna, ali ako je zadana mora biti
    /// aktivna i pripadati istoj poslovnici kao grupa.</summary>
    private async Task EnsureRoomExists(Guid organizationId, Guid companyId, Guid? roomId)
    {
        if (!roomId.HasValue)
            return;

        Room room = await _roomHandler.GetById(organizationId, roomId.Value);
        if (room == null)
            throw new NotFoundAppException("Room", roomId.Value);

        if (!room.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveRoom, $"Prostorija '{room.Name}' nije aktivna.");

        if (room.CompanyId != companyId)
            throw new BusinessRuleException(ErrorCodes.RoomCompanyMismatch, "Prostorija ne pripada odabranoj poslovnici.");
    }

    private async Task<GroupDto> GetDtoById(Guid organizationId, Guid id)
    {
        Group group = await _groupHandler.GetById(organizationId, id);
        if (group == null)
            throw new NotFoundAppException("Group", id);

        GroupDto dto = new GroupDto();
        MapGroup(group, dto);
        return dto;
    }

    private static void MapGroup(Group group, GroupDto dto)
    {
        dto.Id = group.Id.GetValueOrDefault();
        dto.Name = group.Name;
        dto.ServiceId = group.ServiceId;
        dto.ServiceName = group.Service?.Name;
        dto.CompanyId = group.CompanyId;
        dto.CompanyName = group.Company?.Name;
        dto.Capacity = group.Capacity;
        dto.DefaultTrainerId = group.DefaultTrainerId;
        dto.DefaultTrainerName = group.DefaultTrainer != null ? $"{group.DefaultTrainer.FirstName} {group.DefaultTrainer.LastName}" : null;
        dto.DefaultRoomId = group.DefaultRoomId;
        dto.DefaultRoomName = group.DefaultRoom?.Name;
        dto.IsActive = group.IsActive;
        dto.Note = group.Note;
        dto.Slots = group.Slots.Select(s => new GroupSlotDto
        {
            Id = s.Id.GetValueOrDefault(),
            DayOfWeek = s.DayOfWeek,
            StartTime = s.StartTime,
            IsActive = s.IsActive
        }).ToList();
        dto.ActiveMemberCount = group.Members.Count(m => m.IsActive);
        dto.CreatedAt = group.CreatedAt;
        dto.CreatedBy = group.CreatedBy;
        dto.UpdatedAt = group.UpdatedAt;
        dto.UpdatedBy = group.UpdatedBy;
    }

    private static GroupMemberDto ToMemberDto(GroupMember member)
    {
        return new GroupMemberDto
        {
            Id = member.Id.GetValueOrDefault(),
            ClientId = member.ClientId,
            ClientName = member.Client != null ? $"{member.Client.FirstName} {member.Client.LastName}" : null,
            JoinedAt = member.JoinedAt,
            IsActive = member.IsActive
        };
    }

    /// <summary>Appointments returned by GetAppointmentsForGroup su uvijek Form=Group (filtrirano po GroupId) —
    /// groupName/expectedCount dolaze iz već učitanog Group entiteta, ne iz a.Group (nije uključen u upit).</summary>
    private static AppointmentScheduleCellDto ToScheduleCellDto(Appointment a, string groupName, int expectedCount)
    {
        return new AppointmentScheduleCellDto
        {
            Id = a.Id.GetValueOrDefault(),
            StartsAt = a.StartsAt,
            DurationMinutes = a.DurationMinutes,
            ServiceId = a.ServiceId,
            ServiceName = a.Service?.Name,
            ServiceCategoryColorHex = a.Service?.ColorHex,
            EmployeeId = a.EmployeeId,
            EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}" : null,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            RoomId = a.RoomId,
            RoomName = a.Room?.Name,
            Status = a.Status,
            IsCancelled = a.Status == AppointmentStatus.Cancelled,
            Form = AppointmentForm.Group,
            GroupId = a.GroupId,
            GroupName = groupName,
            AttendanceCount = a.Bookings.Count(b => b.Status == BookingStatus.Completed),
            ExpectedCount = expectedCount
        };
    }
}
