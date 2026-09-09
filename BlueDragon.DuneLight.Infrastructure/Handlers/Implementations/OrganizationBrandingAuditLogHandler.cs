using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class OrganizationBrandingAuditLogHandler : IOrganizationBrandingAuditLogHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public OrganizationBrandingAuditLogHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(OrganizationBrandingAuditLog entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.OrganizationBrandingAuditLog.Add(entry);
        await context.SaveChangesAsync();
    }
}
