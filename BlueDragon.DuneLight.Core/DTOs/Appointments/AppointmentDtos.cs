using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.DTOs.Appointments;

/// <summary>Jedan Klijent na jednom Appointmentu — vidi Booking.cs za domensku napomenu. Zamjenjuje
/// nekadašnje AppointmentClientDto (individualni) i ClientAttendanceDto (grupni). Nosi klijent-specifičnu
/// komercijalnu evidenciju (Amount/SuggestedAmount) — od 2026-09-15 više NIJE zajednička za cijeli termin.
/// PaidAmount/OutstandingAmount/IsPaid su IZVEDENI iz Payment ledgera (vidi ParticipationSettlement) —
/// od 2026-09-16 Booking više ne nosi persistirani PaymentMethod/IsPaid (vidi Payment.cs).</summary>
public class BookingDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; }

    /// <summary>Phase M0: IZVEDENI sažetak statusa sudjelovanja (sva ista → taj status, inače Mixed) — read-model, nije
    /// ciljni status prijelaza. Za jedno sudjelovanje identičan statusu tog sudjelovanja.</summary>
    public BookingStatusSummary Status { get; set; }

    /// <summary>Phase M0: komercijalni sažetak — zbroj cijena sudjelovanja (FinalPrice); SuggestedAmount je zbroj
    /// predloženih, IsAmountManuallyOverridden = barem jedno sudjelovanje ručno.</summary>
    public decimal Amount { get; set; }
    public decimal SuggestedAmount { get; set; }
    public bool IsAmountManuallyOverridden { get; set; }

    /// <summary>Zbroj aktivnih (ne-voidanih) Paymenta ovog Bookinga — vidi ParticipationSettlement.</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>P1: Σ POZITIVNOG duga sudjelovanja (preplata jednog sudjelovanja ne umanjuje dug drugog; nikad negativno) —
    /// vidi ParticipationSettlement. Otkazano/izostalo sudjelovanje duguje samo aktivnu naknadu politike. Sirovi
    /// (neklampani) dug je na Participations[].OutstandingAmount, preplata u SurplusAmount.</summary>
    public decimal OutstandingAmount { get; set; }

    /// <summary>P1 (D7): Σ max(PaidAmount − MonetaryDue, 0) sudjelovanja — informativno, NIJE kredit klijenta.</summary>
    public decimal SurplusAmount { get; set; }

    /// <summary>Izvedeno: svako sudjelovanje ima OutstandingAmount &lt;= 0 (uklj. paket-pokriće i besplatan termin).</summary>
    public bool IsPaid { get; set; }

    public Guid? ClientPackageId { get; set; }
    public AttendanceCoverageType? CoverageType { get; set; }
    public bool PackageCoverageApplied { get; set; }
    public bool PackageCoverageReturned { get; set; }

    /// <summary>Puna povijest Paymenta ovog Bookinga (uklj. voidane), najnoviji prvi — vidi PaymentDto.</summary>
    public List<PaymentDto> Payments { get; set; } = new();

    public string Note { get; set; }
    public string CancellationReason { get; set; }

    /// <summary>P1: klasifikacija klijentskog otkazivanja (prozor politike) kad je ista za sva sudjelovanja; null za
    /// Business/System otkazivanje i za ne-otkazana sudjelovanja. Točne vrijednosti su na Participations.</summary>
    public bool? IsLateCancellation { get; set; }

    /// <summary>Phase M0: sudjelovanja Bookinga (izvršne/komercijalne jedinice) — adresa za participation-native naredbe
    /// (ParticipationId). Polja Bookinga iznad su IZVEDENI sažeci ovih redaka.</summary>
    public List<BookingParticipationDto> Participations { get; set; } = new();
}

/// <summary>Phase M0: jedno sudjelovanje Bookinga u segmentu termina — životni ciklus, cijena i namirenje.</summary>
public class BookingParticipationDto
{
    public Guid Id { get; set; }
    public Guid AppointmentSegmentId { get; set; }
    public BookingStatus Status { get; set; }
    public int StatusVersion { get; set; }
    public decimal Amount { get; set; }
    public decimal SuggestedAmount { get; set; }
    public bool IsAmountManuallyOverridden { get; set; }
    public decimal PaidAmount { get; set; }

    /// <summary>P1 (D5): dug izveden iz statusa — Confirmed/Completed: cijena usluge (0 uz paketno pokriće); Cancelled/NoShow:
    /// naknada AKTIVNE posljedice politike (0 kad je kazna podmirena jedinicom paketa ili posljedice nema).</summary>
    public decimal MonetaryDue { get; set; }

    /// <summary>P1: MonetaryDue − PaidAmount, ne klampa se (negativno = preplata).</summary>
    public decimal OutstandingAmount { get; set; }

    /// <summary>P1 (D7): max(PaidAmount − MonetaryDue, 0) — informativno, NIJE kredit klijenta.</summary>
    public decimal SurplusAmount { get; set; }
    public bool IsPaid { get; set; }

    /// <summary>Usluga je pokrivena paketom (aktivna potrošnja izvršenja usluge). Jedinica potrošena kao kazna politike je
    /// na PolicyConsequence.</summary>
    public bool PackageCovered { get; set; }
    public Guid? ClientPackageId { get; set; }

