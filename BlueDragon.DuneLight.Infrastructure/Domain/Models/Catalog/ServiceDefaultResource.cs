using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// K1-6 (P-7) — zadani resurs usluge (npr. masaža → 1 masažni stol). Resurs pripada jednoj poslovnici, pa termin dobiva samo
/// zadane resurse SVOJE poslovnice. Primjenjuje se kad zahtjev zakazivanja ne navodi resurse (null); poslani resursi (i prazna
/// lista) imaju prednost. Konfiguracijski zapis (ne povijesni) — isti obrazac kao ServiceCompany: bez OrganizationId (tenant
/// integritet provjerava ServiceAvailabilityService), kaskadno brisanje sa Service/Resource.
/// </summary>
[Table("service_default_resources")]
public class ServiceDefaultResource
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    [Column("resource_id")]
    public Guid ResourceId { get; set; }

    /// <summary>Količina po segmentu (≥ 1, najviše kapacitet resursa pri spremanju).</summary>
    [Column("quantity_required")]
    public int QuantityRequired { get; set; }

    public Service Service { get; set; }
    public Resource Resource { get; set; }
}
