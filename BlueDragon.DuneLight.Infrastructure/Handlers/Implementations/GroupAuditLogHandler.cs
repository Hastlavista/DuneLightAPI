using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class GroupAuditLogHandler : IGroupAuditLogHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public GroupAuditLogHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(GroupAuditLog entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.GroupAuditLog.Add(entry);
        await context.SaveChangesAsync();
    }
}
