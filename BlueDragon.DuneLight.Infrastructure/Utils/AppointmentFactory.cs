using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Phase M1B — plan jednog segmenta za konstrukciju termina: izvršni podaci segmenta i njegovi sudionici.
/// Zaposlenici su skup (shema podržava više; broj ograničava validacija servisa — ograničenje proizvoda).</summary>
public sealed record SegmentPlan(
    Guid ServiceId,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd,
    IReadOnlyList<Guid> EmployeeIds,
    Guid? RoomId,
    IReadOnlyList<ParticipantPlan> Participants,
    IReadOnlyList<SegmentResourcePlan> Resources = null);

/// <summary>Phase M1D: resurs koji segment zauzima, u količini QuantityRequired (&gt; 0).</summary>
public sealed record SegmentResourcePlan(Guid ResourceId, int QuantityRequired);

/// <summary>Sudionik segmenta: klijent i cjenovno stanje njegovog sudjelovanja na tom segmentu.</summary>
public sealed record ParticipantPlan(Guid ClientId, BookingPricing Pricing);

/// <summary>
/// Phase M1B — KONSTRUKCIJSKA JEZGRA termina (jedino mjesto koje sastavlja novi Appointment): termin (spremnik, uvijek
/// nastaje kao Scheduled) + N segmenata (izvršne jedinice) + JEDINSTVENI Booking po klijentu + po jedno sudjelovanje za
/// svaki segment u kojem klijent sudjeluje (klijent na dva segmenta = jedan Booking + dva sudjelovanja; različiti klijenti
/// smiju birati različite segmente). Bez odabira "jedinog" segmenta i bez okvira termina. Samo sastavlja perzistencijski
/// oblik — sva validacija (podobnost, radno vrijeme, preklapanja, vlasništvo, ograničenja proizvoda) ostaje u servisima
/// PRIJE poziva. Navigacijska svojstva prema katalogu se namjerno ne postavljaju (termin se dodaje kroz svjež DbContext).
/// </summary>
public static class AppointmentFactory
{
    /// <summary>Individualni termin (Create, /recurring, CompleteNew). Sudjelovanja nastaju u <paramref name="initialStatus"/>
    /// (Confirmed; CompleteNew: Completed) — status termina se zatim IZVODI (AppointmentLifecycle).</summary>
    public static Appointment CreateIndividual(
        Guid organizationId, Guid companyId, string note, Guid? recurrenceGroupId, Guid createdBy, DateTimeOffset createdAt,
        IReadOnlyList<SegmentPlan> segments, ParticipationStatus initialStatus)
    {
        Appointment appointment = NewAppointment(organizationId, companyId, AppointmentForm.Individual, createdBy, createdAt);
        appointment.Note = note;
        appointment.RecurrenceGroupId = recurrenceGroupId;
        Populate(appointment, segments, initialStatus, createdAt);
        return appointment;
    }

    /// <summary>Generirani occurrence grupe — bez napomene, veza na Group/GroupSlot. Do GroupSegmentTemplates grupa generira
    /// JEDAN segment (sudionici = aktivni članovi; može ih biti nula — valjan prazan occurrence sa segmentom).</summary>
    public static Appointment CreateGroupOccurrence(
        Guid organizationId, Guid companyId, Guid groupId, Guid groupSlotId, Guid createdBy, DateTimeOffset createdAt, SegmentPlan segment)
    {
        Appointment appointment = NewAppointment(organizationId, companyId, AppointmentForm.Group, createdBy, createdAt);
        appointment.GroupId = groupId;
        appointment.GroupSlotId = groupSlotId;
        Populate(appointment, new[] { segment }, ParticipationStatus.Confirmed, createdAt);
        return appointment;
    }

    private static Appointment NewAppointment(
        Guid organizationId, Guid companyId, AppointmentForm form, Guid createdBy, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Form = form,
        CompanyId = companyId,
        Status = AppointmentStatus.Scheduled,
        CreatedAt = createdAt,
        CreatedBy = createdBy
    };

    private static void Populate(
        Appointment appointment, IReadOnlyList<SegmentPlan> plans, ParticipationStatus initialStatus, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(plans);
        if (plans.Count == 0)
            throw new InvalidAppointmentSegmentStateException("Termin mora imati barem jedan segment.");

        Dictionary<Guid, Booking> bookingByClient = new();
        foreach (SegmentPlan plan in plans)
        {
            AppointmentSegment segment = NewSegment(appointment, plan, createdAt);
            foreach (ParticipantPlan participant in plan.Participants)
            {
                if (!bookingByClient.TryGetValue(participant.ClientId, out Booking booking))
                {
                    booking = BookingFactory.NewContainer(appointment.OrganizationId, appointment.Id.GetValueOrDefault(), participant.ClientId, createdAt);
                    bookingByClient.Add(participant.ClientId, booking);
                    appointment.Bookings.Add(booking);
                }

                BookingFactory.AddParticipation(booking, segment, initialStatus, participant.Pricing, createdAt);
            }
        }
    }

    private static AppointmentSegment NewSegment(Appointment appointment, SegmentPlan plan, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.PlannedEnd <= plan.PlannedStart)
            throw new InvalidAppointmentSegmentStateException("Kraj segmenta mora biti nakon početka.");

        AppointmentSegment segment = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = appointment.OrganizationId,
            AppointmentId = appointment.Id.GetValueOrDefault(),
            ServiceId = plan.ServiceId,
            RoomId = plan.RoomId,
            PlannedStart = plan.PlannedStart,
            PlannedEnd = plan.PlannedEnd,
            CreatedAt = createdAt
        };
        foreach (Guid employeeId in plan.EmployeeIds.Distinct())
            segment.Employees.Add(new AppointmentSegmentEmployee { AppointmentSegmentId = segment.Id.GetValueOrDefault(), EmployeeId = employeeId });
        foreach (SegmentResourcePlan resource in plan.Resources ?? Array.Empty<SegmentResourcePlan>())
        {
            if (resource.QuantityRequired <= 0)
                throw new InvalidAppointmentSegmentStateException("Količina resursa mora biti veća od 0.");
            if (segment.Resources.Any(r => r.ResourceId == resource.ResourceId))
                throw new InvalidAppointmentSegmentStateException($"Resurs {resource.ResourceId} je već dodijeljen segmentu.");
            segment.Resources.Add(new AppointmentSegmentResource
            {
                AppointmentSegmentId = segment.Id.GetValueOrDefault(), ResourceId = resource.ResourceId, QuantityRequired = resource.QuantityRequired
            });
        }

        appointment.Segments.Add(segment);
        return segment;
    }
}
