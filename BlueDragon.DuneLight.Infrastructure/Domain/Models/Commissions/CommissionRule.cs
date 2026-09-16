using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;

/// <summary>
/// Konfiguracija provizije: Employee + točno jedan predmet (Service/Product/Package prema SubjectType).
/// Namjerno BEZ ValidFrom/ValidTo — provizija se ne preklapa u vremenu (najviše jedan AKTIVAN redak po
/// Employee+Subject, vidi djelomični unique indeks u migraciji), promjena vrijednosti ide kroz Update
/// (izravna izmjena Value/CalculationType) ili deaktivaciju+novi redak. Ovo je namjerno manji model od
/// PriceListItem/PriceListItemHistory (koji datumski raspon treba jer se prošle cijene moraju rekonstruirati
/// za povijesne izvještaje) — CommissionEntry već snapshotta CalculationType/RuleValue/BaseAmount/
/// CommissionAmount u trenutku zarade, pa povijesni izračun NIKAD ne čita trenutni CommissionRule (vidi
/// CommissionEntry.cs) i datumski raspon ovdje ne bi ništa dodao osim složenosti preklapanja (vidi spec section
/// 11 — eksplicitno dopušten manji model uz obrazloženje).
///
/// Value je Percentage (0-100) ili Fixed (&gt;=0) prema CalculationType — validirano u CommissionRuleService,
/// CHECK constraint u migraciji kao backstop. Percentage NIJE dopušten kad je SubjectType=Service i
/// referencirani Service.ExecutionMode=Group (nema nedvosmislene per-occurrence osnovice za postotak na grupni
/// termin — vidi spec section 16/54, CommissionRuleService baca COMMISSION_GROUP_PERCENTAGE_NOT_SUPPORTED).
/// </summary>
[Table("commission_rules")]
public class CommissionRule
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("subject_type")]
    public CommissionSubjectType SubjectType { get; set; }

    [Column("service_id")]
    public Guid? ServiceId { get; set; }

    [Column("product_id")]
    public Guid? ProductId { get; set; }

    [Column("package_id")]
    public Guid? PackageId { get; set; }

    [Column("calculation_type")]
    public CommissionCalculationType CalculationType { get; set; }

    [Column("value")]
    public decimal Value { get; set; }

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

    public Employee Employee { get; set; }
    public Service Service { get; set; }
    public Product Product { get; set; }
    public Package Package { get; set; }
}
