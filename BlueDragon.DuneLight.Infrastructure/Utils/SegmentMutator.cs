using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1B — JEDINO mjesto koje piše izvršne podatke ZADANOG segmenta (zamjenjuje nekadašnji mutator okvira termina: termin više
/// nema okvir). Eksplicitne segmentne operacije — promjena usluge, vremena, dodjele zaposlenika, prostorije — nad
/// učitanim entitetom u memoriji: bez čitanja, spremanja, validacije i audita (to ostaje kod pozivatelja — segmentne
/// naredbe).
/// </summary>
public static class SegmentMutator
{
    public static void ChangeService(AppointmentSegment segment, Guid serviceId, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(segment);
        segment.ServiceId = serviceId;
        segment.UpdatedAt = updatedAt;
    }

    public static void ChangeTime(AppointmentSegment segment, DateTimeOffset plannedStart, DateTimeOffset plannedEnd, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (plannedEnd <= plannedStart)
            throw new InvalidAppointmentSegmentStateException($"Segment {segment.Id}: kraj mora biti nakon početka.");
        segment.PlannedStart = plannedStart;
        segment.PlannedEnd = plannedEnd;
        segment.UpdatedAt = updatedAt;
    }

    public static void ChangeRoom(AppointmentSegment segment, Guid? roomId, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(segment);
        segment.RoomId = roomId;
        segment.UpdatedAt = updatedAt;
    }

    /// <summary>Zamjenjuje dodjelu zaposlenika segmenta (samo FK vrijednosti; spremanje dodjela radi AppointmentHandler).
    /// Nepromijenjen skup je no-op.</summary>
    public static void AssignEmployees(AppointmentSegment segment, IReadOnlyCollection<Guid> employeeIds, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(employeeIds);
        HashSet<Guid> target = employeeIds.ToHashSet();
        if (target.SetEquals(segment.Employees.Select(e => e.EmployeeId)))
            return;

        segment.Employees.Clear();
        foreach (Guid employeeId in target)
            segment.Employees.Add(new AppointmentSegmentEmployee { AppointmentSegmentId = segment.Id.GetValueOrDefault(), EmployeeId = employeeId });
        segment.UpdatedAt = updatedAt;
    }
    /// <summary>Phase M1E — zamjenjuje dodjelu resursa segmenta (diff nad učitanom kolekcijom: promjena količine, uklanjanje
    /// nenavedenih, dodavanje novih). Nepromijenjen skup je no-op.</summary>
    public static void ReplaceResources(AppointmentSegment segment, IReadOnlyList<(Guid ResourceId, int Quantity)> resources, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(resources);
        Dictionary<Guid, int> target = resources.ToDictionary(r => r.ResourceId, r => r.Quantity);
        bool changed = false;
        foreach (AppointmentSegmentResource existing in segment.Resources.ToList())
        {
            if (!target.TryGetValue(existing.ResourceId, out int quantity))
            {
                segment.Resources.Remove(existing);
                changed = true;
            }
            else if (existing.QuantityRequired != quantity)
            {
                existing.QuantityRequired = quantity;
                changed = true;
            }
        }

        foreach ((Guid resourceId, int quantity) in target)
        {
            if (segment.Resources.Any(r => r.ResourceId == resourceId))
                continue;
            segment.Resources.Add(new AppointmentSegmentResource
            {
                AppointmentSegmentId = segment.Id.GetValueOrDefault(),
                ResourceId = resourceId,
                QuantityRequired = quantity
            });
            changed = true;
        }

        if (changed)
            segment.UpdatedAt = updatedAt;
    }
}
