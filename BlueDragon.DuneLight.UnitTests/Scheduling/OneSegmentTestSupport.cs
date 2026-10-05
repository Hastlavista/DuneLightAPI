using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Dashboard;
using BlueDragon.DuneLight.Core.DTOs.Groups;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// M1H — TEST-ONLY builder for the common characterization shape "one segment, one employee, N clients". It is NOT an API
/// contract (the flat request types were removed from production): it only produces the target requests
/// (<see cref="AppointmentCreateRequest"/> with exactly one segment, or <see cref="AppointmentCompleteNowRequest"/>).
/// </summary>
public class TestAppointmentSpec
{
    public DateTimeOffset StartsAt { get; set; }
    public Guid ServiceId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? RoomId { get; set; }
    public List<Guid> ClientIds { get; set; } = new();
    public decimal? Amount { get; set; }
    public string Note { get; set; }
    public bool OverrideAvailability { get; set; }

    public AppointmentSegmentDefinitionRequest Segment() => new()
    {
        ServiceId = ServiceId,
        PlannedStart = StartsAt,
        EmployeeIds = new List<Guid> { EmployeeId },
        RoomId = RoomId
    };

    public AppointmentCreateRequest ToTarget()
    {
        AppointmentSegmentDefinitionRequest definition = Segment();
        return new AppointmentCreateRequest
        {
            CompanyId = CompanyId,
            Note = Note,
            OverrideAvailability = OverrideAvailability,
            Segments = new List<AppointmentSegmentCreateRequest>
            {
                new()
                {
                    ServiceId = definition.ServiceId,
                    PlannedStart = definition.PlannedStart,
                    EmployeeIds = definition.EmployeeIds,
                    RoomId = definition.RoomId,
                    Participants = ClientIds.Distinct()
                        .Select(id => new AppointmentParticipantCreateRequest { ClientId = id, Amount = Amount })
                        .ToList()
                }
            }
        };
    }
}

/// <summary>TEST-ONLY: one-segment spec plus one settlement per client — produces the target "complete now" request.</summary>
public class TestCompletionSpec : TestAppointmentSpec
{
    public List<AppointmentCompletedClientRequest> Settlements { get; set; } = new();

    public AppointmentCompleteNowRequest ToCompleteNow() => new()
    {
        CompanyId = CompanyId,
        Note = Note,
        OverrideAvailability = OverrideAvailability,
        Segment = Segment(),
        Clients = Settlements
    };
}

/// <summary>
/// M1H — TEST-ONLY read views for characterization tests of ONE-segment appointments: each member asserts the appointment has
/// exactly one segment (and, where relevant, one employee) and reads it from the target <c>Segments</c> list. Production
/// DTOs no longer carry these singular projections.
/// </summary>
public static class OneSegmentTestViews
{
    private static AppointmentSegmentDto Only(List<AppointmentSegmentDto> segments) => Assert.Single(segments);

    private static Guid? OnlyEmployee(List<AppointmentSegmentDto> segments) =>
        Only(segments).Employees.Count switch { 0 => null, 1 => Only(segments).Employees[0].EmployeeId, _ => throw new InvalidOperationException("Segment has several employees.") };

    private static string OnlyEmployeeName(List<AppointmentSegmentDto> segments) =>
        Only(segments).Employees.Count == 1 ? Only(segments).Employees[0].EmployeeName : null;

    extension(AppointmentDto dto)
    {
        public DateTimeOffset StartsAt => dto.PlannedStart;
        public int DurationMinutes => (int)(dto.PlannedEnd - dto.PlannedStart).TotalMinutes;
        public Guid? ServiceId => Only(dto.Segments).ServiceId;
        public string ServiceName => Only(dto.Segments).ServiceName;
        public Guid? EmployeeId => OnlyEmployee(dto.Segments);
        public string EmployeeName => OnlyEmployeeName(dto.Segments);
        public Guid? RoomId => Only(dto.Segments).RoomId;
        public string RoomName => Only(dto.Segments).RoomName;
    }

    extension(AppointmentScheduleCellDto dto)
    {
        public DateTimeOffset StartsAt => dto.PlannedStart;
        public int DurationMinutes => (int)(dto.PlannedEnd - dto.PlannedStart).TotalMinutes;
        public Guid? ServiceId => Only(dto.Segments).ServiceId;
        public string ServiceName => Only(dto.Segments).ServiceName;
        public Guid? EmployeeId => OnlyEmployee(dto.Segments);
        public string EmployeeName => OnlyEmployeeName(dto.Segments);
        public Guid? RoomId => Only(dto.Segments).RoomId;
        public string RoomName => Only(dto.Segments).RoomName;
    }

    extension(ClientAppointmentHistoryDto dto)
    {
        public DateTimeOffset StartsAt => dto.PlannedStart;
        public int DurationMinutes => (int)(dto.PlannedEnd - dto.PlannedStart).TotalMinutes;
        public Guid? ServiceId => Only(dto.Segments).ServiceId;
        public string ServiceName => Only(dto.Segments).ServiceName;
        public Guid? EmployeeId => OnlyEmployee(dto.Segments);
        public string EmployeeName => OnlyEmployeeName(dto.Segments);
    }

    extension(DashboardScheduleOccurrenceDto dto)
    {
        public DateTimeOffset StartsAt => dto.PlannedStart;
        public int DurationMinutes => (int)(dto.PlannedEnd - dto.PlannedStart).TotalMinutes;
        public Guid? ServiceId => Only(dto.Segments).ServiceId;
        public Guid? EmployeeId => OnlyEmployee(dto.Segments);
        public Guid? RoomId => Only(dto.Segments).RoomId;
    }

    /// <summary>One-template group views (asserts exactly one template).</summary>
    extension(GroupDto dto)
    {
        public Guid? ServiceId => Assert.Single(dto.SegmentTemplates).ServiceId;
        public int? Capacity => Assert.Single(dto.SegmentTemplates).Capacity;
        public Guid? DefaultRoomId => Assert.Single(dto.SegmentTemplates).RoomId;
        public Guid? DefaultTrainerId =>
            Assert.Single(dto.SegmentTemplates).Employees.Count == 1 ? dto.SegmentTemplates[0].Employees[0].EmployeeId : null;
        public Guid OnlyTemplateId => Assert.Single(dto.SegmentTemplates).Id;
    }
}
