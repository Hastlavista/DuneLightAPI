using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Groups;

namespace BlueDragon.DuneLight.Core.Interfaces.Groups;

/// <summary>
/// P2 (faza 2D, Q18/Q53) — članovi grupe preskočeni u generiranim occurrenceima zbog duga članarine uz postavku "blokiraj
/// rezervaciju": popis za recepciju i naknadno dodavanje kad dug prestane.
/// </summary>
public interface IGroupMembershipSkipService
{
    /// <summary>Preskočeni članovi grupe (otvoreni i razriješeni), redom po početku termina.</summary>
    Task<List<GroupMembershipSkipDto>> GetForGroup(Guid organizationId, Guid groupId);

    /// <summary>Q53 — kad članstvo klijenta više nije blokirano: dodavanje u preskočene BUDUĆE segmente redom po datumu termina,
    /// dok ima mjesta; pun termin, sudar rasporeda ili termin koji više nije primjenjiv se bilježi (popis recepciji). Svaki
    /// segment u vlastitoj transakciji s redoslijedom lockova kao rezervacija (subjekti → termin → članstvo). Vraća broj dodanih.</summary>
    Task<int> BackfillForClient(Guid organizationId, Guid clientId, Guid? userId);

    /// <summary>Isto za sve klijente organizacije s otvorenim preskakanjima (dnevni prolaz).</summary>
    Task<int> BackfillForOrganization(Guid organizationId);
}
