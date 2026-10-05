#nullable disable
using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Test-only read convenience for SINGLE-segment characterization tests: the legacy Appointment frame properties no
/// longer exist (and since M1B neither does the production frame), so these read the values of the appointment's only
/// segment (M1H: test-local resolution — production has no single-segment resolver; any other segment count fails the
/// test). Read-only and in-memory only. Multi-segment tests address segments directly.
/// </summary>
public static class SingleSegmentTestExtensions
{
    extension(Appointment appointment)
    {
        private AppointmentSegment OnlySegment => Assert.Single(appointment.Segments);
        public DateTimeOffset StartsAt => appointment.OnlySegment.PlannedStart;
        public int DurationMinutes => AppointmentSegments.DurationMinutes(appointment.OnlySegment);
        public Guid ServiceId => appointment.OnlySegment.ServiceId;
        public Guid? EmployeeId => appointment.OnlySegment.Employees.Count == 0 ? null : Assert.Single(appointment.OnlySegment.Employees).EmployeeId;
        public Guid? RoomId => appointment.OnlySegment.RoomId;
    }

    /// <summary>M1B: adds an in-memory segment (one employee at most) to an appointment — the same shape the production
    /// construction core (AppointmentFactory) builds — for pure unit tests and for seeding.</summary>
    public static AppointmentSegment AddTestSegment(
        Appointment appointment, Guid serviceId, Guid? employeeId, Guid? roomId, DateTimeOffset plannedStart, int durationMinutes)
    {
        AppointmentSegment segment = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = appointment.OrganizationId,
            AppointmentId = appointment.Id.GetValueOrDefault(),
            ServiceId = serviceId,
            RoomId = roomId,
            PlannedStart = plannedStart,
            PlannedEnd = plannedStart.AddMinutes(durationMinutes),
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (employeeId.HasValue)
            segment.Employees.Add(new AppointmentSegmentEmployee { AppointmentSegmentId = segment.Id.Value, EmployeeId = employeeId.Value });
        appointment.Segments.Add(segment);
        return segment;
    }

    /// <summary>D3B1/M0: single-participation characterization tests read the lifecycle/price of a Booking's ONLY
    /// participation (M1H: test-local resolution — any other participation count fails the test). Read-only, in-memory only. Multi-participation tests address participations directly.</summary>
    extension(Booking booking)
    {
        private BookingSegmentParticipation SingleParticipation => Assert.Single(booking.Participations);
        public BookingStatus Status => BookingParticipations.ToBookingStatus(booking.SingleParticipation.Status);
        public int StatusVersion => booking.SingleParticipation.StatusVersion;
        public string CancellationReason => booking.SingleParticipation.CancellationReason;
        public bool? IsLateCancellation => booking.SingleParticipation.IsLateCancellation;

        // D3B2: price likewise lives on the participation.
        public decimal Amount => booking.SingleParticipation.Amount;
        public decimal SuggestedAmount => booking.SingleParticipation.SuggestedAmount;
        public bool IsAmountManuallyOverridden => booking.SingleParticipation.IsAmountManuallyOverridden;

        // D3B3A: package usage is the participation's PackageConsumption history; the former Booking columns are
        // derived through the PRODUCTION view (form from the loaded Appointment, Individual when it is not loaded).
        private PackageCoverageView Coverage => PackageConsumptions.CoverageOf(booking.SingleParticipation, booking.Appointment?.Form ?? AppointmentForm.Individual);
        public Guid? ClientPackageId => booking.Coverage.ClientPackageId;
        public AttendanceCoverageType? CoverageType => booking.Coverage.CoverageType;
        public bool PackageCoverageApplied => booking.Coverage.PackageCoverageApplied;
        public bool PackageCoverageReturned => booking.Coverage.PackageCoverageReturned;
        public DateTimeOffset? PackageCoverageReturnedAt => booking.Coverage.PackageCoverageReturnedAt;
        public Guid? PackageCoverageReturnedBy => booking.Coverage.PackageCoverageReturnedBy;
    }

    /// <summary>D3B1: an in-memory Booking carrying its single participation (lifecycle) — for pure unit tests of the
    /// lifecycle seams.</summary>
    public static Booking InMemoryBooking(BookingStatus status, int statusVersion = 0, string cancellationReason = null)
    {
        Guid bookingId = Guid.NewGuid();
        Guid organizationId = Guid.NewGuid();
        Booking booking = new() { Id = bookingId, OrganizationId = organizationId, AppointmentId = Guid.NewGuid() };
        booking.Participations.Add(new BookingSegmentParticipation
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, BookingId = bookingId, AppointmentSegmentId = Guid.NewGuid(),
            Status = BookingParticipations.ToParticipationStatus(status), StatusVersion = statusVersion,
            CancellationReason = cancellationReason
        });
        return booking;
    }
}
