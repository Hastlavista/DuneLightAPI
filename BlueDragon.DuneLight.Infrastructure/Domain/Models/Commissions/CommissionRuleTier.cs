using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;

/// <summary>
/// P2 (2F, Vagaro) — razina općeg pravila zaposlenika (SubjectType = AllServices). U P2 opće pravilo ima TOČNO jednu razinu bez
/// praga (FromRevenue = 0); faza Payroll ("Tiered by Revenue") dodaje razine po prometu zaposlenika u obračunskom razdoblju bez
/// promjene modela ili migracije podataka. Izračun Percentage | Fixed (None nije dopušten na razini). Verzionirano s pravilom.
/// </summary>
[Table("commission_rule_tiers")]
public class CommissionRuleTier
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("commission_rule_id")]
    public Guid CommissionRuleId { get; set; }

    /// <summary>Prag prometa od kojeg razina vrijedi; u P2 uvijek 0.</summary>
    [Column("from_revenue")]
    public decimal FromRevenue { get; set; }

    [Column("calculation_type")]
    public CommissionCalculationType CalculationType { get; set; }

    [Column("value")]
    public decimal Value { get; set; }
}
