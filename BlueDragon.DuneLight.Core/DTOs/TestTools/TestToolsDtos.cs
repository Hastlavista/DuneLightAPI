using System;

namespace BlueDragon.DuneLight.Core.DTOs.TestTools;

// T1 — PRIVREMENI testni alati (Management, samo uz TestTools:Enabled); uklanjaju se prije go-livea.

/// <summary>Stanje testnih alata organizacije: pomak sata i je li demo organizacija (samo demo ima reset; u ostalima je pomak
/// trajan — Management upozorava prije potvrde).</summary>
public class TestToolsOrganizationStatusDto
{
    public Guid OrganizationId { get; set; }
    public bool IsDemo { get; set; }

    /// <summary>Razina demo organizacije (reset stvara istu razinu); null kad organizacija nije demo.</summary>
    public DemoSeedLevel? DemoLevel { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public DateTimeOffset RealUtc { get; set; }
    public DateTimeOffset EffectiveUtc { get; set; }
    public TimeSpan Offset { get; set; }
    public DateOnly LocalDate { get; set; }
    public string TimeZone { get; set; }
    public DateTimeOffset? ClockAdvancedAt { get; set; }

    /// <summary>Upozorenje za Management: pomak u organizaciji koja nije demo ne može se vratiti.</summary>
    public bool OffsetIsPermanent => !IsDemo;
}

/// <summary>Pomak sata naprijed: ili broj dana (Days), ili ciljni trenutak (To). Točno jedno.</summary>
public class TestClockAdvanceRequest
{
    public int? Days { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class TestClockAdvanceResultDto
{
    public DateTimeOffset RealUtc { get; set; }
    public DateTimeOffset PreviousEffectiveUtc { get; set; }
    public DateTimeOffset EffectiveUtc { get; set; }
    public TimeSpan Offset { get; set; }
    public DateOnly LocalDate { get; set; }

    /// <summary>Broj lokalnih dana organizacije za koje je pokrenut prolaz obnove (dan po dan).</summary>
    public int DaysProcessed { get; set; }

    /// <summary>Zbroj obrađenih članstava kroz sve dane.</summary>
    public int MembershipsProcessed { get; set; }
}
