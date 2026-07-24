using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGroupAuditLogHandler
{
    Task Add(GroupAuditLog entry);
}
