using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IClientMembershipHandler
{
    /// <summary>Članstva klijenta s planom, uvjetima (i zakazanim uvjetima) i pauzama, za čitanje.</summary>
    Task<List<ClientMembership>> GetByClient(Guid organizationId, Guid clientId);

    Task<ClientMembership> GetById(Guid organizationId, Guid id);

    /// <summary>Pregled 2B — članstva (neponištena, nezavršena) s trajnom oznakom da izmjena plana nije primijenjena.</summary>
    Task<List<ClientMembership>> GetPlanUpdateNotApplied(Guid organizationId, Guid? planId, DateOnly today);

    /// <summary>Zaključava redak članstva (FOR UPDATE) i vraća ga praćenog, s pauzama, uvjetima i zakazanim uvjetima.</summary>
    Task<ClientMembership> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Zaključava redak klijenta (FOR UPDATE) — serijalizira provjeru preklapanja članarina istog klijenta (Q10/Q46).</summary>
    Task LockClient(IUnitOfWork uow, Guid organizationId, Guid clientId);

    /// <summary>Neponištena članstva klijenta s uslugama i poslovnicama trenutnih i zakazanih uvjeta (provjera preklapanja).</summary>
    Task<List<ClientMembership>> GetCoverageCandidates(IUnitOfWork uow, Guid organizationId, Guid clientId);

    /// <summary>Kandidati za kapacitet plana (pregled 2B, #8): neponištena članstva koja nisu završila prije danas i koja su na
    /// planu ili imaju zakazanu promjenu na njega, s grafom za izračun datuma promjene. Unutar transakcije (pod lockom plana).</summary>
    Task<List<ClientMembership>> GetCapacityCandidates(IUnitOfWork uow, Guid organizationId, Guid planId, DateOnly today);

    /// <summary>Praćena članstva plana koja nisu poništena ni završila prije danas, s pauzama i uvjetima (prijenos izmjene plana).</summary>
    Task<List<ClientMembership>> GetActiveForPlanForUpdate(IUnitOfWork uow, Guid organizationId, Guid planId, DateOnly today);

    void AddAudit(IUnitOfWork uow, ClientMembershipAuditLog entry);

    /// <summary>2C — organizacije koje imaju neponištenih članstava (scheduler obnove).</summary>
    Task<List<Guid>> GetOrganizationsWithActiveMemberships();

    /// <summary>2C — članstva organizacije koja nisu poništena ni završila prije danas (kandidati za obnovu).</summary>
    Task<List<Guid>> GetRenewalCandidateIds(Guid organizationId, DateOnly today);

    /// <summary>2C — zaključava zaduženje (FOR UPDATE) i vraća ga praćenog sa stavkama checkouta i alokacijama.</summary>
    Task<MembershipCharge> GetChargeForUpdate(IUnitOfWork uow, Guid organizationId, Guid chargeId);

    /// <summary>2C (Q16.3) — JEDINI pisac projekcije plaćenosti zaduženja: računa iz aktivnih alokacija u istoj transakciji.</summary>
    Task RefreshChargeSettlement(IUnitOfWork uow, IReadOnlyCollection<Guid> chargeIds);

    /// <summary>2C — članstva plana koja nisu poništena ni završila prije danas, za čitanje.</summary>
    Task<List<ClientMembership>> GetActiveForPlan(Guid organizationId, Guid planId, DateOnly today);
}
