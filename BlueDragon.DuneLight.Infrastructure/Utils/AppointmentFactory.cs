using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje konstruira novi Appointment redak — od Phase D3A uvijek termin (kontejner) + točno JEDAN
/// autoritativni AppointmentSegment s okvirom (<see cref="AppointmentFrame"/>: usluga, raspon, prostorija, zaposlenik);
/// oboje se sprema istim SaveChanges (jedna transakcija). Samo sastavlja perzistencijski oblik — sva validacija
/// (podobnost, radno vrijeme, preklapanja, vlasništvo) ostaje u servisima PRIJE poziva. Sudjelovanja se ne kreiraju.
/// Navigacijska svojstva prema katalogu se namjerno ne postavljaju (termin se dodaje kroz svjež DbContext).
/// </summary>
public static class AppointmentFactory
{
    /// <summary>Individualni termin — Create i /recurring (uz RecurrenceGroupId za niz) te CompleteNew. Phase M1A: termin
    /// UVIJEK nastaje kao Scheduled; daljnji status se izvodi iz sudjelovanja (AppointmentLifecycle) — CompleteNew ga
    /// izvodi iz upravo stvorenih Completed sudjelovanja (→ Closed).</summary>
    public static Appointment CreateIndividual(
        Guid organizationId, Guid companyId, AppointmentFrame frame, string note,
        Guid? recurrenceGroupId, Guid createdBy, DateTimeOffset createdAt)
    {
        Appointment appointment = NewAppointment(organizationId, companyId, frame, AppointmentForm.Individual, createdBy, createdAt);
        appointment.Note = note;
        appointment.RecurrenceGroupId = recurrenceGroupId;
        return appointment;
    }

    /// <summary>Generirani occurrence grupe — uvijek Scheduled, bez napomene, veza na Group/GroupSlot.</summary>
    public static Appointment CreateGroupOccurrence(
        Guid organizationId, Guid companyId, AppointmentFrame frame, Guid groupId, Guid groupSlotId, Guid createdBy, DateTimeOffset createdAt)
    {
        Appointment appointment = NewAppointment(organizationId, companyId, frame, AppointmentForm.Group, createdBy, createdAt);
        appointment.GroupId = groupId;
        appointment.GroupSlotId = groupSlotId;
        return appointment;
    }

    private static Appointment NewAppointment(
        Guid organizationId, Guid companyId, AppointmentFrame frame, AppointmentForm form, Guid createdBy, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Appointment appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Form = form,
            CompanyId = companyId,
            Status = AppointmentStatus.Scheduled,
            CreatedAt = createdAt,
            CreatedBy = createdBy
        };
        AppointmentFrameMutator.NewSegment(appointment, frame);
        return appointment;
    }
}
