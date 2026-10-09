using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>K1-4 — šifrarnik razloga otkazivanja/izostanka.</summary>
public interface ICancellationReasonHandler
{
    Task<List<CancellationReason>> GetAll(Guid organizationId);
    Task<CancellationReason> GetById(Guid organizationId, Guid id);
    Task Add(CancellationReason reason);
    Task Update(CancellationReason reason);

    /// <summary>Isti ključ kao unique indeks ux_cancellation_reasons_org_name_active (lower(trim(name)), samo aktivni).</summary>
    Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId);
}
