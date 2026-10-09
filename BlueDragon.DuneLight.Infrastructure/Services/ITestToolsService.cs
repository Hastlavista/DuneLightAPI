using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.TestTools;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// T1 — PRIVREMENI testni alati za Management (samo uz TestTools:Enabled; uklanjaju se prije go-livea): stanje i simulirani
/// pomak poslovnog sata organizacije. Pomak ide samo naprijed (najviše 400 dana po skoku), a sve što bi u preskočenom
/// razdoblju nastalo po rasporedu (prolaz obnove članarina) izvršava se odmah, dan po dan.
/// </summary>
public interface ITestToolsService
{
    Task<TestToolsOrganizationStatusDto> GetStatus(Guid organizationId);

    Task<TestClockAdvanceResultDto> AdvanceClock(Guid organizationId, TestClockAdvanceRequest request, Guid? platformAccountId);
}
