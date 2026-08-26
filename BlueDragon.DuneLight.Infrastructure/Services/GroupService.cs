using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
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
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class GroupService : IGroupService
{
    private readonly IGroupHandler _groupHandler;
    private readonly IGroupAuditLogHandler _auditLogHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IClientHandler _clientHandler;
    private readonly ICompanyHolidayHandler _companyHolidayHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly IWorkingHoursTemplateHandler _workingHoursTemplateHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public GroupService(
        IGroupHandler groupHandler,
        IGroupAuditLogHandler auditLogHandler,
        IServiceHandler serviceHandler,
        ICompanyHandler companyHandler,
        IEmployeeHandler employeeHandler,
        IClientHandler clientHandler,
        ICompanyHolidayHandler companyHolidayHandler,
        IAppointmentHandler appointmentHandler,
        IRosterEntryHandler rosterEntryHandler,
        IWorkingHoursTemplateHandler workingHoursTemplateHandler,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _groupHandler = groupHandler;
        _auditLogHandler = auditLogHandler;
        _serviceHandler = serviceHandler;
        _companyHandler = companyHandler;
        _employeeHandler = employeeHandler;
        _clientHandler = clientHandler;
        _companyHolidayHandler = companyHolidayHandler;
        _appointmentHandler = appointmentHandler;
        _rosterEntryHandler = rosterEntryHandler;
        _workingHoursTemplateHandler = workingHoursTemplateHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    public async Task<GroupDto> Create(Guid organizationId, Guid userId, GroupCreateRequest request)
    {
        if (request.Slots == null || request.Slots.Count == 0)
            throw new ValidationAppException("Grupa mora imati barem jedan slot.");

        foreach (GroupSlotCreateRequest slot in request.Slots)
            ValidateSlotTime(slot.StartTime);

        await EnsureServiceExists(organizationId, request.ServiceId);
        await EnsureCompanyExists(organizationId, request.CompanyId);
        await EnsureTrainerExists(organizationId, request.DefaultTrainerId);

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
        return await GetDtoById(organizationId, groupId);
    }

    public async Task<GroupDto> Update(Guid organizationId, Guid userId, Guid id, GroupUpdateRequest request)
    {
        Group existing = await _groupHandler.GetByIdLight(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Group", id);

        await EnsureServiceExists(organizationId, request.ServiceId);
        await EnsureCompanyExists(organizationId, request.CompanyId);
        await EnsureTrainerExists(organizationId, request.DefaultTrainerId);

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

        return await GetDtoById(organizationId, id);
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

            await uow.CommitAsync();
        }

        return await GetDtoById(organizationId, id);
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

        await _groupHandler.AddSlot(new GroupSlot
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });

        return await GetDtoById(organizationId, groupId);
    }

    public async Task<GroupDto> UpdateSlot(Guid organizationId, Guid userId, Guid groupId, Guid slotId, GroupSlotUpdateRequest request)
    {
        await EnsureGroupExists(organizationId, groupId);

        GroupSlot slot = await _groupHandler.GetSlotById(organizationId, groupId, slotId);
        if (slot == null)
            throw new NotFoundAppException("GroupSlot", slotId);

        ValidateSlotTime(request.StartTime);

        // Izmjena ne dira već generirane termine (GroupSlotId ostaje isti) — utječe samo na sljedeća generiranja.
        slot.DayOfWeek = request.DayOfWeek;
        slot.StartTime = request.StartTime;
        await _groupHandler.UpdateSlot(slot);

        return await GetDtoById(organizationId, groupId);
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

        GroupMember existingActive = await _groupHandler.GetActiveMember(organizationId, groupId, request.ClientId);
        if (existingActive != null)
            throw new BusinessRuleException(ErrorCodes.AlreadyMember, "Klijent je već aktivan član ove grupe.");

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

            await uow.CommitAsync();
        }

        GroupDto dto = await GetDtoById(organizationId, groupId);

        int activeMembers = await _groupHandler.CountActiveMembers(groupId);
        if (activeMembers > group.Capacity)
            dto.Warnings.Add("Kapacitet grupe je premašen.");

        return dto;
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
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });

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

        await EnsureNoTrainerConflicts(organizationId, candidates);

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
            toCreate.Add(new Appointment
            {
                Id = appointmentId,
                OrganizationId = organizationId,
                Form = AppointmentForm.Group,
                StartsAt = startsAt,
                DurationMinutes = group.Service.DefaultDurationMinutes,
                ServiceId = group.ServiceId,
                EmployeeId = group.DefaultTrainerId,
                CompanyId = group.CompanyId,
                Amount = 0,
                SuggestedAmount = 0,
                IsAmountManuallyOverridden = false,
                PaymentMethod = null,
                IsPaid = false,
                Status = AppointmentStatus.Scheduled,
                GroupId = group.Id,
                GroupSlotId = slot.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            });

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
                Status = AppointmentStatus.Scheduled,
                IsCancelled = false,
                Form = AppointmentForm.Group,
                GroupId = group.Id,
                GroupName = group.Name,
                AttendanceCount = 0,
                ExpectedCount = group.Members.Count(m => m.IsActive)
            });
        }

        await _groupHandler.AddAppointments(toCreate);

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

    /// <summary>Tvrda, unaprijedna provjera za GenerateAppointments — ako bilo koji kandidat u nizu sudara s postojećim
    /// terminom trenera ili pada izvan njegovog radnog vremena/rostera, baca RECURRING_CONFLICT (409) prije nego se
    /// bilo što spremi (isti obrazac kao AppointmentService.EnsureNoRecurringConflicts). Grupe bez dodijeljenog
    /// trenera (DefaultTrainerId == null) se preskaču — nema koga provjeriti. Kandidati se grupiraju po treneru kako
    /// bi se termini/roster/predlošci dohvatili JEDNOM po treneru za cijeli raspon, umjesto po occurrenceu.</summary>
    private async Task EnsureNoTrainerConflicts(Guid organizationId, List<GroupOccurrenceCandidate> candidates)
    {
        List<RecurringConflictDetail> conflicts = new List<RecurringConflictDetail>();

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

            List<RosterEntry> rosterEntriesInRange = await _rosterEntryHandler.GetForPeriod(
                organizationId, new List<Guid> { employeeId }, rangeFrom, rangeTo);

            List<RosterEntry> absences = rosterEntriesInRange.Where(e => e.RosterType.IsAbsence).ToList();

            WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);

            Dictionary<Guid, WorkingHoursTemplate> companyTemplatesById = new Dictionary<Guid, WorkingHoursTemplate>();
            foreach (Guid companyId in ordered.Select(c => c.Group.CompanyId).Distinct())
                companyTemplatesById[companyId] = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);

            foreach (GroupOccurrenceCandidate candidate in ordered)
            {
                int durationMinutes = candidate.Group.Service.DefaultDurationMinutes;
                DateTimeOffset startsAt = candidate.StartsAt;
                DateTimeOffset occurrenceEnd = startsAt.AddMinutes(durationMinutes);

                bool appointmentHit = candidateAppointments.Any(a =>
                    a.StartsAt < occurrenceEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes));

                if (appointmentHit)
                {
                    conflicts.Add(new RecurringConflictDetail { Date = startsAt, Reason = ErrorCodes.RecurringConflictReasonAppointment });
                    continue;
                }

                bool absenceHit = absences.Any(a =>
                    a.DateFrom.Date <= startsAt.Date && (a.DateTo == null || startsAt.Date <= a.DateTo.Value.Date));

                if (absenceHit)
                {
                    conflicts.Add(new RecurringConflictDetail { Date = startsAt, Reason = ErrorCodes.RecurringConflictReasonRosterAbsence });
                    continue;
                }

                List<RosterEntry> rosterEntriesForOccurrence = rosterEntriesInRange
                    .Where(e => !e.RosterType.IsAbsence && e.DateFrom.Date == startsAt.Date)
                    .ToList();

                WorkingHoursTemplate companyTemplate = companyTemplatesById[candidate.Group.CompanyId];

                if (!IsWithinWorkingHours(employeeTemplate, companyTemplate, rosterEntriesForOccurrence, startsAt, durationMinutes))
                    conflicts.Add(new RecurringConflictDetail { Date = startsAt, Reason = ErrorCodes.RecurringConflictReasonOutsideWorkingHours });
            }
        }

        if (conflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Neki termini u nizu se sudaraju s postojećim obavezama.",
                new { conflicts });
    }

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

    private async Task EnsureServiceExists(Guid organizationId, Guid serviceId)
    {
        ServiceEntity service = await _serviceHandler.GetById(organizationId, serviceId);
        if (service == null)
            throw new NotFoundAppException("Service", serviceId);
    }

    private async Task EnsureCompanyExists(Guid organizationId, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);
    }

    private async Task EnsureTrainerExists(Guid organizationId, Guid? employeeId)
    {
        if (!employeeId.HasValue)
            return;

        Employee employee = await _employeeHandler.GetById(organizationId, employeeId.Value);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId.Value);
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
            Status = a.Status,
            IsCancelled = a.Status == AppointmentStatus.Cancelled,
            Form = AppointmentForm.Group,
            GroupId = a.GroupId,
            GroupName = groupName,
            AttendanceCount = a.Attendances.Count(x => x.Attended == true),
            ExpectedCount = expectedCount
        };
    }
}
