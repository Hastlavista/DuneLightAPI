using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGroupAuditLogHandler
{
    Task Add(GroupAuditLog entry);

    /// <summary>Kao <see cref="Add(GroupAuditLog)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task Add(IUnitOfWork uow, GroupAuditLog entry);
}
