using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICheckoutAuditLogHandler
{
    Task Add(IUnitOfWork uow, CheckoutAuditLog entry);
}
