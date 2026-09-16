using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;

/// <summary>
/// Katalog maloprodajnog proizvoda (napr. proteinska pločica, podloga za jogu, majica) — Organization-level
/// entitet, isti obrazac kao Service (nije vlasništvo jedne Company). Dostupnost/zaliha je Company-specifična
/// preko ProductStock (vidi ProductStock.cs) — Product sam po sebi ne nosi nikakvu količinu.
///
/// DefaultPrice je Organization-level zadana cijena koja se snapshotta na CheckoutItem.UnitPrice u trenutku
/// dodavanja u Checkout (vidi CheckoutService.AddProductItem) — kasnija promjena ne mijenja retroaktivno već
/// dodane stavke (isti obrazac kao Service.DefaultPrice/Package cijena).
/// </summary>
[Table("products")]
public class Product
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("description")]
    public string Description { get; set; }

    /// <summary>Opcionalno — kad popunjeno, jedinstveno po Organization (normalizirano trim/case-insensitive,
    /// vidi ux_products_org_sku). Nema barcode/hardware semantiku (deferred, vidi spec).</summary>
    [Column("sku")]
    public string Sku { get; set; }

    [Column("default_price")]
    public decimal DefaultPrice { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }
}
