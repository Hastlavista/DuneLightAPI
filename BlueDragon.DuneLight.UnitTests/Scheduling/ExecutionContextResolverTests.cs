#nullable disable
using System;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Execution-context seam: <see cref="ExecutionContextResolver"/> maps an EXECUTION UNIT — a segment, or a participation
/// on its segment — to the context that pricing, packages, commission, cancellation policy and checkout read. M1B: there
/// is no appointment-level context any more (an appointment may carry several services/employees/starts). Pure mapping
/// over already-loaded entities — no database — so these are plain unit tests.
/// </summary>
public class ExecutionContextResolverTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly DateTimeOffset StartsAt = new(2031, 3, 3, 10, 0, 0, TimeSpan.Zero);

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
        AppointmentSegment segment = SingleSegmentTestExtensions.AddTestSegment(appointment, service?.Id ?? Guid.NewGuid(), employeeId, roomId, StartsAt, 45);
        segment.Service = service;
        return appointment;
    }

    private static AppointmentSegment Only(Appointment appointment) => appointment.Segments.Single();

    private static Booking NewBooking(Appointment appointment, Guid? organizationId = null, Guid? appointmentId = null) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId ?? appointment.OrganizationId,
        AppointmentId = appointmentId ?? appointment.Id.Value,
        ClientId = Guid.NewGuid()
    };

    private static BookingSegmentParticipation Participate(Booking booking, AppointmentSegment segment)
    {
        BookingSegmentParticipation participation = new()
        {
            Id = Guid.NewGuid(), OrganizationId = booking.OrganizationId, BookingId = booking.Id.Value,
            AppointmentSegmentId = segment.Id.Value, Status = ParticipationStatus.Confirmed
        };
        booking.Participations.Add(participation);
        return participation;
    }

    #region Segment -> execution

    [Fact]
    public void ForSegment_MapsTheSegment()
    {
        ServiceEntity service = new() { Id = Guid.NewGuid(), Name = "Massage" };
        Guid employeeId = Guid.NewGuid();
        Appointment appointment = NewAppointment(employeeId, service, roomId: Guid.NewGuid());
        AppointmentSegment segment = Only(appointment);

        SegmentExecutionContext execution = ExecutionContextResolver.ForSegment(appointment, segment);

        Assert.Equal(OrganizationId, execution.OrganizationId);
        Assert.Equal(appointment.Id.Value, execution.AppointmentId);
        Assert.Equal(segment.Id.Value, execution.SegmentId);
        Assert.Equal(appointment.CompanyId, execution.CompanyId);
        Assert.Equal(service.Id.Value, execution.ServiceId);
        Assert.Equal("Massage", execution.ServiceName);
        Assert.Equal(employeeId, Assert.Single(execution.EmployeeIds));
        Assert.Equal(StartsAt, execution.StartsAt);
    }

    [Fact]
    public void ForSegment_WithoutAnEmployee_HasANullEmployee()
    {
        Appointment appointment = NewAppointment(employeeId: null);

        Assert.Empty(ExecutionContextResolver.ForSegment(appointment, Only(appointment)).EmployeeIds);
    }

    [Fact]
    public void ForSegment_WithoutALoadedServiceNavigation_HasANullServiceName_ButStillTheServiceId()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());

        SegmentExecutionContext execution = ExecutionContextResolver.ForSegment(appointment, Only(appointment));

        Assert.Null(execution.ServiceName);
        Assert.Equal(Only(appointment).ServiceId, execution.ServiceId);
    }

    [Fact]
    public void ForSegment_ReflectsInMemoryChanges_NotAStoredCopy()
    {
        // CompleteExisting rewrites the segment on the loaded entity and then earns commission / deducts packages in the
        // same transaction — the context must see those values.
        Appointment appointment = NewAppointment(Guid.NewGuid());
        AppointmentSegment segment = Only(appointment);
        Guid newService = Guid.NewGuid(), newEmployee = Guid.NewGuid();
        DateTimeOffset newStart = StartsAt.AddHours(4);
        SegmentMutator.ChangeService(segment, newService, DateTimeOffset.UtcNow);
        SegmentMutator.AssignEmployees(segment, new[] { newEmployee }, DateTimeOffset.UtcNow);
        SegmentMutator.ChangeTime(segment, newStart, newStart.AddMinutes(45), DateTimeOffset.UtcNow);

        SegmentExecutionContext execution = ExecutionContextResolver.ForSegment(appointment, segment);

        Assert.Equal(newService, execution.ServiceId);
        Assert.Equal(newEmployee, Assert.Single(execution.EmployeeIds));
        Assert.Equal(newStart, execution.StartsAt);
    }

    [Fact]
    public void ForSegment_TwoSegments_EachResolvesItsOwnServiceEmployeeAndStart()
    {
        ServiceEntity serviceA = new() { Id = Guid.NewGuid(), Name = "A" };
        ServiceEntity serviceB = new() { Id = Guid.NewGuid(), Name = "B" };
        Guid employeeA = Guid.NewGuid(), employeeB = Guid.NewGuid();
        Appointment appointment = NewAppointment(employeeA, serviceA);
        AppointmentSegment a = Only(appointment);
        AppointmentSegment b = SingleSegmentTestExtensions.AddTestSegment(appointment, serviceB.Id.Value, employeeB, null, StartsAt.AddMinutes(60), 30);
        b.Service = serviceB;

        SegmentExecutionContext ctxA = ExecutionContextResolver.ForSegment(appointment, a);
        SegmentExecutionContext ctxB = ExecutionContextResolver.ForSegment(appointment, b);

        Assert.Equal((a.Id.Value, serviceA.Id.Value, "A", (Guid?)employeeA, StartsAt), (ctxA.SegmentId, ctxA.ServiceId, ctxA.ServiceName, ctxA.EmployeeIds.SingleOrDefault() as Guid?, ctxA.StartsAt));
        Assert.Equal((b.Id.Value, serviceB.Id.Value, "B", (Guid?)employeeB, StartsAt.AddMinutes(60)), (ctxB.SegmentId, ctxB.ServiceId, ctxB.ServiceName, ctxB.EmployeeIds.SingleOrDefault() as Guid?, ctxB.StartsAt));
    }

    [Fact]
    public void ForSegment_RejectsMissingOrUnsavedInputs_AndASegmentOfAnotherAppointment()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());
        Assert.Throws<ArgumentNullException>(() => ExecutionContextResolver.ForSegment(null, Only(appointment)));
        Assert.Throws<ArgumentNullException>(() => ExecutionContextResolver.ForSegment(appointment, null));

        Appointment other = NewAppointment(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => ExecutionContextResolver.ForSegment(appointment, Only(other)));

        Appointment withoutId = NewAppointment(Guid.NewGuid());
        AppointmentSegment segment = Only(withoutId);
        withoutId.Id = null;
        Assert.Throws<InvalidOperationException>(() => ExecutionContextResolver.ForSegment(withoutId, segment));
    }

    #endregion

    #region Participation -> execution

    [Fact]
    public void ForParticipation_CarriesTheSegmentExecutionPlusBookingParticipationAndClient()
    {
        ServiceEntity service = new() { Id = Guid.NewGuid(), Name = "Pilates" };
        Guid employeeId = Guid.NewGuid();
        Appointment appointment = NewAppointment(employeeId, service);
        Booking booking = NewBooking(appointment);
        BookingSegmentParticipation participation = Participate(booking, Only(appointment));

        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, booking, participation);

        Assert.Equal(booking.Id.Value, execution.BookingId);
        Assert.Equal(participation.Id.Value, execution.ParticipationId);
        Assert.Equal(booking.ClientId, execution.ClientId);
        Assert.Equal(appointment.Id.Value, execution.AppointmentId);
        Assert.Equal(Only(appointment).Id.Value, execution.SegmentId);
        Assert.Equal(OrganizationId, execution.OrganizationId);
        Assert.Equal(appointment.CompanyId, execution.CompanyId);
        Assert.Equal(service.Id.Value, execution.ServiceId);
        Assert.Equal("Pilates", execution.ServiceName);
        Assert.Equal(employeeId, Assert.Single(execution.EmployeeIds));
        Assert.Equal(StartsAt, execution.StartsAt);
    }

    [Fact]
    public void ForParticipation_OneBookingOnTwoSegments_EachParticipationResolvesItsOwnSegment()
    {
        Guid employeeA = Guid.NewGuid(), employeeB = Guid.NewGuid();
        Appointment appointment = NewAppointment(employeeA);
        AppointmentSegment a = Only(appointment);
        AppointmentSegment b = SingleSegmentTestExtensions.AddTestSegment(appointment, Guid.NewGuid(), employeeB, null, StartsAt.AddMinutes(90), 30);
        Booking booking = NewBooking(appointment);
        BookingSegmentParticipation onA = Participate(booking, a);
        BookingSegmentParticipation onB = Participate(booking, b);

        ParticipationExecutionContext ctxA = ExecutionContextResolver.ForParticipation(appointment, booking, onA);
        ParticipationExecutionContext ctxB = ExecutionContextResolver.ForParticipation(appointment, booking, onB);

        Assert.Equal(a.Id.Value, ctxA.SegmentId);
        Assert.Equal(employeeA, Assert.Single(ctxA.EmployeeIds));
        Assert.Equal(a.ServiceId, ctxA.ServiceId);
        Assert.Equal(StartsAt, ctxA.StartsAt);
        Assert.Equal(b.Id.Value, ctxB.SegmentId);
        Assert.Equal(employeeB, Assert.Single(ctxB.EmployeeIds));
        Assert.Equal(b.ServiceId, ctxB.ServiceId);
        Assert.Equal(StartsAt.AddMinutes(90), ctxB.StartsAt);
        Assert.Equal(ctxA.BookingId, ctxB.BookingId);
        Assert.NotEqual(ctxA.ParticipationId, ctxB.ParticipationId);
    }

    [Fact]
    public void ForParticipation_OnATrainerlessOccurrence_HasANullEmployee()
    {
        Appointment appointment = NewAppointment(employeeId: null);
        appointment.Form = AppointmentForm.Group;
        Booking booking = NewBooking(appointment);

        Assert.Empty(ExecutionContextResolver.ForParticipation(appointment, booking, Participate(booking, Only(appointment))).EmployeeIds);
    }

    [Fact]
    public void ForParticipation_RejectsMissingInputs()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());
        Booking booking = NewBooking(appointment);
        BookingSegmentParticipation participation = Participate(booking, Only(appointment));

        Assert.Throws<ArgumentNullException>(() => ExecutionContextResolver.ForParticipation(appointment, null, participation));
        Assert.Throws<ArgumentNullException>(() => ExecutionContextResolver.ForParticipation(null, booking, participation));
        Assert.Throws<ArgumentNullException>(() => ExecutionContextResolver.ForParticipation(appointment, booking, null));

        Booking unsaved = NewBooking(appointment);
        unsaved.Id = null;
        Assert.Throws<InvalidOperationException>(() => ExecutionContextResolver.ForParticipation(appointment, unsaved, participation));
    }

    [Fact]
    public void ForParticipation_RejectsABookingOfAnotherOrganization_NoCrossTenantContext()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());
        Booking foreign = NewBooking(appointment, organizationId: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(
            () => ExecutionContextResolver.ForParticipation(appointment, foreign, Participate(foreign, Only(appointment))));
    }

    [Fact]
    public void ForParticipation_RejectsABookingOfAnotherAppointment_AndAParticipationOfAnotherBookingOrSegment()
    {
        Appointment appointment = NewAppointment(Guid.NewGuid());
        Booking elsewhere = NewBooking(appointment, appointmentId: Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(
            () => ExecutionContextResolver.ForParticipation(appointment, elsewhere, Participate(elsewhere, Only(appointment))));

        Booking booking = NewBooking(appointment);
        Booking otherBooking = NewBooking(appointment);
        Assert.Throws<InvalidOperationException>(
            () => ExecutionContextResolver.ForParticipation(appointment, booking, Participate(otherBooking, Only(appointment))));

        Appointment other = NewAppointment(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(
            () => ExecutionContextResolver.ForParticipation(appointment, booking, Participate(booking, Only(other))));
    }

    #endregion
}
