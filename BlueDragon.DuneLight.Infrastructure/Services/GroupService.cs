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
    private readonly ISchedulingOccupancyHandler _schedulingOccupancyHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly IWorkingHoursTemplateHandler _workingHoursTemplateHandler;
    private readonly IScheduleBreakHandler _scheduleBreakHandler;
    private readonly IWaitlistPromotionService _waitlistPromotionService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly IBookingSegmentParticipationHandler _participationHandler;

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
        ISchedulingOccupancyHandler schedulingOccupancyHandler,
        IRosterEntryHandler rosterEntryHandler,
        IWorkingHoursTemplateHandler workingHoursTemplateHandler,
        IScheduleBreakHandler scheduleBreakHandler,
        IWaitlistPromotionService waitlistPromotionService,
        IOutboxWriter outboxWriter,
        IUnitOfWorkFactory unitOfWorkFactory,
        IOrganizationCalendarService organizationCalendarService,
        IBookingSegmentParticipationHandler participationHandler)
    {
        _organizationCalendarService = organizationCalendarService;
        _participationHandler = participationHandler;
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
        _schedulingOccupancyHandler = schedulingOccupancyHandler;
        _rosterEntryHandler = rosterEntryHandler;
        _workingHoursTemplateHandler = workingHoursTemplateHandler;
        _scheduleBreakHandler = scheduleBreakHandler;
        _waitlistPromotionService = waitlistPromotionService;
        _outboxWriter = outboxWriter;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    /// <summary>Isti IPricingService poziv kao AppointmentService.ResolveServicePrice — centralni resolver,
    /// ne duplicira logiku razrješavanja cijene.</summary>
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
        dto.UpcomingAppointments = appointments.Where(a => AppointmentRange.Of(a).PlannedStart >= now)
            .OrderBy(a => AppointmentRange.Of(a).PlannedStart).Select(a => ToScheduleCellDto(a, group.Name, expectedCount)).ToList();
        dto.PastAppointments = appointments.Where(a => AppointmentRange.Of(a).PlannedStart < now)
            .OrderByDescending(a => AppointmentRange.Of(a).PlannedStart).Select(a => ToScheduleCellDto(a, group.Name, expectedCount)).ToList();

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
            // Phase M1C: klijentov raspored se zaključava PRVI u transakciji (prije Appointment lockova kapaciteta niže) —
            // provjera sudara i upis Bookinga na budućim occurrenceima su time serijalizirani s ostalim upisima klijenta.
            await _schedulingOccupancyHandler.LockSchedulingSubjects(uow, Array.Empty<Guid>(), new[] { request.ClientId });

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
            // Phase M1D: novi član je +1 osoba u prostoriji svakog budućeg occurrencea — prostorije se zaključavaju odmah nakon
            // klijenta (uzlazno, jednim pozivom — globalni redoslijed), PRIJE Appointment lockova kapaciteta grupe niže.
            await _schedulingOccupancyHandler.LockSchedulingSubjects(
                uow, Array.Empty<Guid>(), Array.Empty<Guid>(),
                futureAppointments.SelectMany(a => a.Segments).Where(seg => seg.RoomId.HasValue).Select(seg => seg.RoomId.Value).Distinct());

            // Klijent ne smije završiti dvostruko zakazan (spec section 17) — provjera PRIJE ijednog Bookinga,
            // cijela operacija (uklj. samo članstvo) abortira atomično (uow se baca bez commit-a = rollback) ako
            // ijedan budući occurrence sudara s postojećim aktivnim Bookingom klijenta na BILO KOJEM terminu.
            List<RecurringConflictDetail> conflicts = new List<RecurringConflictDetail>();
            foreach (Appointment futureAppointment in futureAppointments)
            {
                if (futureAppointment.Bookings.Any(b => b.ClientId == request.ClientId))
                    continue;

                // Član dobiva sudjelovanje na (jedinom) segmentu occurrencea — preklapanje se provjerava nad TIM segmentom.
                AppointmentSegment futureSegment = SingleSegmentCompatibility.Resolve(futureAppointment);
                List<OccupancySlot> overlapping = await _schedulingOccupancyHandler.GetOverlappingForClients(
                    uow, organizationId, new[] { request.ClientId }, futureSegment.PlannedStart, futureSegment.PlannedEnd, excludedSegmentIds: null);

                if (overlapping.Count > 0)
                    conflicts.Add(new RecurringConflictDetail
                    {
                        Date = futureSegment.PlannedStart,
                        Reason = ErrorCodes.RecurringConflictReasonAppointment
                    });
            }

            // Phase M1D: fizički kapacitet prostorije (osobe) — tvrdo, odvojeno od Group.Capacity (poslovni broj mjesta).
            if (conflicts.Count == 0)
            {
                List<SegmentClaim> roomClaims = futureAppointments
                    .Where(a => a.Bookings.All(b => b.ClientId != request.ClientId))
                    .Select(a => SingleSegmentCompatibility.Resolve(a))
                    .Where(seg => seg.RoomId.HasValue)
                    .Select(seg => new SegmentClaim(null, seg.PlannedStart, seg.PlannedEnd, Array.Empty<Guid>(), Array.Empty<Guid>())
                    {
                        RoomId = seg.RoomId,
                        RoomPeople = 1
                    })
                    .ToList();
                CapacityViolation roomViolation = (await SchedulingConflictGuard.FindCapacityViolations(
                    _schedulingOccupancyHandler, uow, organizationId, roomClaims)).FirstOrDefault();
                if (roomViolation != null)
                    throw roomViolation.ToException();
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

                AppointmentSegment memberSegment = SingleSegmentCompatibility.Resolve(futureAppointment);
                SegmentExecutionContext execution = ExecutionContextResolver.ForSegment(futureAppointment, memberSegment);
                ResolvePriceResponse resolvedPrice = await ResolveServicePrice(
                    organizationId, execution.ServiceId, execution.CompanyId, execution.StartsAt);

                await _appointmentHandler.AddBooking(uow, BookingFactory.CreateConfirmed(
                    organizationId, memberSegment, request.ClientId,
                    BookingPricing.AtSuggested(resolvedPrice), now));
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
                // Phase M0: Booking-wide otkazivanje bivšeg člana (A) — svako AKTIVNO (Confirmed) sudjelovanje njegovog
                // Bookinga prelazi u Cancelled (zaključano, redoslijed po Id-u); terminalna sudjelovanja ostaju povijest.
                Booking booking = futureAppointment.Bookings.FirstOrDefault(b =>
                    b.ClientId == member.ClientId && b.Participations.Any(p => p.Status == ParticipationStatus.Confirmed));
                if (booking == null)
                    continue;

                List<BookingSegmentParticipation> active = booking.Participations
                    .Where(p => p.Status == ParticipationStatus.Confirmed)
                    .OrderBy(p => p.Id)
                    .ToList();
                await _participationHandler.LockForUpdate(uow, organizationId, active.Select(p => p.Id.GetValueOrDefault()));

                foreach (BookingSegmentParticipation participation in active)
                {
                    ParticipationStatus oldStatus = participation.Status;
                    ParticipationLifecycle.TrySetStatus(participation, ParticipationStatus.Cancelled);
                    ParticipationLifecycle.SetCancellationReason(participation, "Klijent uklonjen iz grupe");
                    booking.UpdatedAt = now;
                    booking.UpdatedBy = userId;
                    await _appointmentHandler.UpdateBooking(uow, booking);

                    // Isti obrazac kao BookingService/AppointmentService.ChangeToTerminalStatus — bez ovoga bi ovaj SUSTAVOM
                    // izveden (iz GroupMember odjave) prijelaz ostao bez traga tko/kada/zašto.
                    await _appointmentAuditLogHandler.Add(uow, new AppointmentAuditLog
                    {
                        Id = Guid.NewGuid(),
                        AppointmentId = futureAppointment.Id.GetValueOrDefault(),
                        BookingId = booking.Id,
                        BookingSegmentParticipationId = participation.Id,
                        ChangeType = "BookingStatus",
                        OldValue = oldStatus.ToString(),
                        NewValue = participation.Status.ToString(),
                        StatusVersion = participation.StatusVersion,
                        ChangedAt = now,
                        ChangedBy = userId
                    });

                    // Isti booking.cancelled.v1 event kao izravno otkazivanje — izvor (GroupMember odjava) ne mijenja
                    // event-tip/handler (vidi spec section 34). Ista uow transakcija kao mutacija iznad.
                    await ParticipationEvents.WriteCancelled(_outboxWriter, uow, organizationId, futureAppointment, booking, participation);
                }

                // Oslobođeno mjesto -> pokušaj promocije liste čekanja za OVAJ occurrence (spec section 39) —
                // ista promocijska logika kao izravno otkazivanje Bookinga (BookingService), ne duplicirana ovdje.
                await _waitlistPromotionService.PromoteEligibleWaiters(
                    uow, organizationId, futureAppointment.Id.GetValueOrDefault(), userId);

                // Phase M1A: status occurrencea se izvodi NAKON otkazivanja i promocije (npr. zadnji član otkazan bez
                // čekača → Cancelled; promoviran čekač → ostaje Scheduled).
                await AppointmentLifecycle.Refresh(
                    _appointmentHandler, _appointmentAuditLogHandler, uow, organizationId, futureAppointment.Id.GetValueOrDefault(), userId);
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

        // Raspon su kalendarski datumi kako ih je klijent napisao; vrijeme slota je lokalno vrijeme u efektivnoj zoni
        // poslovnice grupe (i preko DST prijelaza) — ne offset zahtjeva ni hosta (F-19 / timezone foundation).
        DateOnly fromDate = CalendarDates.FromWallDate(request.FromDate);
        DateOnly toDate = CalendarDates.FromWallDate(request.ToDate);
        List<Guid> candidateCompanyIds = candidateGroups.Select(g => g.CompanyId).Distinct().ToList();
        Dictionary<Guid, OrganizationCalendar> calendarsByCompany =
            await _organizationCalendarService.GetCompanyCalendars(organizationId, candidateCompanyIds);

        List<GroupSlot> activeSlots = candidateGroups.SelectMany(g => g.Slots.Where(s => s.IsActive)).ToList();
        List<Guid> activeSlotIds = activeSlots.Select(s => s.Id.GetValueOrDefault()).ToList();

        // Postojeći occurrencei: raspon je unija lokalnih dana [fromDate, toDate] svih uključenih poslovnica.
        HashSet<(Guid GroupSlotId, DateTimeOffset StartsAt)> existing = activeSlotIds.Count == 0
            ? new HashSet<(Guid, DateTimeOffset)>()
            : await _groupHandler.GetExistingSlotOccurrences(
                activeSlotIds,
                calendarsByCompany.Values.Min(c => c.StartOfDay(fromDate)),
                calendarsByCompany.Values.Max(c => c.ToInstant(toDate, new TimeSpan(23, 59, 59))));
        List<CompanyHoliday> holidaysForCompanies = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, candidateCompanyIds, fromDate, toDate);

        List<GroupOccurrenceCandidate> candidates = new List<GroupOccurrenceCandidate>();
        int skipped = 0;

        foreach (Group group in candidateGroups)
        {
            foreach (GroupSlot slot in group.Slots.Where(s => s.IsActive))
            {
                for (DateOnly date = fromDate; date <= toDate; date = date.AddDays(1))
                {
                    if (date.DayOfWeek != slot.DayOfWeek)
                        continue;

                    if (holidaysForCompanies.Any(h => h.CompanyId == group.CompanyId && h.Date == date))
                    {
                        skipped++;
                        continue;
                    }

                    DateTimeOffset startsAt = calendarsByCompany[group.CompanyId].ToInstant(date, slot.StartTime);
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

        // Phase M1C: provjere sudara trenera/prostorije/članova i upis idu u JEDNOJ transakciji koja najprije zaključa subjekte
        // rasporeda (treneri, pa aktivni članovi) — tek zatim (redoslijed!) advisory lock slota u AddAppointments. Provjere
        // niže tada vide sve što je konkurentno commitano prije njih.
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        await _schedulingOccupancyHandler.LockSchedulingSubjects(
            uow,
            candidates.Where(c => c.Group.DefaultTrainerId.HasValue).Select(c => c.Group.DefaultTrainerId.Value),
            candidates.SelectMany(c => c.Group.Members.Where(m => m.IsActive).Select(m => m.ClientId)),
            candidates.Where(c => c.Group.DefaultRoomId.HasValue).Select(c => c.Group.DefaultRoomId.Value));

        Dictionary<(Guid SlotId, DateTimeOffset StartsAt), List<WarningDto>> warningsByCandidate =
            await EnsureNoTrainerConflicts(organizationId, candidates, request.OverrideAvailability);
        await EnsureNoRoomConflicts(uow, organizationId, candidates);
        await EnsureNoMemberConflicts(organizationId, candidates);

        List<Appointment> toCreate = new List<Appointment>();
        List<AppointmentScheduleCellDto> createdDtos = new List<AppointmentScheduleCellDto>();

        foreach (GroupOccurrenceCandidate candidate in candidates)
        {
            Group group = candidate.Group;
            GroupSlot slot = candidate.Slot;
            DateTimeOffset startsAt = candidate.StartsAt;

            // Navigacijska svojstva se namjerno NE postavljaju (AppointmentFactory ih nikad ne postavlja) — appointment ide u
            // AddAppointments preko svježeg DbContext-a, a Service/Company/DefaultTrainer su
            // materijalizirani u kontekstu GetAll/GetById poziva pa bi ih EF pokušao ponovno umetnuti.
            // Predložena cijena se snapshotta ODMAH po članu (isti IPricingService poziv kao za Individual) —
            // grupni termin prije ovog zahvata nikad nije imao cijenu (uvijek 0 na Appointment). Amount ostaje
            // jednak SuggestedAmount i booking ostaje financijski neplaćen do stvarnog check-ina
            // (BookingService.ResolveCoverage), koji po potrebi razrješava paket/naplatu.
            ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, group.ServiceId, group.CompanyId, startsAt);

            // Booking (Status=Confirmed) se stvara ODMAH za svakog aktivnog člana grupe u trenutku generiranja
            // occurrencea — preferirani model iz spec section 11: daje eksplicitnu po-terminsku evidenciju
            // sudjelovanja prije nego se itko čekira, čime otkazivanje/no-show jednog člana unaprijed postaje
            // moguće (vidi BookingService.SetStatus). Gost izvan popisa članova i dalje dobiva ad-hoc Booking
            // tek na check-inu (BookingService.AddBooking / SetStatus s nepostojećim bookingom).
            //
            // Phase M1B — grupa (do GroupSegmentTemplates) generira JEDAN segment izravno iz svojih zadanih vrijednosti
            // (usluga, trener ako postoji, dvorana, trajanje usluge); sudionici segmenta su aktivni članovi.
            SegmentPlan plan = new SegmentPlan(
                group.ServiceId, startsAt, startsAt.AddMinutes(group.Service.DefaultDurationMinutes),
                group.DefaultTrainerId.HasValue ? new[] { group.DefaultTrainerId.Value } : Array.Empty<Guid>(),
                group.DefaultRoomId,
                group.Members.Where(m => m.IsActive)
                    .Select(m => new ParticipantPlan(m.ClientId, BookingPricing.AtSuggested(resolvedPrice)))
                    .ToList());
            Appointment appointment = AppointmentFactory.CreateGroupOccurrence(
                organizationId, group.CompanyId, group.Id.GetValueOrDefault(), slot.Id.GetValueOrDefault(),
                userId, DateTimeOffset.UtcNow, plan);
            Guid appointmentId = appointment.Id.GetValueOrDefault();

            toCreate.Add(appointment);

            warningsByCandidate.TryGetValue((slot.Id.GetValueOrDefault(), startsAt), out List<WarningDto> occurrenceWarnings);

            createdDtos.Add(new AppointmentScheduleCellDto
            {
                Id = appointmentId,
                PlannedStart = plan.PlannedStart,
                PlannedEnd = plan.PlannedEnd,
                Segments = appointment.Segments.Select(segment => GeneratedSegmentDto(segment, group)).ToList(),
                StartsAt = plan.PlannedStart,
                DurationMinutes = (int)(plan.PlannedEnd - plan.PlannedStart).TotalMinutes,
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

        // Phase D3A: jedinstvenost (slot, početak) provodi GroupHandler.AddAppointments (advisory lock + ponovna provjera
        // pod lockom) umjesto nekadašnjeg unique indeksa nad appointments.starts_at — isti ishod za pozivatelja.
        bool added = await _groupHandler.AddAppointments(uow, toCreate);
        if (!added)
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

        await uow.CommitAsync();

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
        // Datum i lokalno vrijeme svakog kandidata u efektivnoj zoni poslovnice njegove grupe.
        Dictionary<Guid, OrganizationCalendar> calendarsByCompany = await _organizationCalendarService.GetCompanyCalendars(
            organizationId, candidates.Select(c => c.Group.CompanyId));
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

            List<OccupancySlot> candidateAppointments = await _schedulingOccupancyHandler.GetForEmployeeInRange(
                organizationId, employeeId, rangeFrom, rangeTo);

            List<ScheduleBreak> candidateBreaks = await _scheduleBreakHandler.GetForEmployeeInRange(
                organizationId, employeeId, rangeFrom, rangeTo);

            List<OrganizationCalendar> employeeCalendars = ordered.Select(c => calendarsByCompany[c.Group.CompanyId]).Distinct().ToList();
            List<RosterEntry> rosterEntriesInRange = await _rosterEntryHandler.GetForPeriod(
                organizationId, new List<Guid> { employeeId },
                employeeCalendars.Min(c => c.LocalDate(rangeFrom)), employeeCalendars.Max(c => c.LocalDate(rangeTo)));

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

                bool appointmentHit = candidateAppointments.Any(a => a.Overlaps(startsAt, occurrenceEnd));

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
                    SchedulingInterval.Overlaps(b.StartsAt, b.StartsAt.AddMinutes(b.DurationMinutes), startsAt, occurrenceEnd));

                OrganizationCalendar calendar = calendarsByCompany[candidate.Group.CompanyId];
                DateOnly localDate = calendar.LocalDate(startsAt);

                bool absenceHit = absences.Any(a =>
                    a.DateFrom <= localDate && (a.DateTo == null || localDate <= a.DateTo.Value));

                List<RosterEntry> rosterEntriesForOccurrence = rosterEntriesInRange
                    .Where(e => !e.RosterType.IsAbsence && e.DateFrom == localDate)
                    .ToList();

                WorkingHoursTemplate companyTemplate = companyTemplatesById[candidate.Group.CompanyId];

                // Praznik je za ovu granu već obrađen ranije u GenerateAppointments (tiho preskačanje kandidata
                // na dan praznika) — holidayHit je uvijek false ovdje, vidi IsWithinWorkingHours doc-komentar.
                bool withinHours = absenceHit || IsWithinWorkingHours(
                    employeeTemplate, companyTemplate, rosterEntriesForOccurrence, localDate, calendar.LocalTimeOfDay(startsAt), durationMinutes);

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

    /// <summary>Phase M1D — fizički kapacitet prostorije u OSOBAMA (trener ako postoji + aktivni članovi) za svaki kandidat,
    /// vremenski raslojeno zajedno s postojećim segmentima i ostalim kandidatima istog batcha (više grupa u istoj prostoriji
    /// smije istovremeno ako stanu). Pozivatelj je zaključao prostorije. Povrede se prijavljuju po occurrenceu
    /// (RECURRING_CONFLICT, razlog prostorija) — cijeli batch abortira.</summary>
    private async Task EnsureNoRoomConflicts(IUnitOfWork uow, Guid organizationId, List<GroupOccurrenceCandidate> candidates)
    {
        Dictionary<SegmentClaim, GroupOccurrenceCandidate> byClaim = new(ReferenceEqualityComparer.Instance);
        foreach (GroupOccurrenceCandidate candidate in candidates.Where(c => c.Group.DefaultRoomId.HasValue))
        {
            DateTimeOffset startsAt = candidate.StartsAt;
            byClaim[new SegmentClaim(null, startsAt, startsAt.AddMinutes(candidate.Group.Service.DefaultDurationMinutes), Array.Empty<Guid>(), Array.Empty<Guid>())
            {
                RoomId = candidate.Group.DefaultRoomId,
                RoomPeople = RoomPeopleCount.Of(candidate.Group.DefaultTrainerId.HasValue ? 1 : 0, candidate.Group.Members.Count(m => m.IsActive))
            }] = candidate;
        }

        List<CapacityViolation> violations = await SchedulingConflictGuard.FindCapacityViolations(
            _schedulingOccupancyHandler, uow, organizationId, byClaim.Keys.ToList());
        List<RecurringConflictDetail> conflicts = violations
            .SelectMany(v => v.Claims)
            .Select(claim => byClaim[claim].StartsAt)
            .Distinct()
            .OrderBy(date => date)
            .Select(date => new RecurringConflictDetail { Date = date, Reason = ErrorCodes.RecurringConflictReasonRoom })
            .ToList();

        if (conflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Neki termini u nizu premašili bi kapacitet prostorije.",
                new { conflicts });
    }

    /// <summary>Standardno pravilo preklapanja (newStart &lt; existingEnd &amp;&amp; newEnd &gt; existingStart) —
    /// susjedni intervali (kraj jednog = početak drugog) NISU sudar. Dijeli ga svaka batch-vs-batch provjera
    /// u ovoj klasi (trener/soba/član) umjesto da svaka duplicira vlastitu formulu preklapanja.</summary>
    private static bool IntervalsOverlap(DateTimeOffset aStart, int aDurationMinutes, DateTimeOffset bStart, int bDurationMinutes)
    {
        return SchedulingInterval.Overlaps(aStart, aStart.AddMinutes(aDurationMinutes), bStart, bStart.AddMinutes(bDurationMinutes));
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

        List<OccupancySlot> candidateAppointments = await _schedulingOccupancyHandler.GetForClientsInRange(
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
                a.Overlaps(startsAt, occurrenceEnd) && a.ActiveClientIds.Any(memberClientIds.Contains));

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
        DateOnly localDate, TimeSpan localStart, int durationMinutes)
    {
        TimeSpan start = localStart;
        TimeSpan end = start + TimeSpan.FromMinutes(durationMinutes);

        (List<WorkingHoursCalculator.Interval> employeeIntervals, _) =
            WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterEntriesForDate, localDate);
        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, new List<CompanyHoliday>(), localDate);

        return WorkingHoursCalculator.IsWithinIntervals(employeeIntervals, start, end)
            && WorkingHoursCalculator.IsWithinIntervals(companyIntervals, start, end);
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
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, companyId);
        DateOnly today = calendar.LocalDate(DateTimeOffset.UtcNow);

        foreach ((DayOfWeek dayOfWeek, TimeSpan startTime) in slots)
        {
            DateOnly representativeDate = NextOccurrenceDate(today, dayOfWeek);

            if (!IsWithinWorkingHours(employeeTemplate, companyTemplate, new List<RosterEntry>(), representativeDate, startTime, durationMinutes))
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

    private static DateOnly NextOccurrenceDate(DateOnly from, DayOfWeek dayOfWeek)
    {
        int diff = ((int)dayOfWeek - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(diff);
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

    /// <summary>Segment netom generiranog occurrencea — navigacije segmenta nisu postavljene (svjež DbContext pri upisu),
    /// pa se nazivi uzimaju iz već učitane grupe (iste vrijednosti iz kojih je segment i nastao).</summary>
    private static AppointmentSegmentDto GeneratedSegmentDto(AppointmentSegment segment, Group group)
    {
        AppointmentSegmentDto dto = AppointmentSegmentReadModel.ToDto(segment);
        dto.ServiceName = group.Service?.Name;
        dto.ServiceCategoryColorHex = group.Service?.ColorHex;
        dto.RoomName = group.DefaultRoom?.Name;
        foreach (AppointmentSegmentEmployeeDto employee in dto.Employees)
            if (group.DefaultTrainer != null && employee.EmployeeId == group.DefaultTrainerId)
                employee.EmployeeName = $"{group.DefaultTrainer.FirstName} {group.DefaultTrainer.LastName}";
        return dto;
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
        AppointmentRange range = AppointmentRange.Of(a);
        SingleSegmentProjection compat = SingleSegmentProjection.Of(a);
        return new AppointmentScheduleCellDto
        {
            Id = a.Id.GetValueOrDefault(),
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(a),
            StartsAt = range.PlannedStart,
            DurationMinutes = range.SpanMinutes,
            ServiceId = compat.ServiceId,
            ServiceName = compat.ServiceName,
            ServiceCategoryColorHex = compat.ServiceColorHex,
            EmployeeId = compat.EmployeeId,
            EmployeeName = compat.EmployeeName,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            RoomId = compat.RoomId,
            RoomName = compat.RoomName,
            Status = a.Status,
            IsCancelled = a.Status == AppointmentStatus.Cancelled,
            Form = AppointmentForm.Group,
            GroupId = a.GroupId,
            GroupName = groupName,
            AttendanceCount = a.Bookings.SelectMany(b => b.Participations).Count(p => p.Status == ParticipationStatus.Completed),
            ExpectedCount = expectedCount
        };
    }
}
