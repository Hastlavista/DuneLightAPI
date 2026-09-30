using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje konstruira novi Appointment redak (današnji jednostruki oblik). Samo sastavlja perzistencijski
/// oblik — sva validacija (podobnost, radno vrijeme, preklapanja, vlasništvo) ostaje u servisima PRIJE poziva.
/// Kad se uvede AppointmentSegment, <see cref="AppointmentFrame"/> se ovdje preusmjerava u segment. Navigacijska svojstva
/// se namjerno ne postavljaju (termin se dodaje kroz svjež DbContext — vidi GroupService.GenerateAppointments).
/// </summary>
public static class AppointmentFactory
{
    /// <summary>Individualni termin — Create i /recurring (Scheduled, uz RecurrenceGroupId za niz) te CompleteNew
    /// ("upiši odrađeno": odmah Completed).</summary>
    public static Appointment CreateIndividual(
        Guid organizationId, Guid companyId, AppointmentFrame frame, AppointmentStatus status, string note,
        Guid? recurrenceGroupId, Guid createdBy, DateTimeOffset createdAt)
    {
        Appointment appointment = NewAppointment(organizationId, companyId, frame, AppointmentForm.Individual, status, createdBy, createdAt);
        appointment.Note = note;
        appointment.RecurrenceGroupId = recurrenceGroupId;
        return appointment;
    }

    /// <summary>Generirani occurrence grupe — uvijek Scheduled, bez napomene, veza na Group/GroupSlot.</summary>
    public static Appointment CreateGroupOccurrence(
        Guid organizationId, Guid companyId, AppointmentFrame frame, Guid groupId, Guid groupSlotId, Guid createdBy, DateTimeOffset createdAt)
    {
        Appointment appointment = NewAppointment(
            organizationId, companyId, frame, AppointmentForm.Group, AppointmentStatus.Scheduled, createdBy, createdAt);
        appointment.GroupId = groupId;
        appointment.GroupSlotId = groupSlotId;
        return appointment;
    }

    private static Appointment NewAppointment(
        Guid organizationId, Guid companyId, AppointmentFrame frame, AppointmentForm form, AppointmentStatus status,
        Guid createdBy, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Appointment appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Form = form,
            CompanyId = companyId,
            Status = status,
            CreatedAt = createdAt,
            CreatedBy = createdBy
        };
        AppointmentFrameMutator.Apply(appointment, frame);
        return appointment;
    }
}
