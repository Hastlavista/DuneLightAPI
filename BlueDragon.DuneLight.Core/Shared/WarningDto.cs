using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedinstven ugovor za sva neblokirajuća upozorenja koja API vraća uz uspješan odgovor (create/update i sl.).
/// Isti obrazac kao ErrorResponse/ErrorDetail — Code je stabilan ugovor prema frontendu (i18n se veže na njega),
/// Details nosi samo strukturirane podatke (npr. datume, imena, brojeve), nikad gotov tekst na hrvatskom.
/// </summary>
public class WarningDto
{
    public WarningDto()
    {
    }

    public WarningDto(string code, object details = null)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; set; }
    public object Details { get; set; }
}

/// <summary>WarningCodes.OutsideWorkingHours details za definiciju grupnog slota (Group Create/Update/AddSlot/
/// UpdateSlot) — nema konkretnog datuma, samo dan-u-tjednu + vrijeme.</summary>
public class WarningSlotDetails
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
}

/// <summary>WarningCodes.GroupCapacityExceeded details.</summary>
public class WarningGroupCapacityDetails
{
    public int Capacity { get; set; }
    public int ActiveMemberCount { get; set; }
}

/// <summary>WarningCodes.GroupAppointmentUnresolvedBookings details — Bookinzi koji su ostali Confirmed
/// (nerazrješeni) u trenutku zatvaranja grupnog termina. ClientId nije PII u ovom ugovoru (isti nivo detalja
/// kao ostali booking payloadi u ovom API-ju).</summary>
public class WarningUnresolvedBookingsDetails
{
    public List<Guid> ClientIds { get; set; } = new();
}