    /// <summary>P1 (D2/D3) — trenutni metapodaci otkazivanja (samo kad je Status Cancelled).</summary>
    public CancellationInitiator? CancellationInitiator { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string CancellationReason { get; set; }

    /// <summary>Samo za klijentsko otkazivanje: kasno (true) / na vrijeme (false); null za Business/System.</summary>
    public bool? IsLateCancellation { get; set; }
    public Guid? CancellationPolicyId { get; set; }
    public int? CancellationPolicyVersion { get; set; }
    public int? AppliedCancellationWindowMinutes { get; set; }

    /// <summary>P1 (D3) — trenutni metapodaci izostanka (samo kad je Status NoShow).</summary>
    public DateTimeOffset? NoShowAt { get; set; }
    public Guid? NoShowBy { get; set; }
    public string NoShowReason { get; set; }

    /// <summary>P1 (D5) — najnovija posljedica politike sudjelovanja (Active, Waived ili Reversed), null ako je nikad nije
    /// bilo. Puna povijest je u ledgeru posljedica.</summary>
    public ParticipationPolicyConsequenceDto PolicyConsequence { get; set; }

    /// <summary>Phase M1G — POVIJESNI cjenovni snapshot sudjelovanja: razriješena osnovna cijena i razina cjenika, te izvor
    /// cijene (način + zaposlenik) korišten pri razrješavanju. Null kad cijena nije razriješena iz cjenika.</summary>
    public decimal? BaseAmount { get; set; }
    public Catalog.PriceSource? BaseAmountSource { get; set; }
    public SegmentPricingMode? PricingMode { get; set; }
    public Guid? PricingEmployeeId { get; set; }

    /// <summary>P2 (2D) — odluka o pokriću članarinom s razlogom (prikaz recepciji). Null = klijent nema članarinu relevantnu
    /// za termin (ponašanje kao prije P2).</summary>
    public ParticipationMembershipCoverageDto MembershipCoverage { get; set; }

    /// <summary>P2 (2E, Q1/§10.4) — primijenjena prilagodba cijene (null = cijena je cjenik ili ručna) i evaluacija svih kandidata
    /// u trenutku cijene (zašto je pogodnost primijenjena ili nije). Null kad prilagodbe nikad nisu evaluirane (bez članarine).</summary>
    public ParticipationPriceAdjustmentDto PriceAdjustment { get; set; }
}

/// <summary>P2 (2E) — prilagodba cijene sesije: tip i izvor primijenjene (null = nijedna), osnovna cijena (cjenik), iznos
/// prilagodbe (predložena − osnovna) i svi kandidati s ishodom.</summary>
public class ParticipationPriceAdjustmentDto
{
    public PriceAdjustmentType? AppliedType { get; set; }
    public Guid? AppliedSourceId { get; set; }
    public decimal? BaseAmount { get; set; }
    public decimal? AdjustmentAmount { get; set; }
    public List<PriceAdjustmentCandidateDto> Candidates { get; set; } = new();
}

public class PriceAdjustmentCandidateDto
{
    public PriceAdjustmentType Type { get; set; }
    public Guid SourceId { get; set; }
    public PriceAdjustmentOutcome Outcome { get; set; }
    /// <summary>Uz NotApplicable.</summary>
    public PriceAdjustmentReason? Reason { get; set; }
    /// <summary>Cijena koju bi kandidat dao (null uz NotApplicable).</summary>
    public decimal? ResultingPrice { get; set; }
    /// <summary>Primijenjeno pravilo (opseg, tip, vrijednost) — snapshot.</summary>
    public MembershipPriceBenefitScope? RuleScope { get; set; }
    public MembershipPriceBenefitType? RuleType { get; set; }
    public decimal? RuleValue { get; set; }
}

/// <summary>P2 (2D) — objašnjiva odluka o pokriću sudjelovanja članarinom: stanje, razlog, događaj koji ju je zadnji
/// promijenio i, uz LimitReached, koji limit je pun (prozor, usluga ili cijeli plan, maksimum, iskorišteno).</summary>
public class ParticipationMembershipCoverageDto
{
    public Guid? ClientMembershipId { get; set; }
    public MembershipCoverageStatus Status { get; set; }
    public MembershipCoverageReason Reason { get; set; }
    public MembershipCoverageEvent ChangedByEvent { get; set; }
    public MembershipUsageWindow? LimitWindow { get; set; }
    /// <summary>Null uz LimitWindow = limit plana (sve usluge zajedno).</summary>
    public Guid? LimitServiceId { get; set; }
    public int? LimitMaxUses { get; set; }
    public int? LimitUsed { get; set; }
    /// <summary>Uz BeyondHorizon: početak perioda u kojem se pokriće evaluira.</summary>
    public DateOnly? ExpectedPeriodStartsOn { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }

    /// <summary>P2 (2E) — zadnja AUTOMATSKA promjena cijene zbog pokrića/pogodnosti (stara i nova cijena, događaj, vrijeme).</summary>
    public decimal? LastPriceChangeOldAmount { get; set; }
    public decimal? LastPriceChangeNewAmount { get; set; }
    public MembershipCoverageEvent? LastPriceChangeEvent { get; set; }
    public DateTimeOffset? LastPriceChangeAt { get; set; }
    /// <summary>Cijena se NE mijenja automatski: ručni iznos ili već (djelomično) plaćeno.</summary>
    public PriceProtectionReason? PriceProtectedReason { get; set; }
    /// <summary>Promjena cijene čeka (sudjelovanje je bilo zaključano drugom naredbom); primjenjuje se pri sljedećoj obradi.</summary>
    public bool PriceStale { get; set; }
}

/// <summary>P1 (D4/D5) — nepromjenjiv zapis posljedice politike (snapshot pravila i izračuna u trenutku događaja).</summary>
public class ParticipationPolicyConsequenceDto
{
    public Guid Id { get; set; }
    public int SourceVersion { get; set; }
    public PolicyConsequenceEvent Event { get; set; }
    public CancellationFeeType FeeType { get; set; }
    public decimal? ConfiguredFeeValue { get; set; }
    public decimal FeeBaseAmount { get; set; }
    public decimal CalculatedFeeAmount { get; set; }
    public bool WasFeeCapped { get; set; }
    public Guid PolicyId { get; set; }
    public int PolicyVersion { get; set; }
    public CancellationPackageAction PackageAction { get; set; }
    public bool PackageUnitConsumed { get; set; }
    public Guid? ClientPackageId { get; set; }
    public PolicyConsequenceStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public Guid? ReversedBy { get; set; }
    public string ReversalReason { get; set; }
    public DateTimeOffset? WaivedAt { get; set; }
    public Guid? WaivedBy { get; set; }
    public string WaiverReason { get; set; }

