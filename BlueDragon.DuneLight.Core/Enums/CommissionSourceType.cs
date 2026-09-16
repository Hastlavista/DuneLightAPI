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
    PackageSale
}
