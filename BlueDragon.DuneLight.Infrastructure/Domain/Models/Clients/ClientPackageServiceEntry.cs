using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// Jedan red po usluzi iz izvornog Package.Services u trenutku kupnje. Služi i kao snapshot
/// "koje usluge ovaj paket pokriva" (bitno za SharedPool eligibility), ne samo za PerService
/// brojanje. Kod SharedPool načina TotalEntries/RemainingEntries ostaju null — red tada postoji
/// samo kao marker da je usluga pokrivena, a brojanje ide preko ClientPackage.RemainingSharedEntries.
/// </summary>
[Table("client_package_service_entries")]
public class ClientPackageServiceEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("client_package_id")]
    public Guid ClientPackageId { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    /// <summary>Snapshot; null kad je SharedPool ili neograničeno PerService.</summary>
    [Column("total_entries")]
    public int? TotalEntries { get; set; }

    /// <summary>Mutable; null i neiskorišten kad je nadređeni ClientPackage.EntryMode = SharedPool.</summary>
    [Column("remaining_entries")]
    public int? RemainingEntries { get; set; }

    public ClientPackage ClientPackage { get; set; }
    public Service Service { get; set; }
}