    /// <summary>P2 (Q31) — sesija je u trenutku događaja bila pokrivena članarinom: primijenjena akcija politike, članstvo i
    /// je li kredit perioda propao umjesto naknade (dug 0).</summary>
    public CancellationMembershipAction? MembershipAction { get; set; }
    public Guid? ClientMembershipId { get; set; }
    public bool MembershipCreditForfeited { get; set; }
}

/// <summary>P1 (D6) — eksplicitni paket za kaznu politike jednog sudjelovanja unutar Booking-wide / appointment-wide naredbe.</summary>
public class ParticipationPackageSelection
{
    [Required]
    public Guid? ParticipationId { get; set; }

    [Required]
    public Guid? ClientPackageId { get; set; }
}

/// <summary>P1 (D12) — korekcija natrag na Confirmed; razlog je obavezan kad korekcija poništava aktivnu posljedicu politike
/// sa stvarnim učinkom (uz appointments.policy.override).</summary>
public class ParticipationConfirmRequest
{
    [MaxLength(500)]
    public string CorrectionReason { get; set; }
}

/// <summary>P1 (D10) — naknadni otpis aktivne posljedice politike (cijela posljedica, razlog obavezan).</summary>
public class PolicyConsequenceWaiveRequest
{
    [Required]
    [MaxLength(500)]
    public string WaiverReason { get; set; }
}

public class AppointmentDto
{
    public Guid Id { get; set; }
    public AppointmentForm Form { get; set; }

    /// <summary>Phase M1B: IZVEDENI raspon termina — MIN(segment.PlannedStart) .. MAX(segment.PlannedEnd).</summary>
    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }

    /// <summary>Phase M1B: segmenti termina — JEDINI izvor izvršnih podataka (usluga, vrijeme, zaposlenici, prostorija,
    /// resursi).</summary>
    public List<AppointmentSegmentDto> Segments { get; set; } = new();

    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public AppointmentStatus Status { get; set; }
    public string Note { get; set; }

    /// <summary>Razlog otkazivanja termina (eksplicitno otkazivanje) ili bulk no-showa — metapodatak termina.</summary>
    public string CancellationReason { get; set; }

    /// <summary>Phase M1A.1: kada je termin EKSPLICITNO otkazan (trenutno; null ako nije ili je korekcija vratila rad).</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Phase M1A.1: kada je grupna sesija zatvorena (close-out) — poslovna činjenica, ne status termina.</summary>
    public DateTimeOffset? ClosedOutAt { get; set; }

    public Guid? GroupId { get; set; }

    /// <summary>Popunjeno samo za Form=Group, kad je grupa učitana (npr. GetByClient).</summary>
    public string GroupName { get; set; }

    public Guid? RecurrenceGroupId { get; set; }

    /// <summary>Amount/PaidAmount/OutstandingAmount/IsPaid žive po Bookingu (vidi BookingDto), ne ovdje —
    /// omogućuje mješovito plaćanje po klijentu na istom terminu. Frontend zbraja/derivira agregate ako treba
    /// (npr. "sve plaćeno") — ovaj DTO namjerno ne nosi duplicirane Appointment-razina agregate.</summary>
    public List<BookingDto> Bookings { get; set; } = new();

    /// <summary>Popunjeno samo kao odgovor na create/update (preklapanje trenera/klijenata) — inače prazno.</summary>
    public List<WarningDto> Warnings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Jedan red povijesti termina za KONKRETNOG klijenta (GetByClient) — namjerno ne nosi
/// AppointmentDto.Bookings (druge klijente na istom terminu). Klijent-povijest pristup != roster pristup:
/// vidi domensku napomenu na AppointmentService.ToClientHistoryDto. Amount/PaidAmount/OutstandingAmount/
/// IsPaid dolaze s OVOG klijenta vlastitog Bookinga, ne dijele se s ostalim klijentima na istom terminu.</summary>
public class ClientAppointmentHistoryDto
{
    public Guid Id { get; set; }
    public AppointmentForm Form { get; set; }

