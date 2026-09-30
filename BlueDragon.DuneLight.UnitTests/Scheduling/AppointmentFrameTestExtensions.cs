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

    /// <summary>D3B1: the Booking lifecycle lives on its single authoritative participation; tests read it through the
    /// PRODUCTION resolver (<see cref="BookingParticipations"/>). Read-only, in-memory only.</summary>
    extension(Booking booking)
    {
        public BookingStatus Status => BookingParticipations.StatusOf(booking);
        public int StatusVersion => BookingParticipations.StatusVersionOf(booking);
        public string CancellationReason => BookingParticipations.CancellationReasonOf(booking);
        public bool? IsLateCancellation => BookingParticipations.IsLateCancellationOf(booking);
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
