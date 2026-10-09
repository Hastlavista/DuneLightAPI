using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// K2 (ADR-0032) — JEDINO mjesto koje određuje je li termin ZATVOREN, isto za individualni i grupni termin. Zatvoren je kad je
/// ručno zatvoren (<see cref="Appointment.ClosedAt"/>) ILI je prošao trenutak automatskog zatvaranja: ponoć (zona poslovnice
/// termina) nakon poslovnog dana najkasnijeg od kraja zadnjeg segmenta, upisa termina i ponovnog otvaranja. Ponovno otvaranje
/// briše ručno zatvaranje i pomiče automatsko na kraj tog dana. Izvedeno pri svakoj provjeri — nema noćnog posla; automatsko
/// zatvaranje ne zarađuje proviziju i ne ističe listu čekanja (to radi samo prvi ručni close-out grupe).
///
/// Zatvaranje zaključava samo prijelaze IZ terminalnog statusa (Completed, NoShow, Cancelled): takva korekcija traži grant
/// korekcije po izvornom statusu i razlog. Prije zatvaranja promjena statusa je normalno označavanje (bez granta i razloga).
/// Sudjelovanje koje je ostalo Confirmed označava se i nakon zatvaranja bez granta (audit to bilježi).
/// </summary>
public static class AppointmentClosure
{
    public static DateTimeOffset AutoClosesAt(Appointment appointment, OrganizationCalendar calendar)
    {
        DateTimeOffset anchor = AppointmentRange.Of(appointment).PlannedEnd;
        if (appointment.CreatedAt > anchor)
            anchor = appointment.CreatedAt;
        if (appointment.ReopenedAt > anchor)
            anchor = appointment.ReopenedAt.Value;
        return calendar.StartOfDay(calendar.LocalDate(anchor).AddDays(1));
    }

    public static bool IsClosed(Appointment appointment, OrganizationCalendar calendar, DateTimeOffset now) =>
        appointment.ClosedAt.HasValue || now >= AutoClosesAt(appointment, calendar);

    /// <summary>Grant korekcije za izvorni status; null za Confirmed (označavanje, ne korekcija).</summary>
    public static string CorrectionGrantFor(ParticipationStatus from) => from switch
    {
        ParticipationStatus.Completed => Grants.AppointmentsCorrectionsCompleted,
        ParticipationStatus.NoShow => Grants.AppointmentsCorrectionsNoShow,
        ParticipationStatus.Cancelled => Grants.AppointmentsCorrectionsCancelled,
        _ => null
    };

    /// <summary>Korekcija iz terminalnog statusa na zatvorenom terminu: grant po izvornom statusu (403) i razlog (400).
    /// Grant nikad ne širi own opseg — pristup sudjelovanju provjerava pozivatelj.</summary>
    public static void EnsureCorrectionAllowed(GrantContext grants, ParticipationStatus from, string reason)
    {
        string grant = CorrectionGrantFor(from);
        if (grant == null)
            return;
        if (!grants.Has(grant))
            throw new ForbiddenAppException($"Korekcija statusa {from} na zatvorenom terminu zahtijeva ovlast {grant}.");
        EnsureReason(reason, "Korekcija statusa na zatvorenom terminu zahtijeva razlog.");
    }

    /// <summary>Ponovno otvaranje: grant korekcije za SVAKI terminalni status prisutan na terminu (inače bi otvaranje zaobišlo
    /// pravilo); bez terminalnih sudjelovanja dovoljan je bilo koji grant korekcije. Razlog obavezan.</summary>
    public static void EnsureReopenAllowed(GrantContext grants, IEnumerable<ParticipationStatus> statuses, string reason)
    {
        List<string> required = statuses.Select(CorrectionGrantFor).Where(g => g != null).Distinct().OrderBy(g => g).ToList();
        if (required.Count == 0)
        {
            if (!grants.HasAny(Grants.AppointmentsCorrectionsCompleted, Grants.AppointmentsCorrectionsNoShow, Grants.AppointmentsCorrectionsCancelled))
                throw new ForbiddenAppException("Ponovno otvaranje termina zahtijeva ovlast korekcije (appointments.corrections.*).");
        }
        else
        {
            List<string> missing = required.Where(g => !grants.Has(g)).ToList();
            if (missing.Count > 0)
                throw new ForbiddenAppException($"Ponovno otvaranje termina zahtijeva ovlast {string.Join(", ", missing)}.");
        }
        EnsureReason(reason, "Ponovno otvaranje termina zahtijeva razlog.");
    }

    public static void EnsureReason(string reason, string message)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationAppException(ErrorCodes.CorrectionReasonRequired, message);
    }
}