    /// <summary>Phase M1B: izvedeni raspon termina i njegovi segmenti.</summary>
    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }
    public List<AppointmentSegmentDto> Segments { get; set; } = new();

    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }

    /// <summary>Status termina (occurrence) — Scheduled/Cancelled/Closed, izveden iz sudjelovanja (vidi AppointmentStatus).</summary>
    public AppointmentStatus Status { get; set; }

    /// <summary>Popunjeno samo za Form=Group.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Popunjeno samo za Form=Group — naziv grupe, ne otkriva identitet drugih članova.</summary>
    public string GroupName { get; set; }

    /// <summary>Booking-razina (vlastiti booking ovog klijenta) — vidi domensku napomenu na klasi.</summary>
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal SurplusAmount { get; set; }
    public bool IsPaid { get; set; }

    /// <summary>Booking ID zahtjevanog klijenta — NE Appointment.Bookings (to bi otkrilo druge klijente).</summary>
    public Guid BookingId { get; set; }

    /// <summary>Phase M0: izvedeni sažetak statusa sudjelovanja vlastitog Bookinga (vidi BookingStatusSummary).</summary>
    public BookingStatusSummary BookingStatus { get; set; }
    public Guid? ClientPackageId { get; set; }
    public AttendanceCoverageType? CoverageType { get; set; }
    public bool PackageCoverageApplied { get; set; }
    public bool PackageCoverageReturned { get; set; }
    public string BookingNote { get; set; }

    /// <summary>Razlog otkazivanja kad ga sva sudjelovanja dijele (samo otkazana sudjelovanja ga imaju).</summary>
    public string BookingCancellationReason { get; set; }
}

/// <summary>Lagani DTO za ćelije rasporeda — puni detalj dolazi preko GetById na klik.</summary>
public class AppointmentScheduleCellDto
{
    public Guid Id { get; set; }

    /// <summary>Phase M1B: izvedeni raspon termina (MIN/MAX segmenata) i blokovi rasporeda po segmentu.</summary>
    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }
    public List<AppointmentSegmentDto> Segments { get; set; } = new();

    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public List<string> ClientNames { get; set; } = new();

    /// <summary>ID-jevi klijenata, indeksno poravnati s ClientNames (isti redoslijed, isti broj elemenata). Prazan za grupne termine.</summary>
    public List<Guid> ClientIds { get; set; } = new();
    public AppointmentStatus Status { get; set; }
    public bool IsCancelled { get; set; }

    public AppointmentForm Form { get; set; }

    /// <summary>Popunjeno samo za Form=Group.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Naziv grupe — za prikaz u ćeliji umjesto imena klijenta (ClientNames je prazan za grupne termine).</summary>
    public string GroupName { get; set; }

    /// <summary>Broj članova s Attended=true. Null za individualne termine.</summary>
    public int? AttendanceCount { get; set; }

    /// <summary>Broj aktivnih članova grupe (roster kapacitet za prikaz, npr. "6/8"). Null za individualne termine.</summary>
    public int? ExpectedCount { get; set; }

    /// <summary>Popunjeno samo kao odgovor na GroupService.GenerateAppointments (izvan radnog vremena/odsutnost
    /// trenera na ovoj konkretnoj instanci) — inače prazno. Isto polje/semantika kao AppointmentDto.Warnings.</summary>
    public List<WarningDto> Warnings { get; set; } = new();
}

public class AppointmentScheduleQuery
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }

    public Guid? CompanyId { get; set; }
    public Guid? RoomId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? ServiceId { get; set; }
    public ServiceExecutionMode? ExecutionMode { get; set; }
    public AppointmentStatus? Status { get; set; }
}

/// <summary>Phase M1H — JEDAN klijent "odmah odrađenog" termina (<see cref="AppointmentCompleteNowRequest"/>): klijent dobiva
/// jedan Booking i jedno sudjelovanje na segmentu naredbe, opcionalni ručni konačni iznos i vlastito namirenje (paket ILI
/// novac) — mješovito namirenje po klijentu (npr. duo: jedan paket, drugi kartica).
///
/// Paket-pokriće (ClientPackageId popunjen) i novčano plaćanje (PaymentMethod popunjen) se međusobno
/// isključuju — kad je ClientPackageId popunjen, PaymentMethod se IGNORIRA (paket namiruje obvezu bez
/// stvaranja Payment retka, vidi Payment.cs/spec section 3/40). Kad ni jedno ni drugo nije popunjeno (i
/// Amount &gt; 0), booking ostaje evidentiran ali financijski neplaćen (naplata naknadno).</summary>
public class AppointmentCompletedClientRequest
{
    [Required]
    public Guid ClientId { get; set; }

    /// <summary>Novčani način plaćanja — IGNORIRA se ako je ClientPackageId popunjen (vidi klasnu napomenu).
    /// Null = booking se ne naplaćuje sada (naknadna naplata preko Payment API-ja).</summary>
    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>Ručni override predložene cijene za OVOG klijenta. Null = koristi se predložena cijena iz cjenika.</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }

    /// <summary>Paket koji pokriva ovaj booking — kad je popunjen, booking je entitlement-namiren i PaymentMethod
    /// se ignorira (vidi klasnu napomenu).</summary>
    public Guid? ClientPackageId { get; set; }

    /// <summary>Zadano true — relevantno samo kad je PaymentMethod popunjen i ClientPackageId nije: true stvara
    /// stvaran Payment za puni Amount odmah (vidi spec section 13), false znači "evidentirano, plaćanje
    /// naknadno" (booking ostaje financijski outstanding). Bez učinka kad je PaymentMethod null ili je booking
    /// paket-pokriven (paket uvijek namiruje obvezu, vidi spec section 32).</summary>
    public bool IsPaid { get; set; } = true;
}

