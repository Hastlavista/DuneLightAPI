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
    public bool PackageEntryDeducted { get; set; }
    public bool PackageEntryReturned { get; set; }
    public string Note { get; set; }

    /// <summary>Je li klijent trenutno aktivan član grupe (razlikuje ga od gosta/zamjene dodanog u prisutnost).</summary>
    public bool IsMember { get; set; }
}

/// <summary>Expected = aktivni članovi grupe bez zabilježene prisutnosti na ovom terminu (kandidati za čekiranje).
/// Recorded = svi već zabilježeni retci prisutnosti (uklj. goste izvan popisa članova).</summary>
public class GroupAttendanceListDto
{
    public List<GroupAttendanceEntryDto> Expected { get; set; } = new();
    public List<GroupAttendanceEntryDto> Recorded { get; set; } = new();
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

    public string Note { get; set; }
}
