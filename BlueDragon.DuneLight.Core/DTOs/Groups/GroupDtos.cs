using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.DTOs.Appointments;

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
}

public class GroupMemberAddRequest
{
    [Required]
    public Guid ClientId { get; set; }
}

public class GroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public int Capacity { get; set; }
    public Guid? DefaultTrainerId { get; set; }
    public string DefaultTrainerName { get; set; }
    public bool IsActive { get; set; }
    public string Note { get; set; }
    public List<GroupSlotDto> Slots { get; set; } = new();
    public int ActiveMemberCount { get; set; }

    /// <summary>Popunjeno samo kao odgovor na dodavanje člana preko kapaciteta — inače prazno.</summary>
    public List<string> Warnings { get; set; } = new();

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
    public Guid ServiceId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kapacitet mora biti veći od 0.")]
    public int Capacity { get; set; }

    public Guid? DefaultTrainerId { get; set; }

    public string Note { get; set; }

    /// <summary>Barem jedan slot je obavezan pri kreiranju grupe.</summary>
    public List<GroupSlotCreateRequest> Slots { get; set; } = new();
}

public class GroupUpdateRequest
{
    [Required]
    public string Name { get; set; }

    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kapacitet mora biti veći od 0.")]
    public int Capacity { get; set; }

    public Guid? DefaultTrainerId { get; set; }

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
}

public class GenerateGroupAppointmentsResult
{
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<AppointmentScheduleCellDto> Created { get; set; } = new();
}

/// <summary>Za dopunu Klijent detalja — grupe čiji je klijent član (aktivno i povijesno).</summary>
public class ClientGroupMembershipDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; }
    public string ServiceName { get; set; }
    public string CompanyName { get; set; }

    /// <summary>Samo aktivni slotovi (raspored koji trenutno vrijedi) — za razliku od GroupDto.Slots, ovdje nema
    /// potrebe za upravljanjem uklonjenim slotovima, prikazuje se samo trenutni raspored klijentu.</summary>
    public List<GroupSlotDto> Slots { get; set; } = new();

    public DateTimeOffset JoinedAt { get; set; }
    public bool IsActive { get; set; }
}
