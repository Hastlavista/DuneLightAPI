using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>Usluga uključena u paket. EntryCount se koristi samo kod PerService načina trošenja.</summary>
[Table("package_services")]
public class PackageServiceItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("package_id")]
    public Guid PackageId { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    [Column("entry_count")]
    public int? EntryCount { get; set; }

    public Package Package { get; set; }
    public Service Service { get; set; }
}
