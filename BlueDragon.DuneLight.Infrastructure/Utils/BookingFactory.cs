using System;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje konstruira novi Booking — od Phase D3B1 uvijek Booking + točno JEDNO sudjelovanje
/// (<see cref="BookingSegmentParticipation"/>) na autoritativnom segmentu termina, oboje u istom grafu (sprema ih isti
/// SaveChanges pozivatelja, tj. jedna transakcija). Booking nosi identitet i paketno stanje; početni životni ciklus
/// (status, StatusVersion = 0) i, od Phase D3B2, cijena (<see cref="BookingPricing"/> → Amount/SuggestedAmount/ručna
/// promjena + istinit snapshot razrješavanja) nosi sudjelovanje — Booking nema kopiju cijene. Samo sastavlja
/// perzistencijski oblik; validacija ostaje kod pozivatelja.
/// </summary>
public static class BookingFactory
{
    /// <summary>Confirmed booking — Create, /recurring, UpdateWithBookings (novi klijent), AddBooking, gost na check-inu,
    /// generiranje grupe, AddMember, promocija s liste čekanja.</summary>
    public static Booking CreateConfirmed(
        Guid organizationId, AppointmentSegment segment, Guid clientId, BookingPricing pricing, DateTimeOffset createdAt)
    {
        return NewBooking(organizationId, segment, clientId, ParticipationStatus.Confirmed, pricing, createdAt);
    }

    /// <summary>CompleteNew — booking je odrađen u trenutku upisa (sudjelovanje odmah Completed, StatusVersion 0 — isto
    /// kao prije na Bookingu, pinned F-07). Phase D3B3A: paket se ovdje NE bilježi — pozivatelj ga troši kroz
    /// IPackageConsumptionLedgerService (PackageConsumption na sudjelovanju) u istoj transakciji.</summary>
    public static Booking CreateCompletedAtCreation(
        Guid organizationId, AppointmentSegment segment, Guid clientId, BookingPricing pricing, DateTimeOffset createdAt)
    {
        return NewBooking(organizationId, segment, clientId, ParticipationStatus.Completed, pricing, createdAt);
    }

    private static Booking NewBooking(
        Guid organizationId, AppointmentSegment segment, Guid clientId, ParticipationStatus status, BookingPricing pricing, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Booking booking = NewContainer(organizationId, segment.AppointmentId, clientId, createdAt);
        AddParticipation(booking, segment, status, pricing, createdAt);
        return booking;
    }

    /// <summary>Phase M1B: Booking kao SPREMNIK klijenta na terminu, još bez sudjelovanja — koristi ga konstrukcijska
    /// jezgra (AppointmentFactory) koja jednom klijentu daje jedan Booking i po jedno sudjelovanje na svakom odabranom
    /// segmentu.</summary>
    internal static Booking NewContainer(Guid organizationId, Guid appointmentId, Guid clientId, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        AppointmentId = appointmentId,
        ClientId = clientId,
        CreatedAt = createdAt
    };

    /// <summary>Phase M1B: novo sudjelovanje Bookinga na ZADANOM segmentu (StatusVersion 0 — nastanak nije prijelaz).
    /// Booking i segment moraju pripadati istom terminu; isti segment se ne dodaje dvaput.</summary>
    internal static BookingSegmentParticipation AddParticipation(
        Booking booking, AppointmentSegment segment, ParticipationStatus status, BookingPricing pricing, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.AppointmentId != booking.AppointmentId)
            throw new InvalidOperationException("Segment i Booking ne pripadaju istom terminu.");
        if (booking.Participations.Any(p => p.AppointmentSegmentId == segment.Id))
            throw new InvalidOperationException($"Booking {booking.Id} već sudjeluje u segmentu {segment.Id}.");

        BookingSegmentParticipation participation = new BookingSegmentParticipation
        {
            Id = Guid.NewGuid(),
            OrganizationId = booking.OrganizationId,
            BookingId = booking.Id.GetValueOrDefault(),
            AppointmentSegmentId = segment.Id.GetValueOrDefault(),
            Status = status,
            StatusVersion = 0,
            CreatedAt = createdAt
        };
        ParticipationPrice.ApplyTo(participation, pricing);
        booking.Participations.Add(participation);
        return participation;
    }
}
