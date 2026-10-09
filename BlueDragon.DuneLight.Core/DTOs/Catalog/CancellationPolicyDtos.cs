using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

/// <summary>
/// P1 (ADR-0015, D1/D11) — imenovani profil politike otkazivanja s nepromjenjivim verzijama. Kreiranje verzije je objava
/// (nema Draft/EffectiveFrom); dodjele pokazuju na profil, a primjenjuje se uvijek NAJNOVIJA verzija u trenutku događaja.
/// </summary>
public class CancellationPolicyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Zadana politika organizacije (zadnja razina razrješavanja). Točno jedna po organizaciji.</summary>
    public bool IsOrganizationDefault { get; set; }

    public CancellationPolicyVersionDto? LatestVersion { get; set; }

    /// <summary>Puna povijest verzija (najnovija prva) — samo u detalju profila.</summary>
    public List<CancellationPolicyVersionDto> Versions { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class CancellationPolicyVersionDto
{
    public Guid Id { get; set; }
    public int Version { get; set; }

    /// <summary>D3: klijentsko otkazivanje je kasno kad je do početka segmenta preostalo STROGO manje od ovoliko minuta.</summary>
    public int CancellationWindowMinutes { get; set; }

    public CancellationPolicyEventRuleDto LateCancellation { get; set; }
    public CancellationPolicyEventRuleDto NoShow { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>D4/D6 — pravilo jednog događaja politike: naknada (None | Fixed iznos | Percentage konačne cijene, uvijek
/// ograničena konačnom cijenom) i, neovisno, smije li se umjesto naknade potrošiti jedinica brojenog paketa.</summary>
public class CancellationPolicyEventRuleDto
{
    [Required]
    public CancellationFeeType? FeeType { get; set; }

    /// <summary>Fixed: iznos u EUR (&gt;= 0, najviše 2 decimale). Percentage: 0–100 (najviše 2 decimale). None: mora biti prazno.</summary>
    public decimal? FeeValue { get; set; }

    [Required]
    public CancellationPackageAction? PackageAction { get; set; }

    /// <summary>P2 (Q31) — sesija pokrivena članarinom: ForfeitCredit (kredit propada umjesto naknade; plan bez kredita perioda
    /// naplaćuje naknadu) ili ReturnCreditChargeFee (claim se vraća, naplaćuje se naknada). Prazno = ForfeitCredit.</summary>
    public CancellationMembershipAction? MembershipAction { get; set; }
}

/// <summary>Pravila nove verzije (i prve verzije pri kreiranju profila).</summary>
public class CancellationPolicyRulesRequest
{
    [Range(0, int.MaxValue, ErrorMessage = "Prozor otkazivanja ne smije biti negativan.")]
    public int CancellationWindowMinutes { get; set; }

    [Required]
    public CancellationPolicyEventRuleDto LateCancellation { get; set; }

    [Required]
    public CancellationPolicyEventRuleDto NoShow { get; set; }
}

public class CancellationPolicyCreateRequest : CancellationPolicyRulesRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }
}

public class CancellationPolicyRenameRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }
}

public class CancellationPolicyDefaultRequest
{
    [Required]
    public Guid? CancellationPolicyId { get; set; }
}

/// <summary>D1 — dodjela profila po scopeu: Company+Service, samo Service ili samo Company (barem jedno je obavezno).
/// Jedna dodjela po scopeu; ponovna dodjela istog scopea zamjenjuje profil.</summary>
public class CancellationPolicyAssignmentDto
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public Guid? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public Guid CancellationPolicyId { get; set; }
    public string? CancellationPolicyName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class CancellationPolicyAssignmentRequest
{
    public Guid? CompanyId { get; set; }
    public Guid? ServiceId { get; set; }

    [Required]
    public Guid? CancellationPolicyId { get; set; }
}

/// <summary>Rezultat razrješavanja (pregled za UI): koji bi profil i verzija sada vrijedili za poslovnicu i uslugu.</summary>
public class CancellationPolicyResolutionDto
{
    public Guid CancellationPolicyId { get; set; }
    public string CancellationPolicyName { get; set; }

    /// <summary>CompanyAndService | Service | Company | OrganizationDefault.</summary>
    public string ResolvedFrom { get; set; }

    public CancellationPolicyVersionDto Version { get; set; }
}