/// <summary>
/// Phase M1H — "upiši odrađeno" (POS): ATOMIČNO stvara termin s JEDNIM eksplicitnim segmentom (izvedena usluga, sada ili u
/// prošlosti), po jedan Booking i sudjelovanje za svakog klijenta, odrađuje ta sudjelovanja kroz isti životni ciklus
/// sudjelovanja (cijena, paket/novac, provizija po zaposleniku, audit) i sve se commita zajedno ili ništa. Jedan segment je
/// DOSEG ove poslovne naredbe (jedna odrađena usluga), ne pretpostavka modela; segment koristi puni ciljni model osoblja
/// (EmployeeIds + izvor cijene).
/// </summary>
public class AppointmentCompleteNowRequest
{
    [Required]
    public Guid CompanyId { get; set; }

    public string Note { get; set; }

    /// <summary>Vidi AppointmentCreateRequest.OverrideAvailability (samo uz appointments.write.all; provjera radne snage
    /// vrijedi samo za budući početak).</summary>
    public bool OverrideAvailability { get; set; }

    [Required]
    public AppointmentSegmentDefinitionRequest Segment { get; set; }

    /// <summary>Klijenti izvedene usluge (barem jedan, svaki jednom) s vlastitim iznosom i namirenjem.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "Potreban je barem jedan klijent.")]
    public List<AppointmentCompletedClientRequest> Clients { get; set; } = new();
}

/// <summary>Phase M1H — napomena termina (metapodatak agregata; ne dira segmente, cijene, raspored ni životni ciklus).</summary>
public class AppointmentNoteChangeRequest
{
    public string Note { get; set; }
}

/// <summary>Phase M1H — ručni konačni iznos JEDNOG sudjelovanja (samo Confirmed). Null = povratak na predloženu cijenu
/// (SuggestedAmount; snapshot razrješavanja cjenika se ne mijenja).</summary>
public class ParticipationPriceChangeRequest
{
    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }
}

/// <summary>P1 (D2) — otkazivanje CIJELOG termina je uvijek poslovno (Business): initiator je obavezan i mora biti
/// Business (Client se odbija), razlog je obavezan. Politika se ne evaluira.</summary>
public class AppointmentCancelRequest
{
    [Required]
    public CancellationInitiator? CancellationInitiator { get; set; }

    [MaxLength(500)]
    public string CancellationReason { get; set; }
}

/// <summary>P1 (D3/D10) — izostanak (NoShow): jedno sudjelovanje ili svi aktivni na terminu. NoShow nema initiator; razlog je
/// opcionalan. ClientPackageId (samo za jedno sudjelovanje) služi odabiru paketa kad politika troši jedinicu.
/// WaivePolicyConsequence + WaiverReason otpisuju posljedicu odmah (zahtijeva appointments.policy.override).
/// CorrectionReason je obavezan kad korekcija poništava aktivnu posljedicu sa stvarnim učinkom.</summary>
public class NoShowRequest
{
    [MaxLength(500)]
    public string NoShowReason { get; set; }

    /// <summary>Samo za izostanak JEDNOG sudjelovanja. Izostanak cijelog termina bira paket po sudjelovanju
    /// (<see cref="PackageSelections"/>); ClientPackageId se tamo odbija.</summary>
    public Guid? ClientPackageId { get; set; }

    /// <summary>P1 (D6) — izostanak CIJELOG termina: eksplicitni paket za kaznu po sudjelovanju (samo za sudjelovanja s više
    /// prihvatljivih brojenih paketa ili kad se želi određeni paket). Svako sudjelovanje mora biti aktivno u tom terminu.</summary>
    public List<ParticipationPackageSelection> PackageSelections { get; set; } = new();

    public bool WaivePolicyConsequence { get; set; }

    [MaxLength(500)]
    public string WaiverReason { get; set; }

    [MaxLength(500)]
    public string CorrectionReason { get; set; }
}

public class RecurringAppointmentCreateRequest
{
    /// <summary>Daily = svaki kalendarski dan uključivo vikend; Weekly = +7 dana (postojeće ponašanje).</summary>
    [Required]
    public RecurrenceType RecurrenceType { get; set; }

    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    /// <summary>Opcionalno — mora pripadati istoj CompanyId.</summary>
    public Guid? RoomId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Termin mora imati barem jednog klijenta.")]
    public List<Guid> ClientIds { get; set; } = new();

    /// <summary>Datum/vrijeme prvog termina — dan u tjednu i vrijeme se ponavljaju iz ovoga.</summary>
    [Required]
    public DateTimeOffset FirstOccurrenceStartsAt { get; set; }

    [Required]
    public DateTimeOffset EndDate { get; set; }

    public string Note { get; set; }

    /// <summary>Vidi AppointmentCreateRequest.OverrideAvailability — primjenjuje se po occurrenceu.</summary>
    public bool OverrideAvailability { get; set; }
}

/// <summary>Jedan sudarajući datum u nizu — dio { conflicts: [...] } priloga uz 409 RECURRING_CONFLICT.</summary>
public class RecurringConflictDetail
{
    public DateTimeOffset Date { get; set; }

    /// <summary>ErrorCodes.RecurringConflictReasonAppointment ili ErrorCodes.RecurringConflictReasonRosterAbsence.</summary>
    public string Reason { get; set; }
}

public class AvailableSlotsQuery
{
    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    public DateTimeOffset Date { get; set; }

    /// <summary>Ako je postavljeno, vraća se samo za tog zaposlenika (npr. Member zaključan na sebe u formi).</summary>
    public Guid? EmployeeId { get; set; }
}

public class AvailableSlotDto
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}

/// <summary>Slobodni slotovi jednog zaposlenika za traženi dan — dio odgovora GET .../available-slots.
/// Uključen i s praznim Slots ako zaposlenik radi/smije uslugu ali nema ništa slobodno tog dana.</summary>
public class EmployeeAvailableSlotsDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public string ColorHex { get; set; }
    public List<AvailableSlotDto> Slots { get; set; } = new();
}

