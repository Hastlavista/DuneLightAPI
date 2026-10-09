using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;

namespace BlueDragon.DuneLight.Core.Interfaces.Organization;

/// <summary>T1 — trenutno (poslovno) vrijeme organizacije za prikaz i za frontend "sada"/"danas".</summary>
public interface IOrganizationClockService
{
    Task<OrganizationClockDto> GetClock(Guid organizationId);
}
