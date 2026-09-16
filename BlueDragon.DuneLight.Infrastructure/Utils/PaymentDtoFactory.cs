using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Jedino mjesto koje mapira Payment -> PaymentDto — koriste CheckoutService/PaymentService, da se
/// mapiranje ne duplicira.</summary>
public static class PaymentDtoFactory
{
    public static PaymentDto ToDto(Payment payment)
    {
        return new PaymentDto
        {
            Id = payment.Id.GetValueOrDefault(),
            CheckoutId = payment.CheckoutId,
            Amount = payment.Amount,
            Method = payment.Method,
            Status = payment.Status,
            Note = payment.Note,
            IsCheckInGenerated = payment.IsCheckInGenerated,
            CreatedAt = payment.CreatedAt,
            CreatedBy = payment.CreatedBy,
            VoidedAt = payment.VoidedAt,
            VoidedBy = payment.VoidedBy,
            VoidReason = payment.VoidReason
        };
    }
}
