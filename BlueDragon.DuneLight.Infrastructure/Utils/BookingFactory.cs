using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje konstruira novi Booking redak. Dvije postojeće varijante su namjerno odvojene metode, ne
/// normalizirane u jednu (vidi F-07 u docs/appointment-booking-characterization.md). Cijenu i paket razrješava pozivatelj;
/// ovdje se ne računa ništa. U ciljnom modelu ovo postaje Booking + BookingSegmentParticipation.
/// </summary>
public static class BookingFactory
{
    /// <summary>Uobičajeni novi Booking: Confirmed, StatusVersion 0, bez paket-pokrića, bez metapodataka otkazivanja.
    /// Koriste ga Create, /recurring, UpdateWithBookings (novi klijent), AddBooking, gost na check-inu, generiranje grupe,
    /// AddMember i promocija s liste čekanja.</summary>
    public static Booking CreateConfirmed(
        Guid organizationId, Guid appointmentId, Guid clientId, BookingPricing pricing, DateTimeOffset createdAt)
    {
        return NewBooking(organizationId, appointmentId, clientId, BookingStatus.Confirmed, pricing, createdAt);
    }

    /// <summary>CompleteNew ("upiši odrađeno"): Booking nastaje IZRAVNO kao Completed. StatusVersion OSTAJE 0 (nije
    /// prošao kroz BookingStatusVersioning.TrySetStatus — za razliku od CompleteExisting gdje isti prijelaz daje 1; pinned
    /// F-07). Kad je odabran paket, pokriće je primijenjeno već kod kreiranja (ClientPackageId + PackageCoverageApplied).</summary>
    public static Booking CreateCompletedAtCreation(
        Guid organizationId, Guid appointmentId, Guid clientId, BookingPricing pricing, Guid? clientPackageId, DateTimeOffset createdAt)
    {
        Booking booking = NewBooking(organizationId, appointmentId, clientId, BookingStatus.Completed, pricing, createdAt);
        booking.ClientPackageId = clientPackageId;
        booking.PackageCoverageApplied = clientPackageId.HasValue;
        return booking;
    }

    private static Booking NewBooking(
        Guid organizationId, Guid appointmentId, Guid clientId, BookingStatus status, BookingPricing pricing, DateTimeOffset createdAt)
    {
        return new Booking
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppointmentId = appointmentId,
            ClientId = clientId,
            Status = status,
            Amount = pricing.Amount,
            SuggestedAmount = pricing.SuggestedAmount,
            IsAmountManuallyOverridden = pricing.IsAmountManuallyOverridden,
            CreatedAt = createdAt
        };
    }
}
