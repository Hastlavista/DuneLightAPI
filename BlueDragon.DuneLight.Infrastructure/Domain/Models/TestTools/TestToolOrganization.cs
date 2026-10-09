using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.TestTools;

/// <summary>
/// T1 — PRIVREMENI testni alat (uklanja se prije go-livea zajedno s tablicom): stanje testnih alata po organizaciji —
/// simulirani pomak poslovnog sata (samo naprijed) i oznaka demo organizacije koju je stvorio seed (samo demo organizacija
/// ima "reset"). Nije dio poslovnog modela.
/// </summary>
[Table("test_tool_organizations")]
public class TestToolOrganization
{
    [Key]
    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("is_demo")]
    public bool IsDemo { get; set; }

    /// <summary>Razina demo organizacije (DemoSeedLevel kao tekst: "Basic" / "Full"); reset stvara istu. Null = nije demo ili je
    /// demo stvoren prije razina (tretira se kao "Full").</summary>
    [Column("demo_level")]
    public string DemoLevel { get; set; }

    [Column("clock_offset_seconds")]
    public long ClockOffsetSeconds { get; set; }

    [Column("clock_advanced_at")]
    public DateTimeOffset? ClockAdvancedAt { get; set; }

    [Column("clock_advanced_by")]
    public Guid? ClockAdvancedBy { get; set; }

    /// <summary>Demo organizacija zamijenjena resetom (njeni korisnici su deaktivirani).</summary>
    [Column("retired_at")]
    public DateTimeOffset? RetiredAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
