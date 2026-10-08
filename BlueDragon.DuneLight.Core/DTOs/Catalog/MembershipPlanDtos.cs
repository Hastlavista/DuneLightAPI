using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

/// <summary>
/// P2 (docs/p2) — plan članarine: profil (naziv, opis, aktivnost, kapacitet prodaje) s nepromjenjivim verzijama uvjeta.
/// Objava nove verzije = izmjena uvjeta; u 2A vrijedi samo za nove prodaje (Q14 default), a prijenos na postojeća članstva
/// i klasifikacija izmjene (Q48) dolaze s članstvima (2B).
/// </summary>
public class MembershipPlanDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Najveći broj aktivnih članstava plana (zasebno od kapaciteta termina); null = bez ograničenja.</summary>
    public int? MaxActiveMemberships { get; set; }

    public MembershipPlanVersionDto LatestVersion { get; set; }

    /// <summary>Puna povijest verzija (najnovija prva) — samo u detalju plana.</summary>
    public List<MembershipPlanVersionDto> Versions { get; set; } = new();

    /// <summary>Neblokirajuća upozorenja nad najnovijom verzijom (limiti bez učinka, nijedna odabrana poslovnica aktivna).</summary>
    public List<WarningDto> Warnings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class MembershipPlanVersionDto
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public decimal Price { get; set; }
    public decimal StartFee { get; set; }
    public MembershipBillingInterval BillingInterval { get; set; }
    public MembershipRenewalAnchor RenewalAnchor { get; set; }
    public MembershipCompanyScope CompanyScope { get; set; }
    public List<MembershipPlanCompanyDto> Companies { get; set; } = new();
    public List<MembershipPlanServiceDto> Services { get; set; } = new();
    public List<MembershipUsageLimitDto> UsageLimits { get; set; } = new();
    public List<MembershipPriceBenefitDto> PriceBenefits { get; set; } = new();
    public int? MinimumCommitmentPeriods { get; set; }
    public int? CancellationNoticeDays { get; set; }
    public MembershipPauseRulesDto Pause { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public class MembershipPlanCompanyDto
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public bool IsActive { get; set; }
}

public class MembershipPlanServiceDto
{
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Q13/Q17 — limit korištenja: ServiceId = null je limit cijelog plana (sve pokrivene usluge zajedno),
/// inače limit jedne pokrivene usluge. Window = Period su krediti jednog perioda.</summary>
public class MembershipUsageLimitDto
{
    public Guid? ServiceId { get; set; }
    public string ServiceName { get; set; }

    [Required]
    public MembershipUsageWindow? Window { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Limit korištenja mora biti barem 1.")]
    public int MaxUses { get; set; }
}

/// <summary>Q5/Q12 — pravila pauze plana. Planovi "od datuma kupnje" pauziraju po danima (MaxPauseDays, ExtendsPeriod);
/// kalendarski planovi samo u cijelim periodima (MaxPausePeriods). Null limit = bez ograničenja.</summary>
/// <summary>P2 (2E) — jedno pravilo cjenovne pogodnosti za članove. Scope = AllServices (ServiceId prazno; vrijedi i za kasnije
/// dodane usluge) ili Service (ServiceId obavezan) — prazna usluga nikad ne znači "sve". Pravilo usluge ima prednost pred
/// AllServices. Vrijedi samo u poslovnicama plana i samo na nepokrivene sesije; rezultat zaokružen na 0,01, nikad ispod 0.</summary>
public class MembershipPriceBenefitDto
{
    [Required]
    public MembershipPriceBenefitScope? Scope { get; set; }

    public Guid? ServiceId { get; set; }
    public string ServiceName { get; set; }

    [Required]
    public MembershipPriceBenefitType? Type { get; set; }

    /// <summary>PercentOff: (0, 100]; AmountOff: &gt; 0 (EUR); FixedPrice: &gt;= 0 (EUR). Najviše 2 decimale.</summary>
    public decimal Value { get; set; }
}

public class MembershipPauseRulesDto
{
    public bool Allowed { get; set; }
    public int? MaxPauseDays { get; set; }
    public int? MaxPausePeriods { get; set; }

    /// <summary>Najviše pauza u 12 mjeseci od početka članstva (rolling).</summary>
    public int? MaxPausesPer12Months { get; set; }

