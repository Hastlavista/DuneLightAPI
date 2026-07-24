using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class GroupAttendanceService : IGroupAttendanceService
{
    private readonly IGroupAttendanceHandler _attendanceHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IClientPackageService _clientPackageService;
    private readonly IEmployeeHandler _employeeHandler;

    public GroupAttendanceService(
        IGroupAttendanceHandler attendanceHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientPackageService clientPackageService,
        IEmployeeHandler employeeHandler)
    {
        _attendanceHandler = attendanceHandler;
        _auditLogHandler = auditLogHandler;
        _clientPackageService = clientPackageService;
        _employeeHandler = employeeHandler;
    }

    public async Task<GroupAttendanceListDto> GetAttendance(Guid organizationId, Guid appointmentId)
    {
        Appointment appointment = await LoadGroupAppointmentOrThrow(organizationId, appointmentId);
        return BuildListDto(appointment);
    }

    public async Task<GroupAttendanceListDto> SetAttendance(
        Guid organizationId, Guid userId, bool isAdmin, Guid appointmentId, SetGroupAttendanceRequest request)
    {
        Appointment appointment = await LoadGroupAppointmentOrThrow(organizationId, appointmentId);
        await ValidateOwnership(organizationId, userId, isAdmin, appointment.EmployeeId);

        AppointmentAttendance existing = await _attendanceHandler.GetAttendanceRow(appointmentId, request.ClientId);

        if (request.Attended)
            await HandleAttended(organizationId, userId, appointment, existing, request);
        else
            await HandleNotAttended(organizationId, userId, appointment, existing, request);

        Appointment refreshed = await LoadGroupAppointmentOrThrow(organizationId, appointmentId);
        return BuildListDto(refreshed);
    }

    private async Task HandleAttended(
        Guid organizationId, Guid userId, Appointment appointment, AppointmentAttendance existing, SetGroupAttendanceRequest request)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Nema postojećeg retka, ili je prethodno bio false/null, ili je ulazak već vraćen — u sva tri
        // slučaja treba svježe razriješiti pokriće (i eventualno ponovno skinuti ulazak).
        bool needsFreshCoverage = existing == null || existing.Attended != true ||
            (existing.PackageEntryDeducted && existing.PackageEntryReturned);

        if (existing == null)
        {
            existing = new AppointmentAttendance
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                ClientId = request.ClientId,
                CreatedAt = now
            };

            if (needsFreshCoverage)
                await ResolveCoverage(organizationId, userId, appointment, existing, request.ClientPackageId);

            existing.Attended = true;
            existing.Note = request.Note;
            await _attendanceHandler.AddAttendance(existing);
            return;
        }

        if (needsFreshCoverage)
            await ResolveCoverage(organizationId, userId, appointment, existing, request.ClientPackageId);

        existing.Attended = true;
        if (request.Note != null)
            existing.Note = request.Note;

        await _attendanceHandler.UpdateAttendance(existing);
    }

    private async Task HandleNotAttended(
        Guid organizationId, Guid userId, Appointment appointment, AppointmentAttendance existing, SetGroupAttendanceRequest request)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (existing == null)
        {
            existing = new AppointmentAttendance
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                ClientId = request.ClientId,
                Attended = false,
                Note = request.Note,
                CreatedAt = now
            };

            await _attendanceHandler.AddAttendance(existing);
            return;
        }

        // Poništenje prisutnosti — automatski vraća skinuti ulazak (za razliku od individualnih termina,
        // gdje je vraćanje eksplicitna admin/trener akcija preko ReturnEntryForClientIds).
        if (existing.PackageEntryDeducted && !existing.PackageEntryReturned && existing.ClientPackageId.HasValue)
        {
            await _clientPackageService.ReturnEntry(organizationId, existing.ClientPackageId.Value, appointment.ServiceId, userId);

            existing.PackageEntryReturned = true;
            existing.PackageEntryReturnedAt = now;
            existing.PackageEntryReturnedBy = userId;

            await _auditLogHandler.Add(new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                ChangeType = "PackageEntryReturn",
                OldValue = "Deducted",
                NewValue = "Returned",
                ChangedAt = now,
                ChangedBy = userId
            });
        }

        existing.Attended = false;
        if (request.Note != null)
            existing.Note = request.Note;

        await _attendanceHandler.UpdateAttendance(existing);
    }

    /// <summary>Razrješava CoverageType/ClientPackageId za prvi (ili ponovljeni nakon vraćanja) check-in — skida
    /// ulazak kod SessionPackage, ništa ne skida kod MonthlyPackage (neograničen brojač), SinglePaid bez paketa.</summary>
    private async Task ResolveCoverage(
        Guid organizationId, Guid userId, Appointment appointment, AppointmentAttendance attendance, Guid? requestedPackageId)
    {
        List<ClientPackageDto> eligible = await _clientPackageService.GetEligibleForService(
            organizationId, attendance.ClientId, appointment.ServiceId, appointment.StartsAt);

        ClientPackageDto selected;
        if (requestedPackageId.HasValue)
        {
            selected = eligible.FirstOrDefault(p => p.Id == requestedPackageId.Value);
            if (selected == null)
                throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Odabrani paket nije valjan za klijenta ili ne pokriva ovu uslugu.");
        }
        else if (eligible.Count == 1)
        {
            selected = eligible[0];
        }
        else if (eligible.Count > 1)
        {
            throw new ValidationAppException("Klijent ima više prihvatljivih paketa — potrebno je ručno odabrati jedan (ClientPackageId).");
        }
        else
        {
            selected = null;
        }

        if (selected == null)
        {
            attendance.CoverageType = AttendanceCoverageType.SinglePaid;
            attendance.ClientPackageId = null;
            attendance.PackageEntryDeducted = false;
            attendance.PackageEntryReturned = false;
            attendance.PackageEntryReturnedAt = null;
            attendance.PackageEntryReturnedBy = null;
            return;
        }

        bool isUnlimited = IsUnlimited(selected, appointment.ServiceId);

        attendance.ClientPackageId = selected.Id;
        attendance.PackageEntryReturned = false;
        attendance.PackageEntryReturnedAt = null;
        attendance.PackageEntryReturnedBy = null;

        if (isUnlimited)
        {
            attendance.CoverageType = AttendanceCoverageType.MonthlyPackage;
            attendance.PackageEntryDeducted = false;
        }
        else
        {
            attendance.CoverageType = AttendanceCoverageType.SessionPackage;
            await _clientPackageService.DeductEntry(organizationId, selected.Id, appointment.ServiceId, userId);
            attendance.PackageEntryDeducted = true;
        }
    }

    private static bool IsUnlimited(ClientPackageDto package, Guid serviceId)
    {
        if (package.EntryMode == PackageEntryMode.SharedPool)
            return !package.RemainingSharedEntries.HasValue;

        ClientPackageServiceEntryDto entry = package.ServiceEntries.FirstOrDefault(e => e.ServiceId == serviceId);
        return entry == null || !entry.RemainingEntries.HasValue;
    }

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool isAdmin, Guid? appointmentEmployeeId)
    {
        if (isAdmin)
            return;

        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || !appointmentEmployeeId.HasValue || employee.Id != appointmentEmployeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, "Trener smije čekirati prisutnost samo na svojim vlastitim grupnim terminima.");
    }

    private async Task<Appointment> LoadGroupAppointmentOrThrow(Guid organizationId, Guid appointmentId)
    {
        Appointment appointment = await _attendanceHandler.GetGroupAppointment(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        return appointment;
    }

    private static GroupAttendanceListDto BuildListDto(Appointment appointment)
    {
        List<Guid> recordedClientIds = appointment.Attendances.Select(a => a.ClientId).ToList();
        List<GroupMember> activeMembers = appointment.Group?.Members.Where(m => m.IsActive).ToList() ?? new List<GroupMember>();

        List<GroupAttendanceEntryDto> expected = activeMembers
            .Where(m => !recordedClientIds.Contains(m.ClientId))
            .Select(m => new GroupAttendanceEntryDto
            {
                ClientId = m.ClientId,
                ClientName = m.Client != null ? $"{m.Client.FirstName} {m.Client.LastName}" : null,
                Attended = null,
                IsMember = true
            })
            .ToList();

        List<GroupAttendanceEntryDto> recorded = appointment.Attendances
            .Select(a => new GroupAttendanceEntryDto
            {
                ClientId = a.ClientId,
                ClientName = a.Client != null ? $"{a.Client.FirstName} {a.Client.LastName}" : null,
                Attended = a.Attended,
                CoverageType = a.CoverageType,
                ClientPackageId = a.ClientPackageId,
                PackageEntryDeducted = a.PackageEntryDeducted,
                PackageEntryReturned = a.PackageEntryReturned,
                Note = a.Note,
                IsMember = activeMembers.Any(m => m.ClientId == a.ClientId)
            })
            .ToList();

        return new GroupAttendanceListDto { Expected = expected, Recorded = recorded };
    }
}
