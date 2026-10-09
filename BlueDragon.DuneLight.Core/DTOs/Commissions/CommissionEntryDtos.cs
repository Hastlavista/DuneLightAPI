using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Commissions;

public class CommissionEntryDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public Guid CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public CommissionSourceType SourceType { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? BookingId { get; set; }

    /// <summary>Phase M1G — izvorno sudjelovanje (IndividualService) ili segment sesije (GroupService); jedno sudjelovanje
    /// smije imati više zapisa (po jedan za svakog zaposlenika segmenta).</summary>
    public Guid? BookingSegmentParticipationId { get; set; }
    public Guid? AppointmentSegmentId { get; set; }
    public Guid? CheckoutItemId { get; set; }

    /// <summary>P2 (2F) — izvor MembershipSale (članstvo) odnosno PolicyFee (posljedica politike).</summary>
    public Guid? ClientMembershipId { get; set; }
    public Guid? ParticipationPolicyConsequenceId { get; set; }
    public decimal BaseAmount { get; set; }
    public CommissionCalculationType CalculationType { get; set; }
    public decimal RuleValue { get; set; }
    public decimal CommissionAmount { get; set; }

    /// <summary>P2 (2F) — snapshot: način plaćanja (informativno), izvor pokrića, cijena sesije / cjenik i primijenjene postavke.</summary>
    public CommissionPaymentSource? PaymentSource { get; set; }
    public Guid? CoverageSourceId { get; set; }
    public decimal? SessionPriceAmount { get; set; }
    public decimal? ListPriceAmount { get; set; }
    public bool? IsManualPrice { get; set; }
    public bool? DeductDiscounts { get; set; }
    public bool? DeductMembershipDiscounts { get; set; }

    /// <summary>P2 (2F, Vagaro) — razina primijenjenog pravila i objašnjenje izbora (primijenjeno pravilo, zašto, neprimijenjena
    /// pravila s razlogom). Null za izvore bez izbora pravila (npr. stari zapisi).</summary>
    public CommissionRuleScope? AppliedRuleScope { get; set; }
    public CommissionRuleEvaluationDto? RuleEvaluation { get; set; }
    public bool WasCapped { get; set; }
    public CommissionEntryStatus Status { get; set; }
    public DateTimeOffset EarnedAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversalReason { get; set; }
    public Guid? CorrectionOfEntryId { get; set; }

    /// <summary>P2 (2F) — iznos ovog zapisa unutar traženog razdoblja (brojanje po događajima): +CommissionAmount ako je zarađen u
    /// razdoblju, −CommissionAmount ako je storniran u razdoblju (oboje = 0).</summary>
    public decimal PeriodAmount { get; set; }
}

/// <summary>Ograđen datumski raspon je obavezan (From/To) — namjerno bez "sva povijest" endpointa (vidi spec
/// section 46). EmployeeId/CompanyId su opcionalni filtri. P2 (2F): vraća zapise zarađene ILI stornirane u razdoblju.</summary>
public class CommissionEntryQuery
{
    public Guid? EmployeeId { get; set; }
    public Guid? CompanyId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CommissionSummaryQuery
{
    public Guid? EmployeeId { get; set; }
    public Guid? CompanyId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}

/// <summary>P2 (2F, brojanje po događajima): EarnedAmount = zarade nastale u razdoblju (bez obzira na kasniji storno),
/// ReversedAmount = storna izvršena u razdoblju (bez obzira kad je provizija nastala); NetAmount = razlika. Prošlo razdoblje se
/// naknadnim stornom ne mijenja.</summary>
public class EmployeeCommissionSummaryDto
{
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public decimal EarnedAmount { get; set; }
    public decimal ReversedAmount { get; set; }
    public decimal NetAmount { get; set; }
    public int EntryCount { get; set; }
}

public class CommissionSummaryResultDto
{
    public List<EmployeeCommissionSummaryDto> Employees { get; set; } = new();
    public decimal TotalNetAmount { get; set; }
}

/// <summary>P2 (2F, Q50) — korekcija korisnika provizije na prodaju nakon nastanka: storno postojeće + nova provizija za novog
/// korisnika (po njegovom pravilu važećem na datum nastanka izvorne provizije). Razlog je obavezan.</summary>
public class CommissionEntryReassignRequest
{
    [Required]
    public Guid? EmployeeId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; }

    /// <summary>P2 (2F-11) — svjesna potvrda kad novi korisnik nema pravilo za prodaju važeće na datum izvorne provizije (stara se
    /// stornira, nova NE nastaje). Bez potvrde naredba ništa ne mijenja i vraća COMMISSION_REASSIGN_WITHOUT_RULE.</summary>
    public bool ConfirmWithoutCommission { get; set; }
}

public class CommissionEntryReassignResultDto
{
    public CommissionEntryDto Reversed { get; set; }

    /// <summary>Null kad novi korisnik nema primjenjivo pravilo (nema pravila = nema provizije).</summary>
    public CommissionEntryDto? Created { get; set; }
}

/// <summary>P2 (2F, §18.1/§16.3) — korisnik provizije na prodaju (stavka checkouta ili članstvo); null = bez provizije.</summary>
public class SaleCommissionEmployeeRequest
{
    public Guid? EmployeeId { get; set; }
}

/// <summary>P2 (2F, Vagaro) — jednoznačna i vidljiva pravila prednosti: koje je pravilo primijenjeno i zašto, te koja nisu.</summary>
public class CommissionRuleEvaluationDto
{
    public CommissionRuleEvaluationItemDto Applied { get; set; }
    public List<CommissionRuleEvaluationItemDto> NotApplied { get; set; } = new();
}

public class CommissionRuleEvaluationItemDto
{
    public Guid RuleId { get; set; }
    public CommissionRuleScope Scope { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public CommissionCalculationType CalculationType { get; set; }
    public decimal Value { get; set; }

    /// <summary>Npr. "Pravilo za uslugu ima prednost pred općim pravilom zaposlenika."</summary>
    public string Reason { get; set; }
}

/// <summary>P2 (2F, pregled — izbor 8) — naknadna dodjela korisnika provizije na prodaju kad provizija nije nastala jer korisnika
/// nije bilo: točno jedan izvor (stavka proizvoda/paketa zatvorenog checkouta ili članstvo s evaluiranom prvom prodajom).
/// Provizija nastaje po pravilu korisnika važećem na izvorni datum nastanka; "nema pravila" i "osnovica 0" ostaju konačni.</summary>
public class CommissionSaleAssignmentRequest
{
    public Guid? CheckoutItemId { get; set; }
    public Guid? ClientMembershipId { get; set; }

    [Required]
    public Guid? EmployeeId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; }
}

public class CommissionSaleAssignmentResultDto
{
    /// <summary>Null kad dodijeljeni korisnik nema primjenjivo pravilo (korisnik je zapisan, provizija ne nastaje — konačno).</summary>
    public CommissionEntryDto? Created { get; set; }
}
