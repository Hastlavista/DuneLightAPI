using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IOrganizationBrandingHandler
{
    Task<Organization> GetById(Guid organizationId);
    Task Update(Organization organization);
}
