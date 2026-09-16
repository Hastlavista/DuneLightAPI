using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;

/// <summary>
/// Materijalizirano TRENUTNO stanje zalihe za par Product+Company — točno jedan redak po
/// (ProductId, CompanyId), vidi ux_product_stock_product_company. Quantity NIKAD se ne mijenja izravno bez
/// pripadajućeg StockMovement retka (vidi StockMovement.cs) — StockMovement je izvor istine/povijest,
/// ProductStock.Quantity je samo brzi materijalizirani zbroj za čitanje (isto načelo kao
/// ClientPackage.RemainingSharedEntries + ClientPackageServiceEntry.RemainingEntries).
///
/// Quantity &gt;= 0 uvijek (CHECK constraint u migraciji + provjera u ProductStockHandler/StockService prije
/// svakog decrementa) — nema rezervacije zalihe za Open Checkout stavke (vidi spec section 24/25), pa dvije
/// istovremeno otvorene košarice mogu "vidjeti" istu jedinicu; samo prva uspješno završena Checkout.Complete
/// je stvarno konzumira (concurrency-safe preko FOR UPDATE zaključavanja retka, vidi ProductStockHandler.
/// GetOrCreateForUpdate).
///
/// Redak se stvara lijeno (prvi Initial/Adjustment/TransferIn za dani par) preko INSERT ... ON CONFLICT DO
/// NOTHING + SELECT ... FOR UPDATE u istom upitu — atomski siguran i pod konkurentnim prvim stvaranjem, bez
/// potrebe za retry petljom (vidi ProductStockHandler).
/// </summary>
[Table("product_stock")]
public class ProductStock
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("product_id")]
    public Guid ProductId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public Product Product { get; set; }
    public Company Company { get; set; }
}
