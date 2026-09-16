using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;

/// <summary>
/// Nepromjenjiv redak povijesti zalihe — jedini izvor istine za svaku promjenu ProductStock.Quantity (vidi
/// ProductStock.cs klasnu napomenu). Nikad se ne briše/update-a (isto načelo kao CheckoutAuditLog/
/// AppointmentAuditLog) — ispravka ide kroz nov Adjustment redak, ne kroz brisanje starog.
///
/// QuantityDelta može biti pozitivan ili negativan; zbroj svih redaka za dani (ProductId, CompanyId) uvijek
/// odgovara trenutnom ProductStock.Quantity (invarijant koji ProductStockHandler/StockService održavaju
/// atomski — isti movement + isti balance update unutar iste transakcije).
///
/// CheckoutItemId popunjen SAMO za Type=Sale (djelomični unique indeks ux_stock_movements_sale_checkout_item
/// sprječava dvostruki decrement iste stavke, vidi migraciju) — poveznica na komercijalnu prodaju koja je
/// pokrenula decrement. RelatedCompanyId + TransferCorrelationId popunjeni SAMO za TransferOut/TransferIn par
/// (vidi StockService.Transfer) — RelatedCompanyId je "druga strana" transfera (odredište za TransferOut,
/// izvor za TransferIn), TransferCorrelationId veže točno ta dva retka kao jedan atomski transfer.
/// </summary>
[Table("stock_movements")]
public class StockMovement
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

    [Column("type")]
    public StockMovementType Type { get; set; }

    [Column("quantity_delta")]
    public int QuantityDelta { get; set; }

    [Column("reason")]
    public string Reason { get; set; }

    [Column("checkout_item_id")]
    public Guid? CheckoutItemId { get; set; }

    [Column("related_company_id")]
    public Guid? RelatedCompanyId { get; set; }

    [Column("transfer_correlation_id")]
    public Guid? TransferCorrelationId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public Product Product { get; set; }
    public Company Company { get; set; }
    public Company RelatedCompany { get; set; }
    public CheckoutItem CheckoutItem { get; set; }
}
