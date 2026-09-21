using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IDefaultRoleTemplateHandler
{
    /// <summary>Puni razrješeni graf (capability ScopeModel+Grants uključeni) najnovije AKTIVNE verzije po Key-u —
    /// dovoljno za ICapabilityMaterializationService bez dodatnih upita. Null ako predložak ne postoji/nije aktivan.</summary>
    Task<ResolvedDefaultRoleTemplate> GetLatestActiveByKey(string key);

    /// <summary>Za svaki Key vraća samo najnoviju AKTIVNU verziju — za read-only listing (FAZA 1 Part R).</summary>
    Task<List<DefaultRoleTemplate>> GetLatestActive();

    Task<DefaultRoleTemplate> GetByKey(string key, int? version);

    /// <summary>Dijagnostika-only, presijeca sve verzije — vidi GrantDiagnosticsService.</summary>
    Task<List<DefaultRoleTemplate>> GetAllForDiagnostics();
}
