namespace BlueDragon.DuneLight.Infrastructure.Domain.Settings;

/// <summary>Konfiguracija OutboxProcessorService (vidi spec section 14/15/20/21) — namjerno jednostavna, bez
/// generičkog retry-frameworka. RetryBackoffSeconds je ograđen niz: pokušaj N koristi
/// RetryBackoffSeconds[min(N-1, length-1)] sekundi odgode; nakon MaxAttempts neuspjelih pokušaja redak postaje
/// Failed (terminalno, vidi spec section 21).</summary>
public class OutboxSettings
{
    public int PollIntervalSeconds { get; set; } = 3;
    public int BatchSize { get; set; } = 50;
    public int LeaseSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
    public int[] RetryBackoffSeconds { get; set; } = { 10, 30, 120, 600 };
}
