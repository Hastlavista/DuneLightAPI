using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje konstruira novi Booking — od Phase D3B1 uvijek Booking + točno JEDNO sudjelovanje
/// (<see cref="BookingSegmentParticipation"/>) na autoritativnom segmentu termina, oboje u istom grafu (sprema ih isti
/// SaveChanges pozivatelja, tj. jedna transakcija). Booking nosi identitet i komercijalno stanje (cijena/paket);
/// početni životni ciklus (status, StatusVersion = 0) nosi sudjelovanje. Cjenovni snapshot sudjelovanja se u D3B1 NE
/// popunjava (ostaje NULL — cijena je i dalje autoritativno na Bookingu, vidi Migration_2026_10_10). Samo sastavlja
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
    /// kao prije na Bookingu, pinned F-07).</summary>
    public static Booking CreateCompletedAtCreation(
        Guid organizationId, AppointmentSegment segment, Guid clientId, BookingPricing pricing, Guid? clientPackageId, DateTimeOffset createdAt)
    {
        Booking booking = NewBooking(organizationId, segment, clientId, ParticipationStatus.Completed, pricing, createdAt);
        booking.ClientPackageId = clientPackageId;
        booking.PackageCoverageApplied = clientPackageId.HasValue;
        return booking;
    }

    private static Booking NewBooking(
        Guid organizationId, AppointmentSegment segment, Guid clientId, ParticipationStatus status, BookingPricing pricing, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Booking booking = new Booking
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppointmentId = segment.AppointmentId,
            ClientId = clientId,
            Amount = pricing.Amount,
            SuggestedAmount = pricing.SuggestedAmount,
            IsAmountManuallyOverridden = pricing.IsAmountManuallyOverridden,
            CreatedAt = createdAt
        };
        booking.Participations.Add(new BookingSegmentParticipation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BookingId = booking.Id.Value,
            AppointmentSegmentId = segment.Id.GetValueOrDefault(),
            Status = status,
            StatusVersion = 0,
            CreatedAt = createdAt
        });
        return booking;
    }
}