/// <summary>Agregirane brojke dolazaka jednog klijenta (individualni + grupni termini zajedno) — interni
/// rezultat AppointmentHandler.GetStatsForClient, koristi ga IClientHistoryService za Povijest klijenta.</summary>
public class ClientAppointmentStatsDto
{
    public int CompletedVisitsCount { get; set; }
    public int NoShowCount { get; set; }
    public int CancelledCount { get; set; }
    public DateTimeOffset? LastVisitAt { get; set; }
    public DateTimeOffset? NextVisitAt { get; set; }
}

/// <summary>Ad-hoc dodavanje Bookinga na postojeći termin bez pune izmjene (npr. gost/zamjena na grupnom
/// terminu izvan popisa članova) — vidi IBookingService.AddGroupGuest.</summary>
public class BookingCreateRequest
{
    [Required]
    public Guid ClientId { get; set; }

    /// <summary>Segment grupnog occurrencea u koji se dodaje gost (postojeći Booking klijenta se ponovno koristi). Phase M1H:
    /// uvijek obavezan — segment se nikad ne zaključuje.</summary>
    [Required]
    public Guid? SegmentId { get; set; }

    /// <summary>Phase M1F — eksplicitno prekoračenje mekog kapaciteta segmenta grupe; zahtijeva groups.capacity.override.</summary>
    public bool OverrideCapacity { get; set; }
}

/// <summary>Otkazivanje JEDNOG Bookinga (svih njegovih aktivnih sudjelovanja; npr. jedan od dvoje na duo terminu) ili
/// jednog sudjelovanja — vidi AppointmentsController.CancelBooking i ParticipationsController.
/// P1 (D2/D3/D10): initiator je obavezan (Client | Business; System se nikad ne prihvaća). Client: politika se evaluira
/// (kasno/na vrijeme), razlog opcionalan, otkazivanje mora biti prije početka segmenta. Business: zahtijeva
/// appointments.write.all i razlog, bez posljedice. ClientPackageId služi samo odabiru paketa kad kasno otkazivanje troši
/// jedinicu; WaivePolicyConsequence + WaiverReason otpisuju posljedicu odmah (appointments.policy.override).</summary>
public class BookingCancelRequest
{
    [Required]
    public CancellationInitiator? CancellationInitiator { get; set; }

    [MaxLength(500)]
    public string CancellationReason { get; set; }

    /// <summary>Samo za otkazivanje JEDNOG sudjelovanja. Booking-wide otkazivanje bira paket po sudjelovanju
    /// (<see cref="PackageSelections"/>); ClientPackageId se tamo odbija.</summary>
    public Guid? ClientPackageId { get; set; }

    /// <summary>P1 (D6) — Booking-wide otkazivanje: eksplicitni paket za kaznu po sudjelovanju (segmenti mogu imati
    /// različite usluge, pa jedan paket ne vrijedi nužno za sve). Svako sudjelovanje mora biti aktivno u tom Bookingu.</summary>
    public List<ParticipationPackageSelection> PackageSelections { get; set; } = new();

    public bool WaivePolicyConsequence { get; set; }

    [MaxLength(500)]
    public string WaiverReason { get; set; }

    [MaxLength(500)]
    public string CorrectionReason { get; set; }
}

/// <summary>Prijelaz statusa jednog sudjelovanja — P1 (D12): jedna matrica za Individual i Group; svaki prijelaz u
/// drugi status je dopušten uz guardove ciljnog događaja, isti status je pravi no-op. Vidi IBookingService.SetParticipationStatus.</summary>
public class BookingSetStatusRequest
{
    [Required]
    public BookingStatus Status { get; set; }

    /// <summary>Ručni odabir paketa: Completed (grupni check-in s više prihvatljivih paketa / individualno pokriće) ili
    /// P1 kazna politike (Cancelled/NoShow) kad politika troši jedinicu, a klijent ima više brojenih paketa. Inače se
    /// ignorira.</summary>
    public Guid? ClientPackageId { get; set; }

    /// <summary>Relevantno samo za Form=Group prijelaz u Completed BEZ paketa (CoverageType=SinglePaid) —
    /// ako je popunjen i IsPaid=true, stvara stvaran Payment za puni Amount ovog check-ina (vidi
    /// IPaymentLedgerService.RecordPayment). Ako je izostavljen, booking ostaje evidentiran (Attended) ali
    /// financijski neplaćen — isto ponašanje kao prije uvođenja naplate na grupne bookinge. IGNORIRA se kad
    /// je pokriće paketom (ClientPackageId popunjen) — paket namiruje obvezu bez stvaranja Paymenta.</summary>
    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>Ručni override predložene cijene za ovaj check-in — vidi PaymentMethod. Null = koristi
    /// predloženu cijenu iz cjenika.</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }

    /// <summary>Zadano true — vidi AppointmentCompletedClientRequest.IsPaid za istu semantiku.</summary>
    public bool IsPaid { get; set; } = true;

    public string Note { get; set; }

    /// <summary>Grupni occurrence, (termin, klijent, segment) adresiranje (prisutnost/check-in gosta): segment prijelaza —
    /// Phase M1H: obavezan na tom putu. Participation-native naredba (ParticipationId) ga ne koristi.</summary>
    public Guid? SegmentId { get; set; }