    /// <summary>Samo za planove "od datuma kupnje": pauza pomiče kraj perioda i obnovu za broj dana pauze.</summary>
    public bool ExtendsPeriod { get; set; }
}

public class MembershipPlanCoveredServiceRequest
{
    [Required]
    public Guid? ServiceId { get; set; }
}

/// <summary>Uvjeti jedne verzije plana (pri kreiranju i objavi nove verzije). Iznosi u EUR, najviše 2 decimale.</summary>
public class MembershipPlanTermsRequest
{
    [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal Price { get; set; }

    [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Početna naknada ne smije biti negativna.")]
    public decimal StartFee { get; set; }

    [Required]
    public MembershipBillingInterval? BillingInterval { get; set; }

    [Required]
    public MembershipRenewalAnchor? RenewalAnchor { get; set; }

    [Required]
    public MembershipCompanyScope? CompanyScope { get; set; }

    /// <summary>Obavezno (barem jedna) za SelectedCompanies, prazno za AllCompanies.</summary>
    public List<Guid> CompanyIds { get; set; } = new();

    public List<MembershipPlanCoveredServiceRequest> Services { get; set; } = new();
    public List<MembershipUsageLimitDto> UsageLimits { get; set; } = new();

    /// <summary>2E — cjenovna pogodnost za članove (samo nepokrivene sesije, Q2): najviše jedno pravilo po usluzi i jedno "Sve
    /// usluge"; plan mora imati barem jednu pokrivenu uslugu ILI barem jedno pravilo pogodnosti.</summary>
    public List<MembershipPriceBenefitDto> PriceBenefits { get; set; } = new();

    /// <summary>Minimalno trajanje obveze u periodima (bez pauziranih); null = nema.</summary>
    public int? MinimumCommitmentPeriods { get; set; }

    /// <summary>Otkazni rok u danima prije obnove; null = nema.</summary>
    public int? CancellationNoticeDays { get; set; }

    [Required]
    public MembershipPauseRulesDto Pause { get; set; }
}

/// <summary>Objava nove verzije uvjeta (Q14): ApplyTo = samo nove prodaje (default) ili i postojeća članstva.</summary>
public class MembershipPlanVersionPublishRequest : MembershipPlanTermsRequest
{
    public MembershipPlanApplyTo ApplyTo { get; set; } = MembershipPlanApplyTo.NewSalesOnly;
}

/// <summary>Q14/Q48 — rezultat objave: klasifikacija izmjene u odnosu na prethodnu verziju, pogoršane ili nejasne dimenzije
/// i, uz NewSalesAndExisting, pogođena članstva s datumom od kad nove uvjete dobivaju.</summary>
public class MembershipPlanVersionPublishResultDto
{
    public MembershipPlanDto Plan { get; set; }
    public MembershipPlanChangeClassification Classification { get; set; }
    public List<string> WorsenedDimensions { get; set; } = new();
    public MembershipPlanApplyTo ApplyTo { get; set; }
    public List<MembershipPlanAffectedMembershipDto> AffectedMemberships { get; set; } = new();
}

public class MembershipPlanAffectedMembershipDto
{
    public Guid MembershipId { get; set; }
    public Guid ClientId { get; set; }
    public DateOnly EffectiveOn { get; set; }

    /// <summary>True kad nova verzija NIJE zakazana jer bi stvorila preklapanje s drugom članarinom klijenta (Q10).</summary>
    public bool Skipped { get; set; }

    /// <summary>Kod razloga preskakanja (npr. MEMBERSHIP_OVERLAPPING_COVERAGE); članstvo dobiva trajnu oznaku.</summary>
    public string SkipReason { get; set; }

    /// <summary>Članstvo ima zakazanu klijentovu promjenu plana: izmjena se ne primjenjuje sada, nego se pamti s ovim datumom i
    /// vraća ako klijent povuče promjenu.</summary>
    public bool HeldByClientPlanChange { get; set; }
}

public class MembershipPlanCreateRequest : MembershipPlanTermsRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [MaxLength(2000)]
    public string Description { get; set; }

    public int? MaxActiveMemberships { get; set; }
}

public class MembershipPlanDetailsRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [MaxLength(2000)]
    public string Description { get; set; }
}

public class MembershipPlanCapacityRequest
{
    /// <summary>Null = bez ograničenja.</summary>
    public int? MaxActiveMemberships { get; set; }
}
