using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ILeaveFundHandler
{
    /// <summary>Svi fondovi zaposlenika (ne samo aktivni/neistekli) — za GET pregled.</summary>
    Task<List<LeaveFund>> GetForEmployee(Guid organizationId, Guid employeeId);

    /// <summary>Vraća postojeći fond za tu obračunsku godinu ili ga stvara iz EmployeeLeaveSettings snapshot-a — unutar transakcije (isti context za čitanje i pisanje, izbjegava reattach).</summary>
    Task<LeaveFund> GetOrCreateForYear(
        IUnitOfWork uow, Guid organizationId, Guid employeeId, EmployeeLeaveSettings settings, int fundYear, Guid userId);

    /// <summary>Fondovi zaposlenika koji još nisu istekli (ExpiresAt >= asOf) i imaju preostalog kapaciteta, poredani od najstarijeg — vidi LeaveFundAllocator.</summary>
    Task<List<LeaveFund>> GetEligible(IUnitOfWork uow, Guid organizationId, Guid employeeId, DateTimeOffset asOf);

    Task Update(IUnitOfWork uow, LeaveFund fund);

    /// <summary>LeaveFund uključen (Include) — za povrat dana pri brisanju/izmjeni RosterEntry-ja.</summary>
    Task<List<LeaveFundUsage>> GetUsagesForEntry(IUnitOfWork uow, Guid organizationId, Guid rosterEntryId);

    Task AddUsage(IUnitOfWork uow, LeaveFundUsage usage);

    Task DeleteUsages(IUnitOfWork uow, List<LeaveFundUsage> usages);

    /// <summary>Ručna korekcija/otvaranje fonda za točno određenu godinu (admin) — upsert po (employeeId, FundYear), ne dira UsedDays.</summary>
    Task<LeaveFund> ManualUpsert(
        Guid organizationId, Guid employeeId, EmployeeLeaveSettings settings, int fundYear, int allocatedDays, Guid userId);
}
