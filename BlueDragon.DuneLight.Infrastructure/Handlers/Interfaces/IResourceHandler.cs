using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IResourceHandler
{
    Task<(List<Resource> Items, int TotalCount)> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request);
    Task<Resource> GetById(Guid organizationId, Guid id);
    Task Add(Resource resource);
    Task Update(Resource resource);
    Task Delete(Resource resource);

    /// <summary>Naziv se uspoređuje normalizirano (trim + case-insensitive) unutar iste Company, isto kao
    /// ux_resources_org_company_name_active (i Room / ux_rooms_org_company_name_active).</summary>
    Task<bool> NameExistsAmongActive(Guid organizationId, Guid companyId, string name, Guid? excludeId);

    /// <summary>Resurs je referenciran ako ga zauzima barem jedan segment termina (appointment_segment_resources).</summary>
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
