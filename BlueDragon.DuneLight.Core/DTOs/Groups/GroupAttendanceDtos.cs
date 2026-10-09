using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Groups;

public class GroupAttendanceEntryDto
{
    public Guid ClientId { get; set; }
    public string ClientName { get; set; }
    public bool? Attended { get; set; }
    public AttendanceCoverageType? CoverageType { get; set; }
    public Guid? ClientPackageId { get; set; }
    public bool PackageCoverageApplied { get; set; }
    public bool PackageCoverageReturned { get; set; }

    /// <summary>Booking-razina komercijalno stanje (vidi Booking.cs) — 0/false dok booking nije čekiran.</summary>
    public decimal Amount { get; set; }
    public decimal SuggestedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }

    /// <summary>P1 (D7): max(PaidAmount − MonetaryDue, 0) — informativno, nije kredit klijenta.</summary>
    public decimal SurplusAmount { get; set; }
    public bool IsPaid { get; set; }
    public string Note { get; set; }

    /// <summary>P1 — stanje najnovije posljedice politike (npr. izostanak) ako postoji, inače null.</summary>
    public PolicyConsequenceStatus? PolicyConsequenceStatus { get; set; }
    public decimal? PolicyFeeAmount { get; set; }

    /// <summary>Je li klijent trenutno aktivan član grupe (razlikuje ga od gosta/zamjene dodanog u prisutnost). Phase M1F.1:
    /// u popisu SEGMENTA = aktivan član koji je odabrao predložak tog segmenta.</summary>
    public bool IsMember { get; set; }

    /// <summary>Phase M1F.1 — sudjelovanje (samo u popisu segmenta; null za očekivanog člana bez sudjelovanja).</summary>
    public Guid? ParticipationId { get; set; }
}

/// <summary>Phase M1F.1 — prisutnost JEDNOG segmenta occurrencea. Recorded = konkretna sudjelovanja tog segmenta (istina
/// occurrencea, uključivo goste). Expected = aktivni članovi koji su ODABRALI predložak segmenta, a nemaju sudjelovanje — samo
/// dok segment još nije počeo (izmjena članstva propagira samo u buduće segmente; za počete/prošle segmente su sudjelovanja
/// jedina istina i današnje članstvo ih ne prepisuje).</summary>
public class GroupSegmentAttendanceDto
{
    public Guid SegmentId { get; set; }
    public Guid? SegmentTemplateId { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }
    public List<GroupAttendanceEntryDto> Expected { get; set; } = new();
    public List<GroupAttendanceEntryDto> Recorded { get; set; } = new();
}

/// <summary>Prisutnost grupnog occurrencea — isključivo po segmentu (Phase M1H: nema sažetka "po occurrenceu").</summary>
public class GroupAttendanceListDto
{
    public List<GroupSegmentAttendanceDto> Segments { get; set; } = new();

    /// <summary>K1-9 — upozorenja naredbe prisutnosti (npr. PARTICIPATION_NOT_COVERED); samo u odgovoru naredbe.</summary>
    public List<BlueDragon.DuneLight.Core.Shared.WarningDto> Warnings { get; set; } = new();
}

public class SetGroupAttendanceRequest
{
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    public bool Attended { get; set; }

    /// <summary>Ručni odabir paketa kad klijent ima više prihvatljivih paketa za ovu uslugu. Ako je izostavljen
    /// i postoji točno jedan prihvatljiv paket, koristi se automatski; ako nema nijednog, pokriće je SinglePaid.</summary>
    public Guid? ClientPackageId { get; set; }

    /// <summary>Relevantno samo kad Attended=true i pokriće nije paketom (SinglePaid) — vidi
    /// BookingSetStatusRequest.PaymentMethod za istu semantiku. Izostavljeno = evidentirano bez naplate
    /// (isto ponašanje kao prije uvođenja naplate na grupne bookinge).</summary>
    public PaymentMethod? PaymentMethod { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }

    public bool IsPaid { get; set; } = true;

    public string Note { get; set; }

    /// <summary>Segment occurrencea čija se prisutnost bilježi — uvijek obavezan.</summary>
    [Required]
    public Guid? SegmentId { get; set; }

    /// <summary>P1 (D9/D10) — Attended=false je izostanak (NoShow politika); otpis posljedice u trenutku događaja traži
    /// WaiverReason i grant po učinku (K2: appointments.policy.fee.waive / appointments.policy.unit.waive).</summary>
    public bool WaivePolicyConsequence { get; set; }

    [MaxLength(500)]
    public string WaiverReason { get; set; }

    /// <summary>P1 (D3) — opcionalan razlog izostanka (Attended=false).</summary>
    [MaxLength(500)]
    public string NoShowReason { get; set; }

    /// <summary>K1-4 — šifra razloga izostanka (Attended=false).</summary>
    public Guid? NoShowReasonCodeId { get; set; }

    /// <summary>P1 (D12) — razlog korekcije kad prijelaz poništava aktivnu posljedicu sa stvarnim učinkom.</summary>
    [MaxLength(500)]
    public string CorrectionReason { get; set; }
}
