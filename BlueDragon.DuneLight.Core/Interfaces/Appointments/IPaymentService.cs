using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;

namespace BlueDragon.DuneLight.Core.Interfaces.Appointments;

/// <summary>
/// Read-only povijest Paymenta jednog Bookinga — vidi Payment.cs (Infrastructure) za punu domensku napomenu.
/// Kreiranje/void Paymenta više NE ide kroz ovaj booking-scoped API (Payment pripada Checkoutu, ne izravno
/// Bookingu — vidi ICheckoutService.RecordPayment/VoidPayment), osim internog check-in-generated toka
/// (vidi IPaymentLedgerService, poziva se iz AppointmentService/BookingService, ne preko ovog sučelja).
/// </summary>
public interface IPaymentService
{
    /// <summary>Puna povijest Paymenta jednog Bookinga (uklj. voidane, preko svih njegovih povijesnih
    /// CheckoutItem stavki — vidi Booking.CheckoutItems), najnoviji prvi.</summary>
    Task<List<PaymentDto>> GetForBooking(Guid organizationId, Guid appointmentId, Guid clientId);
}
