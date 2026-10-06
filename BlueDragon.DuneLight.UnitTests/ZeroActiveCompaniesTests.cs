#nullable disable
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// ADR-0021 — organizacija smije imati nula aktivnih poslovnica: deaktivacija zadnje aktivne poslovnice je dopuštena
/// (nema više LAST_ACTIVE_COMPANY), a neaktivna poslovnica se može ponovno aktivirati.
/// </summary>
public class ZeroActiveCompaniesTests
{
    [Fact]
    public async Task LastActiveCompany_CanBeDeactivated_AndReactivated()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LastActiveCompany_CanBeDeactivated_AndReactivated));
        ICompanyService companies = w.Resolve<ICompanyService>();

        CompanyDto deactivated = await companies.SetActive(w.OrganizationId, w.ActorUserId, w.Company.Id.Value, false);
        Assert.False(deactivated.IsActive);

        // Ponovljena deaktivacija je idempotentna.
        CompanyDto again = await companies.SetActive(w.OrganizationId, w.ActorUserId, w.Company.Id.Value, false);
        Assert.False(again.IsActive);

        CompanyDto reactivated = await companies.SetActive(w.OrganizationId, w.ActorUserId, w.Company.Id.Value, true);
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task AllCompanies_CanBeDeactivated()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AllCompanies_CanBeDeactivated));
        ICompanyService companies = w.Resolve<ICompanyService>();
        var second = await w.AddCompany("Second company");

        await companies.SetActive(w.OrganizationId, w.ActorUserId, second.Id.Value, false);
        CompanyDto last = await companies.SetActive(w.OrganizationId, w.ActorUserId, w.Company.Id.Value, false);

        Assert.False(last.IsActive);
        Assert.False((await companies.GetById(w.OrganizationId, second.Id.Value)).IsActive);
    }
}
