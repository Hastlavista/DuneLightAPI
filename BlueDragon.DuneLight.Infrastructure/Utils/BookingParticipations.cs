using System;
using System.Linq;
using System.Linq.Expressions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M0 — razrješavanje sudjelovanja iz Bookinga. Booking je spremnik sudjelovanja jednog klijenta na terminu i NEMA
/// vlastiti životni ciklus, cijenu, namirenje ni verziju; svaka izvršna/komercijalna naredba adresira SUDJELOVANJE.
///
/// Razrješenja su eksplicitna: <see cref="OnSegment"/> (sudjelovanje Bookinga na ZADANOM segmentu) i ById (adresirano
/// sudjelovanje). Phase M1H: nema "jedinog sudjelovanja Bookinga" — naredbe adresiraju sudjelovanje. Tuđi segment/termin je
/// integritetna greška (<see cref="InvalidBookingParticipationStateException"/>).
/// Booking.Participations se učitava automatski (AutoInclude) kad god se učita Booking.
/// </summary>
public static class BookingParticipations
{
    /// <summary>Sudjelovanje Bookinga na zadanom segmentu (najviše jedno — jedinstveni indeks (booking, segment)).
    /// Integritetna greška ako ga nema.</summary>
    public static BookingSegmentParticipation OnSegment(Booking booking, AppointmentSegment segment)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(segment);

        BookingSegmentParticipation participation = booking.Participations.SingleOrDefault(p => p.AppointmentSegmentId == segment.Id);
        if (participation == null)
            throw new InvalidBookingParticipationStateException($"Booking {booking.Id} ne sudjeluje u segmentu {segment.Id}.");

        EnsureBelongs(booking, participation);
        return participation;
    }

    /// <summary>Sudjelovanje po vlastitom Id-u unutar Bookinga (participation-native adresiranje).</summary>
    public static BookingSegmentParticipation ById(Booking booking, Guid participationId)
    {
        ArgumentNullException.ThrowIfNull(booking);
        BookingSegmentParticipation participation = booking.Participations.SingleOrDefault(p => p.Id == participationId);
        if (participation == null)
            throw new InvalidBookingParticipationStateException($"Sudjelovanje {participationId} ne pripada Bookingu {booking.Id}.");

        EnsureBelongs(booking, participation);
        return participation;
    }

    private static void EnsureBelongs(Booking booking, BookingSegmentParticipation participation)
    {
        if (participation.BookingId != booking.Id.GetValueOrDefault() || participation.OrganizationId != booking.OrganizationId)
            throw new InvalidBookingParticipationStateException($"Sudjelovanje {participation.Id} ne pripada Bookingu {booking.Id}.");
        if (participation.Segment != null && participation.Segment.AppointmentId != booking.AppointmentId)
            throw new InvalidBookingParticipationStateException($"Sudjelovanje {participation.Id} je na segmentu drugog termina.");
    }

    public static BookingStatus ToBookingStatus(ParticipationStatus status) => status switch
    {
        ParticipationStatus.Confirmed => BookingStatus.Confirmed,
        ParticipationStatus.Completed => BookingStatus.Completed,
        ParticipationStatus.Cancelled => BookingStatus.Cancelled,
        ParticipationStatus.NoShow => BookingStatus.NoShow,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static ParticipationStatus ToParticipationStatus(BookingStatus status) => status switch
    {
        BookingStatus.Confirmed => ParticipationStatus.Confirmed,
        BookingStatus.Completed => ParticipationStatus.Completed,
        BookingStatus.Cancelled => ParticipationStatus.Cancelled,
        BookingStatus.NoShow => ParticipationStatus.NoShow,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}

/// <summary>
/// Phase M0 — JEDINO mjesto koje definira koji statusi sudjelovanja ZAUZIMAJU raspored (klijentovo vrijeme na segmentu).
/// Ne uvodi pravila kapaciteta: samo centralizira postojeće "aktivno" pravilo (Confirmed i Completed zauzimaju;
/// Cancelled i NoShow ne) koje je prije bilo prepisano po upitima kao Booking.Participations.Any(...).
/// </summary>
public static class ParticipationOccupancy
{
    /// <summary>Za EF upite (translatable) — koristiti nad sudjelovanjima KONKRETNOG segmenta.</summary>
    public static readonly Expression<Func<BookingSegmentParticipation, bool>> OccupiesSchedule =
        p => p.Status != ParticipationStatus.Cancelled && p.Status != ParticipationStatus.NoShow;

    public static bool Occupies(ParticipationStatus status) =>
        status != ParticipationStatus.Cancelled && status != ParticipationStatus.NoShow;
}

/// <summary>
/// Phase M0 — JEDINA putanja koja mijenja izvršni životni ciklus, adresirana SUDJELOVANJEM (bivši BookingLifecycle nad
/// Bookingom). StatusVersion je po sudjelovanju i raste TOČNO kad se status stvarno promijeni (nikad za idempotentan
/// poziv s istim statusom); Booking nema agregatnu verziju — Booking-wide naredba poziva ovo za svako sudjelovanje
/// posebno, pa svako mijenjano sudjelovanje dobiva svoj inkrement neovisno.
/// </summary>
public static class ParticipationLifecycle
{
    public static bool TrySetStatus(BookingSegmentParticipation participation, ParticipationStatus target)
    {
        ArgumentNullException.ThrowIfNull(participation);
        if (participation.Status == target)
            return false;

        participation.Status = target;
        participation.StatusVersion++;
        participation.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public static void SetCancellationReason(BookingSegmentParticipation participation, string reason)
    {
        participation.CancellationReason = reason;
        participation.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static void SetLateCancellation(BookingSegmentParticipation participation, bool? isLateCancellation)
    {
        participation.IsLateCancellation = isLateCancellation;
        participation.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Phase M0 — JEDINA putanja koja mijenja cijenu postojećeg sudjelovanja (re-cijenjenje kod segmentnih naredbi i
/// check-ina, ručni iznos sudjelovanja, poništenje grupnog check-ina). Cijena NIJE izvršna povijest: ne dira Status/StatusVersion
/// niti ParticipationHistory. AdjustmentAmount se ne piše — trenutni cjenovni model nema eksplicitnu prilagodbu.
/// </summary>
public static class ParticipationPrice
{
    public static void Apply(BookingSegmentParticipation participation, BookingPricing pricing)
    {
        ArgumentNullException.ThrowIfNull(participation);
        ApplyTo(participation, pricing);
        participation.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Upis cjenovnog stanja na sudjelovanje — dijele ga Apply i BookingFactory (nastanak).</summary>
    internal static void ApplyTo(BookingSegmentParticipation participation, BookingPricing pricing)
    {
        participation.Amount = pricing.Amount;
        participation.SuggestedAmount = pricing.SuggestedAmount;
        participation.IsAmountManuallyOverridden = pricing.IsAmountManuallyOverridden;
        participation.BaseAmount = pricing.BaseAmount;
        participation.BaseAmountSource = pricing.BaseAmountSource;
        participation.PricingMode = pricing.PricingMode;
        participation.PricingEmployeeId = pricing.PricingEmployeeId;
    }
}

/// <summary>Booking nije u obliku koji tok podržava (nula sudjelovanja, tuđi segment/termin). Integritetna greška (500).</summary>
public sealed class InvalidBookingParticipationStateException : InvalidOperationException
{
    public InvalidBookingParticipationStateException(string message) : base(message)
    {
    }
}
