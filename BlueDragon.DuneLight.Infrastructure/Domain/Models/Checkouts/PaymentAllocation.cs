using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

/// <summary>
/// Veže dio (ili cijeli) iznosa jednog Paymenta na jednu CheckoutItem stavku — omogućuje da jedan stvaran
/// novčani Payment (npr. jedna kartična transakcija od 100 €) namiri VIŠE komercijalnih stavki odjednom
/// (split po stavkama), umjesto lažnog modeliranja kao više odvojenih Paymenta (vidi spec section 17/18).
///
/// Zbroj Allocationa jednog Paymenta == Payment.Amount kod kreiranja (nema neraspoređenog novca u ovom MVP-u
/// — vidi spec section 19). Alokacija se NE briše kad se Payment voida — jednostavno se isključuje iz
/// izračuna jer roditeljski Payment.Status više nije Completed (vidi CheckoutFinancialsCalculator/
/// BookingFinancialsCalculator, isti obrazac kao stari Payment Void).
/// </summary>
[Table("payment_allocations")]
public class PaymentAllocation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("payment_id")]
    public Guid PaymentId { get; set; }

    [Column("checkout_item_id")]
    public Guid CheckoutItemId { get; set; }

    /// <summary>Uvijek &gt; 0.</summary>
    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public Payment Payment { get; set; }
    public CheckoutItem CheckoutItem { get; set; }
}
