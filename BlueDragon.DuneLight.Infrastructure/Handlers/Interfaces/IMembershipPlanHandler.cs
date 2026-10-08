using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IMembershipPlanHandler
{
    /// <summary>Svi planovi organizacije s verzijama (i njihovim uslugama, poslovnicama i limitima), za čitanje.</summary>
    Task<List<MembershipPlan>> GetAll(Guid organizationId);

    Task<MembershipPlan> GetById(Guid organizationId, Guid id);

    /// <summary>Zaključava redak plana (FOR UPDATE) i vraća ga praćenog u kontekstu transakcije.</summary>
    Task<MembershipPlan> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Najnovija verzija plana s uslugama, poslovnicama i limitima (grandfathering i klasifikacija izmjene pri objavi,
    /// prodaja), unutar transakcije.</summary>
    Task<MembershipPlanVersion> GetLatestVersion(IUnitOfWork uow, Guid planId);

    Task<bool> ActiveNameExists(IUnitOfWork uow, Guid organizationId, string name, Guid? exceptId);
}
