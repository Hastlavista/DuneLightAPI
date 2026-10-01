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
/// Phase M1A — JEDINO mjesto koje određuje životni ciklus termina. Termin je agregat: njegov status se IZVODI iz statusa
/// SVIH sudjelovanja na svim njegovim segmentima i nikad se ne postavlja neovisno o njima (nema Completed termina,
/// nema ručnog "reopen" — korekcija koja vrati sudjelovanje na Confirmed automatski vraća termin u Scheduled).
///
/// Pravila (redom):
/// 1. bilo koje sudjelovanje Confirmed                                  → Scheduled;
/// 2. inače, SVA sudjelovanja Cancelled                                  → Cancelled;
/// 3. inače (Completed/NoShow, s ili bez Cancelled)                      → Closed (razriješeno, ne nužno plaćeno).
///
/// Termin BEZ sudjelovanja (legitimno: grupni occurrence generiran za grupu bez aktivnih članova, gosti dolaze tek
/// check-inom) NEMA izvedeni status — <see cref="Derive"/> vraća null i status se ne mijenja (vidi napomenu u
/// AppointmentService.ChangeToTerminalStatus za jedinu eksplicitnu iznimku).
/// </summary>
public static class AppointmentLifecycle
{
    public static AppointmentStatus? Derive(IEnumerable<ParticipationStatus> participationStatuses)
    {
        ArgumentNullException.ThrowIfNull(participationStatuses);
        List<ParticipationStatus> statuses = participationStatuses.ToList();

        if (statuses.Count == 0)
            return null;
        if (statuses.Any(s => s == ParticipationStatus.Confirmed))
            return AppointmentStatus.Scheduled;
        if (statuses.All(s => s == ParticipationStatus.Cancelled))
            return AppointmentStatus.Cancelled;
        return AppointmentStatus.Closed;
    }

    /// <summary>Izvođenje nad terminom čija su sudjelovanja već u memoriji (npr. termin koji se tek stvara).</summary>
    public static AppointmentStatus? Derive(Appointment appointment) =>
        Derive(appointment.Bookings.SelectMany(b => b.Participations).Select(p => p.Status));

    /// <summary>
    /// Ponovno izvodi i (ako se promijenio) persistira status termina unutar pozivateljeve transakcije, s istim "Status"
    /// audit zapisom kao prije. Poziva se NAKON prijelaza sudjelovanja i njegovih nuspojava (uključujući promociju liste
    /// čekanja, koja može dodati novo Confirmed sudjelovanje). Termin se zaključava (isti redak koji pozivatelj već drži —
    /// besplatan re-lock). Statusi sudjelovanja čitaju se praćenim upitom: već praćena (u ovoj transakciji promijenjena)
    /// sudjelovanja daju svoje trenutno stanje. Vraća novi status, ili null kad se ništa nije promijenilo.
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

        AppointmentStatus? derived = Derive(participations.Select(p => p.Status));
        if (derived == null || derived.Value == appointment.Status)
            return null;

        await Apply(appointmentHandler, auditLogHandler, uow, appointment, derived.Value, userId);
        return derived;
    }

    /// <summary>Persistira već odlučen status (izvedeni, ili jedina eksplicitna iznimka praznog termina) uz audit.</summary>
    internal static async Task Apply(
        IAppointmentHandler appointmentHandler, IAppointmentAuditLogHandler auditLogHandler, IUnitOfWork uow,
        Appointment appointment, AppointmentStatus status, Guid userId)
    {
        AppointmentStatus oldStatus = appointment.Status;
        if (oldStatus == status)
            return;

        appointment.Status = status;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;
        await appointmentHandler.UpdateScalar(uow, appointment);

        await auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id.GetValueOrDefault(),
            ChangeType = "Status",
            OldValue = oldStatus.ToString(),
            NewValue = status.ToString(),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
    }
}
