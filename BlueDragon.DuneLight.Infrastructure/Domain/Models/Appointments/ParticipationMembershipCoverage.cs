using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// P2 (faza 2D) — projekcija odluke o pokriću jednog sudjelovanja članarinom (objašnjivost: stanje, razlog, događaj koji ju
/// je zadnji promijenio, iscrpljeni limit). Izvor istine za potrošnju je ledger MembershipUsage (ActiveUsageId); ovo je jedini
/// zapis koji settlement i read modeli čitaju. Jedan pisac: IMembershipCoverageService (pod lockom članstva). Nema retka =
/// klijent nema članarinu relevantnu za termin — ponašanje kao prije P2. Bez FK na sudjelovanje (vidi migraciju 2D).
/// </summary>
[Table("participation_membership_coverages")]
public class ParticipationMembershipCoverage
{
    [Key]
    [Column("participation_id")]
    public Guid ParticipationId { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_membership_id")]
    public Guid? ClientMembershipId { get; set; }

    [Column("status")]
    public MembershipCoverageStatus Status { get; set; }

    [Column("reason")]
    public MembershipCoverageReason Reason { get; set; }

    [Column("changed_by_event")]
    public MembershipCoverageEvent ChangedByEvent { get; set; }

    [Column("active_usage_id")]
    public Guid? ActiveUsageId { get; set; }

    /// <summary>Samo uz LimitReached: koji limit je pun (Q13.4).</summary>
    [Column("limit_window")]
    public MembershipUsageWindow? LimitWindow { get; set; }

    /// <summary>Null = limit plana (sve usluge zajedno), inače limit usluge.</summary>
    [Column("limit_service_id")]
    public Guid? LimitServiceId { get; set; }

    [Column("limit_max_uses")]
    public int? LimitMaxUses { get; set; }

    [Column("limit_used")]
    public int? LimitUsed { get; set; }

    /// <summary>Uz BeyondHorizon: početak perioda u koji termin pada (kad se evaluira).</summary>
    [Column("expected_period_starts_on")]
    public DateOnly? ExpectedPeriodStartsOn { get; set; }

    /// <summary>P2 (2E) — zadnja automatska promjena cijene (stara, nova, događaj, vrijeme).</summary>
    [Column("last_price_change_old_amount")]
    public decimal? LastPriceChangeOldAmount { get; set; }

    [Column("last_price_change_new_amount")]
    public decimal? LastPriceChangeNewAmount { get; set; }

    [Column("last_price_change_event")]
    public MembershipCoverageEvent? LastPriceChangeEvent { get; set; }

    [Column("last_price_change_at")]
    public DateTimeOffset? LastPriceChangeAt { get; set; }

    /// <summary>Zašto se cijena NE mijenja automatski (ručni iznos, već plaćeno); null = automatska cijena.</summary>
    [Column("price_protected_reason")]
    public PriceProtectionReason? PriceProtectedReason { get; set; }

    /// <summary>Automatska promjena cijene čeka jer je sudjelovanje bilo zaključano drugom naredbom.</summary>
    [Column("price_stale")]
    public bool PriceStale { get; set; }

    [Column("evaluated_at")]
    public DateTimeOffset EvaluatedAt { get; set; }

    [Column("evaluated_by")]
    public Guid? EvaluatedBy { get; set; }
}
