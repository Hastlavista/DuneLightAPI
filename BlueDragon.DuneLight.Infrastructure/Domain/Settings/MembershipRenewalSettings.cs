namespace BlueDragon.DuneLight.Infrastructure.Domain.Settings;

/// <summary>P2 (2C) — scheduler obnove članarina (MembershipRenewalBackgroundService). Obnova je idempotentna, pa interval
/// određuje samo koliko brzo nakon ponoći (zona organizacije) novi period i zaduženje nastaju.</summary>
public class MembershipRenewalSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 30;
}
