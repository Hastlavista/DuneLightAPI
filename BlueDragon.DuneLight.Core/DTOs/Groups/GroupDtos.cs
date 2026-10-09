using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.DTOs.Groups;

public class GroupSlotDto
{
    public Guid Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public bool IsActive { get; set; }
}

public class GroupSlotCreateRequest
{
    [Required]
    public DayOfWeek DayOfWeek { get; set; }

    [Required]
    public TimeSpan StartTime { get; set; }
}

public class GroupSlotUpdateRequest
{
    [Required]
    public DayOfWeek DayOfWeek { get; set; }

    [Required]
    public TimeSpan StartTime { get; set; }
}

public class GroupMemberDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Phase M1F — predlošci segmenata koje je član odabrao (sudjeluje SAMO u tim segmentima occurrencea).</summary>
    public List<Guid> SegmentTemplateIds { get; set; } = new();
}

public class GroupMemberAddRequest
{
    [Required]
    public Guid ClientId { get; set; }

    /// <summary>Eksplicitan odabir predložaka u kojima član sudjeluje (barem jedan) — nikad se ne zaključuje (ni za grupu s
    /// jednim predloškom).</summary>
    [Required]
    [MinLength(1, ErrorMessage = "Odaberite barem jedan predložak segmenta.")]
    public List<Guid> SegmentTemplateIds { get; set; } = new();

    /// <summary>Eksplicitno prekoračenje mekog kapaciteta odabranih predložaka; zahtijeva groups.capacity.override.</summary>
    public bool OverrideCapacity { get; set; }
}

/// <summary>Phase M1F — nova (potpuna) selekcija predložaka postojećeg člana.</summary>
public class GroupMemberSegmentTemplatesRequest
{
    [Required]
    public List<Guid> SegmentTemplateIds { get; set; } = new();

    /// <summary>Eksplicitno prekoračenje mekog kapaciteta NOVO odabranih predložaka; zahtijeva groups.capacity.override.</summary>
    public bool OverrideCapacity { get; set; }
}

public class GroupSegmentTemplateResourceDto
{
    public Guid ResourceId { get; set; }
    public string ResourceName { get; set; }
    public int QuantityRequired { get; set; }
}

/// <summary>Phase M1F — predložak segmenta grupe (vrijeme relativno sidru occurrencea = vrijeme slota).</summary>
public class GroupSegmentTemplateDto
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public int StartOffsetMinutes { get; set; }
    public int DurationMinutes { get; set; }
    public Guid? RoomId { get; set; }
    public string RoomName { get; set; }

    /// <summary>MEKI poslovni broj mjesta segmenta (prekoračenje samo eksplicitno uz groups.capacity.override).</summary>
    public int Capacity { get; set; }

    public List<GroupSegmentTemplateResourceDto> Resources { get; set; } = new();

    /// <summary>Phase M1G — osoblje predloška (ravnopravni zaposlenici; prazno = sesija bez trenera). Kopira se u generirani
    /// segment.</summary>
    public List<AppointmentSegmentEmployeeDto> Employees { get; set; } = new();

    /// <summary>Phase M1G — izvor cijene generiranih segmenata (isto pravilo kao segment).</summary>
    public SegmentPricingMode PricingMode { get; set; }
    public Guid? PricingEmployeeId { get; set; }
}

public class GroupSegmentTemplateResourceRequest
{
    [Required]
    public Guid ResourceId { get; set; }

    [Range(1, int.MaxValue)]
    public int QuantityRequired { get; set; } = 1;
}

/// <summary>Phase M1F — predložak segmenta (kreiranje grupe, dodavanje/izmjena predloška).</summary>
public class GroupSegmentTemplateRequest
{
    [Required]
    public Guid ServiceId { get; set; }

    /// <summary>Pomak početka segmenta od sidra occurrencea (lokalno vrijeme slota), 0..1439 min. Barem jedan predložak
    /// grupe ima pomak 0 (sidro = početak occurrencea).</summary>
    [Range(0, 1439)]
    public int StartOffsetMinutes { get; set; }

    /// <summary>Null = prijedlog iz zadanog trajanja usluge (predložak zatim posjeduje svoje trajanje).</summary>
    [Range(1, 1440)]
    public int? DurationMinutes { get; set; }

    public Guid? RoomId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kapacitet mora biti veći od 0.")]
    public int Capacity { get; set; }

    /// <summary>K1-6 — null (izostavljeno): pri kreiranju zadani resursi usluge u poslovnici grupe, pri izmjeni postojeći resursi
    /// predloška; poslana lista (i prazna) vrijedi kako je poslana.</summary>
    public List<GroupSegmentTemplateResourceRequest> Resources { get; set; }

    /// <summary>Osoblje predloška (skup ravnopravnih zaposlenika, bez duplikata; prazno = sesija bez trenera). Izmjena
    /// predloška je potpuna zamjena definicije, uključivo osoblje.</summary>
    public List<Guid> EmployeeIds { get; set; } = new();

    /// <summary>Phase M1G — izvor cijene (isto pravilo kao segment): 1 zaposlenik → automatski; 2+ → obavezan.</summary>
    public SegmentPricingMode? PricingMode { get; set; }

    public Guid? PricingEmployeeId { get; set; }
}

