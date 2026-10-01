using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Pomoćnici nad JEDNIM segmentom (izvršnom jedinicom). Phase M1B: termin nema "izvršni okvir" — više nema odabira
/// "jedinog" segmenta ovdje (vidi <see cref="SingleSegmentCompatibility"/> za privremene granice postojećih endpointa).
/// </summary>
public static class AppointmentSegments
{
    /// <summary>Zaposlenik segmenta (null = bez zaposlenika, npr. grupa bez trenera). Više zaposlenika na segmentu je
    /// OGRANIČENJE PROIZVODA (atribucija cijene/provizije je otvorena odluka), ne sheme — baca iznimku.</summary>
    public static Guid? GetSingleEmployeeId(AppointmentSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return GetSingleEmployeeId(segment.Id, segment.Employees.Select(e => e.EmployeeId).ToList());
    }

    /// <summary>Isto pravilo nad projekcijom (bez učitanog entiteta) — vidi SchedulingOccupancyHandler.</summary>
    public static Guid? GetSingleEmployeeId(Guid? segmentId, IReadOnlyList<Guid> employeeIds)
    {
        return employeeIds.Count switch
        {
            0 => null,
            1 => employeeIds[0],
            _ => throw new InvalidAppointmentSegmentStateException(
                $"Segment {segmentId} ima {employeeIds.Count} zaposlenika — više zaposlenika po segmentu još nije podržano.")
        };
    }

    /// <summary>Trajanje segmenta izvedeno iz planiranog raspona.</summary>
    public static int DurationMinutes(AppointmentSegment segment) => (int)(segment.PlannedEnd - segment.PlannedStart).TotalMinutes;

    public static bool HasEmployee(AppointmentSegment segment, Guid employeeId) => segment.Employees.Any(e => e.EmployeeId == employeeId);
}

/// <summary>
/// Phase M1B — PRIVREMENA granica postojećih (single-segment) API operacija: Update/Move/CompleteExisting/AddBooking/
/// gost na check-inu/lista čekanja i grupni occurrence adresiraju TERMIN, a produkcija trenutno stvara točno jedan
/// segment (višesegmentno kreiranje je isključeno dok validacija zauzetosti i kapaciteta po segmentu ne bude potpuna;
/// grupe do GroupSegmentTemplates generiraju jedan segment). Ovdje se taj segment razrješava NA GRANICI i dalje se
/// prosljeđuje eksplicitno; termin s više segmenata odbija se (<see cref="InvalidAppointmentSegmentStateException"/>)
/// umjesto proizvoljnog izbora. Ne koristiti u jezgri (konstrukcija, vlasništvo, kontekst, read-model, raspon).
/// </summary>
public static class SingleSegmentCompatibility
{
    public static AppointmentSegment Resolve(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return appointment.Segments.Count switch
        {
            1 => appointment.Segments[0],
            0 => throw new InvalidAppointmentSegmentStateException(
                $"Termin {appointment.Id} nema segment (ili segmenti nisu učitani)."),
            _ => throw new InvalidAppointmentSegmentStateException(
                $"Termin {appointment.Id} ima {appointment.Segments.Count} segmenata — ova (single-segment) operacija ga ne može " +
                "adresirati; koristiti segmentnu operaciju.")
        };
    }
}

/// <summary>Termin/segment nije u obliku koji operacija podržava (nula segmenata, više segmenata na single-segment
/// granici, više zaposlenika na segmentu). Integritetna greška, ne korisnička (500).</summary>
public sealed class InvalidAppointmentSegmentStateException : InvalidOperationException
{
    public InvalidAppointmentSegmentStateException(string message) : base(message)
    {
    }
}
