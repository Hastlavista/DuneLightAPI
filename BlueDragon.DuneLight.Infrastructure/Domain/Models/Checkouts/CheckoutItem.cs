using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

/// <summary>
/// Jedna komercijalna stavka unutar Checkouta — točno JEDAN tipizirani subjekt (BookingSegmentParticipationId XOR
/// PackageId XOR ProductId, prema Type; CHECK constraint u migraciji, vidi spec section 9). Type=Booking je stavka
/// USLUGE: od Phase D3B3B namiruje SUDJELOVANJE (BookingSegmentParticipation), ne Booking — Booking/klijent/usluga su
/// dostupni kroz Participation -&gt; Booking/Segment. Financijska činjenica se SNAPSHOTTA ovdje u trenutku dodavanja
/// (Description/UnitPrice/Amount) — kasnija promjena cijene sudjelovanja ili Package cijene ne mijenja retroaktivno
/// već dodanu stavku (vidi spec section 31/67).
///
/// Amount = maloprodajna (retail) vrijednost stavke, uvijek puna cijena bez obzira na način podmirenja.
/// Stvaran novčani dug (MonetaryDue) se IZVODI (ne persistira) — 0 za paket-pokriven Booking (vidi
/// CheckoutFinancialsCalculator / ParticipationSettlement), inače jednako Amount. Ovo razdvaja "koliko usluga stvarno vrijedi" od
/// "koliko se duguje u novcu" (vidi spec section 22/23).
///
/// LocksParticipation je denormaliziran flag koji vrijedi TOČNO dok je roditeljski Checkout Open I stavka nije
/// uklonjena — nosi jedinstveni djelomični indeks (ux_checkout_items_locks_participation) da isto sudjelovanje ne može
/// istovremeno biti aktivna stavka u dva Open checkouta (vidi spec section 29/60). Servis ga postavlja na
/// false čim Checkout prestane biti Open (Complete/Cancel) ili je stavka uklonjena — NIJE izvor istine ni za
/// što drugo, samo mehanizam uniqueness-a.
/// </summary>
[Table("checkout_items")]
public class CheckoutItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("checkout_id")]
    public Guid CheckoutId { get; set; }

    [Column("type")]
    public CheckoutItemType Type { get; set; }

    /// <summary>Prikazni naziv snapshotan u trenutku dodavanja (npr. naziv usluge/paketa) — buduće
    /// preimenovanje kataloškog subjekta ne mijenja povijesnu stavku (vidi spec section 67/68/69). Namjerno
    /// bez osobnih podataka (Client je već poznat preko Checkout.ClientId, vidi spec section 68).</summary>
    [Column("description")]
    public string Description { get; set; }

    [Column("unit_price")]
    public decimal UnitPrice { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    /// <summary>Retail vrijednost stavke = UnitPrice * Quantity, snapshotana zajedno s UnitPrice (ne izvedena u
    /// upitu) radi jednostavnog agregiranja — vidi CheckoutFinancialsCalculator.</summary>
    [Column("amount")]
    public decimal Amount { get; set; }

    /// <summary>Phase D3B3B: sudjelovanje koje stavka usluge (Type=Booking) namiruje — iste organizacije (složeni FK
    /// (booking_segment_participation_id, organization_id) u bazi), RESTRICT brisanje.</summary>
    [Column("booking_segment_participation_id")]
    public Guid? BookingSegmentParticipationId { get; set; }

    [Column("package_id")]
    public Guid? PackageId { get; set; }

    [Column("product_id")]
    public Guid? ProductId { get; set; }

    /// <summary>Popunjeno TEK nakon uspješnog Checkout.Complete za Type=Package — rezultantni izdani entitlement
    /// (vidi spec section 27/62, ključ za idempotentno sprječavanje dvostrukog izdavanja).</summary>
    [Column("client_package_id")]
    public Guid? ClientPackageId { get; set; }

    [Column("locks_participation")]
    public bool LocksParticipation { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public Checkout Checkout { get; set; }
    public BookingSegmentParticipation Participation { get; set; }
    public Package Package { get; set; }
    public Product Product { get; set; }
    public ClientPackage ClientPackage { get; set; }

    public List<PaymentAllocation> Allocations { get; set; } = new();
}
