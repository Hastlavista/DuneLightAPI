#nullable disable
using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Test-only read convenience after the D3A storage cutover: the legacy Appointment frame properties no longer exist,
/// so characterization tests read the same values through the PRODUCTION single-segment frame
/// (<see cref="AppointmentFrame.Of"/> — the authoritative segment). Read-only and in-memory only: never usable in EF
/// queries, never available to production code. Requires the appointment to be loaded with Segments (+ Employees),
/// as SchedulingWorld.LoadAppointment does.
/// </summary>
public static class AppointmentFrameTestExtensions
{
    extension(Appointment appointment)
    {
        public DateTimeOffset StartsAt => AppointmentFrame.Of(appointment).StartsAt;
        public int DurationMinutes => AppointmentFrame.Of(appointment).DurationMinutes;
        public Guid ServiceId => AppointmentFrame.Of(appointment).ServiceId;
        public Guid? EmployeeId => AppointmentFrame.Of(appointment).EmployeeId;
        public Guid? RoomId => AppointmentFrame.Of(appointment).RoomId;
    }

    /// <summary>D3B1/M0: single-participation characterization tests read the lifecycle/price of a Booking's ONLY
    /// participation through the PRODUCTION compatibility resolver (<see cref="BookingParticipations.GetSingleParticipation"/>,
    /// which rejects more than one). Read-only, in-memory only. Multi-participation tests address participations directly.</summary>
    extension(Booking booking)
    {
        private BookingSegmentParticipation SingleParticipation => BookingParticipations.GetSingleParticipation(booking);
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
