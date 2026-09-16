using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>Ishod pokušaja deaktivacije — vidi CompanyHandler.Deactivate za objašnjenje atomarnosti.</summary>
public enum CompanyDeactivationOutcome
{
    NotFound,
    AlreadyInactive,
    Blocked,
    Deactivated
}

public interface ICompanyHandler
{
    Task<(List<Company> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<Company> GetById(Guid organizationId, Guid id);

    /// <summary>Batch dohvat po ID-evima u jednom upitu — izbjegava N+1 kod validacije liste tvrtka.</summary>
    Task<List<Company>> GetByIds(Guid organizationId, List<Guid> ids);
    Task Add(Company company);
    Task Update(Company company);
    Task Delete(Company company);

    /// <summary>Naziv se uspoređuje normalizirano (trim + case-insensitive), isto kao ux_companies_org_name_active.</summary>
    Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId);

    /// <summary>
    /// Atomarno provjerava i, ako je dopušteno, deaktivira tvrtku unutar jedne transakcije — brave-lockira
    /// (SELECT ... FOR UPDATE) sve trenutno aktivne tvrtke organizacije prije prebrojavanja kako dva paralelna
    /// zahtjeva ne bi mogla oba proći provjeru i organizaciju ostaviti bez ijedne aktivne tvrtke.
    /// </summary>
    Task<CompanyDeactivationOutcome> Deactivate(Guid organizationId, Guid id, Guid userId);

    /// <summary>Provjerava SVE poznate FK reference na tvrtku (cjenik, prostorije, zaposlenici, klijenti,
    /// termini, pauze, grupe, predlošci radnog vremena, praznici) — ne samo cjenik.</summary>
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
