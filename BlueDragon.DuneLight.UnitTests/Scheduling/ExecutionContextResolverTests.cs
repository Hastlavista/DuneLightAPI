#nullable disable
using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// S2 execution-context seam: <see cref="ExecutionContextResolver"/> is the single mapping from today's singular
/// Appointment frame (+ Booking) to the context that pricing, packages, commission, cancellation policy and checkout read.
/// Pure mapping over already-loaded entities — no database — so these are plain unit tests.
/// </summary>
public class ExecutionContextResolverTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly DateTimeOffset StartsAt = new(2031, 3, 3, 10, 0, 0, TimeSpan.Zero);

    /// <summary>D3A: the frame lives on the appointment's single segment (Service navigation on the segment).</summary>
    private static Appointment NewAppointment(Guid? employeeId, ServiceEntity service = null, Guid? roomId = null)
    {
        Appointment appointment = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            Form = AppointmentForm.Individual,
            CompanyId = Guid.NewGuid(),
            Status = AppointmentStatus.Scheduled
        };
        AppointmentSegment segment = AppointmentFrameMutator.NewSegment(appointment,
            new AppointmentFrame(service?.Id ?? Guid.NewGuid(), employeeId, roomId, StartsAt, 45));
        segment.Service = service;
        return appointment;
    }

    private static Booking NewBooking(Appointment appointment, Guid? organizationId = null, Guid? appointmentId = null) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId ?? appointment.OrganizationId,
        AppointmentId = appointmentId ?? appointment.Id.Value,
        ClientId = Guid.NewGuid(),
        Status = BookingStatus.Confirmed
    };

    #region Appointment -> execution

    [Fact]
    public void ForAppointment_MapsTheSingularFrame()
    {
        ServiceEntity service = new() { Id = Guid.NewGuid(), Name = "Massage" };
        Guid employeeId = Guid.NewGuid();
        Appointment appointment = NewAppointment(employeeId, service, roomId: Guid.NewGuid());

        AppointmentExecutionContext execution = ExecutionContextResolver.ForAppointment(appointment);

        Assert.Equal(OrganizationId, execution.OrganizationId);
        Assert.Equal(appointment.Id.Value, execution.AppointmentId);
        Assert.Equal(appointment.CompanyId, execution.CompanyId);
        Assert.Equal(service.Id.Value, execution.ServiceId);
        Assert.Equal("Massage", execution.ServiceName);
        Assert.Equal(employeeId, execution.EmployeeId);
        Assert.Equal(StartsAt, execution.StartsAt);
    }

    [Fact]
    public void ForAppointment_WithoutAnEmployee_HasANullEmployee()
    {
        AppointmentExecutionContext execution = ExecutionContextResolver.ForAppointment(NewAppointment(employeeId: null));

        Assert.Null(execution.EmployeeId);
    }

    [Fact]
    public void ForAppointment_WithoutALoadedServiceNavigation_HasANullServiceName_ButStillTheServiceId()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());

        AppointmentExecutionContext execution = ExecutionContextResolver.ForAppointment(appointment);

        Assert.Null(execution.ServiceName);
        Assert.Equal(appointment.ServiceId, execution.ServiceId);
    }

    [Fact]
    public void ForAppointment_ReflectsInMemoryChanges_NotAStoredCopy()
    {
        // CompleteExisting rewrites the frame on the loaded entity and then earns commission / deducts packages in the
        // same transaction — the context must see those values.
        Appointment appointment = NewAppointment(Guid.NewGuid());
        Guid newService = Guid.NewGuid(), newEmployee = Guid.NewGuid();
        DateTimeOffset newStart = StartsAt.AddHours(4);
        AppointmentFrameMutator.Apply(appointment, AppointmentFrame.Of(appointment) with
        {
            ServiceId = newService, EmployeeId = newEmployee, StartsAt = newStart
        }, DateTimeOffset.UtcNow);

        AppointmentExecutionContext execution = ExecutionContextResolver.ForAppointment(appointment);

        Assert.Equal(newService, execution.ServiceId);
        Assert.Equal(newEmployee, execution.EmployeeId);
        Assert.Equal(newStart, execution.StartsAt);
    }

    [Fact]
    public void ForAppointment_RejectsAMissingOrUnsavedAppointment()
    {
        Assert.Throws<ArgumentNullException>(() => ExecutionContextResolver.ForAppointment(null));

        Appointment withoutId = NewAppointment(Guid.NewGuid());
        withoutId.Id = null;
        Assert.Throws<InvalidOperationException>(() => ExecutionContextResolver.ForAppointment(withoutId));
    }

    #endregion

    #region Booking -> execution

    [Fact]
    public void ForBooking_CarriesTheAppointmentExecutionPlusBookingAndClient()
    {
        ServiceEntity service = new() { Id = Guid.NewGuid(), Name = "Pilates" };
        Guid employeeId = Guid.NewGuid();
        Appointment appointment = NewAppointment(employeeId, service);
        Booking booking = NewBooking(appointment);

        BookingExecutionContext execution = ExecutionContextResolver.ForBooking(appointment, booking);

        Assert.Equal(booking.Id.Value, execution.BookingId);
        Assert.Equal(booking.ClientId, execution.ClientId);
        Assert.Equal(appointment.Id.Value, execution.AppointmentId);
        Assert.Equal(OrganizationId, execution.OrganizationId);
        Assert.Equal(appointment.CompanyId, execution.CompanyId);
        Assert.Equal(service.Id.Value, execution.ServiceId);
        Assert.Equal("Pilates", execution.ServiceName);
        Assert.Equal(employeeId, execution.EmployeeId);
        Assert.Equal(StartsAt, execution.StartsAt);
    }

    [Fact]
    public void ForBooking_OnATrainerlessOccurrence_HasANullEmployee()
    {
        Appointment appointment = NewAppointment(employeeId: null);
        appointment.Form = AppointmentForm.Group;

        Assert.Null(ExecutionContextResolver.ForBooking(appointment, NewBooking(appointment)).EmployeeId);
    }

    [Fact]
    public void ForBooking_ClientsOnTheSameAppointment_ShareTheExecutionButNotTheBooking()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());
        BookingExecutionContext first = ExecutionContextResolver.ForBooking(appointment, NewBooking(appointment));
        BookingExecutionContext second = ExecutionContextResolver.ForBooking(appointment, NewBooking(appointment));

        Assert.Equal(first.ServiceId, second.ServiceId);
        Assert.Equal(first.StartsAt, second.StartsAt);
        Assert.NotEqual(first.BookingId, second.BookingId);
        Assert.NotEqual(first.ClientId, second.ClientId);
    }

    [Fact]
    public void ForBooking_RejectsAMissingBookingOrAppointment()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());

        Assert.Throws<ArgumentNullException>(() => ExecutionContextResolver.ForBooking(appointment, null));
        Assert.Throws<ArgumentNullException>(() => ExecutionContextResolver.ForBooking(null, NewBooking(appointment)));

        Booking unsaved = NewBooking(appointment);
        unsaved.Id = null;
        Assert.Throws<InvalidOperationException>(() => ExecutionContextResolver.ForBooking(appointment, unsaved));
    }

    [Fact]
    public void ForBooking_RejectsABookingOfAnotherOrganization_NoCrossTenantContext()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());
        Booking foreign = NewBooking(appointment, organizationId: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => ExecutionContextResolver.ForBooking(appointment, foreign));
    }

    [Fact]
    public void ForBooking_RejectsABookingOfAnotherAppointment()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());
        Booking elsewhere = NewBooking(appointment, appointmentId: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => ExecutionContextResolver.ForBooking(appointment, elsewhere));
    }

    #endregion
}