public class GroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }

    /// <summary>Phase M1F — autoritativna izvršna definicija grupe.</summary>
    public List<GroupSegmentTemplateDto> SegmentTemplates { get; set; } = new();
    public bool IsActive { get; set; }
    public string Note { get; set; }
    public List<GroupSlotDto> Slots { get; set; } = new();
    public int ActiveMemberCount { get; set; }

    /// <summary>Neblokirajuća upozorenja — kapacitet grupe premašen (AddMember) ili slot izvan radnog vremena
    /// trenera/poslovnice (Create/Update/AddSlot/UpdateSlot). Inače prazno.</summary>
    public List<WarningDto> Warnings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class GroupDetailDto : GroupDto
{
    public List<GroupMemberDto> Members { get; set; } = new();
    public List<AppointmentScheduleCellDto> UpcomingAppointments { get; set; } = new();
    public List<AppointmentScheduleCellDto> PastAppointments { get; set; } = new();
}

public class GroupCreateRequest
{
    [Required]
    public string Name { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    public string Note { get; set; }

    /// <summary>Barem jedan slot je obavezan pri kreiranju grupe.</summary>
    public List<GroupSlotCreateRequest> Slots { get; set; } = new();

    /// <summary>Izvršna definicija grupe: predlošci segmenata (barem jedan; jedan ima pomak 0). Grupa s jednom uslugom = jedan
    /// predložak.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "Grupa mora imati barem jedan predložak segmenta.")]
    public List<GroupSegmentTemplateRequest> SegmentTemplates { get; set; } = new();
}

public class GroupUpdateRequest
{
    [Required]
    public string Name { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    public string Note { get; set; }
}

/// <summary>GroupId=null generira za sve aktivne grupe. Idempotentno — ponovno pokretanje za isti raspon ne stvara duplikate.</summary>
public class GenerateGroupAppointmentsRequest
{
    public Guid? GroupId { get; set; }

    [Required]
    public DateTimeOffset FromDate { get; set; }

    [Required]
    public DateTimeOffset ToDate { get; set; }

    /// <summary>Zaobilazi MEKE radne-snage blokade (izvan radnog vremena, odsutnost, pauza trenera) za sve occurrence u ovom
    /// rasponu — vidi AppointmentCreateRequest.OverrideAvailability. K1-7 (P-5): uz potvrdu se generira i na praznik
    /// poslovnice (upozorenje COMPANY_CLOSED_HOLIDAY); bez nje se praznik preskače i navodi u Skipped. K2 (P-2): traži
    /// appointments.availability.override (ne više groups.manage); zatražen bez granta → 403; svaki stvarni override se
    /// bilježi u audit generiranog termina.</summary>
    public bool OverrideAvailability { get; set; }
}

public class GenerateGroupAppointmentsResult
{
    public int CreatedCount { get; set; }

    /// <summary>Preskočeni occurrencei: već generirani + datumi praznika (bez grupa neaktivnih poslovnica).</summary>
    public int SkippedCount { get; set; }
    public List<AppointmentScheduleCellDto> Created { get; set; } = new();

    /// <summary>K1-7 — što nije generirano i zašto: grupe neaktivnih poslovnica i datumi praznika (jedan prikaz).</summary>
    public List<GroupGenerationSkipDto> Skipped { get; set; } = new();

    /// <summary>P2 (Q18) — članovi grupe preskočeni zbog duga članarine uz postavku "blokiraj rezervaciju" (ostaju članovi
    /// grupe); popis za recepciju, po segmentu occurrencea.</summary>
    public List<GroupMembershipSkipDto> MembershipSkips { get; set; } = new();
}

/// <summary>K1-7 — jedna stavka popisa preskočenog pri generiranju. CompanyInactive: cijela grupa (Slot/Date null);
/// CompanyHoliday: jedan datum slota (lokalni datum poslovnice).</summary>
public class GroupGenerationSkipDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? GroupSlotId { get; set; }
    public DateOnly? Date { get; set; }
    public GroupGenerationSkipReason Reason { get; set; }
}

/// <summary>P2 (Q18/Q53) — član grupe preskočen u segmentu occurrencea zbog duga članarine i (kad je riješeno) kako.</summary>
public class GroupMembershipSkipDto
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid AppointmentSegmentId { get; set; }
    public DateTimeOffset PlannedStart { get; set; }
    public Guid ClientId { get; set; }
    public Guid ClientMembershipId { get; set; }
    public DateTimeOffset SkippedAt { get; set; }
    /// <summary>Null = još preskočen (čeka plaćanje duga); CapacityFull/Conflict = ostaje na popisu recepciji.</summary>
    public GroupMembershipSkipResolution? Resolution { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ParticipationId { get; set; }
}

/// <summary>Za dopunu Klijent detalja — grupe čiji je klijent član (aktivno i povijesno).</summary>
public class ClientGroupMembershipDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; }

    /// <summary>Phase M1H — usluge predložaka koje je klijent odabrao (redom početka), ne "usluga grupe".</summary>
    public List<string> ServiceNames { get; set; } = new();
    public string CompanyName { get; set; }

    /// <summary>Samo aktivni slotovi (raspored koji trenutno vrijedi) — za razliku od GroupDto.Slots, ovdje nema
    /// potrebe za upravljanjem uklonjenim slotovima, prikazuje se samo trenutni raspored klijentu.</summary>
    public List<GroupSlotDto> Slots { get; set; } = new();

    public DateTimeOffset JoinedAt { get; set; }
    public bool IsActive { get; set; }
}
