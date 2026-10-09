using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1A/M1A.1 — JEDINO mjesto koje određuje životni ciklus termina. Status je agregat izveden iz DVA ulaza:
/// statusa SVIH sudjelovanja (na svim segmentima) i TRENUTNE eksplicitne otkazanosti termina
/// (<see cref="Appointment.CancelledAt"/>, postavlja je samo otkazivanje na razini termina). Eksplicitna otkazanost se
/// NIKAD ne izvodi iz sudjelovanja: "svi klijenti pojedinačno otkazali" ≠ "sesija otkazana".
///
/// Pravila (redom):
/// 1. bilo koje sudjelovanje Confirmed                          → Scheduled (posao nerazriješen; eventualna eksplicitna
///    otkazanost je time poništena — vidi <see cref="Refresh"/>);
/// 2. bilo koje sudjelovanje Completed ili NoShow               → Closed (operativno razriješeno, ne nužno plaćeno);
/// 3. inače (nula sudjelovanja ili sva Cancelled):
///    eksplicitno otkazan termin → Cancelled, inače → Scheduled (sesija i dalje postoji i može primiti posao).
/// </summary>
public static class AppointmentLifecycle
{
    public static AppointmentStatus Derive(IEnumerable<ParticipationStatus> participationStatuses, bool isExplicitlyCancelled)
    {
        ArgumentNullException.ThrowIfNull(participationStatuses);
        List<ParticipationStatus> statuses = participationStatuses.ToList();

        if (statuses.Any(s => s == ParticipationStatus.Confirmed))
            return AppointmentStatus.Scheduled;
        if (statuses.Any(s => s == ParticipationStatus.Completed || s == ParticipationStatus.NoShow))
            return AppointmentStatus.Closed;
        return isExplicitlyCancelled ? AppointmentStatus.Cancelled : AppointmentStatus.Scheduled;
    }

    /// <summary>Izvođenje nad terminom čija su sudjelovanja već u memoriji (npr. termin koji se tek stvara).</summary>
    public static AppointmentStatus Derive(Appointment appointment) =>
        Derive(appointment.Bookings.SelectMany(b => b.Participations).Select(p => p.Status), appointment.IsExplicitlyCancelled);

    /// <summary>
    /// Eksplicitno otkazivanje TERMINA (jedini izvor <see cref="Appointment.CancelledAt"/>): postavlja trenutnu otkazanost
    /// i bilježi "AppointmentCancelled" audit (i kad je ishod Closed zbog već izvršenog rada). Pozivatelj je prije toga
    /// otkazao sva Confirmed sudjelovanja; pozivatelj sprema termin, a status se zatim IZVODI (<see cref="Refresh"/>).
    /// </summary>
    public static async Task MarkExplicitlyCancelled(
        IAppointmentAuditLogHandler auditLogHandler, IUnitOfWork uow, Appointment appointment, string reason, Guid userId,
        (Guid? Id, string Name) reasonCode = default, DateTimeOffset? at = null)
    {
        // K1-5: isti trenutak kao kaskada otkazivanja sudjelovanja — po njemu "vrati termin" prepoznaje što je otkazao otkaz termina.
        DateTimeOffset now = at ?? DateTimeOffset.UtcNow;
        appointment.CancelledAt = now;
        appointment.CancelledBy = userId;
        appointment.CancellationReason = reason;
        // K1-4: šifra razloga i naziv u trenutku otkaza (snapshot).
        appointment.CancellationReasonCodeId = reasonCode.Id;
        appointment.CancellationReasonCodeName = reasonCode.Name;
        appointment.UpdatedAt = now;
        appointment.UpdatedBy = userId;

        await auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id.GetValueOrDefault(),
            ChangeType = "AppointmentCancelled",
            OldValue = null,
            NewValue = reason,
            ChangedAt = now,
            ChangedBy = userId
        });
    }

    /// <summary>
    /// Ponovno izvodi i (ako se promijenio) persistira status termina unutar pozivateljeve transakcije, s "Status" audit
    /// zapisom. Poziva se NAKON prijelaza sudjelovanja i njegovih nuspojava (uključujući promociju liste čekanja). Kad
    /// ishod postane Scheduled (neko sudjelovanje je opet Confirmed) a termin je bio eksplicitno otkazan, TRENUTNI učinak
    /// otkazivanja se briše (CancelledAt/By/CancellationReason) uz "AppointmentCancellationCleared" audit — povijesni
    /// "AppointmentCancelled" zapis ostaje. Termin se zaključava (isti redak koji pozivatelj već drži). Statusi sudjelovanja
    /// čitaju se praćenim upitom (u ovoj transakciji promijenjena sudjelovanja daju trenutno stanje).
    /// Vraća novi status, ili null kad se status nije promijenio.
    /// </summary>
    public static async Task<AppointmentStatus?> Refresh(
        IAppointmentHandler appointmentHandler, IAppointmentAuditLogHandler auditLogHandler, IUnitOfWork uow,
        Guid organizationId, Guid appointmentId, Guid userId)
    {
        Appointment appointment = await appointmentHandler.GetForUpdate(uow, organizationId, appointmentId);
        if (appointment == null)
            return null;

        List<BookingSegmentParticipation> participations = await uow.Context.BookingSegmentParticipations
            .Where(p => p.OrganizationId == organizationId && p.Segment.AppointmentId == appointmentId)
            .ToListAsync();

        AppointmentStatus derived = Derive(participations.Select(p => p.Status), appointment.IsExplicitlyCancelled);
        AppointmentStatus oldStatus = appointment.Status;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool clearsCancellation = derived == AppointmentStatus.Scheduled && appointment.IsExplicitlyCancelled;

        if (clearsCancellation)
        {
            await auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                ChangeType = "AppointmentCancellationCleared",
                OldValue = appointment.CancellationReason,
                NewValue = null,
                ChangedAt = now,
                ChangedBy = userId
            });
            appointment.CancelledAt = null;
            appointment.CancelledBy = null;
            appointment.CancellationReason = null;
            appointment.CancellationReasonCodeId = null;
            appointment.CancellationReasonCodeName = null;
        }

        if (derived == oldStatus && !clearsCancellation)
            return null;

        appointment.Status = derived;
        appointment.UpdatedAt = now;
        appointment.UpdatedBy = userId;
        await appointmentHandler.UpdateScalar(uow, appointment);

        if (derived != oldStatus)
        {
            await auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                ChangeType = "Status",
                OldValue = oldStatus.ToString(),
                NewValue = derived.ToString(),
                ChangedAt = now,
                ChangedBy = userId
            });
            return derived;
        }

        return null;
    }
}