/// <summary>WarningCodes.RosterEntryOverlap details — postojeći zapis s kojim se preklapa.</summary>
public class WarningRosterOverlapDetails
{
    public string RosterTypeName { get; set; }
    public bool IsAbsence { get; set; }
    public DateTimeOffset DateFrom { get; set; }
    public DateTimeOffset? DateTo { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
}

/// <summary>Phase M1G — GROUP_COMMISSION_RULE_NOT_SUPPORTED: pravilo provizije zaposlenika za uslugu segmenta koje grupna
/// sesija ne evaluira (unos nije stvoren).</summary>
/// <summary>P2 (2F) — COMMISSION_SERVICE_RULE_GENERAL_APPLIES: "Od [EffectiveOn] za ovu uslugu vrijedi opće pravilo ([CalculationType]
/// [Value]). Za isključenje usluge odaberi Bez provizije."</summary>
public class WarningCommissionGeneralRuleAppliesDetails
{
    public Guid ServiceId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public Guid GeneralRuleId { get; set; }
    public string CalculationType { get; set; }
    public decimal Value { get; set; }
}

public class WarningGroupCommissionRuleDetails
{
    public Guid SegmentId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid CommissionRuleId { get; set; }
    public string CalculationType { get; set; }
}

/// <summary>P2 (Q49) — MEMBERSHIP_LIMIT_WITHOUT_EFFECT: limit plana i limit zbog kojeg nema učinka (krediti perioda ili limit
/// cijelog plana za isti prozor). ServiceId = null znači limit cijelog plana.</summary>
public class WarningMembershipLimitDetails
{
    public Guid? ServiceId { get; set; }
    public string Window { get; set; }
    public int MaxUses { get; set; }
    public Guid? ComparedWithServiceId { get; set; }
    public string ComparedWithWindow { get; set; }
    public int ComparedWithMaxUses { get; set; }
}

/// <summary>P2 (2C) — MEMBERSHIP_PLAN_MEMBERSHIPS_ENDING: broj članstava koja će završiti zbog deaktivacije (popis:
/// GET /api/membership-plans/{id}/memberships-ending).</summary>
public class WarningMembershipsEndingDetails
{
    public int Count { get; set; }
}

/// <summary>P2 — MEMBERSHIP_CHANGE_NOTICE_SHORT: postavljeni rok i preporučeni najkraći rok (14 dana).</summary>
public class WarningMembershipChangeNoticeDetails
{
    public int Days { get; set; }
    public int RecommendedMinimumDays { get; set; }
}

/// <summary>P2 — MEMBERSHIP_SCHEDULED_PAUSE_CANCELLED: poništene pauze koje još nisu počele.</summary>
public class WarningMembershipPausesDetails
{
    public List<Guid> PauseIds { get; set; } = new();
}

/// <summary>CHECKOUT_ITEM_PRICE_CHANGED — stavke sesije čiji iznos više ne odgovara trenutnom dugu sesije.</summary>
public class WarningCheckoutItemPriceDetails
{
    public List<WarningCheckoutItemPrice> Items { get; set; } = new();
}

public class WarningCheckoutItemPrice
{
    public Guid CheckoutItemId { get; set; }
    public Guid ParticipationId { get; set; }
    /// <summary>Iznos zapisan na stavci pri dodavanju.</summary>
    public decimal ItemAmount { get; set; }
    /// <summary>Trenutni dug sesije (cijena, ili 0 ako je sada pokrivena).</summary>
    public decimal CurrentDue { get; set; }
}

/// <summary>P2 (2E) — MEMBERSHIP_BENEFIT_WITHOUT_EFFECT: usluga i poslovnica u kojima fiksna cijena za člana nije niža od cjenika.</summary>
public class WarningMembershipBenefitDetails
{
    public List<WarningMembershipBenefitPrice> Prices { get; set; } = new();
}

public class WarningMembershipBenefitPrice
{
    public Guid ServiceId { get; set; }
    public Guid CompanyId { get; set; }
    public decimal ListPrice { get; set; }
    public decimal MemberPrice { get; set; }
}

/// <summary>P2 (2D) — pogođeni termini (npr. MEMBERSHIP_VOIDED_SESSIONS_UNCOVERED): sudjelovanje, termin i početak.</summary>
public class WarningMembershipSessionsDetails
{
    public List<WarningMembershipSession> Sessions { get; set; } = new();
}

public class WarningMembershipSession
{
    public Guid ParticipationId { get; set; }
    public Guid AppointmentId { get; set; }
    public DateTimeOffset PlannedStart { get; set; }
}

/// <summary>K1-5 — upisi liste čekanja (APPOINTMENT_RESTORED_WAITLIST_NOT_RESTORED).</summary>
public class WarningWaitlistEntriesDetails
{
    public List<WarningWaitlistEntry> Entries { get; set; } = new();
}

public class WarningWaitlistEntry
{
    public Guid WaitlistEntryId { get; set; }
    public Guid ClientId { get; set; }
    public Guid AppointmentSegmentId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>K1-9 — PARTICIPATION_NOT_COVERED / PARTICIPATION_PACKAGE_AVAILABLE: sesija, dug, prihvatljivi neodabrani paketi
/// (samo uz PACKAGE_AVAILABLE) i odluka pokrića članarinom kad članarina postoji, ali ne pokriva (null = bez članarine).</summary>
public class WarningParticipationCoverageDetails
{
    public Guid ParticipationId { get; set; }
    public Guid ClientId { get; set; }
    public decimal OutstandingAmount { get; set; }
    public List<WarningEligiblePackage> EligiblePackages { get; set; } = new();
    public BlueDragon.DuneLight.Core.Enums.MembershipCoverageStatus? MembershipCoverageStatus { get; set; }
    public BlueDragon.DuneLight.Core.Enums.MembershipCoverageReason? MembershipCoverageReason { get; set; }
}

public class WarningEligiblePackage
{
    public Guid ClientPackageId { get; set; }
    public string PackageName { get; set; }
}

/// <summary>P2 (Q29) — MEMBERSHIP_PLAN_NO_ACTIVE_COMPANY: odabrane poslovnice plana, nijedna aktivna.
/// Isti oblik za MEMBERSHIP_PLAN_NOT_VALID_AT_SALE_COMPANY (poslovnice u kojima plan vrijedi).</summary>
public class WarningMembershipPlanCompaniesDetails
{
    public List<Guid> CompanyIds { get; set; } = new();
}
