using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M0 — IZVEDENI sažeci Bookinga iz njegovih sudjelovanja. Ništa se ne sprema (nema Booking.Status, Booking.Amount
/// ni agregatne verzije); Booking je spremnik, sudjelovanje je izvršna i komercijalna jedinica.
///
/// Status: sva sudjelovanja u istom statusu → taj status; bilo koja razlika → <see cref="BookingStatusSummary.Mixed"/>
/// (npr. Completed+Cancelled, Confirmed+Completed, NoShow+Cancelled). Sažetak nikad nije ciljni status prijelaza.
/// Booking bez sudjelovanja je integritetna greška.
/// </summary>
public static class BookingSummary
{
    public static BookingStatusSummary StatusOf(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        if (booking.Participations.Count == 0)
            throw new InvalidBookingParticipationStateException($"Booking {booking.Id} nema sudjelovanja.");

        return StatusOf(booking.Participations.Select(p => p.Status));
    }

    public static BookingStatusSummary StatusOf(IEnumerable<ParticipationStatus> statuses)
    {
        List<ParticipationStatus> distinct = statuses.Distinct().ToList();
        if (distinct.Count == 0)
            throw new InvalidBookingParticipationStateException("Sažetak statusa traži barem jedno sudjelovanje.");
        if (distinct.Count > 1)
            return BookingStatusSummary.Mixed;

        return distinct[0] switch
        {
            ParticipationStatus.Confirmed => BookingStatusSummary.Confirmed,
            ParticipationStatus.Completed => BookingStatusSummary.Completed,
            ParticipationStatus.Cancelled => BookingStatusSummary.Cancelled,
            ParticipationStatus.NoShow => BookingStatusSummary.NoShow,
            _ => throw new ArgumentOutOfRangeException(nameof(statuses))
        };
    }

    /// <summary>Vrijednost koju sva sudjelovanja dijele, inače default (npr. razlog otkazivanja kad se razlikuju) —
    /// precizna vrijednost je tada na sudjelovanju.</summary>
    public static T Agreed<T>(Booking booking, Func<BookingSegmentParticipation, T> selector)
    {
        List<T> values = booking.Participations.Select(selector).Distinct().ToList();
        return values.Count == 1 ? values[0] : default;
    }
}

/// <summary>
/// Phase M0 — izvedeni komercijalni sažetak Bookinga preko njegovih sudjelovanja (ParticipationSettlement po svakom):
/// FinalPrice = Σ cijena; SuggestedPrice = Σ predloženih; MonetarySettled = Σ aktivnih novčanih alokacija;
/// Outstanding = Σ preostalog duga (svaki ≥ 0); FullySettled = SVAKO sudjelovanje ima preostali dug 0. Paket nije novac:
/// sudjelovanje pokriveno paketom ima dug 0 i ne doprinosi MonetarySettled (broji se u PackageCoveredCount).
/// </summary>
public readonly record struct BookingCommercialSummary(
    decimal FinalPrice,
    decimal SuggestedPrice,
    bool AnyManuallyOverridden,
    decimal MonetarySettled,
    decimal Outstanding,
    bool FullySettled,
    int PackageCoveredCount,
    int ParticipationCount)
{
    public static BookingCommercialSummary Of(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        List<(BookingSegmentParticipation Participation, ParticipationSettlement Settlement)> rows = booking.Participations
            .Select(p => (p, ParticipationSettlement.Of(p)))
            .ToList();

        return new BookingCommercialSummary(
            rows.Sum(r => r.Settlement.FinalPrice),
            rows.Sum(r => r.Participation.SuggestedAmount),
            rows.Any(r => r.Participation.IsAmountManuallyOverridden),
            rows.Sum(r => r.Settlement.SettledAmount),
            rows.Sum(r => r.Settlement.OutstandingAmount),
            rows.All(r => r.Settlement.FullySettled),
            rows.Count(r => r.Settlement.EntitlementCovered),
            rows.Count);
    }
}

/// <summary>
/// Phase M0 — JEDINO mapiranje Booking → BookingDto (prije prepisano u BookingService i AppointmentService). Polja
/// Bookinga su izvedeni sažeci (BookingSummary/BookingCommercialSummary/PackageConsumptions.CoverageOfBooking); za
/// Booking s jednim sudjelovanjem izlaz je identičan dosadašnjem. Participations nosi točne podatke po sudjelovanju.
/// </summary>
public static class BookingReadModel
{
    public static BookingDto ToDto(Booking booking, AppointmentForm form, string clientName)
    {
        BookingCommercialSummary commercial = BookingCommercialSummary.Of(booking);
        PackageCoverageView coverage = PackageConsumptions.CoverageOfBooking(booking, form);

        return new BookingDto
        {
            Id = booking.Id.GetValueOrDefault(),
            ClientId = booking.ClientId,
            ClientName = clientName,
            Status = BookingSummary.StatusOf(booking),
            Amount = commercial.FinalPrice,
            SuggestedAmount = commercial.SuggestedPrice,
            IsAmountManuallyOverridden = commercial.AnyManuallyOverridden,
            PaidAmount = commercial.MonetarySettled,
            OutstandingAmount = commercial.Outstanding,
            IsPaid = commercial.FullySettled,
            ClientPackageId = coverage.ClientPackageId,
            CoverageType = coverage.CoverageType,
            PackageCoverageApplied = coverage.PackageCoverageApplied,
            PackageCoverageReturned = coverage.PackageCoverageReturned,
            Payments = ParticipationSettlement.PaymentsOf(booking.Participations).Select(PaymentDtoFactory.ToDto).ToList(),
            Note = booking.Note,
            CancellationReason = BookingSummary.Agreed(booking, p => p.CancellationReason),
            IsLateCancellation = BookingSummary.Agreed(booking, p => p.IsLateCancellation),
            Participations = booking.Participations
                .OrderBy(p => p.Segment?.PlannedStart ?? DateTimeOffset.MinValue).ThenBy(p => p.Id)
                .Select(p => ToDto(p, form))
                .ToList()
        };
    }

    public static BookingParticipationDto ToDto(BookingSegmentParticipation participation, AppointmentForm form)
    {
        ParticipationSettlement settlement = ParticipationSettlement.Of(participation);
        return new BookingParticipationDto
        {
            Id = participation.Id.GetValueOrDefault(),
            AppointmentSegmentId = participation.AppointmentSegmentId,
            Status = BookingParticipations.ToBookingStatus(participation.Status),
            StatusVersion = participation.StatusVersion,
            Amount = participation.Amount,
            SuggestedAmount = participation.SuggestedAmount,
            IsAmountManuallyOverridden = participation.IsAmountManuallyOverridden,
            PaidAmount = settlement.SettledAmount,
            OutstandingAmount = settlement.OutstandingAmount,
            IsPaid = settlement.FullySettled,
            PackageCovered = settlement.EntitlementCovered,
            ClientPackageId = PackageConsumptions.CoverageOf(participation, form).ClientPackageId,
            CancellationReason = participation.CancellationReason,
            IsLateCancellation = participation.IsLateCancellation,
            BaseAmount = participation.BaseAmount,
            BaseAmountSource = participation.BaseAmountSource,
            PricingMode = participation.PricingMode,
            PricingEmployeeId = participation.PricingEmployeeId
        };
    }
}
