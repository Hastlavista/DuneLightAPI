using System;

namespace BlueDragon.DuneLight.Infrastructure.Time;

/// <summary>
/// T1 — poslovni sat sustava (jedini izvor "sada" za poslovna pravila): stvarni sat (ili fiksni sat u testovima) + simulirani
/// pomak organizacije iz <see cref="OrganizationClockContext"/>. Bez uključenih testnih alata pomak je uvijek nula.
/// Sistemske stvari (istek JWT-a, outbox lease, Task.Delay) koriste <see cref="TimeProvider.System"/>, nikad ovaj sat.
/// </summary>
public sealed class BusinessTimeProvider : TimeProvider
{
    private readonly TimeProvider _source;
    private readonly IClockOffsetStore _offsets;

    public BusinessTimeProvider(IClockOffsetStore offsets, TimeProvider source = null)
    {
        _offsets = offsets;
        _source = source ?? System;
    }

    /// <summary>Instanti su uvijek UTC; poslovni datumi se računaju u zoni poslovnice kroz OrganizationCalendar (ADR-0013).</summary>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow()
    {
        DateTimeOffset now = _source.GetUtcNow();
        Guid? organizationId = OrganizationClockContext.OrganizationId;
        return organizationId.HasValue ? now + _offsets.GetOffset(organizationId.Value) : now;
    }

    /// <summary>Stvarni (nepomaknuti) trenutak izvora — za endpoint vremena organizacije i testne alate.</summary>
    public DateTimeOffset GetRealUtcNow() => _source.GetUtcNow();

    public TimeSpan GetOffset(Guid organizationId) => _offsets.GetOffset(organizationId);
}
