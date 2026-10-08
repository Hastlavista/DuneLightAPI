using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;

/// <summary>
/// Konfiguracija provizije: Employee + vrsta (Kind) + predmet (Service/Product/Package/MembershipPlan prema SubjectType, ili
/// AllServices = opće pravilo zaposlenika) + datum od kad vrijedi.
///
/// P2 (2F, Vagaro model, ADR-0030): pravilo nije obavezno (nema pravila = nema provizije). Povijest: promjena koja treba vrijediti
/// od datuma je NOVI redak s novim EffectiveFrom (stari ostaje); izmjena retka ispravlja tu verziju. Za datum se bira verzija s
/// najvećim EffectiveFrom &lt;= lokalni datum (odrađeno: datum sesije u kalendaru poslovnice termina; prodaja: datum nastanka
/// provizije) — UKLJUČUJUĆI deaktivirane verzije: deaktivirana verzija od DeactivatedFrom znači "nema pravila" i NIKAD ne vraća
/// stariju verziju (bez povratka). Za grešku se verzija bez ijedne provizije može obrisati; tada za njezin raspon vrijedi
/// prethodna verzija. EffectiveFrom = DateOnly.MinValue = "bez početnog datuma". Unique (Employee, Kind, predmet, EffectiveFrom)
/// neovisno o aktivnosti. CommissionEntry snapshotta sve primijenjene vrijednosti.
///
/// Kind: Service/AllServices → Performance; Product/Package/MembershipPlan → Sale (provizija na prodaju usluge je odluka nakon 2F,
/// Q52b). Pravilo za uslugu ima prednost pred općim pravilom i može biti None ("Bez provizije", izričito isključuje uslugu). Opće
/// pravilo vrijedi samo za individualne usluge; iznos je u razinama (Tiers, u P2 jedna bez praga). Grupni treninzi: samo pravilo
/// usluge, fiksno po terminu.
///
/// Value je Percentage (0-100) ili Fixed (&gt;=0) prema CalculationType (None = 0); za AllServices su CalculationType/Value prazni
/// (iznos je u razini). Percentage NIJE dopušten kad je SubjectType=Service i Service.ExecutionMode=Group.
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

    /// <summary>P2 (2F, §18.1) — za odrađeno ili za prodaju.</summary>
    [Column("kind")]
    public CommissionRuleKind Kind { get; set; } = CommissionRuleKind.Performance;

    [Column("subject_type")]
    public CommissionSubjectType SubjectType { get; set; }

    [Column("service_id")]
    public Guid? ServiceId { get; set; }

    [Column("product_id")]
    public Guid? ProductId { get; set; }

    [Column("package_id")]
    public Guid? PackageId { get; set; }

    /// <summary>P2 (2F, Q39) — plan članarine (samo Kind = Sale).</summary>
    [Column("membership_plan_id")]
    public Guid? MembershipPlanId { get; set; }

    /// <summary>P2 (2F, Q28.5) — od kad pravilo vrijedi (uključivo); MinValue = bez početnog datuma.</summary>
    [Column("effective_from")]
    public DateOnly EffectiveFrom { get; set; } = DateOnly.MinValue;

    /// <summary>P2 (2F, pregled) — od kad je verzija deaktivirana (uključivo): od tog datuma nema pravila, bez povratka na stariju
    /// verziju. Null uz IsActive = false = deaktivirana bez datuma (nikad ne vrijedi).</summary>
    [Column("deactivated_from")]
    public DateOnly? DeactivatedFrom { get; set; }

    /// <summary>Null samo za AllServices (iznos je u razini).</summary>
    [Column("calculation_type")]
    public CommissionCalculationType? CalculationType { get; set; }

    [Column("value")]
    public decimal? Value { get; set; }

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
    public MembershipPlan MembershipPlan { get; set; }

    /// <summary>P2 (2F) — razine općeg pravila (AllServices); prazno za ostale predmete.</summary>
    public List<CommissionRuleTier> Tiers { get; set; } = new();

    /// <summary>Vrijedi li ova verzija (kad je izabrana za datum) na taj datum, ili je deaktivirana.</summary>
    public bool AppliesOn(DateOnly date) => IsActive || (DeactivatedFrom.HasValue && date < DeactivatedFrom.Value);
}
