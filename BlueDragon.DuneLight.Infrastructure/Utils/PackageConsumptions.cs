using System;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3B3A — JEDINO mjesto koje čita potrošnju paketa za Booking: povijest PackageConsumption njegovog jedinog
/// sudjelovanja (BookingParticipations.GetSingleParticipation). Booking više nema paketnih stupaca; sve što su
/// pokazivali (ClientPackageId/CoverageType/PackageCoverageApplied/Returned/ReturnedAt/ReturnedBy) izvodi se ovdje.
/// Pisanje isključivo kroz IPackageConsumptionLedgerService.
/// </summary>
public static class PackageConsumptions
{
    /// <summary>Aktivna (Consumed) potrošnja sudjelovanja ili null — najviše jedna (unique indeks u bazi).</summary>
    public static PackageConsumption ActiveOf(Booking booking) =>
        BookingParticipations.GetSingleParticipation(booking).PackageConsumptions
            .SingleOrDefault(c => c.Status == PackageConsumptionStatus.Consumed);

    /// <summary>"Je li obveza namirena paketom" — aktivna potrošnja postoji (zamjenjuje ClientPackageId &amp;&amp;
    /// PackageCoverageApplied &amp;&amp; !PackageCoverageReturned). Paket nije novac: namirenje paketom je cijela usluga
    /// (jedinični paketi), ne iznos.</summary>
    public static bool IsSettledByPackage(Booking booking) => ActiveOf(booking) != null;

    /// <summary>Najnovija potrošnja (aktivna ima prednost, inače zadnja poništena) ili null.</summary>
    public static PackageConsumption LatestOf(Booking booking)
    {
        BookingSegmentParticipation participation = BookingParticipations.GetSingleParticipation(booking);
        return participation.PackageConsumptions.SingleOrDefault(c => c.Status == PackageConsumptionStatus.Consumed)
               ?? participation.PackageConsumptions.OrderByDescending(c => c.CreatedAt).FirstOrDefault();
    }

    /// <summary>
    /// Izvedeni prikaz paketnog pokrića za postojeće API ugovore (BookingDto/GroupAttendanceEntryDto), bez spremljenih
    /// zastavica na Bookingu:
    /// - bez potrošnje: ništa (null/false) — osim grupnog check-ina bez paketa (vidi dolje);
    /// - zadnja potrošnja: ClientPackageId te potrošnje (i nakon poništenja — "koji je paket korišten"), Applied = true,
    ///   Returned = je li poništena (+ kada/tko);
    /// - CoverageType postoji samo za Form=Group (isto kao prije): SessionPackage kad je potrošnja skinula jedinicu,
    ///   MonthlyPackage kad je paket neograničen (Units = 0); grupno sudjelovanje Completed BEZ aktivne potrošnje je
    ///   check-in razriješen bez paketa → SinglePaid (bez paketnog linka, Applied/Returned = false).
    /// </summary>
    public static PackageCoverageView CoverageOf(Booking booking, AppointmentForm form)
    {
        BookingSegmentParticipation participation = BookingParticipations.GetSingleParticipation(booking);
        PackageConsumption latest = LatestOf(booking);
        bool isGroup = form == AppointmentForm.Group;

        if (isGroup && participation.Status == ParticipationStatus.Completed &&
            (latest == null || latest.Status == PackageConsumptionStatus.Reversed))
            return new PackageCoverageView(null, AttendanceCoverageType.SinglePaid, false, false, null, null);

        if (latest == null)
            return PackageCoverageView.None;

        AttendanceCoverageType? coverageType = isGroup
            ? latest.Units > 0 ? AttendanceCoverageType.SessionPackage : AttendanceCoverageType.MonthlyPackage
            : null;
        bool reversed = latest.Status == PackageConsumptionStatus.Reversed;
        return new PackageCoverageView(latest.ClientPackageId, coverageType, true, reversed, latest.ReversedAt, latest.ReversedBy);
    }
}

/// <summary>Izvedeno paketno pokriće Bookinga (vidi PackageConsumptions.CoverageOf) — isti oblik kao bivša polja Bookinga.</summary>
public sealed record PackageCoverageView(
    Guid? ClientPackageId,
    AttendanceCoverageType? CoverageType,
    bool PackageCoverageApplied,
    bool PackageCoverageReturned,
    DateTimeOffset? PackageCoverageReturnedAt,
    Guid? PackageCoverageReturnedBy)
{
    public static readonly PackageCoverageView None = new(null, null, false, false, null, null);
}
