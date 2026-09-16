using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi IPaymentService (Core, read-only kontroler-strana) i IPaymentLedgerService (Infrastructure, poziv iz
/// tuđe transakcije — BookingService/AppointmentService check-in/completion tokovi) — isti obrazac kao
/// WaitlistService/IWaitlistPromotionService. Stvarno kreiranje/void Paymenta preko eksplicitnog korisničkog
/// API-ja sada radi ICheckoutService (Payment pripada Checkoutu, vidi Payment.cs) — ovaj servis interno svejedno
/// stvara Checkout/CheckoutItem/PaymentAllocation graf za check-in-generated put (RecordPayment), jer je Payment
/// tablica dijeljena, jedinstvena za oba puta.
/// </summary>
public class PaymentService : IPaymentService, IPaymentLedgerService
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly ICheckoutAuditLogHandler _checkoutAuditLogHandler;

    public PaymentService(
        IAppointmentHandler appointmentHandler,
        ICheckoutHandler checkoutHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        ICheckoutAuditLogHandler checkoutAuditLogHandler)
    {
        _appointmentHandler = appointmentHandler;
        _checkoutHandler = checkoutHandler;
        _auditLogHandler = auditLogHandler;
        _checkoutAuditLogHandler = checkoutAuditLogHandler;
    }

    public async Task<List<PaymentDto>> GetForBooking(Guid organizationId, Guid appointmentId, Guid clientId)
    {
        Booking booking = await _appointmentHandler.GetBooking(organizationId, appointmentId, clientId);
        if (booking == null)
            throw new NotFoundAppException("Booking", clientId);

        List<CheckoutItem> items = await _checkoutHandler.GetItemsForBooking(organizationId, booking.Id.GetValueOrDefault());
        List<Payment> payments = DistinctActivePayments(items, includeVoided: true);

        return payments.Select(PaymentDtoFactory.ToDto).ToList();
    }

    /// <summary>Interni check-in-generated put — uvijek naplaćuje TOČNO amount u jednom potezu (pozivatelj već
    /// zna da je amount == puni dug, vidi AppointmentService/BookingService), pa se ovdje ne broje postojeći
    /// Paymenti niti postoji parcijalno stanje: stvara se jedan jednostavačni Checkout (jedan Booking CheckoutItem,
    /// jedan Payment, jedna PaymentAllocation), odmah Status=Completed — vidi Payment.cs klasnu napomenu i
    /// IPaymentLedgerService domensku napomenu za zašto ovo NIJE isto što i redovan POS Checkout.</summary>
    public async Task<Payment> RecordPayment(
        IUnitOfWork uow, Guid organizationId, Guid userId, Guid companyId, Booking booking, PaymentMethod method, decimal amount,
        string note, bool isCheckInGenerated = false)
    {
        if (amount <= 0m)
            throw new ValidationAppException("Iznos plaćanja mora biti veći od 0.");

        if (booking.ClientPackageId.HasValue)
            throw new BusinessRuleException(
                ErrorCodes.PaymentNotAllowed, "Booking je pokriven paketom — dodatna novčana naplata nije dopuštena.");

        if (booking.Amount <= 0m)
            throw new BusinessRuleException(ErrorCodes.PaymentNotAllowed, "Booking je besplatan (iznos 0) — plaćanje nije potrebno.");

        if (amount > booking.Amount)
            throw new BusinessRuleException(
                ErrorCodes.PaymentExceedsOutstandingAmount, "Iznos premašuje preostali dug za ovaj booking.",
                new { outstanding = booking.Amount, requested = amount });

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid checkoutId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        Guid paymentId = Guid.NewGuid();

        Checkout checkout = new Checkout
        {
            Id = checkoutId,
            OrganizationId = organizationId,
            CompanyId = companyId,
            ClientId = booking.ClientId,
            Status = CheckoutStatus.Completed,
            CreatedAt = now,
            CreatedBy = userId,
            CompletedAt = now,
            CompletedBy = userId
        };

        CheckoutItem item = new CheckoutItem
        {
            Id = itemId,
            OrganizationId = organizationId,
            CheckoutId = checkoutId,
            Type = CheckoutItemType.Booking,
            Description = "Booking",
            UnitPrice = amount,
            Quantity = 1,
            Amount = amount,
            BookingId = booking.Id,
            LocksBooking = false,
            CreatedAt = now,
            CreatedBy = userId
        };

        Payment payment = new Payment
        {
            Id = paymentId,
            OrganizationId = organizationId,
            CheckoutId = checkoutId,
            Amount = amount,
            Method = method,
            Status = PaymentStatus.Completed,
            Note = note,
            IsCheckInGenerated = isCheckInGenerated,
            CreatedAt = now,
            CreatedBy = userId
        };

        item.Allocations.Add(new PaymentAllocation
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            CheckoutItemId = itemId,
            Amount = amount,
            CreatedAt = now
        });

        checkout.Items.Add(item);
        checkout.Payments.Add(payment);

        await _checkoutHandler.Add(uow, checkout);

        await _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = booking.AppointmentId,
            BookingId = booking.Id,
            ChangeType = "PaymentCreated",
            OldValue = null,
            NewValue = $"{payment.Amount} {payment.Method}",
            ChangedAt = now,
            ChangedBy = userId
        });

        return payment;
    }

    public async Task VoidCheckInGeneratedPayments(IUnitOfWork uow, Guid organizationId, Guid userId, Booking booking, string reason)
    {
        List<CheckoutItem> items = await _checkoutHandler.GetItemsForBooking(uow, organizationId, booking.Id.GetValueOrDefault());
        List<Payment> payments = DistinctActivePayments(items, includeVoided: false)
            .Where(p => p.IsCheckInGenerated)
            .ToList();

        foreach (Payment payment in payments)
        {
            VoidInTransaction(payment, userId, reason);
            await _checkoutHandler.UpdatePayment(uow, payment);

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = booking.AppointmentId,
                BookingId = booking.Id,
                ChangeType = "PaymentVoided",
                OldValue = $"{payment.Amount} {payment.Method}",
                NewValue = "Voided",
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });

            await TryVoidSoleAutoCheckout(uow, organizationId, userId, payment.CheckoutId, reason);
        }
    }

    /// <summary>Completed -&gt; Voided je USKA interna korekcijska tranzicija (vidi CheckoutStatus.Voided) —
    /// primjenjuje se SAMO kad je sigurno utvrdivo da je ovaj Checkout auto-generiran isključivo za payment koji
    /// upravo voidamo (RecordPayment uvijek stvara jednostavačni Checkout: točno jedna Booking stavka, točno
    /// jedan Payment, oba check-in-generated) — nikad za stvaran POS Checkout koji je slučajno sadržavao
    /// check-in-generated Payment uz druge stavke/plaćanja (takav ostaje Completed, ne postoji za njega redovan
    /// void-checkout put u ovom zahvatu). Bez ovoga bi Completed Checkout ostao s OutstandingAmount &gt; 0 nakon
    /// što mu je jedini Payment voidan, kršeći temeljni invarijant "Completed = financijski namiren".</summary>
    private async Task TryVoidSoleAutoCheckout(IUnitOfWork uow, Guid organizationId, Guid userId, Guid checkoutId, string reason)
    {
        Checkout checkout = await _checkoutHandler.GetGraph(uow, organizationId, checkoutId);
        if (checkout == null || checkout.Status != CheckoutStatus.Completed)
            return;

        bool isSoleAutoGeneratedCheckout =
            checkout.Items.Count == 1 &&
            checkout.Payments.Count == 1 &&
            checkout.Payments[0].IsCheckInGenerated;

        if (!isSoleAutoGeneratedCheckout)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        checkout.Status = CheckoutStatus.Voided;
        checkout.VoidedAt = now;
        checkout.VoidedBy = userId;
        checkout.VoidReason = reason;

        await _checkoutHandler.Update(uow, checkout);

        await _checkoutAuditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "CheckoutVoided",
            OldValue = "Completed",
            NewValue = "Voided",
            ChangedAt = now,
            ChangedBy = userId
        });
    }

    /// <summary>Distinct Payments preko CheckoutItem.Allocations (isti Payment se ne duplicira ako bi teoretski
    /// alociralo na više stavki istog Bookinga), najnoviji prvi.</summary>
    private static List<Payment> DistinctActivePayments(List<CheckoutItem> items, bool includeVoided)
    {
        IEnumerable<Payment> payments = items
            .SelectMany(i => i.Allocations)
            .Select(a => a.Payment)
            .Where(p => p != null);

        if (!includeVoided)
            payments = payments.Where(p => p.Status == PaymentStatus.Completed);

        return payments
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }

    private static void VoidInTransaction(Payment payment, Guid userId, string reason)
    {
        payment.Status = PaymentStatus.Voided;
        payment.VoidedAt = DateTimeOffset.UtcNow;
        payment.VoidedBy = userId;
        payment.VoidReason = reason;
    }
}
