using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
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
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Phase M1F — grupa = kalendar (slotovi = sidro occurrencea) + poslovnica + članovi + PREDLOŠCI SEGMENATA (s osobljem).
/// Occurrence je JEDAN termin s po jednim segmentom za svaki predložak; član sudjeluje SAMO u segmentima predložaka koje je
/// eksplicitno odabrao (jedan Booking po klijentu po occurrenceu, jedno sudjelovanje po odabranom segmentu). Kapacitet
/// predloška je MEKI (poslovni) i smije se prekoračiti samo eksplicitno uz groups.capacity.override; prostorija/resursi/
/// preklapanja zaposlenika i klijenata ostaju tvrdi. Phase M1H: nema plosnatog ("jednopredloškog") ugovora — grupa s jednom
/// uslugom je grupa s jednim predloškom, a odabir predložaka člana je uvijek eksplicitan.
/// </summary>
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
    private readonly IResourceHandler _resourceHandler;
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
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly IBookingSegmentParticipationHandler _participationHandler;
    private readonly IGrantResolver _grantResolver;

    public GroupService(
        IGroupHandler groupHandler,
        IGroupAuditLogHandler auditLogHandler,
        IAppointmentAuditLogHandler appointmentAuditLogHandler,
        IServiceHandler serviceHandler,
        IServiceAvailabilityService serviceAvailabilityService,
        IPricingService pricingService,
        ICompanyHandler companyHandler,
        IRoomHandler roomHandler,
        IResourceHandler resourceHandler,
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
        IOrganizationSettingsService organizationSettingsService,
        IBookingSegmentParticipationHandler participationHandler,
        IGrantResolver grantResolver)
    {
        _organizationCalendarService = organizationCalendarService;
        _organizationSettingsService = organizationSettingsService;
        _participationHandler = participationHandler;
        _grantResolver = grantResolver;
        _groupHandler = groupHandler;
        _auditLogHandler = auditLogHandler;
        _appointmentAuditLogHandler = appointmentAuditLogHandler;
        _serviceHandler = serviceHandler;
        _serviceAvailabilityService = serviceAvailabilityService;
        _pricingService = pricingService;
        _companyHandler = companyHandler;
        _roomHandler = roomHandler;
        _resourceHandler = resourceHandler;
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

    /// <summary>Isti IPricingService poziv kao AppointmentService.ResolveServicePrice — centralni resolver.</summary>
    private Task<ResolvePriceResponse> ResolveServicePrice(
        Guid organizationId, Guid serviceId, Guid companyId, Guid? pricingEmployeeId, DateTimeOffset date)
    {
        return _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = serviceId,
            CompanyId = companyId,
            EmployeeId = pricingEmployeeId,
            Date = date
        });
    }

    #region Group definition

    public async Task<GroupDto> Create(Guid organizationId, Guid userId, GroupCreateRequest request)
    {
        if (request.Slots == null || request.Slots.Count == 0)
            throw new ValidationAppException("Grupa mora imati barem jedan slot.");

        foreach (GroupSlotCreateRequest slot in request.Slots)
            ValidateSlotTime(slot.StartTime);

        if (request.SegmentTemplates == null || request.SegmentTemplates.Count == 0)
            throw new ValidationAppException("Grupa mora imati barem jedan predložak segmenta.");
        await EnsureCompany(organizationId, request.CompanyId);

        Guid groupId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<GroupSegmentTemplate> templates = new();
        foreach (GroupSegmentTemplateRequest templateRequest in request.SegmentTemplates)
            templates.Add(await BuildTemplate(organizationId, groupId, request.CompanyId, templateRequest, now));
        EnsureAnchorTemplate(templates);

        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, request.CompanyId, templates, request.Slots.Select(s => (s.DayOfWeek, s.StartTime)).ToList());

        Group group = new Group
        {
            Id = groupId,
            OrganizationId = organizationId,
            Name = request.Name,
            CompanyId = request.CompanyId,
            IsActive = true,
            Note = request.Note,
            CreatedAt = now,
            CreatedBy = userId,
            SegmentTemplates = templates
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

    /// <summary>Phase M1H — opći podaci grupe (naziv, poslovnica, napomena). Izvršna definicija (usluga, vrijeme, osoblje,
    /// izvor cijene, prostorija, resursi, kapacitet) se mijenja isključivo po predlošku (/segment-templates). Promjena
    /// poslovnice zahtijeva da svaki predložak ostane strukturno valjan za novu poslovnicu.</summary>
    public async Task<GroupDto> Update(Guid organizationId, Guid userId, Guid id, GroupUpdateRequest request)
    {
        Group existing = await _groupHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Group", id);

        await EnsureCompany(organizationId, request.CompanyId);
        foreach (GroupSegmentTemplate template in existing.SegmentTemplates)
            await ValidateTemplate(organizationId, request.CompanyId, template.Employees.Select(e => e.EmployeeId).ToList(),
                template.ServiceId, template.RoomId, template.Resources.Select(r => (r.ResourceId, r.QuantityRequired)).ToList());

        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, request.CompanyId, existing.SegmentTemplates,
            existing.Slots.Where(s => s.IsActive).Select(s => (s.DayOfWeek, s.StartTime)).ToList());

        DateTimeOffset now = DateTimeOffset.UtcNow;
        existing.Name = request.Name;
        existing.CompanyId = request.CompanyId;
        existing.Note = request.Note;
        existing.UpdatedAt = now;
        existing.UpdatedBy = userId;

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _groupHandler.UpdateScalar(uow, Scalar(existing));
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

            // Deaktivacija NE dira već generirane termine niti njihove Bookinge — samo zaustavlja buduće generiranje i
            // blokira nove članove (vidi prijašnju napomenu; nepromijenjeno u M1F).
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

    #endregion

    #region Slots

    public async Task<GroupDto> AddSlot(Guid organizationId, Guid userId, Guid groupId, GroupSlotCreateRequest request)
    {
        Group group = await _groupHandler.GetById(organizationId, groupId);
        if (group == null)
            throw new NotFoundAppException("Group", groupId);

        ValidateSlotTime(request.StartTime);

        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, group.CompanyId, group.SegmentTemplates, new() { (request.DayOfWeek, request.StartTime) });

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
        Group group = await _groupHandler.GetById(organizationId, groupId);
        if (group == null)
            throw new NotFoundAppException("Group", groupId);

        GroupSlot slot = await _groupHandler.GetSlotById(organizationId, groupId, slotId);
        if (slot == null)
            throw new NotFoundAppException("GroupSlot", slotId);

        ValidateSlotTime(request.StartTime);

        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, group.CompanyId, group.SegmentTemplates, new() { (request.DayOfWeek, request.StartTime) });

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

    #endregion

    #region Segment templates

    public async Task<GroupDto> AddSegmentTemplate(Guid organizationId, Guid userId, Guid groupId, GroupSegmentTemplateRequest request)
    {
        Group group = await _groupHandler.GetById(organizationId, groupId)
            ?? throw new NotFoundAppException("Group", groupId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GroupSegmentTemplate template = await BuildTemplate(organizationId, groupId, group.CompanyId, request, now);
        EnsureAnchorTemplate(group.SegmentTemplates.Append(template).ToList());

        List<WarningDto> warnings = await ComputeWorkingHoursWarnings(
            organizationId, group.CompanyId, new[] { template },
            group.Slots.Where(s => s.IsActive).Select(s => (s.DayOfWeek, s.StartTime)).ToList());

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            uow.Context.GroupSegmentTemplates.Add(template);
            await uow.Context.SaveChangesAsync();
            await _auditLogHandler.Add(uow, new GroupAuditLog
            {
                Id = Guid.NewGuid(), GroupId = groupId, ChangeType = "SegmentTemplateAdded", NewValue = template.Id.ToString(),
                ChangedAt = now, ChangedBy = userId
            });
            await uow.CommitAsync();
        }

        GroupDto dto = await GetDtoById(organizationId, groupId);
        dto.Warnings.AddRange(warnings);
        return dto;
    }

    /// <summary>Izmjena predloška vrijedi za BUDUĆA generiranja; već generirani segmenti su konkretni snapshotovi i ne
    /// propagiraju se (nema modela propagacije na budeće occurrencee u ovoj fazi). Smanjenje kapaciteta ispod postojećeg
    /// broja ne uklanja nikoga.</summary>
    public async Task<GroupDto> UpdateSegmentTemplate(
        Guid organizationId, Guid userId, Guid groupId, Guid templateId, GroupSegmentTemplateRequest request)
    {
        Group group = await _groupHandler.GetById(organizationId, groupId)
            ?? throw new NotFoundAppException("Group", groupId);
        GroupSegmentTemplate existing = group.SegmentTemplates.SingleOrDefault(t => t.Id == templateId)
            ?? throw new NotFoundAppException("GroupSegmentTemplate", templateId);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        // Phase M1H: izmjena predloška je POTPUNA zamjena njegove definicije (uključivo osoblje i izvor cijene).
        GroupSegmentTemplate updated = await BuildTemplate(organizationId, groupId, group.CompanyId, request, now);
        EnsureAnchorTemplate(group.SegmentTemplates.Where(t => t.Id != templateId).Append(updated).ToList());

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            GroupSegmentTemplate tracked = await uow.Context.GroupSegmentTemplates
                .Include(t => t.Resources).SingleAsync(t => t.Id == templateId);
            string oldCapacity = tracked.Capacity.ToString();
            tracked.ServiceId = updated.ServiceId;
            tracked.StartOffsetMinutes = updated.StartOffsetMinutes;
            tracked.DurationMinutes = updated.DurationMinutes;
            tracked.RoomId = updated.RoomId;
            tracked.Capacity = updated.Capacity;
            tracked.PricingMode = updated.PricingMode;
            tracked.PricingEmployeeId = updated.PricingEmployeeId;
            tracked.UpdatedAt = now;
            uow.Context.GroupSegmentTemplateResources.RemoveRange(tracked.Resources);
            await uow.Context.SaveChangesAsync();
            await ReplaceTemplateStaff(uow, templateId, updated.Employees.Select(e => e.EmployeeId).ToList());
            uow.Context.GroupSegmentTemplateResources.AddRange(updated.Resources.Select(r => new GroupSegmentTemplateResource
            {
                GroupSegmentTemplateId = templateId, ResourceId = r.ResourceId, QuantityRequired = r.QuantityRequired
            }));
            await uow.Context.SaveChangesAsync();

            await _auditLogHandler.Add(uow, new GroupAuditLog
            {
                Id = Guid.NewGuid(), GroupId = groupId, ChangeType = "SegmentTemplateChanged",
                OldValue = $"{templateId}:capacity={oldCapacity}", NewValue = $"{templateId}:capacity={updated.Capacity}",
                ChangedAt = now, ChangedBy = userId
            });
            await uow.CommitAsync();
        }

        return await GetDtoById(organizationId, groupId);
    }

    /// <summary>Brisanje je ograničeno: nikad posljednji predložak; ne smije ga koristiti nijedan generirani segment (povijest
    /// occurrencea se ne briše kaskadom) niti ga birati aktivni član (članove treba prvo preusmjeriti). Odabiri NEAKTIVNIH
    /// članova (povijest napuštenog članstva) se uklanjaju.</summary>
    public async Task<GroupDto> RemoveSegmentTemplate(Guid organizationId, Guid userId, Guid groupId, Guid templateId)
    {
        Group group = await _groupHandler.GetById(organizationId, groupId)
            ?? throw new NotFoundAppException("Group", groupId);
        if (group.SegmentTemplates.All(t => t.Id != templateId))
            throw new NotFoundAppException("GroupSegmentTemplate", templateId);
        if (group.SegmentTemplates.Count <= 1)
            throw new BusinessRuleException(ErrorCodes.LastGroupSegmentTemplate, "Grupa mora zadržati barem jedan predložak segmenta.");
        EnsureAnchorTemplate(group.SegmentTemplates.Where(t => t.Id != templateId).ToList());
        if (await _groupHandler.IsTemplateUsedBySegments(templateId))
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete,
                "Predložak je generirao segmente termina (povijest occurrencea) i ne može se obrisati.");
        if (await _groupHandler.IsTemplateSelectedByActiveMember(templateId))
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete,
                "Predložak biraju aktivni članovi — najprije promijenite njihov odabir predložaka.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            uow.Context.GroupMemberSegmentTemplates.RemoveRange(
                uow.Context.GroupMemberSegmentTemplates.Where(x => x.GroupSegmentTemplateId == templateId));
            uow.Context.GroupSegmentTemplates.Remove(await uow.Context.GroupSegmentTemplates.SingleAsync(t => t.Id == templateId));
            await uow.Context.SaveChangesAsync();
            await _auditLogHandler.Add(uow, new GroupAuditLog
            {
                Id = Guid.NewGuid(), GroupId = groupId, ChangeType = "SegmentTemplateRemoved", OldValue = templateId.ToString(),
                ChangedAt = now, ChangedBy = userId
            });
            await uow.CommitAsync();
        }

        return await GetDtoById(organizationId, groupId);
    }

    /// <summary>Phase M1G: osoblje predloška = request.EmployeeIds (skup, bez duplikata; prazno = bez trenera). Izvor cijene po
    /// istom pravilu kao segment.</summary>
    private async Task<GroupSegmentTemplate> BuildTemplate(
        Guid organizationId, Guid groupId, Guid companyId, GroupSegmentTemplateRequest request, DateTimeOffset now)
    {
        if (request == null)
            throw new ValidationAppException("Predložak segmenta je obavezan.");
        if (request.StartOffsetMinutes < 0 || request.StartOffsetMinutes >= 1440)
            throw new ValidationAppException("Pomak predloška mora biti između 0 i 1439 minuta.");
        if (request.Capacity < 1)
            throw new ValidationAppException("Kapacitet predloška mora biti veći od 0.");

        List<(Guid ResourceId, int Quantity)> resources = (request.Resources ?? new List<GroupSegmentTemplateResourceRequest>())
            .Select(r => (r.ResourceId, r.QuantityRequired)).ToList();
        List<Guid> employees = request.EmployeeIds?.ToList() ?? new List<Guid>();
        if (employees.Distinct().Count() != employees.Count)
            throw new ValidationAppException("Isti zaposlenik se na predlošku smije navesti samo jednom.");
        PricingSourceValue pricingSource = SegmentPricingSource.Normalize(employees, request.PricingMode, request.PricingEmployeeId);
        ServiceEntity service = await ValidateTemplate(organizationId, companyId, employees, request.ServiceId, request.RoomId, resources);

        // Zadano trajanje usluge je samo PRIJEDLOG; predložak posjeduje svoje trajanje.
        int duration = request.DurationMinutes ?? service.DefaultDurationMinutes;
        if (duration <= 0 || duration > 1440)
            throw new ValidationAppException("Trajanje predloška mora biti između 1 i 1440 minuta.");

        Guid templateId = Guid.NewGuid();
        return new GroupSegmentTemplate
        {
            Id = templateId,
            GroupId = groupId,
            ServiceId = request.ServiceId,
            StartOffsetMinutes = request.StartOffsetMinutes,
            DurationMinutes = duration,
            RoomId = request.RoomId,
            Capacity = request.Capacity,
            PricingMode = pricingSource.Mode,
            PricingEmployeeId = pricingSource.PricingEmployeeId,
            CreatedAt = now,
            Resources = resources.Select(r => new GroupSegmentTemplateResource
            {
                GroupSegmentTemplateId = templateId, ResourceId = r.ResourceId, QuantityRequired = r.Quantity
            }).ToList(),
            Employees = employees.Select(e => new GroupSegmentTemplateEmployee { GroupSegmentTemplateId = templateId, EmployeeId = e }).ToList()
        };
    }

    /// <summary>Zamjenjuje osoblje predloška (skup) unutar transakcije pozivatelja.</summary>
    private static async Task ReplaceTemplateStaff(IUnitOfWork uow, Guid templateId, IReadOnlyCollection<Guid> employeeIds)
    {
        uow.Context.GroupSegmentTemplateEmployees.RemoveRange(
            await uow.Context.GroupSegmentTemplateEmployees.Where(e => e.GroupSegmentTemplateId == templateId).ToListAsync());
        await uow.Context.SaveChangesAsync();
        uow.Context.GroupSegmentTemplateEmployees.AddRange(
            employeeIds.Select(e => new GroupSegmentTemplateEmployee { GroupSegmentTemplateId = templateId, EmployeeId = e }));
        await uow.Context.SaveChangesAsync();
    }

    /// <summary>Strukturna valjanost predloška: usluga (grupni način, ponuđena u poslovnici, SVAKI zaposlenik predloška je
    /// aktivan, dodijeljen poslovnici i smije je izvoditi), prostorija i resursi (aktivni, ista poslovnica, količina &gt; 0,
    /// svaki jednom).</summary>
    private async Task<ServiceEntity> ValidateTemplate(
        Guid organizationId, Guid companyId, IReadOnlyCollection<Guid> employeeIds, Guid serviceId, Guid? roomId,
        List<(Guid ResourceId, int Quantity)> resources)
    {
        ServiceEntity service = await EnsureServiceExists(organizationId, serviceId);
        await EnsureStructuralEligibility(organizationId, service, companyId, null);
        foreach (Guid employeeId in employeeIds)
            await EnsureStructuralEligibility(organizationId, service, companyId, employeeId);
        await EnsureRoomExists(organizationId, companyId, roomId);

        if (resources.Any(r => r.Quantity <= 0))
            throw new ValidationAppException("Količina resursa mora biti veća od 0.");
        if (resources.Select(r => r.ResourceId).Distinct().Count() != resources.Count)
            throw new ValidationAppException("Isti resurs se na predlošku smije navesti samo jednom.");
        foreach ((Guid resourceId, int _) in resources)
        {
            Resource resource = await _resourceHandler.GetById(organizationId, resourceId)
                ?? throw new NotFoundAppException("Resource", resourceId);
            if (!resource.IsActive)
                throw new BusinessRuleException(ErrorCodes.InactiveResource, $"Resurs '{resource.Name}' nije aktivan.");
            if (resource.CompanyId != companyId)
                throw new BusinessRuleException(ErrorCodes.ResourceCompanyMismatch, "Resurs ne pripada odabranoj poslovnici.");
        }

        return service;
    }

    /// <summary>Sidro occurrencea (vrijeme slota) je početak occurrencea: barem jedan predložak ima pomak 0. Time je raspon
    /// generiranog termina uvijek [sidro, ...) i postojeći identitet occurrencea (slot, početak) ostaje nepromijenjen.</summary>
    private static void EnsureAnchorTemplate(IReadOnlyCollection<GroupSegmentTemplate> templates)
    {
        if (templates.Count == 0)
            throw new ValidationAppException("Grupa mora imati barem jedan predložak segmenta.");
        if (templates.All(t => t.StartOffsetMinutes != 0))
            throw new ValidationAppException("Barem jedan predložak mora počinjati u vrijeme slota (pomak 0).");
    }

    private async Task EnsureCompany(Guid organizationId, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId)
            ?? throw new NotFoundAppException("Company", companyId);
        if (!company.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Poslovnica '{company.Name}' nije aktivna.");
    }

    #endregion

    #region Members

    public async Task<GroupDto> AddMember(Guid organizationId, Guid userId, Guid groupId, GroupMemberAddRequest request)
    {
        Group group = await _groupHandler.GetById(organizationId, groupId)
            ?? throw new NotFoundAppException("Group", groupId);

        Client client = await _clientHandler.GetByIdLight(organizationId, request.ClientId);
        if (client == null)
            throw new NotFoundAppException("Client", request.ClientId);

        // Anonimizirani ili neaktivni klijent ne smije ući u novu aktivnu poslovnu relaciju (ovdje: grupno članstvo).
        if (!client.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveClient, "Klijent nije aktivan.");
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran.");

        GroupMember existingActive = await _groupHandler.GetActiveMember(organizationId, groupId, request.ClientId);
        if (existingActive != null)
            throw new BusinessRuleException(ErrorCodes.AlreadyMember, "Klijent je već aktivan član ove grupe.");

        List<GroupSegmentTemplate> selected = ResolveSelection(group, request.SegmentTemplateIds);
        await GroupCapacityOverride.EnsureAllowed(_grantResolver, organizationId, userId, request.OverrideCapacity);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid memberId = Guid.NewGuid();

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Klijentov raspored se zaključava PRVI u transakciji, zatim prostorije budućih segmenata, pa redak grupe (razina
            // "group slot" u globalnom redoslijedu) i tek onda Appointment lockovi niže.
            await _schedulingOccupancyHandler.LockSchedulingSubjects(uow, Array.Empty<Guid>(), new[] { request.ClientId });
            await LockMembershipScope(uow, organizationId, groupId);
            if (await uow.Context.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.ClientId == request.ClientId && m.IsActive))
                throw new BusinessRuleException(ErrorCodes.AlreadyMember, "Klijent je već aktivan član ove grupe.");

            // Meki kapacitet ČLANSTVA po odabranom predlošku (aktivni članovi koji ga biraju) — pod lockom grupe, pa dva
            // konkurentna upisa za posljednje mjesto ne mogu oba proći. Prekoračenje samo eksplicitno.
            await EnsureMembershipCapacity(uow, selected, request.OverrideCapacity);

            await _groupHandler.AddMember(uow, new GroupMember
            {
                Id = memberId,
                GroupId = groupId,
                ClientId = request.ClientId,
                JoinedAt = now,
                IsActive = true,
                CreatedAt = now
            });
            uow.Context.GroupMemberSegmentTemplates.AddRange(selected.Select(t => new GroupMemberSegmentTemplate
            {
                GroupMemberId = memberId, GroupSegmentTemplateId = t.Id.GetValueOrDefault(), GroupId = groupId
            }));
            await uow.Context.SaveChangesAsync();

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

            // Novi član odmah sudjeluje na već generiranim BUDUĆIM occurrenceima — samo u segmentima odabranih predložaka.
            List<Appointment> futureAppointments = await _appointmentHandler.GetFutureScheduledForGroup(uow, organizationId, groupId);
            await JoinFutureOccurrences(uow, organizationId, request.ClientId, futureAppointments,
                selected.Select(t => t.Id.GetValueOrDefault()).ToHashSet(), request.OverrideCapacity, now);

            await uow.CommitAsync();
        }

        return await GetDtoById(organizationId, groupId);
    }

    public async Task<GroupDto> ChangeMemberSegmentTemplates(
        Guid organizationId, Guid userId, Guid groupId, Guid memberId, GroupMemberSegmentTemplatesRequest request)
    {
        Group group = await _groupHandler.GetById(organizationId, groupId)
            ?? throw new NotFoundAppException("Group", groupId);
        GroupMember member = group.Members.SingleOrDefault(m => m.Id == memberId)
            ?? throw new NotFoundAppException("GroupMember", memberId);
        if (request.SegmentTemplateIds == null || request.SegmentTemplateIds.Count == 0)
            throw new ValidationAppException("Aktivan član mora odabrati barem jedan predložak segmenta.");

        List<GroupSegmentTemplate> selected = ResolveSelection(group, request.SegmentTemplateIds);
        HashSet<Guid> target = selected.Select(t => t.Id.GetValueOrDefault()).ToHashSet();
        HashSet<Guid> current = member.SegmentTemplates.Select(x => x.GroupSegmentTemplateId).ToHashSet();
        if (current.SetEquals(target))
            return await GetDtoById(organizationId, groupId);

        await GroupCapacityOverride.EnsureAllowed(_grantResolver, organizationId, userId, request.OverrideCapacity);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _schedulingOccupancyHandler.LockSchedulingSubjects(uow, Array.Empty<Guid>(), new[] { member.ClientId });
            await LockMembershipScope(uow, organizationId, groupId);

            // Phase M1F.1: razlika se računa iz odabira pročitanog POD lockom grupe (konkurentna izmjena istog člana je već
            // commitana ili čeka) — nikad iz zastarjelog odabira pročitanog prije transakcije.
            if (!await uow.Context.GroupMembers.AnyAsync(m => m.Id == memberId && m.GroupId == groupId && m.IsActive))
                throw new NotFoundAppException("GroupMember", memberId);
            current = (await uow.Context.GroupMemberSegmentTemplates
                .Where(x => x.GroupMemberId == memberId).Select(x => x.GroupSegmentTemplateId).ToListAsync()).ToHashSet();
            List<GroupSegmentTemplate> added = selected.Where(t => !current.Contains(t.Id.GetValueOrDefault())).ToList();
            HashSet<Guid> removed = current.Where(id => !target.Contains(id)).ToHashSet();
            await EnsureMembershipCapacity(uow, added, request.OverrideCapacity);

            uow.Context.GroupMemberSegmentTemplates.RemoveRange(
                uow.Context.GroupMemberSegmentTemplates.Where(x => x.GroupMemberId == memberId && removed.Contains(x.GroupSegmentTemplateId)));
            uow.Context.GroupMemberSegmentTemplates.AddRange(added.Select(t => new GroupMemberSegmentTemplate
            {
                GroupMemberId = memberId, GroupSegmentTemplateId = t.Id.GetValueOrDefault(), GroupId = groupId
            }));
            await uow.Context.SaveChangesAsync();

            await _auditLogHandler.Add(uow, new GroupAuditLog
            {
                Id = Guid.NewGuid(), GroupId = groupId, ChangeType = "MemberSegmentTemplates",
                OldValue = string.Join(",", current.OrderBy(x => x)), NewValue = string.Join(",", target.OrderBy(x => x)),
                ChangedAt = now, ChangedBy = userId
            });

            // Budući occurrencei: uklonjeni predlošci — netaknuto sudjelovanje se uklanja, s poviješću se otkazuje (Confirmed);
            // dodani — novo sudjelovanje na istom Bookingu occurrencea. Prošli/odrađeni occurrencei se ne diraju.
            List<Appointment> futureAppointments = await _appointmentHandler.GetFutureScheduledForGroup(uow, organizationId, groupId);
            if (removed.Count > 0)
                await WithdrawFromFutureSegments(uow, organizationId, userId, member.ClientId, futureAppointments,
                    segment => segment.GroupSegmentTemplateId.HasValue && removed.Contains(segment.GroupSegmentTemplateId.Value),
                    "Klijent više ne sudjeluje u ovom dijelu grupe", removeUntouched: true, now);
            if (added.Count > 0)
            {
                futureAppointments = await _appointmentHandler.GetFutureScheduledForGroup(uow, organizationId, groupId);
                await JoinFutureOccurrences(uow, organizationId, member.ClientId, futureAppointments,
                    added.Select(t => t.Id.GetValueOrDefault()).ToHashSet(), request.OverrideCapacity, now);
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
            // Phase M1F.1: isti redoslijed kao AddMember (klijent → prostorije → grupa uz reviziju članstva → termini), pa se
            // uklanjanje serijalizira s generiranjem occurrencea i ostalim izmjenama članstva iste grupe.
            await _schedulingOccupancyHandler.LockSchedulingSubjects(uow, Array.Empty<Guid>(), new[] { member.ClientId });
            await LockMembershipScope(uow, organizationId, groupId);

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

            // Napuštanje grupe povlači bivšeg člana sa SVIH budućih segmenata već generiranih occurrencea (svih predložaka) po
            // centraliziranom pravilu povijesti (Phase M1F.1): NETAKNUTO sudjelovanje (ParticipationHistory.IsUntouched) se
            // briše (prazan Booking s njim) — ponovni upis tada normalno stvara novo; sudjelovanje S POVIJEŠĆU se otkazuje uz
            // kasno-otkazivanje po početku NJEGOVOG segmenta. Prošli/odrađeni segmenti se ne diraju.
            List<Appointment> futureAppointments = await _appointmentHandler.GetFutureScheduledForGroup(uow, organizationId, groupId);
            await WithdrawFromFutureSegments(uow, organizationId, userId, member.ClientId, futureAppointments,
                _ => true, "Klijent uklonjen iz grupe", removeUntouched: true, now);

            await uow.CommitAsync();
        }

        return await GetDtoById(organizationId, groupId);
    }

    /// <summary>Eksplicitan odabir predložaka člana (barem jedan, bez duplikata, bez međusobnog preklapanja).</summary>
    private static List<GroupSegmentTemplate> ResolveSelection(Group group, List<Guid> templateIds)
    {
        // Phase M1H: odabir je uvijek eksplicitan (i za grupu s jednim predloškom) — nikad se ne zaključuje.
        if (templateIds == null || templateIds.Count == 0)
            throw new ValidationAppException("Odaberite predloške segmenata u kojima član sudjeluje (SegmentTemplateIds).");

        if (templateIds.Distinct().Count() != templateIds.Count)
            throw new ValidationAppException("Isti predložak se smije odabrati samo jednom.");

        List<GroupSegmentTemplate> selected = templateIds
            .Select(id => group.SegmentTemplates.SingleOrDefault(t => t.Id == id) ?? throw new NotFoundAppException("GroupSegmentTemplate", id))
            .ToList();

        // Klijent ne smije sudjelovati u dva preklapajuća segmenta istog occurrencea (tvrda invarijanta klijenta): pomaci
        // predložaka su relativni istom sidru, pa se preklapanje vidi već na odabiru.
        for (int i = 0; i < selected.Count; i++)
            for (int j = i + 1; j < selected.Count; j++)
                if (TemplatesOverlap(selected[i], selected[j]))
                    throw new BusinessRuleException(ErrorCodes.AppointmentOverlap,
                        SchedulingConflicts.ClientMessage(null) + " (odabrani predlošci grupe se vremenski preklapaju)");

        return selected;
    }

    private static bool TemplatesOverlap(GroupSegmentTemplate a, GroupSegmentTemplate b) =>
        a.StartOffsetMinutes < b.StartOffsetMinutes + b.DurationMinutes && b.StartOffsetMinutes < a.StartOffsetMinutes + a.DurationMinutes;

    /// <summary>Serijalizira izmjene članstva iste grupe (meki kapacitet članstva se broji pod ovim lockom) i serijalizira ih s
    /// generiranjem occurrencea te grupe (revizija članstva). Prostorije
    /// budućih segmenata grupe (subjekti rasporeda) se zaključavaju PRIJE — globalni redoslijed: subjekti → grupa →
    /// Appointment → sudjelovanja.</summary>
    private async Task LockMembershipScope(IUnitOfWork uow, Guid organizationId, Guid groupId)
    {
        List<Guid> roomIds = await uow.Context.AppointmentSegments
            .Where(seg => seg.Appointment.GroupId == groupId && seg.Appointment.OrganizationId == organizationId &&
                          seg.RoomId != null && seg.PlannedStart >= DateTimeOffset.UtcNow)
            .Select(seg => seg.RoomId.Value)
            .Distinct()
            .ToListAsync();
        await _schedulingOccupancyHandler.LockSchedulingSubjects(uow, Array.Empty<Guid>(), Array.Empty<Guid>(), roomIds);
        // Phase M1F.1: jedan UPDATE i zaključava redak grupe (do commita) i povećava reviziju članstva — generiranje koje je
        // sastavilo planove iz starije revizije to vidi pod svojim FOR SHARE lockom i ponovno ih sastavlja (vidi
        // GenerateAppointments). Redak grupe dolazi NAKON subjekata rasporeda, ISTO kao u generiranju — nema obrnutog redoslijeda.
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dunelight.groups SET membership_version = membership_version + 1 WHERE organization_id = {organizationId} AND id = {groupId}");
    }

    private async Task EnsureMembershipCapacity(IUnitOfWork uow, IEnumerable<GroupSegmentTemplate> templates, bool overrideCapacity)
    {
        foreach (GroupSegmentTemplate template in templates)
        {
            int selecting = await _groupHandler.CountActiveMembersSelecting(uow, template.Id.GetValueOrDefault());
            if (selecting >= template.Capacity && !overrideCapacity)
                throw new BusinessRuleException(
                    ErrorCodes.GroupCapacityReached, "Segment grupe je popunjen — kapacitet je dosegnut.",
                    new { segmentTemplateId = template.Id, capacity = template.Capacity, activeMemberCount = selecting });
        }
    }

    /// <summary>
    /// Klijent dobiva sudjelovanje na BUDUĆIM segmentima zadanih predložaka već generiranih occurrencea (postojeći Booking
    /// occurrencea se ponovno koristi; segment na kojem klijent već ima sudjelovanje u bilo kojem statusu se preskače —
    /// reaktivacija ide kroz prijelaz statusa sudjelovanja). Sve-ili-ništa: tvrda preklapanja klijenta (uključivo sestrinske
    /// segmente) i fizički kapacitet prostorije provjeravaju se PRIJE ijednog upisa (RECURRING_CONFLICT / ROOM), zatim po
    /// occurrenceu pod Appointment lockom meki kapacitet segmenta (override samo eksplicitno) i optimistička provjera okvira.
    /// Pozivatelj je zaključao klijenta.
    /// </summary>
    private async Task JoinFutureOccurrences(
        IUnitOfWork uow, Guid organizationId, Guid clientId, List<Appointment> futureAppointments, HashSet<Guid> templateIds,
        bool overrideCapacity, DateTimeOffset now)
    {
        List<(Appointment Appointment, List<AppointmentSegment> Segments)> targets = futureAppointments
            .Select(a => (a, a.Segments
                .Where(s => s.GroupSegmentTemplateId.HasValue && templateIds.Contains(s.GroupSegmentTemplateId.Value) && s.PlannedStart >= now)
                .Where(s => !a.Bookings.Any(b => b.ClientId == clientId && b.Participations.Any(p => p.AppointmentSegmentId == s.Id)))
                .OrderBy(s => s.PlannedStart).ThenBy(s => s.Id)
                .ToList()))
            .Where(t => t.Item2.Count > 0)
            .ToList();
        if (targets.Count == 0)
            return;

        List<AppointmentSegment> allSegments = targets.SelectMany(t => t.Segments).ToList();
        // Prostorije pa resursi (globalni redoslijed nakon klijenta), PRIJE Appointment lockova niže.
        await _schedulingOccupancyHandler.LockSchedulingSubjects(
            uow, Array.Empty<Guid>(), Array.Empty<Guid>(),
            allSegments.Where(s => s.RoomId.HasValue).Select(s => s.RoomId.Value).Distinct());

        List<RecurringConflictDetail> conflicts = new();
        foreach (AppointmentSegment segment in allSegments)
        {
            List<OccupancySlot> overlapping = await _schedulingOccupancyHandler.GetOverlappingForClients(
                uow, organizationId, new[] { clientId }, segment.PlannedStart, segment.PlannedEnd, excludedSegmentIds: null);
            bool batchOverlap = allSegments.Any(other => other != segment && SchedulingInterval.Overlaps(
                other.PlannedStart, other.PlannedEnd, segment.PlannedStart, segment.PlannedEnd));
            if (overlapping.Count > 0 || batchOverlap)
                conflicts.Add(new RecurringConflictDetail { Date = segment.PlannedStart, Reason = ErrorCodes.RecurringConflictReasonAppointment });
        }

        if (conflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Klijent je već zakazan u vrijeme jednog ili više budućih termina ove grupe.",
                new { conflicts });

        // Fizički kapacitet prostorije (osobe) — tvrdo, neovisno o mekom kapacitetu grupe i o overrideu.
        List<SegmentClaim> roomClaims = allSegments
            .Where(s => s.RoomId.HasValue)
            .Select(s => new SegmentClaim(null, s.PlannedStart, s.PlannedEnd, Array.Empty<Guid>(), Array.Empty<Guid>())
            {
                RoomId = s.RoomId,
                RoomPeople = 1
            })
            .ToList();
        CapacityViolation roomViolation = (await SchedulingConflictGuard.FindCapacityViolations(
            _schedulingOccupancyHandler, uow, organizationId, roomClaims)).FirstOrDefault();
        if (roomViolation != null)
            throw roomViolation.ToException();

        // Occurrencei determinističkim redoslijedom (GetFutureScheduledForGroup: početak, Id) — konkurentni AddMember/RemoveMember
        // zaključavaju termine istim redoslijedom.
        foreach ((Appointment appointment, List<AppointmentSegment> segments) in targets)
        {
            Guid appointmentId = appointment.Id.GetValueOrDefault();
            Booking booking = appointment.Bookings.FirstOrDefault(b => b.ClientId == clientId);
            bool newBooking = booking == null;
            if (newBooking)
                booking = BookingFactory.NewContainer(organizationId, appointmentId, clientId, now);

            List<BookingSegmentParticipation> created = new();
            foreach (AppointmentSegment segment in segments)
            {
                await GroupCapacityGuard.EnsureAvailable(_appointmentHandler, uow, organizationId, appointmentId, segment.Id.GetValueOrDefault(), overrideCapacity);
                // Provjere gore su rađene nad PROČITANIM okvirom segmenta — pod Appointment lockom se potvrđuje da ga segmentna
                // naredba (vrijeme/prostorija) u međuvremenu nije promijenila.
                await SegmentSnapshot.VerifyUnderLock(_appointmentHandler, uow, organizationId, appointmentId,
                    new[]
                    {
                        SegmentSnapshot.Capture(appointment, segment,
                            await _schedulingOccupancyHandler.GetSegmentResources(uow, segment.Id.GetValueOrDefault()))
                    }, includeParticipants: false);

                ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, segment.ServiceId, appointment.CompanyId, SegmentPricingSource.PricingEmployeeOf(segment), segment.PlannedStart);
                created.Add(BookingFactory.AddParticipation(booking, segment, ParticipationStatus.Confirmed, BookingPricing.AtSuggested(resolvedPrice), now));
            }

            if (newBooking)
                uow.Context.Bookings.Add(booking);
            else
                uow.Context.BookingSegmentParticipations.AddRange(created);
            await uow.Context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Klijent izlazi iz BUDUĆIH segmenata (filtar) već generiranih occurrencea. Samo aktivna (Confirmed) sudjelovanja na
    /// segmentima koji još nisu počeli; terminalna i prošla ostaju povijest. <paramref name="removeUntouched"/>: netaknuto
    /// sudjelovanje (bez statusa, paketa, naplate) se uklanja (prazan Booking s njim); inače / s poviješću → Cancelled uz
    /// kasno-otkazivanje po POČETKU SEGMENTA tog sudjelovanja (centralna BookingCancellationPolicy), audit i događaj.
    /// Oslobođeno mjesto promovira SAMO listu čekanja tog segmenta; status occurrencea se zatim izvodi.
    /// </summary>
    private async Task WithdrawFromFutureSegments(
        IUnitOfWork uow, Guid organizationId, Guid userId, Guid clientId, List<Appointment> futureAppointments,
        Func<AppointmentSegment, bool> segmentFilter, string reason, bool removeUntouched, DateTimeOffset now)
    {
        int cutoffMinutes = await _organizationSettingsService.GetCancellationCutoffMinutes(organizationId);

        foreach (Appointment appointment in futureAppointments)
        {
            Booking booking = appointment.Bookings.FirstOrDefault(b => b.ClientId == clientId);
            if (booking == null)
                continue;

            HashSet<Guid> segmentIds = appointment.Segments
                .Where(s => s.PlannedStart > now && segmentFilter(s))
                .Select(s => s.Id.GetValueOrDefault())
                .ToHashSet();
            List<BookingSegmentParticipation> active = booking.Participations
                .Where(p => segmentIds.Contains(p.AppointmentSegmentId) && p.Status == ParticipationStatus.Confirmed)
                .OrderBy(p => p.Id)
                .ToList();
            if (active.Count == 0)
                continue;

            Guid appointmentId = appointment.Id.GetValueOrDefault();
            if (await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId) == null)
                throw new NotFoundAppException("Appointment", appointmentId);
            await _participationHandler.LockForUpdate(uow, organizationId, active.Select(p => p.Id.GetValueOrDefault()));

            if (removeUntouched)
            {
                foreach (BookingSegmentParticipation participation in active)
                {
                    await uow.Context.Entry(participation).Collection(p => p.CheckoutItems).LoadAsync();
                }

                List<BookingSegmentParticipation> untouched = active.Where(ParticipationHistory.IsUntouched).ToList();
                if (untouched.Count > 0)
                {
                    await ParticipationHistory.RemoveUntouchedParticipations(uow.Context, new[] { booking }, untouched,
                        "Sudjelovanje ima povijest — otkazuje se umjesto brisanja.");
                    foreach (BookingSegmentParticipation participation in untouched)
                        await _appointmentAuditLogHandler.Add(uow, new AppointmentAuditLog
                        {
                            Id = Guid.NewGuid(), AppointmentId = appointmentId, ChangeType = "ParticipationRemoved",
                            OldValue = participation.Id.ToString(), NewValue = reason, ChangedAt = now, ChangedBy = userId
                        });
                    active = active.Except(untouched).ToList();
                }
            }

            foreach (BookingSegmentParticipation participation in active)
            {
                ParticipationStatus oldStatus = participation.Status;
                ParticipationLifecycle.TrySetStatus(participation, ParticipationStatus.Cancelled);
                ParticipationLifecycle.SetCancellationReason(participation, reason);
                // Phase M1F: kasno otkazivanje po POČETKU SEGMENTA ovog sudjelovanja (ne grupe ni termina) — isto pravilo
                // kao otkazivanje sudjelovanja/Bookinga/termina.
                ParticipationLifecycle.SetLateCancellation(participation, BookingCancellationPolicy.IsLateCancellation(
                    ExecutionContextResolver.ForParticipation(appointment, booking, participation), now, cutoffMinutes));
                booking.UpdatedAt = now;
                booking.UpdatedBy = userId;
                await _appointmentHandler.UpdateBooking(uow, booking);

                await _appointmentAuditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointmentId,
                    BookingId = booking.Id,
                    BookingSegmentParticipationId = participation.Id,
                    ChangeType = "BookingStatus",
                    OldValue = oldStatus.ToString(),
                    NewValue = participation.Status.ToString(),
                    StatusVersion = participation.StatusVersion,
                    ChangedAt = now,
                    ChangedBy = userId
                });

                await ParticipationEvents.WriteCancelled(_outboxWriter, uow, organizationId, appointment, booking, participation);
            }

            await uow.Context.SaveChangesAsync();

            // Oslobođeno mjesto → promocija liste čekanja (po segmentu; samo segmenti kojima se oslobodilo mjesto imaju
            // slobodnih mjesta).
            await _waitlistPromotionService.PromoteEligibleWaiters(uow, organizationId, appointmentId, userId);

            await AppointmentLifecycle.Refresh(_appointmentHandler, _appointmentAuditLogHandler, uow, organizationId, appointmentId, userId);
        }
    }

    #endregion

    #region Occurrence generation

    /// <summary>Jedan segment kandidata occurrencea (iz jednog predloška).</summary>
    private sealed record CandidateSegment(GroupSegmentTemplate Template, DateTimeOffset Start, DateTimeOffset End, List<Guid> ClientIds)
    {
        public int DurationMinutes => (int)(End - Start).TotalMinutes;
    }

    private sealed class GroupOccurrenceCandidate
    {
        public Group Group { get; init; }
        public GroupSlot Slot { get; init; }

        /// <summary>Sidro occurrencea = početak predloška s pomakom 0 = početak termina (identitet (slot, početak)).</summary>
        public DateTimeOffset StartsAt { get; init; }

        public List<CandidateSegment> Segments { get; init; } = new();
    }

    /// <summary>Planovi occurrencea sastavljeni su iz revizije članstva koja je u međuvremenu promijenjena (vidi
    /// <see cref="EnsureMembershipSnapshotCurrent"/>) — pokušaj se odbacuje (rollback) i sastavlja ponovno.</summary>
    private sealed class StaleMembershipSnapshotException : Exception
    {
    }

    private const int GenerationAttempts = 3;

    /// <summary>
    /// Phase M1F.1 — generiranje je konzistentno s JEDNIM commitanim stanjem članstva svake grupe: planovi se sastavljaju iz
    /// pročitane revizije članstva, a prije upisa (pod FOR SHARE lockom redaka grupa, nakon subjekata rasporeda) se provjerava
    /// da je revizija još važeća. Izmjena članstva koja je commitala u međuvremenu → pokušaj se ponavlja s novim stanjem
    /// (najviše <see cref="GenerationAttempts"/> puta, zatim CONCURRENCY_CONFLICT). Izmjena članstva koja dođe NAKON provjere
    /// čeka na commit generiranja (FOR UPDATE vs FOR SHARE) i zatim propagira u novi occurrence. Lockovi su po grupi — različite
    /// grupe se ne serijaliziraju.
    /// </summary>
    public async Task<GenerateGroupAppointmentsResult> GenerateAppointments(
        Guid organizationId, Guid userId, GenerateGroupAppointmentsRequest request)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await GenerateAppointmentsAttempt(organizationId, userId, request);
            }
            catch (StaleMembershipSnapshotException) when (attempt < GenerationAttempts)
            {
            }
            catch (StaleMembershipSnapshotException)
            {
                throw new BusinessRuleException(ErrorCodes.ConcurrencyConflict,
                    "Članstvo grupe se upravo mijenjalo tijekom generiranja termina — pokušajte ponovno.");
            }
        }
    }

    /// <summary>Pod FOR SHARE lockom redaka grupa (uzlazno po Id-u; nakon subjekata rasporeda, prije locka slota) — revizija
    /// članstva iz koje su sastavljeni planovi mora biti trenutna. FOR SHARE blokira konkurentnu izmjenu članstva (UPDATE
    /// revizije) do commita generiranja, pa ona zatim vidi novi occurrence i propagira u njega.</summary>
    private static async Task EnsureMembershipSnapshotCurrent(IUnitOfWork uow, Guid organizationId, IReadOnlyCollection<Group> groups)
    {
        if (groups.Count == 0)
            return;

        Guid[] ids = groups.Select(g => g.Id.GetValueOrDefault()).Distinct().OrderBy(id => id).ToArray();
        List<GroupVersionRow> current = await uow.Context.Database.SqlQuery<GroupVersionRow>(
                $"SELECT id AS \"Id\", membership_version AS \"MembershipVersion\" FROM dunelight.groups WHERE organization_id = {organizationId} AND id = ANY({ids}) ORDER BY id FOR SHARE")
            .ToListAsync();
        foreach (Group group in groups)
            if (current.SingleOrDefault(r => r.Id == group.Id)?.MembershipVersion != group.MembershipVersion)
                throw new StaleMembershipSnapshotException();
    }

    private sealed class GroupVersionRow
    {
        public Guid Id { get; set; }
        public long MembershipVersion { get; set; }
    }

    private async Task<GenerateGroupAppointmentsResult> GenerateAppointmentsAttempt(
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

        // Raspon su kalendarski datumi kako ih je klijent napisao; vrijeme slota (+ pomak predloška) je lokalno vrijeme u
        // efektivnoj zoni poslovnice grupe (zidni sat, i preko DST prijelaza) — ne offset zahtjeva ni hosta.
        DateOnly fromDate = CalendarDates.FromWallDate(request.FromDate);
        DateOnly toDate = CalendarDates.FromWallDate(request.ToDate);
        List<Guid> candidateCompanyIds = candidateGroups.Select(g => g.CompanyId).Distinct().ToList();
        Dictionary<Guid, OrganizationCalendar> calendarsByCompany =
            await _organizationCalendarService.GetCompanyCalendars(organizationId, candidateCompanyIds);

        List<GroupSlot> activeSlots = candidateGroups.SelectMany(g => g.Slots.Where(s => s.IsActive)).ToList();
        List<Guid> activeSlotIds = activeSlots.Select(s => s.Id.GetValueOrDefault()).ToList();

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
            OrganizationCalendar calendar = calendarsByCompany[group.CompanyId];
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

                    DateTimeOffset startsAt = calendar.ToInstant(date, slot.StartTime);
                    (Guid, DateTimeOffset) key = (slot.Id.GetValueOrDefault(), startsAt);

                    if (existing.Contains(key))
                    {
                        skipped++;
                        continue;
                    }

                    existing.Add(key);
                    candidates.Add(new GroupOccurrenceCandidate
                    {
                        Group = group,
                        Slot = slot,
                        StartsAt = startsAt,
                        Segments = group.SegmentTemplates
                            .OrderBy(t => t.StartOffsetMinutes).ThenBy(t => t.Id)
                            .Select(t =>
                            {
                                (DateTimeOffset start, DateTimeOffset end) = TemplateWindow(calendar, date, slot.StartTime, t);
                                return new CandidateSegment(t, start, end, group.Members
                                    .Where(m => m.IsActive && m.SegmentTemplates.Any(x => x.GroupSegmentTemplateId == t.Id))
                                    .Select(m => m.ClientId)
                                    .ToList());
                            })
                            .ToList()
                    });
                }
            }
        }

        // Batch validacija PRIJE ijednog upisa (candidate vs persisted state + candidate vs candidate + sestrinski segmenti).
        EnsureNoDuplicateOccurrences(candidates);

        // Phase M1F: meki kapacitet NE blokira generiranje — članstvo nastalo normalno ili uz ovlašteni override (ili kapacitet
        // naknadno smanjen) se reproducira; nitko se ne izbacuje. Tvrda ograničenja (zaposlenik, klijent, prostorija, resursi)
        // i dalje blokiraju cijeli batch.
        List<CandidateSegment> allSegments = candidates.SelectMany(c => c.Segments).ToList();

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        await _schedulingOccupancyHandler.LockSchedulingSubjects(
            uow,
            allSegments.SelectMany(s => s.Template.Employees.Select(e => e.EmployeeId)),
            allSegments.SelectMany(s => s.ClientIds),
            allSegments.Where(s => s.Template.RoomId.HasValue).Select(s => s.Template.RoomId.Value),
            allSegments.SelectMany(s => s.Template.Resources.Select(r => r.ResourceId)));

        // Phase M1F.1: članstvo iz kojeg su planovi sastavljeni mora biti trenutno (i ostaje takvo do commita).
        await EnsureMembershipSnapshotCurrent(uow, organizationId,
            candidates.Select(c => c.Group).GroupBy(g => g.Id).Select(g => g.First()).ToList());

        Dictionary<(Guid SlotId, DateTimeOffset StartsAt), List<WarningDto>> warningsByCandidate =
            await EnsureNoTrainerConflicts(organizationId, candidates, request.OverrideAvailability);
        await EnsureNoRoomConflicts(uow, organizationId, candidates);
        await EnsureNoMemberConflicts(organizationId, candidates);

        // Autoritativna tvrda provjera ciljnog stanja (ista kao kreiranje termina): svi predloženi segmenti međusobno i
        // prema postojećem stanju — zaposlenik, klijent, prostorija (osobe), resursi (količina).
        await SchedulingConflictGuard.Claim(_schedulingOccupancyHandler, uow, organizationId, candidates
            .SelectMany(c => c.Segments.Select(s => ClaimOf(c, s)))
            .ToList());

        List<Appointment> toCreate = new List<Appointment>();
        List<AppointmentScheduleCellDto> createdDtos = new List<AppointmentScheduleCellDto>();

        foreach (GroupOccurrenceCandidate candidate in candidates)
        {
            Group group = candidate.Group;
            GroupSlot slot = candidate.Slot;

            // Predložena cijena po SUDJELOVANJU: usluga predloška tog segmenta i početak segmenta. Booking ostaje
            // financijski neplaćen do check-ina (BookingService.ResolveCoverage).
            List<SegmentPlan> plans = new();
            foreach (CandidateSegment segment in candidate.Segments)
            {
                // Phase M1G: osoblje i izvor cijene se KOPIRAJU iz predloška (generiranje ne bira drugi izvor).
                PricingSourceValue pricingSource = new(segment.Template.PricingMode, segment.Template.PricingEmployeeId);
                ResolvePriceResponse resolvedPrice = await ResolveServicePrice(
                    organizationId, segment.Template.ServiceId, group.CompanyId, pricingSource.PricingEmployeeId, segment.Start);
                plans.Add(new SegmentPlan(
                    segment.Template.ServiceId, segment.Start, segment.End,
                    segment.Template.Employees.Select(e => e.EmployeeId).ToList(),
                    segment.Template.RoomId,
                    segment.ClientIds.Select(c => new ParticipantPlan(c, BookingPricing.AtSuggested(resolvedPrice))).ToList(),
                    segment.Template.Resources.Select(r => new SegmentResourcePlan(r.ResourceId, r.QuantityRequired)).ToList(),
                    segment.Template.Id,
                    pricingSource));
            }

            Appointment appointment = AppointmentFactory.CreateGroupOccurrence(
                organizationId, group.CompanyId, group.Id.GetValueOrDefault(), slot.Id.GetValueOrDefault(),
                userId, DateTimeOffset.UtcNow, plans);
            toCreate.Add(appointment);

            warningsByCandidate.TryGetValue((slot.Id.GetValueOrDefault(), candidate.StartsAt), out List<WarningDto> occurrenceWarnings);
            AppointmentRange range = AppointmentRange.Of(appointment);

            createdDtos.Add(new AppointmentScheduleCellDto
            {
                Id = appointment.Id.GetValueOrDefault(),
                PlannedStart = range.PlannedStart,
                PlannedEnd = range.PlannedEnd,
                Segments = appointment.Segments.Select(segment => GeneratedSegmentDto(segment, group)).ToList(),
                CompanyId = group.CompanyId,
                CompanyName = group.Company?.Name,
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

        // Jedinstvenost (slot, početak) provodi GroupHandler.AddAppointments (advisory lock + ponovna provjera pod lockom).
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
            Created = createdDtos.OrderBy(a => a.PlannedStart).ToList()
        };
    }

    /// <summary>Lokalni (zidni) raspon segmenta: početak = sidro + pomak, kraj = početak + trajanje — oba razriješena kroz
    /// kalendar poslovnice (DST). Kraj koji bi na prijelazu pao na/prije početka koristi apsolutno trajanje.</summary>
    private static (DateTimeOffset Start, DateTimeOffset End) TemplateWindow(
        OrganizationCalendar calendar, DateOnly date, TimeSpan slotStart, GroupSegmentTemplate template)
    {
        TimeSpan localStart = slotStart + TimeSpan.FromMinutes(template.StartOffsetMinutes);
        DateTimeOffset start = calendar.ToInstant(date, localStart);
        DateTimeOffset end = calendar.ToInstant(date, localStart + TimeSpan.FromMinutes(template.DurationMinutes));
        if (end <= start)
            end = start.AddMinutes(template.DurationMinutes);
        return (start, end);
    }

    private static SegmentClaim ClaimOf(GroupOccurrenceCandidate candidate, CandidateSegment segment)
    {
        List<Guid> employees = segment.Template.Employees.Select(e => e.EmployeeId).ToList();
        return new SegmentClaim(null, segment.Start, segment.End, employees, segment.ClientIds)
        {
            RoomId = segment.Template.RoomId,
            RoomPeople = RoomPeopleCount.Of(employees.Count, segment.ClientIds.Count),
            Resources = segment.Template.Resources.Select(r => new ResourceClaim(r.ResourceId, r.QuantityRequired)).ToList()
        };
    }

    public async Task<List<ClientGroupMembershipDto>> GetMembershipsByClient(Guid organizationId, Guid clientId)
    {
        List<GroupMember> memberships = await _groupHandler.GetMembershipsByClient(organizationId, clientId);
        return memberships.Select(m => new ClientGroupMembershipDto
        {
            GroupId = m.GroupId,
            GroupName = m.Group?.Name,
            ServiceNames = m.Group == null ? new List<string>() : m.Group.SegmentTemplates
                .Where(t => m.SegmentTemplates.Any(x => x.GroupSegmentTemplateId == t.Id))
                .OrderBy(t => t.StartOffsetMinutes).Select(t => t.Service?.Name).ToList(),
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

    /// <summary>Za GenerateAppointments — STVARNI sudar trenera (postojeći termin/grupa ILI drugi segment istog batcha,
    /// uključivo sestrinske segmente istog occurrencea) uvijek baca RECURRING_CONFLICT prije ikakvog upisa. Odsutnost,
    /// izvan radnog vremena, pauza su tvrda blokada osim uz overrideAvailability (tada upozorenje po occurrenceu). Provjerava
    /// se SVAKI segment (početak i trajanje predloška). Grupe bez trenera se preskaču.</summary>
    private async Task<Dictionary<(Guid SlotId, DateTimeOffset StartsAt), List<WarningDto>>> EnsureNoTrainerConflicts(
        Guid organizationId, List<GroupOccurrenceCandidate> candidates, bool overrideAvailability)
    {
        Dictionary<Guid, OrganizationCalendar> calendarsByCompany = await _organizationCalendarService.GetCompanyCalendars(
            organizationId, candidates.Select(c => c.Group.CompanyId));
        List<RecurringConflictDetail> hardConflicts = new List<RecurringConflictDetail>();
        Dictionary<(Guid, DateTimeOffset), List<WarningDto>> warningsByCandidate = new Dictionary<(Guid, DateTimeOffset), List<WarningDto>>();

        // Phase M1G: SVAKI zaposlenik svakog segmenta (osoblje predloška) prolazi istu provjeru neovisno.
        var byEmployee = candidates
            .SelectMany(c => c.Segments.SelectMany(s => s.Template.Employees.Select(e => (EmployeeId: e.EmployeeId, Candidate: c, Segment: s))))
            .GroupBy(x => x.EmployeeId);

        foreach (var employeeItems in byEmployee)
        {
            Guid employeeId = employeeItems.Key;
            var ordered = employeeItems.Select(x => (x.Candidate, x.Segment)).OrderBy(x => x.Segment.Start).ToList();

            DateTimeOffset rangeFrom = ordered[0].Segment.Start.AddDays(-1);
            DateTimeOffset rangeTo = ordered[^1].Segment.End.AddDays(1);

            List<OccupancySlot> candidateAppointments = await _schedulingOccupancyHandler.GetForEmployeeInRange(
                organizationId, employeeId, rangeFrom, rangeTo);

            List<ScheduleBreak> candidateBreaks = await _scheduleBreakHandler.GetForEmployeeInRange(
                organizationId, employeeId, rangeFrom, rangeTo);

            List<OrganizationCalendar> employeeCalendars = ordered.Select(x => calendarsByCompany[x.Candidate.Group.CompanyId]).Distinct().ToList();
            List<RosterEntry> rosterEntriesInRange = await _rosterEntryHandler.GetForPeriod(
                organizationId, new List<Guid> { employeeId },
                employeeCalendars.Min(c => c.LocalDate(rangeFrom)), employeeCalendars.Max(c => c.LocalDate(rangeTo)));

            List<RosterEntry> absences = rosterEntriesInRange.Where(e => e.RosterType.IsAbsence).ToList();

            WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);

            Dictionary<Guid, WorkingHoursTemplate> companyTemplatesById = new Dictionary<Guid, WorkingHoursTemplate>();
            foreach (Guid companyId in ordered.Select(x => x.Candidate.Group.CompanyId).Distinct())
                companyTemplatesById[companyId] = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);

            HashSet<(Guid, DateTimeOffset)> hardKeys = new();
            for (int i = 0; i < ordered.Count; i++)
            {
                (GroupOccurrenceCandidate candidate, CandidateSegment segment) = ordered[i];
                (Guid, DateTimeOffset) key = (candidate.Slot.Id.GetValueOrDefault(), candidate.StartsAt);
                if (hardKeys.Contains(key))
                    continue;

                bool appointmentHit = candidateAppointments.Any(a => a.Overlaps(segment.Start, segment.End));
                bool batchEmployeeHit = !appointmentHit && ordered.Where((_, j) => j != i)
                    .Any(other => SchedulingInterval.Overlaps(other.Segment.Start, other.Segment.End, segment.Start, segment.End));

                if (appointmentHit || batchEmployeeHit)
                {
                    hardKeys.Add(key);
                    hardConflicts.Add(new RecurringConflictDetail { Date = candidate.StartsAt, Reason = ErrorCodes.RecurringConflictReasonAppointment });
                    continue;
                }

                bool breakHit = candidateBreaks.Any(b =>
                    SchedulingInterval.Overlaps(b.StartsAt, b.StartsAt.AddMinutes(b.DurationMinutes), segment.Start, segment.End));

                OrganizationCalendar calendar = calendarsByCompany[candidate.Group.CompanyId];
                DateOnly localDate = calendar.LocalDate(segment.Start);

                bool absenceHit = absences.Any(a =>
                    a.DateFrom <= localDate && (a.DateTo == null || localDate <= a.DateTo.Value));

                List<RosterEntry> rosterEntriesForOccurrence = rosterEntriesInRange
                    .Where(e => !e.RosterType.IsAbsence && e.DateFrom == localDate)
                    .ToList();

                WorkingHoursTemplate companyTemplate = companyTemplatesById[candidate.Group.CompanyId];

                // Praznik je već obrađen ranije (tiho preskakanje kandidata na dan praznika).
                bool withinHours = absenceHit || IsWithinWorkingHours(
                    employeeTemplate, companyTemplate, rosterEntriesForOccurrence, localDate, calendar.LocalTimeOfDay(segment.Start), segment.DurationMinutes);

                AppointmentEligibilityHelper.WorkforceViolation violation = AppointmentEligibilityHelper.Classify(
                    absenceHit, breakHit, holidayHit: false, withinWorkingHours: withinHours);

                if (violation == AppointmentEligibilityHelper.WorkforceViolation.None)
                    continue;

                if (!overrideAvailability)
                {
                    hardKeys.Add(key);
                    hardConflicts.Add(new RecurringConflictDetail { Date = candidate.StartsAt, Reason = ToRecurringConflictReason(violation) });
                    continue;
                }

                if (!warningsByCandidate.TryGetValue(key, out List<WarningDto> warnings))
                    warningsByCandidate[key] = warnings = new List<WarningDto>();
                if (warnings.Count == 0)
                    AppointmentEligibilityHelper.ThrowOrWarn(violation, overrideAvailability: true, warnings);
            }
        }

        if (hardConflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Neki termini u nizu se sudaraju s postojećim obavezama.",
                new { conflicts = hardConflicts });

        return warningsByCandidate;
    }

    /// <summary>Fizički kapacitet prostorije (osobe: trener ako postoji + sudionici TOG segmenta) i resursa (količina) za svaki
    /// segment kandidata, vremenski raslojeno zajedno s postojećim segmentima i ostalim kandidatima istog batcha. Pozivatelj
    /// je zaključao prostorije/resurse. Povrede se prijavljuju po occurrenceu (RECURRING_CONFLICT, razlog prostorija) — cijeli
    /// batch abortira.</summary>
    private async Task EnsureNoRoomConflicts(IUnitOfWork uow, Guid organizationId, List<GroupOccurrenceCandidate> candidates)
    {
        Dictionary<SegmentClaim, GroupOccurrenceCandidate> byClaim = new(ReferenceEqualityComparer.Instance);
        foreach (GroupOccurrenceCandidate candidate in candidates)
            foreach (CandidateSegment segment in candidate.Segments.Where(s => s.Template.RoomId.HasValue || s.Template.Resources.Count > 0))
                byClaim[ClaimOf(candidate, segment)] = candidate;

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
                "Neki termini u nizu premašili bi kapacitet prostorije ili resursa.",
                new { conflicts });
    }

    /// <summary>Duplikat istog (grupa, početak) unutar batcha (dva aktivna slota iste grupe na isto vrijeme).</summary>
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

    /// <summary>Sudionik segmenta ne smije završiti dvostruko zakazan generiranjem: postojeća zauzetost (bilo koji oblik
    /// termina) ili drugi segment istog batcha (uključivo sestrinske segmente istog occurrencea) s istim klijentom. Tvrda
    /// blokada za cijeli batch, bez override-a. Details ne nose ClientId (PII).</summary>
    private async Task EnsureNoMemberConflicts(Guid organizationId, List<GroupOccurrenceCandidate> candidates)
    {
        List<(GroupOccurrenceCandidate Candidate, CandidateSegment Segment)> items = candidates
            .SelectMany(c => c.Segments.Where(s => s.ClientIds.Count > 0).Select(s => (c, s)))
            .ToList();
        if (items.Count == 0)
            return;

        List<Guid> allClientIds = items.SelectMany(i => i.Segment.ClientIds).Distinct().ToList();
        DateTimeOffset rangeFrom = items.Min(i => i.Segment.Start).AddDays(-1);
        DateTimeOffset rangeTo = items.Max(i => i.Segment.End).AddDays(1);

        List<OccupancySlot> occupied = await _schedulingOccupancyHandler.GetForClientsInRange(
            organizationId, allClientIds, rangeFrom, rangeTo);

        HashSet<(Guid, DateTimeOffset)> conflicted = new();
        List<RecurringConflictDetail> conflicts = new List<RecurringConflictDetail>();

        for (int i = 0; i < items.Count; i++)
        {
            (GroupOccurrenceCandidate candidate, CandidateSegment segment) = items[i];
            (Guid, DateTimeOffset) key = (candidate.Slot.Id.GetValueOrDefault(), candidate.StartsAt);
            if (conflicted.Contains(key))
                continue;

            bool memberConflict = occupied.Any(a =>
                a.Overlaps(segment.Start, segment.End) && a.ActiveClientIds.Any(segment.ClientIds.Contains));

            for (int j = 0; j < items.Count && !memberConflict; j++)
            {
                if (j == i)
                    continue;
                CandidateSegment other = items[j].Segment;
                memberConflict = SchedulingInterval.Overlaps(segment.Start, segment.End, other.Start, other.End)
                                 && segment.ClientIds.Any(other.ClientIds.Contains);
            }

            if (memberConflict)
            {
                conflicted.Add(key);
                conflicts.Add(new RecurringConflictDetail { Date = candidate.StartsAt, Reason = ErrorCodes.RecurringConflictReasonMemberConflict });
            }
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

    #endregion

    #region Helpers

    /// <summary>Isto pravilo kao AppointmentService.IsWithinWorkingHours, bez holiday parametra.</summary>
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

    /// <summary>Radno vrijeme (zaposlenici predloška/poslovnica) za definiciju grupe je UPOZORENJE — za svaki slot, svaki
    /// predložak (početak slota + pomak, trajanje predloška) i SVAKOG zaposlenika tog predloška (Phase M1G) na najbližem
    /// budućem datumu tog dana; jedno upozorenje po slotu. Predlošci bez osoblja se preskaču.</summary>
    private async Task<List<WarningDto>> ComputeWorkingHoursWarnings(
        Guid organizationId, Guid companyId, IReadOnlyCollection<GroupSegmentTemplate> templates,
        List<(DayOfWeek DayOfWeek, TimeSpan StartTime)> slots)
    {
        List<WarningDto> warnings = new List<WarningDto>();
        List<(GroupSegmentTemplate Template, Guid EmployeeId)> staffed = templates
            .SelectMany(t => t.Employees.Select(e => (t, e.EmployeeId)))
            .ToList();
        if (staffed.Count == 0)
            return warnings;

        Dictionary<Guid, WorkingHoursTemplate> employeeTemplates = new();
        foreach (Guid employeeId in staffed.Select(x => x.EmployeeId).Distinct())
            employeeTemplates[employeeId] = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, companyId);
        DateOnly today = calendar.LocalDate(DateTimeOffset.UtcNow);

        foreach ((DayOfWeek dayOfWeek, TimeSpan startTime) in slots)
        {
            DateOnly representativeDate = NextOccurrenceDate(today, dayOfWeek);
            bool outside = staffed.Any(x =>
            {
                TimeSpan localStart = startTime + TimeSpan.FromMinutes(x.Template.StartOffsetMinutes);
                DateOnly date = representativeDate.AddDays((int)localStart.TotalDays);
                return !IsWithinWorkingHours(employeeTemplates[x.EmployeeId], companyTemplate, new List<RosterEntry>(), date,
                    TimeSpan.FromTicks(localStart.Ticks % TimeSpan.TicksPerDay), x.Template.DurationMinutes);
            });

            if (outside)
                warnings.Add(new WarningDto(WarningCodes.OutsideWorkingHours, new WarningSlotDetails
                {
                    DayOfWeek = dayOfWeek,
                    StartTime = startTime
                }));
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

    /// <summary>Puni strukturni lanac: Company/Service aktivni, Service ponuđen u Company, trener (ako je zadan) aktivan i
    /// dodijeljen toj Company i toj usluzi.</summary>
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

    /// <summary>Prostorija je opcionalna; ako je zadana mora biti aktivna i pripadati istoj poslovnici kao grupa.</summary>
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

    /// <summary>Samo skalarna polja grupe (bez navigacija) za Update kroz uow.</summary>
    private static Group Scalar(Group group) => new()
    {
        Id = group.Id, OrganizationId = group.OrganizationId, Name = group.Name, CompanyId = group.CompanyId,
        IsActive = group.IsActive, Note = group.Note, CreatedAt = group.CreatedAt,
        CreatedBy = group.CreatedBy, UpdatedAt = group.UpdatedAt, UpdatedBy = group.UpdatedBy
    };

    private static GroupSegmentTemplate TemplateScalar(GroupSegmentTemplate template) => new()
    {
        Id = template.Id, GroupId = template.GroupId, ServiceId = template.ServiceId, StartOffsetMinutes = template.StartOffsetMinutes,
        DurationMinutes = template.DurationMinutes, RoomId = template.RoomId, Capacity = template.Capacity,
        PricingMode = template.PricingMode, PricingEmployeeId = template.PricingEmployeeId,
        CreatedAt = template.CreatedAt, UpdatedAt = template.UpdatedAt
    };

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
        List<GroupSegmentTemplate> templates = group.SegmentTemplates.OrderBy(t => t.StartOffsetMinutes).ThenBy(t => t.Id).ToList();

        dto.Id = group.Id.GetValueOrDefault();
        dto.Name = group.Name;
        dto.CompanyId = group.CompanyId;
        dto.CompanyName = group.Company?.Name;
        dto.SegmentTemplates = templates.Select(t => new GroupSegmentTemplateDto
        {
            Id = t.Id.GetValueOrDefault(),
            ServiceId = t.ServiceId,
            ServiceName = t.Service?.Name,
            StartOffsetMinutes = t.StartOffsetMinutes,
            DurationMinutes = t.DurationMinutes,
            RoomId = t.RoomId,
            RoomName = t.Room?.Name,
            Capacity = t.Capacity,
            Resources = t.Resources.Select(r => new GroupSegmentTemplateResourceDto
            {
                ResourceId = r.ResourceId, ResourceName = r.Resource?.Name, QuantityRequired = r.QuantityRequired
            }).ToList(),
            Employees = t.Employees.OrderBy(e => e.EmployeeId).Select(e => new AppointmentSegmentEmployeeDto
            {
                EmployeeId = e.EmployeeId,
                EmployeeName = e.Employee != null ? $"{e.Employee.FirstName} {e.Employee.LastName}" : null
            }).ToList(),
            PricingMode = t.PricingMode,
            PricingEmployeeId = t.PricingEmployeeId
        }).ToList();
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

    /// <summary>Segment netom generiranog occurrencea — nazivi iz predloška koji ga je generirao (već učitana grupa).</summary>
    private static AppointmentSegmentDto GeneratedSegmentDto(AppointmentSegment segment, Group group)
    {
        AppointmentSegmentDto dto = AppointmentSegmentReadModel.ToDto(segment);
        GroupSegmentTemplate template = group.SegmentTemplates.SingleOrDefault(t => t.Id == segment.GroupSegmentTemplateId);
        dto.ServiceName = template?.Service?.Name;
        dto.ServiceCategoryColorHex = template?.Service?.ColorHex;
        dto.RoomName = template?.Room?.Name;
        foreach (AppointmentSegmentEmployeeDto employee in dto.Employees)
            if (template?.Employees.SingleOrDefault(e => e.EmployeeId == employee.EmployeeId)?.Employee is Employee staff)
                employee.EmployeeName = $"{staff.FirstName} {staff.LastName}";
        if (template?.Employees.SingleOrDefault(e => e.EmployeeId == dto.PricingEmployeeId)?.Employee is Employee pricing)
            dto.PricingEmployeeName = $"{pricing.FirstName} {pricing.LastName}";
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
            IsActive = member.IsActive,
            SegmentTemplateIds = member.SegmentTemplates.Select(x => x.GroupSegmentTemplateId).OrderBy(x => x).ToList()
        };
    }

    /// <summary>Termini iz GetAppointmentsForGroup su uvijek Form=Group.</summary>
    private static AppointmentScheduleCellDto ToScheduleCellDto(Appointment a, string groupName, int expectedCount)
    {
        AppointmentRange range = AppointmentRange.Of(a);
        return new AppointmentScheduleCellDto
        {
            Id = a.Id.GetValueOrDefault(),
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(a),
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            Status = a.Status,
            IsCancelled = a.Status == AppointmentStatus.Cancelled,
            Form = AppointmentForm.Group,
            GroupId = a.GroupId,
            GroupName = groupName,
            AttendanceCount = a.Bookings.SelectMany(b => b.Participations).Count(p => p.Status == ParticipationStatus.Completed),
            ExpectedCount = expectedCount
        };
    }

    #endregion
}
