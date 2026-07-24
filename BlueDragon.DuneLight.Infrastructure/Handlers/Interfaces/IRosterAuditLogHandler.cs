using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IRosterAuditLogHandler
{
    Task Add(RosterAuditLog entry);
}
