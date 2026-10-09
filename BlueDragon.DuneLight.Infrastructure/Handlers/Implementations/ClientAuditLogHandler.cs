using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ClientAuditLogHandler : IClientAuditLogHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ClientAuditLogHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<ClientAuditLog>> GetByClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientAuditLogs.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.ClientId == clientId)
            .OrderBy(a => a.ChangedAt)
            .ToListAsync();
    }
}
