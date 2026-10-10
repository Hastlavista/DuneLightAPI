#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Auth;
using BlueDragon.DuneLight.Core.DTOs.TestTools;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-10 (odluka "Tri odluke T1 (3)") — "Osnova" mora imati sve što sustav sam daje novoj organizaciji, točno kao stvarna
/// registracija (<c>IAuthService.Register</c>): Admin grupa ovlasti (system_key 'admin') sa svim grantovima i osnivačem, zadani
/// tipovi rostera, neutralna zadana politika otkazivanja; sustav NE stvara razloge otkazivanja ni redak postavki organizacije
/// (zadane postavke su vrijednosti kad retka nema). Seed "Osnove" smije dodati samo podatke studija (poslovnice, zaposlenici,
/// grupe ovlasti, klijenti, praznik) — ne smije ništa od sustavskog preskočiti, udvostručiti ni promijeniti.
/// </summary>
public class T1OnboardingParityTests
{
    [Fact]
    public async Task Basic_HasExactlyWhatARealRegistrationProvides_NothingSkippedDuplicatedOrChanged()
    {
        using IServiceScope scope = SchedulingTestHost.CreateScope();
        Guid suffix = Guid.NewGuid();
        AuthResponse registered = await scope.ServiceProvider.GetRequiredService<IAuthService>().Register(new RegisterRequest
        {
            OrganizationName = $"Onboarding parity {suffix:N}",
            Email = $"founder-{suffix:N}@parity.test",
            Password = "Founder-Password-1"
        });
        DemoOrganizationResultDto basic = await scope.ServiceProvider.GetRequiredService<IDemoSeedService>().CreateDemoOrganization(DemoSeedLevel.Basic);
        try
        {
            SystemProvided fresh = await Read(registered.OrganizationId.GetValueOrDefault(), registered.UserId.GetValueOrDefault());
            SystemProvided osnova = await Read(basic.OrganizationId, basic.Users.First().UserId); // prvi korisnik = osnivač (registracija)

            // Ono što sustav daje sam (popis za T1-10): nije prazno …
            Assert.Single(fresh.AdminGroups);
            Assert.True(fresh.FounderInAdminGroup);
            Assert.NotEmpty(fresh.RosterTypes);
            Assert.Single(fresh.Policies);
            Assert.Equal(0, fresh.CancellationReasons);
            Assert.Equal(0, fresh.SettingsRows);

            // … i "Osnova" ga ima jednako.
            Assert.Equal(fresh.AdminGroups, osnova.AdminGroups);
            Assert.Equal(fresh.FounderInAdminGroup, osnova.FounderInAdminGroup);
            Assert.Equal(fresh.RosterTypes, osnova.RosterTypes);
            Assert.Equal(fresh.Policies, osnova.Policies);
            Assert.Equal(fresh.CancellationReasons, osnova.CancellationReasons);
            Assert.Equal(fresh.SettingsRows, osnova.SettingsRows);
        }
        finally
        {
            await SchedulingWorld.DeleteOrganization(registered.OrganizationId.GetValueOrDefault());
            await SchedulingWorld.DeleteOrganization(basic.OrganizationId);
        }
    }

    private sealed record SystemProvided(
        List<string> AdminGroups, bool FounderInAdminGroup, List<string> RosterTypes, List<string> Policies, int CancellationReasons, int SettingsRows);

    private static async Task<SystemProvided> Read(Guid org, Guid founderUserId)
    {
        await using DatabaseContext db = DatabaseContext.GenerateContext(SchedulingTestHost.ConnectionString);

        var systemGroups = await db.GrantGroups.Include(g => g.Grants)
            .Where(g => g.OrganizationId == org && g.SystemKey != null).ToListAsync();
        List<string> adminGroups = systemGroups
            .Select(g => $"{g.SystemKey}|{g.Name}|{string.Join(",", g.Grants.Select(x => x.GrantKey).OrderBy(k => k, StringComparer.Ordinal))}")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        List<Guid> systemGroupIds = systemGroups.Select(g => g.Id.GetValueOrDefault()).ToList();
        bool founderInAdmin = await db.UserGrantGroups.AnyAsync(u => u.UserId == founderUserId && systemGroupIds.Contains(u.GrantGroupId));

        List<string> rosterTypes = (await db.RosterTypes.Where(t => t.OrganizationId == org).ToListAsync())
            .Select(t => $"{t.Name}|{t.ColorHex}|{t.CountsAsWork}|{t.IsAbsence}|{t.RequiresTime}|{t.DeductsFromLeaveFund}|{t.SortOrder}|{t.IsActive}")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        List<string> policies = (await db.CancellationPolicies.Include(p => p.Versions).Where(p => p.OrganizationId == org).ToListAsync())
            .Select(p => $"{p.Name}|{p.IsActive}|{p.IsOrganizationDefault}|" + string.Join(";", p.Versions.OrderBy(v => v.Version).Select(v =>
                $"{v.Version}|{v.CancellationWindowMinutes}|{v.LateCancellationFeeType}|{v.LateCancellationFeeValue}|{v.LateCancellationPackageAction}|" +
                $"{v.LateCancellationMembershipAction}|{v.NoShowFeeType}|{v.NoShowFeeValue}|{v.NoShowPackageAction}|{v.NoShowMembershipAction}")))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        int reasons = await db.CancellationReasons.CountAsync(r => r.OrganizationId == org);
        int settings = await db.OrganizationSettings.CountAsync(s => s.OrganizationId == org);
        return new SystemProvided(adminGroups, founderInAdmin, rosterTypes, policies, reasons, settings);
    }
}
