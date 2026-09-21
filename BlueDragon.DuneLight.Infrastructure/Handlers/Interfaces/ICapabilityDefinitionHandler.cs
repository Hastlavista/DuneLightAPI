using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICapabilityDefinitionHandler
{
    /// <summary>Za svaki Key vraća samo najnoviju AKTIVNU verziju (najveći Version gdje IsActive=true).</summary>
    Task<List<CapabilityDefinition>> GetLatestActive();

    /// <summary>Konkretna verzija (version=null -&gt; najnovija aktivna po Key-u).</summary>
    Task<CapabilityDefinition> GetByKey(string key, int? version);

    /// <summary>Dijagnostika-only, presijeca sve verzije (uklj. deprecated) — vidi GrantDiagnosticsService.</summary>
    Task<List<CapabilityDefinition>> GetAllForDiagnostics();
}
