using System;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3B1 — JEDINO mjesto koje za Booking bira njegovo autoritativno sudjelovanje i čita/piše izvršni životni ciklus
/// (status, StatusVersion, razlog otkazivanja, klasifikacija kasnog otkazivanja). Booking ostaje identitet "klijent na
/// terminu" + komercijalno stanje (cijena, paket, naplata); životni ciklus živi isključivo na
/// <see cref="BookingSegmentParticipation"/>.
///
/// Kompatibilni jednostruki model: termin ima točno jedan segment, a Booking točno JEDNO sudjelovanje na tom segmentu.
/// Nula, više sudjelovanja ili sudjelovanje na tuđem segmentu/terminu je nepodržano stanje i baca
/// <see cref="InvalidBookingParticipationStateException"/> umjesto proizvoljnog izbora. Booking.Participations se učitava
/// automatski (AutoInclude u DatabaseContext) kad god se učita Booking.
/// </summary>
public static class BookingParticipations
{
    public static BookingSegmentParticipation GetSingleParticipation(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        if (booking.Participations.Count != 1)
            throw new InvalidBookingParticipationStateException(
                $"Booking {booking.Id} ima {booking.Participations.Count} sudjelovanja — jednostruki tok podržava točno jedno.");

        BookingSegmentParticipation participation = booking.Participations[0];
        if (participation.BookingId != booking.Id.GetValueOrDefault() || participation.OrganizationId != booking.OrganizationId)
            throw new InvalidBookingParticipationStateException($"Sudjelovanje {participation.Id} ne pripada Bookingu {booking.Id}.");

        // Kad su segmenti termina / segment sudjelovanja učitani, sudjelovanje mora biti na JEDINOM segmentu ISTOG termina.
        if (participation.Segment != null && participation.Segment.AppointmentId != booking.AppointmentId)
            throw new InvalidBookingParticipationStateException($"Sudjelovanje {participation.Id} je na segmentu drugog termina.");
        if (booking.Appointment?.Segments.Count == 1 && participation.AppointmentSegmentId != booking.Appointment.Segments[0].Id)
            throw new InvalidBookingParticipationStateException($"Sudjelovanje {participation.Id} nije na izvršnom segmentu termina.");

        return participation;
    }

    public static BookingStatus StatusOf(Booking booking) => ToBookingStatus(GetSingleParticipation(booking).Status);

    public static int StatusVersionOf(Booking booking) => GetSingleParticipation(booking).StatusVersion;

    public static string CancellationReasonOf(Booking booking) => GetSingleParticipation(booking).CancellationReason;

    public static bool? IsLateCancellationOf(Booking booking) => GetSingleParticipation(booking).IsLateCancellation;

    public static bool HasStatus(Booking booking, BookingStatus status) => StatusOf(booking) == status;

    public static bool IsActive(Booking booking)
    {
        BookingStatus status = StatusOf(booking);
        return status != BookingStatus.Cancelled && status != BookingStatus.NoShow;
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
/// Phase D3B1 — JEDINA putanja koja mijenja izvršni životni ciklus Bookinga, tj. njegovog jedinog sudjelovanja (bivši
/// BookingStatusVersioning). StatusVersion raste TOČNO kad se status stvarno promijeni (nikad za idempotentan poziv s
/// istim statusom) — isti koncept kao prije na Bookingu, sada na sudjelovanju.
/// </summary>
public static class BookingLifecycle
{
    public static bool TrySetStatus(Booking booking, BookingStatus newStatus)
    {
        BookingSegmentParticipation participation = BookingParticipations.GetSingleParticipation(booking);
        ParticipationStatus target = BookingParticipations.ToParticipationStatus(newStatus);
        if (participation.Status == target)
            return false;

        participation.Status = target;
        participation.StatusVersion++;
        participation.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public static void SetCancellationReason(Booking booking, string reason)
    {
        BookingSegmentParticipation participation = BookingParticipations.GetSingleParticipation(booking);
        participation.CancellationReason = reason;
        participation.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static void SetLateCancellation(Booking booking, bool? isLateCancellation)
    {
        BookingSegmentParticipation participation = BookingParticipations.GetSingleParticipation(booking);
        participation.IsLateCancellation = isLateCancellation;
        participation.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Booking nije u obliku koji jednostruki tok podržava (nula/više sudjelovanja, tuđi segment/termin).
/// Integritetna greška, ne korisnička (500).</summary>
public sealed class InvalidBookingParticipationStateException : InvalidOperationException
{
    public InvalidBookingParticipationStateException(string message) : base(message)
    {
    }
}
