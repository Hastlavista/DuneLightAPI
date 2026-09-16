using System;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Products;

/// <summary>Trenutno stanje zalihe za jedan par Product+Company — vidi ProductStock.cs.</summary>
public class ProductStockDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Jedan StockMovement redak — vidi StockMovement.cs.</summary>
public class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public StockMovementType Type { get; set; }
    public int QuantityDelta { get; set; }
    public string Reason { get; set; }
    public Guid? CheckoutItemId { get; set; }
    public Guid? RelatedCompanyId { get; set; }
    public string RelatedCompanyName { get; set; }
    public Guid? TransferCorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>Postavlja zalihu na apsolutnu vrijednost — server izračunava deltu i sprema Adjustment (ili
/// Initial, ako redak zalihe još ne postoji) StockMovement (vidi spec section 59/61/67). Ne prima deltu
/// izravno kako bi se izbjeglo ručno računanje na strani osoblja.</summary>
public class StockAdjustRequest
{
    [Range(0, int.MaxValue, ErrorMessage = "Količina ne smije biti negativna.")]
    public int Quantity { get; set; }

    [MaxLength(500)]
    public string Reason { get; set; }
}

/// <summary>Premješta količinu istog Producta iz jedne Company u drugu — atomski TransferOut/TransferIn par
/// (vidi spec section 32-35).</summary>
public class StockTransferRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Required]
    public Guid FromCompanyId { get; set; }

    [Required]
    public Guid ToCompanyId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Količina mora biti veća od nule.")]
    public int Quantity { get; set; }
}

public class StockTransferResultDto
{
    public ProductStockDto Source { get; set; }
    public ProductStockDto Destination { get; set; }
}
