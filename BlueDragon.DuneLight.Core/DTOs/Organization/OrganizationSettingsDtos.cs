using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Shared;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Organization;

/// <summary>Poslovne postavke organizacije. P1 (D1): rok otkazivanja više nije postavka organizacije — prozor je dio
/// verzije politike otkazivanja (vidi /api/cancellation-policies).</summary>
public class OrganizationSettingsDto
{
    /// <summary>Phase D3B3A — kad se paket troši; trenutno uvijek OnCompletion (jedino podržano ponašanje).</summary>
    public Enums.PackageConsumptionTiming PackageConsumptionTiming { get; set; }

    /// <summary>IANA vremenska zona poslovnog kalendara organizacije (npr. "Europe/Zagreb").</summary>
    public string TimeZone { get; set; }

    /// <summary>P2 (Q14) — rok najave u danima: nepovoljna izmjena plana prenesena na postojeća članstva vrijedi od prve
    /// obnove koja je najmanje ovoliko dana nakon izmjene.</summary>
    public int MembershipChangeNoticeDays { get; set; }

    /// <summary>P2 (Q15) — grace period u danima od dospijeća zaduženja (default 7).</summary>
    public int MembershipGraceDays { get; set; }

    /// <summary>P2 (Q15) — ponašanje kod duga nakon grace perioda (default StopCovering).</summary>
    public Enums.MembershipDebtBehavior MembershipDebtBehavior { get; set; }

    /// <summary>P2 (2C) — automatski završi članstvo nakon N neplaćenih perioda; null = isključeno (default).</summary>
    public int? MembershipAutoEndAfterUnpaidPeriods { get; set; }

    /// <summary>P2 (Q4, 2D) — ponašanje kad je limit članarine iskorišten (default FallbackToNextSource).</summary>
    public Enums.MembershipLimitExceededBehavior MembershipLimitExceededBehavior { get; set; }

    /// <summary>K1-4 — je li odabir šifre razloga obavezan za otkaz klijenta / otkaz studija / izostanak (default false;
    /// obavezno vrijedi samo kad za događaj postoji aktivna šifra).</summary>
    public bool CancellationReasonRequiredClient { get; set; }
    public bool CancellationReasonRequiredBusiness { get; set; }
    public bool CancellationReasonRequiredNoShow { get; set; }

    /// <summary>Neblokirajuća upozorenja naredbe koja je vratila odgovor (npr. kratak rok najave izmjene plana).</summary>
    public List<WarningDto> Warnings { get; set; } = new();
}

/// <summary>K1-4 — obaveznost odabira šifre razloga po događaju.</summary>
public class OrganizationCancellationReasonRulesUpdateRequest
{
    public bool RequiredForClientCancellation { get; set; }
    public bool RequiredForBusinessCancellation { get; set; }
    public bool RequiredForNoShow { get; set; }
}

/// <summary>P2 (Q15, 2C) — pravila duga članarina.</summary>
public class OrganizationMembershipDebtUpdateRequest
{
    [Range(0, 365, ErrorMessage = "Grace period mora biti od 0 do 365 dana.")]
    public int GraceDays { get; set; }

    [Required]
    public Enums.MembershipDebtBehavior? DebtBehavior { get; set; }

    /// <summary>Null = isključeno; inače barem 1.</summary>
    public int? AutoEndAfterUnpaidPeriods { get; set; }
}

/// <summary>P2 (Q4, 2D) — pravila pokrića članarinom.</summary>
public class OrganizationMembershipCoverageUpdateRequest
{
    [Required]
    public Enums.MembershipLimitExceededBehavior? LimitExceededBehavior { get; set; }
}

/// <summary>P2 (2F, Vagaro, Q38, Q40) — postavke provizija samo na razini organizacije (GET/PUT /api/commissions/settings,
/// commissions.manage). Svi prekidači isključeni (default) = osnovica je cijena sesije: ručni iznos ako je upisan, inače cjenik;
/// sesija pokrivena paketom ima osnovicu plaćenu cijenu paketa po jedinici (T1-10). Provizija nikad nije veća od primljenog iznosa,
/// osim za sesiju pokrivenu članarinom uz isključen "oduzmi popuste članstva" (T1-10). Promjena ne mijenja zarađene provizije.</summary>
public class OrganizationCommissionSettingsDto
{
    /// <summary>Vagaro "Subtract Discounts" — osnovica je cijena nakon popusta (oznaka, promocija; ne članarinski). Ručni iznos
    /// nije popust.</summary>
    public bool DeductDiscounts { get; set; }

    /// <summary>Vagaro "Subtract Membership Discounts" — osnovica je cijena nakon popusta za članove; sesija koju članarina pokriva u
    /// cijelosti ima proviziju za odrađeno 0.</summary>
    public bool DeductMembershipDiscounts { get; set; }

    /// <summary>Q38 — Never (default) ili WhenFeePaid (provizija od plaćene P1 naknade).</summary>
    [Required]
    public Enums.CommissionLateCancellationMode? LateCancellation { get; set; }

    /// <summary>T1-10 — neblokirajuća upozorenja odgovora na spremanje (PUT), npr. COMMISSION_MEMBERSHIP_SESSIONS_AT_LIST_PRICE kad je
    /// "oduzmi popuste članstva" isključen. Prazno u GET-u; u zahtjevu se zanemaruje.</summary>
    public List<WarningDto> Warnings { get; set; } = new();
}

public class OrganizationMembershipChangeNoticeUpdateRequest
{
    [Range(0, 365, ErrorMessage = "Rok najave mora biti od 0 do 365 dana.")]
    public int Days { get; set; }
}

public class OrganizationTimeZoneUpdateRequest
{
    /// <summary>IANA id (npr. "Europe/Zagreb", "UTC"); nepoznat ili Windows id se odbija.</summary>
    [Required]
    [MaxLength(64)]
    public string TimeZone { get; set; }
}
