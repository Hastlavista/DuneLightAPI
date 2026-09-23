using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class GrantGroupTemplateUpgradeAuditLogHandler : IGrantGroupTemplateUpgradeAuditLogHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public GrantGroupTemplateUpgradeAuditLogHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(GrantGroupTemplateUpgradeAuditLog entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.GrantGroupTemplateUpgradeAuditLogs.Add(entry);
        await context.SaveChangesAsync();
    }
}