    /// <summary>Phase M1F — eksplicitno prekoračenje mekog kapaciteta segmenta grupe pri novom/ponovno aktiviranom
    /// Confirmed mjestu; zahtijeva groups.capacity.override.</summary>
    public bool OverrideCapacity { get; set; }

    /// <summary>P1 (D2) — obavezan za Status=Cancelled (Client | Business; System se nikad ne prihvaća), inače se ignorira.</summary>
    public CancellationInitiator? CancellationInitiator { get; set; }

    /// <summary>Razlog otkazivanja (samo Status=Cancelled): opcionalan za Client, obavezan za Business.</summary>
    [MaxLength(500)]
    public string CancellationReason { get; set; }

    /// <summary>P1 (D3) — opcionalan razlog izostanka (samo Status=NoShow).</summary>
    [MaxLength(500)]
    public string NoShowReason { get; set; }

    /// <summary>P1 (D10) — otpis posljedice politike u trenutku događaja (Cancelled/NoShow); traži WaiverReason i
    /// appointments.policy.override.</summary>
    public bool WaivePolicyConsequence { get; set; }

    [MaxLength(500)]
    public string WaiverReason { get; set; }

    /// <summary>P1 (D12) — obavezan kad prijelaz poništava aktivnu posljedicu politike sa stvarnim učinkom (naknada &gt; 0 ili
    /// potrošena jedinica paketa); uz to se traži appointments.policy.override.</summary>
    [MaxLength(500)]
    public string CorrectionReason { get; set; }
}

/// <summary>
/// Phase M1B — segment termina: JEDINA izvršna jedinica (usluga, planirani/stvarni raspon, prostorija, zaposlenici,
/// resursi). Termin nema vlastitu uslugu/zaposlenika/prostoriju/trajanje.
/// </summary>
public class AppointmentSegmentDto
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public string ServiceCategoryColorHex { get; set; }
    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }
    public DateTimeOffset? ActualStart { get; set; }
    public DateTimeOffset? ActualEnd { get; set; }
    public Guid? RoomId { get; set; }
    public string RoomName { get; set; }

    /// <summary>Dodijeljeni zaposlenici — ravnopravni izvršitelji (bez uloga: primarni/sekundarni ne postoje).</summary>
    public List<AppointmentSegmentEmployeeDto> Employees { get; set; } = new();

    /// <summary>Phase M1G — izvor cijene segmenta: Standard (bez razina zaposlenika) ili Employee (razine
    /// PricingEmployeeId). NIJE "glavni" zaposlenik niti korisnik provizije.</summary>
    public SegmentPricingMode PricingMode { get; set; }
    public Guid? PricingEmployeeId { get; set; }
    public string PricingEmployeeName { get; set; }

    /// <summary>Dodijeljeni resursi (čitanje; dodjela kroz kreiranje/izmjenu još nije omogućena — kapacitet resursa).</summary>
    public List<AppointmentSegmentResourceDto> Resources { get; set; } = new();
}

public class AppointmentSegmentEmployeeDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
}

public class AppointmentSegmentResourceDto
{
    public Guid ResourceId { get; set; }
    public string ResourceName { get; set; }
    public int QuantityRequired { get; set; }
}

/// <summary>
/// Phase M1B — CILJNI ugovor kreiranja termina: termin (poslovnica, napomena) + segmenti; svaki segment nosi svoje
/// izvršne podatke i sudionike. Klijent koji se pojavi u više segmenata dobiva JEDAN Booking i po jedno sudjelovanje
/// po segmentu. Phase M1G: segment ima jednog ili više ravnopravnih zaposlenika; za 2+ zaposlenika izvor cijene je obavezan.
/// </summary>
public class AppointmentCreateRequest
{
    [Required]
    public Guid CompanyId { get; set; }

    public string Note { get; set; }

    /// <summary>Zaobilazi MEKE radne-snage blokade (izvan radnog vremena, odsutnost, praznik poslovnice, pauza zaposlenika) —
    /// NIKAD strukturne (neaktivan/nevaljan Company/Service/Employee/Room, sudar). Ignorira se (tretira kao false) ako
    /// pozivatelj nema appointments.write.all — vidi AppointmentEligibilityHelper.</summary>
    public bool OverrideAvailability { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Termin mora imati barem jedan segment.")]
    public List<AppointmentSegmentCreateRequest> Segments { get; set; } = new();
}

/// <summary>Phase M1H — izvršna definicija JEDNOG segmenta (bez sudionika): usluga, vrijeme, zaposlenici + izvor cijene,
/// prostorija, resursi.</summary>
public class AppointmentSegmentDefinitionRequest
{
    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public DateTimeOffset PlannedStart { get; set; }

    /// <summary>Null = PlannedStart + zadano trajanje usluge.</summary>
    public DateTimeOffset? PlannedEnd { get; set; }

    /// <summary>Skup zaposlenika segmenta (bez duplikata; svi ravnopravni izvršitelji).</summary>
    public List<Guid> EmployeeIds { get; set; } = new();

    /// <summary>Phase M1G — izvor cijene. Izostavljen: 1 zaposlenik → automatski Employee/taj zaposlenik. Za 2+ zaposlenika
    /// OBAVEZAN: Standard (bez PricingEmployeeId) ili Employee uz PricingEmployeeId jednog od EmployeeIds.</summary>
    public SegmentPricingMode? PricingMode { get; set; }

    public Guid? PricingEmployeeId { get; set; }

    /// <summary>Opcionalno — mora pripadati CompanyId termina.</summary>
    public Guid? RoomId { get; set; }

