using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;

namespace BlueDragon.DuneLight.Core.Interfaces.Clients;

public interface IClientHistoryService
{
    /// <summary>Sažetak povijesti klijenta — vidi ClientHistorySummaryDto. Radi i za anonimizirane klijente
    /// (povijest termina/paketa se ne briše kod GDPR anonimizacije, samo osobni podaci na Client entitetu).</summary>
    Task<ClientHistorySummaryDto> GetSummary(Guid organizationId, Guid clientId);
}
