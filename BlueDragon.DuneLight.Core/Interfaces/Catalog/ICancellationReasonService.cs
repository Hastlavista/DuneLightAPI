using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

/// <summary>K1-4 (12.3) — šifrarnik razloga otkazivanja i izostanka (upravljanje + jedina provjera odabira pri događaju).</summary>
public interface ICancellationReasonService
{
    /// <summary>Šifre organizacije; filtri: aktivnost i događaj za koji vrijede (za odabir u formi otkaza/izostanka).</summary>
    Task<List<CancellationReasonDto>> GetAll(Guid organizationId, bool? isActive, CancellationReasonEvent? appliesTo);

    Task<CancellationReasonDto> Create(Guid organizationId, Guid userId, CancellationReasonUpsertRequest request);
    Task<CancellationReasonDto> Update(Guid organizationId, Guid userId, Guid id, CancellationReasonUpsertRequest request);
    Task<CancellationReasonDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);

    /// <summary>Provjera odabira pri događaju: šifra mora postojati, biti aktivna i vrijediti za događaj; bez šifre se odbija
    /// kad postavka organizacije za događaj traži odabir i za događaj postoji barem jedna aktivna šifra. Vraća (id, naziv) za
    /// snapshot na sudjelovanju, ili (null, null).</summary>
    Task<(Guid? Id, string Name)> ResolveForEvent(Guid organizationId, Guid? reasonId, CancellationReasonEvent reasonEvent);
}
