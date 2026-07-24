using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Clients;

public class ClientPackageServiceEntryDto
{
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public int? TotalEntries { get; set; }
    public int? RemainingEntries { get; set; }
}

public class ClientPackageDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid PackageId { get; set; }
    public string PackageName { get; set; }
    public DateTimeOffset PurchaseDate { get; set; }
    public decimal PaidPrice { get; set; }
    public PackageEntryMode EntryMode { get; set; }
    public int? TotalEntryCount { get; set; }
    public int? RemainingSharedEntries { get; set; }
    public PackageValidityType ValidityType { get; set; }
    public DateTimeOffset ExpiryDate { get; set; }
    public ClientPackageStatus Status { get; set; }
    public List<ClientPackageServiceEntryDto> ServiceEntries { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class ClientPackageCreateRequest
{
    [Required]
    public Guid PackageId { get; set; }

    public DateTimeOffset? PurchaseDate { get; set; }

    /// <summary>Ako nije zadano, predlaže se preko IPricingService.ResolvePrice za odabranu lokaciju/datum.</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal? PaidPrice { get; set; }

    /// <summary>Koristi se samo za predlaganje cijene (lokacijski cjenik). Ne pohranjuje se na paket.</summary>
    public Guid? LocationId { get; set; }
}
