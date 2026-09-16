using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class CheckoutAuditLogHandler : ICheckoutAuditLogHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CheckoutAuditLogHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(IUnitOfWork uow, CheckoutAuditLog entry)
    {
        uow.Context.CheckoutAuditLog.Add(entry);
        await uow.Context.SaveChangesAsync();
    }
}
