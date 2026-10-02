using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IResourceHandler
{
    Task<(List<Resource> Items, int TotalCount)> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request);
    Task<Resource> GetById(Guid organizationId, Guid id);
    Task Add(Resource resource);
    /// <summary>Opća izmjena kataloga (naziv, napomena, redoslijed, aktivnost) — Phase M1D.1: NIKAD ne piše Capacity (stara
    /// vrijednost učitanog entiteta ne smije prepisati kapacitet mimo zaključane provjere); kapacitet mijenja samo
    /// <see cref="GetForCapacityChange"/> put.</summary>
    Task Update(Resource resource);

    /// <summary>Phase M1D.1: praćen resource učitan UNUTAR transakcije nakon što je pozivatelj zaključao subjekt rasporeda — jedini
    /// put kojim se mijenja Capacity (sprema ga pozivateljev CommitAsync).</summary>
    Task<Resource> GetForCapacityChange(IUnitOfWork uow, Guid organizationId, Guid id);
    Task Delete(Resource resource);

    /// <summary>Naziv se uspoređuje normalizirano (trim + case-insensitive) unutar iste Company, isto kao
    /// ux_resources_org_company_name_active (i Room / ux_rooms_org_company_name_active).</summary>
    Task<bool> NameExistsAmongActive(Guid organizationId, Guid companyId, string name, Guid? excludeId);

    /// <summary>Resurs je referenciran ako ga zauzima barem jedan segment termina (appointment_segment_resources).</summary>
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
