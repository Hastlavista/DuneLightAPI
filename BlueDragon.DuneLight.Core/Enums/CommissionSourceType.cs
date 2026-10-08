namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Poslovni izvor jednog CommissionEntry retka — vidi CommissionEntry.cs.
///
/// IndividualService: jedan odrađen (Booking.Status=Completed) individualni Booking = jedan izvor (BookingId
/// popunjen). GroupService: jedan odrađen (Appointment.Status=Completed) grupni termin = jedan izvor
/// (AppointmentId popunjen, BookingId prazan — namjerno PO TERMINU, ne po sudioniku, vidi CommissionService
/// domensku napomenu zašto). ProductSale/PackageSale: jedna prodajna CheckoutItem stavka na Completed
/// Checkoutu = jedan izvor (CheckoutItemId popunjen).
/// </summary>
public enum CommissionSourceType
{
    IndividualService,
    GroupService,
    ProductSale,
    PackageSale,

    /// <summary>P2 (2F, Q42) — prva prodaja članarine (ClientMembershipId); nastaje kad su prvo zaduženje perioda i početna
    /// naknada konačni, na Checkout Complete ili otpisu.</summary>
    MembershipSale,

    /// <summary>P2 (2F, Q38) — plaćena P1 naknada kasnog otkaza / izostanka individualne sesije (posljedica politike).</summary>
    PolicyFee
}
