using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Commissions;

public class CommissionRuleDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }

    /// <summary>P2 (2F) — za odrađeno (usluga, opće pravilo) ili za prodaju (proizvod, paket, plan članarine).</summary>
    public CommissionRuleKind Kind { get; set; }

    /// <summary>AllServices = opće pravilo zaposlenika za sve individualne usluge (pravilo za uslugu ima prednost).</summary>
    public CommissionSubjectType SubjectType { get; set; }
    public Guid? ServiceId { get; set; }
    public string ServiceName { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; }
    public Guid? PackageId { get; set; }
    public string PackageName { get; set; }
    public Guid? MembershipPlanId { get; set; }
    public string MembershipPlanName { get; set; }

    /// <summary>P2 (2F) — od kad pravilo vrijedi (uključivo); null = bez početnog datuma.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>P2 (2F) — od kad je verzija deaktivirana (od tog datuma nema pravila, bez povratka na stariju verziju).</summary>
    public DateOnly? DeactivatedFrom { get; set; }

    /// <summary>Percentage | Fixed | None ("Bez provizije", samo pravilo usluge); null za opće pravilo (iznos je u razini).</summary>
    public CommissionCalculationType? CalculationType { get; set; }
    public decimal? Value { get; set; }

    /// <summary>P2 (2F) — razine općeg pravila; u P2 točno jedna bez praga (FromRevenue = 0). Prazno za ostala pravila.</summary>
    public List<CommissionRuleTierDto> Tiers { get; set; } = new();
    public bool IsActive { get; set; }

    /// <summary>Neblokirajuća upozorenja naredbe (npr. COMMISSION_SERVICE_RULE_GENERAL_APPLIES pri deaktivaciji pravila usluge).</summary>
    public List<Shared.WarningDto> Warnings { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>P2 (2F, Vagaro) — razina općeg pravila. Faza Payroll dodaje razine po prometu (FromRevenue &gt; 0).</summary>
public class CommissionRuleTierDto
{
    public decimal FromRevenue { get; set; }
    public CommissionCalculationType CalculationType { get; set; }
    public decimal Value { get; set; }
}

/// <summary>Nova verzija pravila = novi redak s novim EffectiveFrom (isti zaposlenik, vrsta i predmet); prethodna verzija ostaje
/// i vrijedi za sesije/prodaje prije tog datuma. Pravilo za predmet: CalculationType + Value. Opće pravilo (AllServices):
/// Tiers s točno jednom razinom bez praga.</summary>
public class CommissionRuleCreateRequest
{
    public Guid EmployeeId { get; set; }

    /// <summary>Opcionalno; izvodi se iz predmeta (Service/AllServices → Performance, ostalo → Sale), a ako je poslano mora se slagati.</summary>
    public CommissionRuleKind? Kind { get; set; }
    public CommissionSubjectType SubjectType { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PackageId { get; set; }
    public Guid? MembershipPlanId { get; set; }

    /// <summary>Od kad pravilo vrijedi; prazno = bez početnog datuma.</summary>
    public DateOnly? EffectiveFrom { get; set; }
    public CommissionCalculationType? CalculationType { get; set; }
    public decimal? Value { get; set; }
    public List<CommissionRuleTierDto> Tiers { get; set; } = new();
}

/// <summary>Ispravak OVE verzije pravila (izračun i vrijednost, odnosno razine općeg pravila). Zaposlenik/vrsta/predmet/datum
/// važenja se ne mijenjaju — promjena od datuma je nova verzija. Zarađene provizije se ne mijenjaju (snapshot).</summary>
public class CommissionRuleUpdateRequest
{
    public CommissionCalculationType? CalculationType { get; set; }
    public decimal? Value { get; set; }
    public List<CommissionRuleTierDto> Tiers { get; set; } = new();
}

public class CommissionRuleQuery
{
    public Guid? EmployeeId { get; set; }
    public CommissionRuleKind? Kind { get; set; }
    public CommissionSubjectType? SubjectType { get; set; }
    public bool? IsActive { get; set; }
}
