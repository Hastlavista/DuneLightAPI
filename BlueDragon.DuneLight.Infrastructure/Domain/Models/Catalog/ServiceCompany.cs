using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// Eksplicitna dostupnost usluge po poslovnici. Service je Organization-level (ne pripada direktno
/// jednoj Company), ali operativno se nudi samo u poslovnicama gdje postoji ovaj zapis — NEMA
/// implicitnog "prazno = svugdje" ponašanja: nova usluga kreće s nula zapisa i time je dostupna nigdje
/// dok je admin eksplicitno ne dodijeli. Ovo je konfiguracijski zapis (ne povijesni/transakcijski) —
/// briše se kad se dodjela ukine i kaskadno kad se Service/Company trajno obrišu (vidi
/// DatabaseContext.ConfigureCatalog). Namjerno bez OrganizationId (isti obrazac kao EmployeeCompany/
/// EmployeeServiceAssignment) — tenant integritet (Service.OrganizationId == Company.OrganizationId ==
/// CurrentOrganizationId) provjerava se u ServiceAvailabilityService prije bilo kakvog upisa/čitanja.
/// </summary>
[Table("service_companies")]
public class ServiceCompany
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    public Service Service { get; set; }
    public Company Company { get; set; }
}
