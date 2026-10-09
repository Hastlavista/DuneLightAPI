using System;
using System.Collections.Concurrent;
using System.Linq;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;

namespace BlueDragon.DuneLight.Infrastructure.Time;

/// <summary>T1 — simulirani pomak poslovnog sata po organizaciji (testni alat).</summary>
public interface IClockOffsetStore
{
    TimeSpan GetOffset(Guid organizationId);

    /// <summary>Osvježava pomak organizacije u memoriji nakon što ga je testni alat spremio u bazu.</summary>
    void Set(Guid organizationId, TimeSpan offset);
}

/// <summary>
/// Pomaci se jednom učitaju iz <c>test_tool_organizations</c> (samo kad su testni alati uključeni) i drže u memoriji; promjenu
/// upisuje testni alat kroz <see cref="Set"/>. Isključeni testni alati: pomak je uvijek nula i baza se ne čita.
/// </summary>
public sealed class ClockOffsetStore : IClockOffsetStore
{
    private readonly bool _enabled;
    private readonly DatabaseSettings _databaseSettings;
    private readonly Lazy<ConcurrentDictionary<Guid, TimeSpan>> _offsets;

    public ClockOffsetStore(TestToolsSettings testTools, DatabaseSettings databaseSettings)
    {
        _enabled = testTools?.Enabled == true;
        _databaseSettings = databaseSettings;
        _offsets = new Lazy<ConcurrentDictionary<Guid, TimeSpan>>(Load);
    }

    public TimeSpan GetOffset(Guid organizationId)
    {
        if (!_enabled)
            return TimeSpan.Zero;
        return _offsets.Value.TryGetValue(organizationId, out TimeSpan offset) ? offset : TimeSpan.Zero;
    }

    public void Set(Guid organizationId, TimeSpan offset)
    {
        if (_enabled)
            _offsets.Value[organizationId] = offset;
    }

    private ConcurrentDictionary<Guid, TimeSpan> Load()
    {
        using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return new ConcurrentDictionary<Guid, TimeSpan>(context.TestToolOrganizations
            .Where(t => t.ClockOffsetSeconds != 0)
            .Select(t => new { t.OrganizationId, t.ClockOffsetSeconds })
            .AsEnumerable()
            .ToDictionary(t => t.OrganizationId, t => TimeSpan.FromSeconds(t.ClockOffsetSeconds)));
    }
}
