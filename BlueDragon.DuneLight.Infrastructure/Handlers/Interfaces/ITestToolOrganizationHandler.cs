using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.TestTools;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>T1 — PRIVREMENI testni alati: stanje po organizaciji (pomak sata, demo). Uklanja se prije go-livea.</summary>
public interface ITestToolOrganizationHandler
{
    Task<bool> OrganizationExists(Guid organizationId);

    /// <summary>Redak testnih alata organizacije ili null (pomak 0, nije demo).</summary>
    Task<TestToolOrganization> Get(Guid organizationId);

    /// <summary>Upisuje pomak sata (stvara redak ako ne postoji).</summary>
    Task SetClockOffset(Guid organizationId, long offsetSeconds, Guid? advancedBy, DateTimeOffset advancedAt);

    /// <summary>Označava organizaciju kao demo zadane razine ("Basic" / "Full"; stvara redak ako ne postoji).</summary>
    Task MarkDemo(Guid organizationId, string demoLevel, DateTimeOffset createdAt);

    /// <summary>Demo organizacija zamijenjena resetom: oznaka i deaktivacija svih njenih korisnika.</summary>
    Task Retire(Guid organizationId, DateTimeOffset retiredAt);
}
