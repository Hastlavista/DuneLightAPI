using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3B3A/M0 — JEDINO mjesto koje čita potrošnju paketa: povijest PackageConsumption ZADANOG sudjelovanja (paket
/// pokriva jednu izvršnu jedinicu, nikad "Booking"). Booking nema paketnih stupaca; sve što su pokazivali
/// (ClientPackageId/CoverageType/PackageCoverageApplied/Returned/ReturnedAt/ReturnedBy) izvodi se ovdje.
/// Pisanje isključivo kroz IPackageConsumptionLedgerService.
/// </summary>
public static class PackageConsumptions
{
    /// <summary>Aktivna (Consumed) potrošnja sudjelovanja ili null — najviše jedna (unique indeks u bazi).</summary>
    public static PackageConsumption ActiveOf(BookingSegmentParticipation participation) =>
        participation.PackageConsumptions.SingleOrDefault(c => c.Status == PackageConsumptionStatus.Consumed);

    /// <summary>"Je li izvršna jedinica namirena paketom" — aktivna potrošnja postoji. Paket nije novac: namirenje
    /// paketom je cijela usluga (jedinični paketi), ne iznos.</summary>
    public static bool IsSettledByPackage(BookingSegmentParticipation participation) => ActiveOf(participation) != null;

    /// <summary>Najnovija potrošnja (aktivna ima prednost, inače zadnja poništena) ili null.</summary>
    public static PackageConsumption LatestOf(BookingSegmentParticipation participation) =>
        participation.PackageConsumptions.SingleOrDefault(c => c.Status == PackageConsumptionStatus.Consumed)
        ?? participation.PackageConsumptions.OrderByDescending(c => c.CreatedAt).FirstOrDefault();

    /// <summary>
    /// Izvedeni prikaz paketnog pokrića jednog sudjelovanja za postojeće API ugovore:
    /// - bez potrošnje: ništa (null/false) — osim grupnog check-ina bez paketa (vidi dolje);
    /// - zadnja potrošnja: ClientPackageId te potrošnje (i nakon poništenja — "koji je paket korišten"), Applied = true,
    ///   Returned = je li poništena (+ kada/tko);
    /// - CoverageType postoji samo za Form=Group (isto kao prije): SessionPackage kad je potrošnja skinula jedinicu,
    ///   MonthlyPackage kad je paket neograničen (Units = 0); grupno sudjelovanje Completed BEZ aktivne potrošnje je
    ///   check-in razriješen bez paketa → SinglePaid (bez paketnog linka, Applied/Returned = false).
    /// </summary>
    public static PackageCoverageView CoverageOf(BookingSegmentParticipation participation, AppointmentForm form)
    {
        PackageConsumption latest = LatestOf(participation);
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

    /// <summary>Phase M0: izvedeni prikaz pokrića za BOOKING read-model — pokriće sudjelovanja kad je jedno ILI kad se sva
    /// sudjelovanja slažu; inače (različito pokriće po sudjelovanjima) <see cref="PackageCoverageView.None"/> — točni
    /// podaci su tada na BookingDto.Participations.</summary>
    public static PackageCoverageView CoverageOfBooking(Booking booking, AppointmentForm form)
    {
        List<PackageCoverageView> views = booking.Participations.Select(p => CoverageOf(p, form)).Distinct().ToList();
        return views.Count == 1 ? views[0] : PackageCoverageView.None;
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
