using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.DTOs.Clients;

/// <summary>
/// P2 (faza 2B, docs/p2) — članstvo klijenta. Uvjeti su nepromjenjiva verzija plana (Terms); stanje je izvedeno na današnji dan
/// u zoni organizacije. Periodi, zaduženja i pokriće dolaze u 2C/2D.
/// </summary>
public class ClientMembershipDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid MembershipPlanId { get; set; }
    public string MembershipPlanName { get; set; }

    /// <summary>Trenutni uvjeti (verzija plana koja vrijedi danas).</summary>
    public MembershipPlanVersionDto Terms { get; set; }

    public MembershipState State { get; set; }

    /// <summary>K1-8 — članstvo stoji (sve poslovnice opsega plana neaktivne): obnova ne otvara periode ni zaduženja. Null = ne
    /// stoji; inače datum od kad stoji.</summary>
    public DateOnly? StandingStillSince { get; set; }
    public DateOnly StartsOn { get; set; }

    /// <summary>Tekući period na današnji dan (za Scheduled prvi period); null kad je članstvo završilo ili poništeno.</summary>
    public MembershipPeriodDto CurrentPeriod { get; set; }

    /// <summary>Zadnji dan članstva (uključivo) kad je završetak zakazan ili nastupio; null = traje i obnavlja se.</summary>
    public DateOnly? EndsOn { get; set; }
    public MembershipEndReason? EndReason { get; set; }
    public DateTimeOffset? CancellationRequestedAt { get; set; }
    public Guid? CancellationRequestedBy { get; set; }
    public string CancellationReason { get; set; }

    /// <summary>Q15 — stanje duga iz najstarijeg nekonačnog zaduženja (Current / InGrace / Delinquent).</summary>
    public MembershipStanding Standing { get; set; }

    /// <summary>Ukupan preostali dug otvorenih zaduženja.</summary>
    public decimal OutstandingAmount { get; set; }

    public MembershipPendingChangeDto PendingChange { get; set; }

    /// <summary>Izmjena plana koju je istisnula klijentova promjena plana (izvorni datum); vraća se ako se promjena povuče.</summary>
    public MembershipPendingChangeDto DisplacedPlanUpdate { get; set; }

    /// <summary>Trajna oznaka: izmjena plana nije primijenjena na ovo članstvo (i zašto), dok je kasnija izmjena ne riješi.</summary>
    public MembershipPlanUpdateNotAppliedDto PlanUpdateNotApplied { get; set; }
    public List<MembershipPauseDto> Pauses { get; set; } = new();

    /// <summary>Q5.4 — preostali dani/periodi i broj pauza u tekućih 12 mjeseci od početka članstva; null kad plan ne dopušta
    /// pauzu.</summary>
    public MembershipPauseAllowanceDto PauseAllowance { get; set; }

    public Guid SoldCompanyId { get; set; }
    public MembershipSaleChannel SoldVia { get; set; }
    public Guid? SoldBy { get; set; }
    /// <summary>2F — korisnik provizije na prodaju (prva prodaja), jedini izvor; null = bez provizije na prodaju.</summary>
    public Guid? SaleCommissionEmployeeId { get; set; }

    /// <summary>2F (Q42) — kad su prvo zaduženje i početna naknada prvi put bili konačni (provizija na prvu prodaju evaluirana).</summary>
    public DateTimeOffset? FirstSaleSettledAt { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public string VoidReason { get; set; }

    /// <summary>Neblokirajuća upozorenja naredbe koja je vratila ovaj odgovor (npr. plan ne vrijedi u poslovnici prodaje,
    /// poništena zakazana pauza).</summary>
    public List<WarningDto> Warnings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class MembershipPeriodDto
{
    public int Sequence { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
}

public class MembershipPendingChangeDto
{
    public MembershipPendingChangeSource Source { get; set; }
    public Guid MembershipPlanId { get; set; }
    public string MembershipPlanName { get; set; }
    public Guid PlanVersionId { get; set; }
    public int PlanVersion { get; set; }
    public DateOnly EffectiveOn { get; set; }
}

/// <summary>P2 (2C) — zaduženje članarine. Plaćenost se izvodi iz aktivnih alokacija plaćanja (Q16); konačno = Paid ili
/// WrittenOff.</summary>
public class MembershipChargeDto
{
    public Guid Id { get; set; }
    public Guid ClientMembershipId { get; set; }
    public Guid ClientId { get; set; }
    public MembershipChargeKind Kind { get; set; }
    public string Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueOn { get; set; }
    public MembershipPeriodDto Period { get; set; }
    public MembershipChargeStatus Status { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal OutstandingAmount { get; set; }

    /// <summary>Zaduženje je trenutno stavka otvorenog checkouta.</summary>
    public bool InOpenCheckout { get; set; }

    public DateTimeOffset? WrittenOffAt { get; set; }
    public Guid? WrittenOffBy { get; set; }
    public string WriteOffReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class MembershipChargeWriteOffRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; }
}

public class MembershipPlanUpdateNotAppliedDto
{
    public Guid PlanVersionId { get; set; }
    public int PlanVersion { get; set; }

    /// <summary>Kod razloga (npr. MEMBERSHIP_OVERLAPPING_COVERAGE).</summary>
    public string Reason { get; set; }

    public DateTimeOffset SkippedAt { get; set; }
}

public class MembershipPauseDto
{
    public Guid Id { get; set; }
    public MembershipPauseKind Kind { get; set; }

    /// <summary>K1-8 — Client ili CompanyClosure (članstvo stoji zbog zatvorenih poslovnica).</summary>
    public MembershipPauseSource Source { get; set; }
    public DateOnly StartsOn { get; set; }

    /// <summary>Null = otvorena sustavna pauza (poslovnice još zatvorene; kraj se zna tek pri ponovnoj aktivaciji).</summary>
    public DateOnly? PlannedEndsOn { get; set; }
    public DateOnly? ActualEndsOn { get; set; }
    public string Reason { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public MembershipPauseCancellationReason? CancellationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public class MembershipPauseAllowanceDto
{
    public DateOnly WindowStartsOn { get; set; }
    public DateOnly WindowEndsOn { get; set; }

    /// <summary>Planovi "od datuma kupnje": preostali dani pauze u prozoru.</summary>
    public int? RemainingDays { get; set; }

    /// <summary>Kalendarski planovi: preostali preskočeni periodi u prozoru.</summary>
    public int? RemainingPeriods { get; set; }

    /// <summary>Preostali broj pauza; null = bez ograničenja.</summary>
    public int? RemainingPauses { get; set; }
}

public class ClientMembershipSellRequest
{
    [Required]
    public Guid? MembershipPlanId { get; set; }

    /// <summary>Q45 — od danas do najviše mjesec dana unaprijed; prazno = danas (zona organizacije).</summary>
    public DateOnly? StartsOn { get; set; }

    /// <summary>Poslovnica prodaje (bilo koja aktivna); izvještaji je razlikuju od poslovnica važenja.</summary>
    [Required]
    public Guid? SoldCompanyId { get; set; }

    /// <summary>Prijedlog korisnika provizije na prodaju (sprema se na članstvo kao jedini izvor; mijenja se na članstvu ili kroz
    /// stavku zaduženja prve prodaje u otvorenom checkoutu, 2F); prazno = zaposlenik koji prodaje (ako ga ima).</summary>
    public Guid? ProposedSaleCommissionEmployeeId { get; set; }
}

public class ClientMembershipCancelRequest
{
    [MaxLength(500)]
    public string Reason { get; set; }
}

public class ClientMembershipPauseRequest
{
    /// <summary>Danas ili kasnije. Kalendarski planovi: prvi dan perioda.</summary>
    [Required]
    public DateOnly? StartsOn { get; set; }

    /// <summary>Planovi "od datuma kupnje": zadnji dan pauze (uključivo).</summary>
    public DateOnly? EndsOn { get; set; }

    /// <summary>Kalendarski planovi: broj uzastopnih preskočenih perioda.</summary>
    public int? Periods { get; set; }

    [MaxLength(500)]
    public string Reason { get; set; }
}

/// <summary>Q47 — raniji povratak iz pauze. Kod kalendarskog plana bez Confirm odgovor vraća samo pregled (koji se period
/// otvara i uz koji iznos), bez promjene.</summary>
public class ClientMembershipPauseEndEarlyRequest
{
    public bool Confirm { get; set; }
}

public class ClientMembershipPauseEndEarlyResultDto
{
    public bool Applied { get; set; }

    /// <summary>Kalendarski plan: period koji se otvara od dana povratka i njegov puni iznos.</summary>
    public MembershipPeriodDto OpensPeriod { get; set; }
    public decimal? OpensPeriodAmount { get; set; }

    public ClientMembershipDto Membership { get; set; }
}

public class ClientMembershipPlanChangeRequest
{
    [Required]
    public Guid? MembershipPlanId { get; set; }
}

public class ClientMembershipEndRequest
{
    [Required]
    public DateOnly? EndsOn { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; }
}

public class ClientMembershipVoidRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; }
}

/// <summary>Pregled datuma od kad bi otkaz zatražen danas djelovao i koje ga je pravilo odredilo.</summary>
public class MembershipCancellationPreviewDto
{
    public DateOnly EffectiveOn { get; set; }
    public MembershipEndEffectiveReason Reason { get; set; }
}