    /// <summary>Resursi segmenta (Phase M1D: omogućeni, količina &gt; 0, svaki resurs jednom).</summary>
    public List<AppointmentSegmentResourceRequest> Resources { get; set; } = new();
}

/// <summary>Segment ciljnog kreiranja/dodavanja: definicija segmenta + sudionici.</summary>
public class AppointmentSegmentCreateRequest : AppointmentSegmentDefinitionRequest
{

    /// <summary>Kreiranje termina: barem jedan sudionik po segmentu (domensko pravilo u AppointmentService — ne atribut, jer
    /// isti ugovor koristi i dodavanje segmenta postojećem terminu, gdje je segment bez sudionika dopušten; Phase M1E.1).</summary>
    public List<AppointmentParticipantCreateRequest> Participants { get; set; } = new();
}

public class AppointmentSegmentResourceRequest
{
    [Required]
    public Guid ResourceId { get; set; }

    [Range(1, int.MaxValue)]
    public int QuantityRequired { get; set; } = 1;
}

public class AppointmentParticipantCreateRequest
{
    [Required]
    public Guid ClientId { get; set; }

    /// <summary>Ručni override predložene cijene sudjelovanja. Null = predložena cijena iz cjenika (usluga segmenta).</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }
}

/// <summary>Phase M1E — dodavanje segmenta postojećem terminu: isti oblik kao segment ciljnog kreiranja (usluga, vrijeme,
/// zaposlenici, prostorija, resursi, sudionici — sudionici su opcionalni). Klijent koji već ima Booking na terminu dobiva
/// samo novo sudjelovanje.</summary>
public class AppointmentSegmentAddRequest : AppointmentSegmentCreateRequest
{
    /// <summary>Vidi AppointmentCreateRequest.OverrideAvailability (samo uz appointments.write.all).</summary>
    public bool OverrideAvailability { get; set; }
}

/// <summary>Phase M1E — promjena vremena JEDNOG segmenta. PlannedEnd null = zadržava se trajanje segmenta.</summary>
public class AppointmentSegmentTimeChangeRequest
{
    [Required]
    public DateTimeOffset PlannedStart { get; set; }

    public DateTimeOffset? PlannedEnd { get; set; }

    public bool OverrideAvailability { get; set; }
}

/// <summary>Phase M1E — promjena usluge JEDNOG segmenta. Trajanje se mijenja SAMO uz UseServiceDuration (početak + zadano
/// trajanje nove usluge) ili eksplicitni PlannedEnd; inače vrijeme segmenta ostaje isto. Aktivna (Confirmed) sudjelovanja se
/// ponovno cijene po novoj usluzi (ručni iznos se čuva).</summary>
public class AppointmentSegmentServiceChangeRequest
{
    [Required]
    public Guid ServiceId { get; set; }

    public bool UseServiceDuration { get; set; }

    public DateTimeOffset? PlannedEnd { get; set; }

    public bool OverrideAvailability { get; set; }
}

/// <summary>Phase M1E/M1G — novi SKUP zaposlenika JEDNOG segmenta (bez duplikata). Izvor cijene: rezultat s 1 zaposlenikom →
/// automatski Employee/taj zaposlenik; rezultat s 2+ zaposlenika → PricingMode je OBAVEZAN (i kad prethodni zaposlenik izvora
/// ostaje dodijeljen — namjera se ne zaključuje).</summary>
public class AppointmentSegmentEmployeesChangeRequest
{
    [Required]
    public List<Guid> EmployeeIds { get; set; } = new();

    public SegmentPricingMode? PricingMode { get; set; }

    public Guid? PricingEmployeeId { get; set; }

    public bool OverrideAvailability { get; set; }
}

/// <summary>Phase M1G — promjena SAMO izvora cijene segmenta (skup zaposlenika ostaje isti). Valjano prema broju zaposlenika:
/// 0 → Standard; 1 → isključivo Employee/taj zaposlenik (Standard nije dopušten); 2+ → Standard ili Employee uz zaposlenika
/// segmenta. Aktivna (Confirmed) sudjelovanja se ponovno cijene (ručni iznos se čuva).</summary>
public class AppointmentSegmentPricingSourceChangeRequest
{
    [Required]
    public SegmentPricingMode? PricingMode { get; set; }

    public Guid? PricingEmployeeId { get; set; }
}

/// <summary>Phase M1E — prostorija JEDNOG segmenta (null = bez prostorije).</summary>
public class AppointmentSegmentRoomChangeRequest
{
    public Guid? RoomId { get; set; }
}

/// <summary>Phase M1E — zamjena SVIH dodjela resursa JEDNOG segmenta (prazan popis = bez resursa).</summary>
public class AppointmentSegmentResourcesChangeRequest
{
    public List<AppointmentSegmentResourceRequest> Resources { get; set; } = new();
}

/// <summary>Phase M1E — klijent se pridružuje ODABRANIM segmentima termina: jedan Booking po (termin, klijent) — postojeći
/// se ponovno koristi — i po jedno sudjelovanje za svaki odabrani segment. Cijena se razrješava po sudjelovanju; dodavanje
/// ne troši paket i ne stvara plaćanje.</summary>
public class AppointmentClientAddRequest
{
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Potreban je barem jedan segment.")]
    public List<AppointmentClientParticipationRequest> Participations { get; set; } = new();
}

public class AppointmentClientParticipationRequest
{
    [Required]
    public Guid SegmentId { get; set; }

    /// <summary>Ručni iznos za ovo sudjelovanje (null = predložena cijena).</summary>
    public decimal? Amount { get; set; }
}
