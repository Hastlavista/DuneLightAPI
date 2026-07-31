using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class RosterAuditLogHandler : IRosterAuditLogHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public RosterAuditLogHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(RosterAuditLog entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.RosterAuditLog.Add(entry);
        await context.SaveChangesAsync();
    }

    public async Task Add(IUnitOfWork uow, RosterAuditLog entry)
    {
        uow.Context.RosterAuditLog.Add(entry);
        await uow.Context.SaveChangesAsync();
    }
}
