using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IRosterAuditLogHandler
{
    Task Add(RosterAuditLog entry);

    /// <summary>Kao <see cref="Add(RosterAuditLog)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task Add(IUnitOfWork uow, RosterAuditLog entry);
}
