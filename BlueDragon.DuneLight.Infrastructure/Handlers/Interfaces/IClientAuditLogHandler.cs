using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>T1-9 — čitanje povijesti klijenta. Zapise upisuju handleri promjene (ClientHandler, ClientPackageHandler) u istom
/// contextu i istom SaveChanges kao i samu promjenu.</summary>
public interface IClientAuditLogHandler
{
    /// <summary>Povijest klijenta, najstariji zapis prvi.</summary>
    Task<List<ClientAuditLog>> GetByClient(Guid organizationId, Guid clientId);
}
