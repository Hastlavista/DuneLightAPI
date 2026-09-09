using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IOrganizationBrandingAuditLogHandler
{
    Task Add(OrganizationBrandingAuditLog entry);
}
