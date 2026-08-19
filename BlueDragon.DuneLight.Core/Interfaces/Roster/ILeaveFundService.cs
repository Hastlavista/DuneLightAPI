using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;

namespace BlueDragon.DuneLight.Core.Interfaces.Roster;

public interface ILeaveFundService
{
    /// <summary>Osigurava (lijeno otvara) tekući fond ako su postavke podešene, pa vraća postavke + sve fondove zaposlenika.</summary>
    Task<EmployeeLeaveFundsDto> GetForEmployee(Guid organizationId, Guid userId, bool hasFullScope, Guid employeeId);

    /// <summary>Ručna korekcija/otvaranje fonda za točno određenu godinu — admin only, ne dira potrošnju.</summary>
    Task<LeaveFundDto> ManualUpsert(Guid organizationId, Guid userId, Guid employeeId, LeaveFundManualUpsertRequest request);
}