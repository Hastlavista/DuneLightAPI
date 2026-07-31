using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// Prodani paket klijenta (instanca). Snapshotira strukturu kataloškog Package-a u trenutku
/// kupnje (EntryMode, TotalEntryCount, ValidityType, izračunati ExpiryDate) tako da kasnija
/// promjena definicije paketa ne utječe na već prodane instance — isto načelo kao snapshot
/// cijene/trajanja na Appointment.
/// </summary>
[Table("client_packages")]
public class ClientPackage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("package_id")]
    public Guid PackageId { get; set; }

    [Column("purchase_date")]
    public DateTimeOffset PurchaseDate { get; set; }

    [Column("paid_price")]
    public decimal PaidPrice { get; set; }

    [Column("entry_mode")]
    public PackageEntryMode EntryMode { get; set; }

    /// <summary>Snapshot Package.TotalEntryCount. Null = neograničeno.</summary>
    [Column("total_entry_count")]
    public int? TotalEntryCount { get; set; }

    /// <summary>Relevantno samo kad je EntryMode = SharedPool. Null = neograničeno.</summary>
    [Column("remaining_shared_entries")]
    public int? RemainingSharedEntries { get; set; }

    [Column("validity_type")]
    public PackageValidityType ValidityType { get; set; }

    [Column("expiry_date")]
    public DateTimeOffset ExpiryDate { get; set; }

    [Column("status")]
    public ClientPackageStatus Status { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    /// <summary>Postgres sistemska xmin kolona kao optimistic-concurrency token (konfigurirano u DatabaseContext) —
    /// mora biti stvarni CLR property (ne shadow) jer entitet putuje detached kroz zaseban DbContext po pozivu;
    /// vidi ClientPackageService.MutateWithConcurrencyRetry.</summary>
    [Column("xmin")]
    public uint Version { get; set; }

    public Client Client { get; set; }
    public Package Package { get; set; }
    public List<ClientPackageServiceEntry> ServiceEntries { get; set; } = new();
}
