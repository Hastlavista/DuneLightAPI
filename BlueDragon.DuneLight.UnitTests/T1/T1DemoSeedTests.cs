#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Auth;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.TestTools;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.Time;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-4 — PRIVREMENI seed (uklanja se prije go-livea): nova demo organizacija razine "Osnova" ili "Puni demo" kroz pravu
/// registraciju, dopuna postojeće organizacije koja samo dodaje i nikad ne pomiče sat, reset samo za demo organizaciju (nova
/// organizacija iste razine, stara umirovljena s neaktivnim korisnicima). "Puni demo" stanja iz prošlosti (dug, završeno
/// članstvo, stajanje, potrošene sesije) dobiva skokom sata kroz ITestToolsService.AdvanceClock. Organizacije koje testovi
/// stvore brišu se u finally (<see cref="SchedulingWorld.DeleteOrganization"/>).
/// </summary>
public class T1DemoSeedTests
{
    [Fact]
    public async Task Basic_RegistersMarksDemo_OnlyCompaniesStaffUsersAndClients_AndTheUsersCanLogIn()
    {
        using IServiceScope scope = SchedulingTestHost.CreateScope();
        IDemoSeedService seed = scope.ServiceProvider.GetRequiredService<IDemoSeedService>();

        DemoOrganizationResultDto result = await seed.CreateDemoOrganization(DemoSeedLevel.Basic);
        try
        {
            Assert.Equal(DemoSeedLevel.Basic, result.Level);
            Assert.Empty(result.Skipped);
            Assert.Equal(0, result.ClockAdvancedDays);
            TestToolsOrganizationStatusDto status = await scope.ServiceProvider.GetRequiredService<ITestToolsService>().GetStatus(result.OrganizationId);
            Assert.True(status.IsDemo);
            Assert.Equal(DemoSeedLevel.Basic, status.DemoLevel);
            Assert.Null(status.RetiredAt);
            Assert.Equal(TimeSpan.Zero, status.Offset);
            Assert.Equal(status.LocalDate, result.LocalDate);

            // Osnivač (registracija) + admin, trener, recepcija i korisnik bez ovlasti; lozinke su u odgovoru i vrijede.
            Assert.Equal(8, result.Users.Count); // + Vlasnik, Trener, Trener + recepcija (grupe prvog klijenta)
            Assert.All(result.Users, u => Assert.False(string.IsNullOrEmpty(u.Password)));
            IAuthService auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            foreach (DemoSeedUserDto user in result.Users)
            {
                AuthResponse login = await auth.Login(new LoginRequest { OrganizationSlug = result.Slug, Email = user.Email, Password = user.Password });
                Assert.Equal(user.UserId, login.UserId);
            }

            DemoSeedCountsDto c = result.Counts;
            Assert.Equal((2, 1, 7, 20, 1, 5), (c.Companies, c.CompanyHolidays, c.Employees, c.Clients, c.EngagementTypes, c.GrantGroups));
            Assert.Equal((0, 0, 0, 0, 0, 0, 0, 0), (c.Rooms, c.Resources, c.Services, c.PriceListItems, c.Packages, c.MembershipPlans, c.CancellationPolicies, c.CancellationReasons));
            Assert.Equal((0, 0, 0, 0, 0, 0, 0), (c.Groups, c.GroupAppointments, c.PastAppointments, c.FutureAppointments, c.CheckoutsCompleted, c.MembershipsSold, c.ClientPackagesSold));
            Assert.Equal((0, 0), (c.CommissionRules, c.CommissionEntries));

            await using DatabaseContext db = DatabaseContext.GenerateContext(SchedulingTestHost.ConnectionString);
            Guid org = result.OrganizationId;
            Assert.False(await db.Services.AnyAsync(s => s.OrganizationId == org));
            Assert.False(await db.MembershipPlans.AnyAsync(p => p.OrganizationId == org));
            Assert.False(await db.Packages.AnyAsync(p => p.OrganizationId == org));
            Assert.False(await db.Groups.AnyAsync(g => g.OrganizationId == org));
            Assert.False(await db.Appointments.AnyAsync(a => a.OrganizationId == org));
            Assert.False(await db.Checkouts.AnyAsync(x => x.OrganizationId == org));

            // Admin (registracija) + Treneri + Recepcija; trener bez write.all, recepcija bez K2; jedan korisnik nema nijednu grupu.
            List<GrantGroup> groups = await db.GrantGroups.Include(g => g.Grants).Where(g => g.OrganizationId == org).ToListAsync();
            Assert.Equal(6, groups.Count);
            Assert.Single(groups, g => g.SystemKey == SystemGrantGroups.Admin);
            GrantGroup reception = Assert.Single(groups, g => g.Name.StartsWith("Recepcija", StringComparison.Ordinal));
            Assert.Contains(reception.Grants, g => g.GrantKey == Grants.AppointmentsWriteAll);
            Assert.DoesNotContain(reception.Grants, g => g.GrantKey.StartsWith("appointments.corrections.", StringComparison.Ordinal)
                || g.GrantKey == Grants.AppointmentsAvailabilityOverride || g.GrantKey == Grants.RosterEntriesWritePast
                || g.GrantKey == Grants.AppointmentsPolicyFeeWaive || g.GrantKey == Grants.AppointmentsPolicyUnitWaive
                || g.GrantKey == Grants.ClientsPackagesWritePast);
            GrantGroup trainers = Assert.Single(groups, g => g.Name.StartsWith("Treneri", StringComparison.Ordinal));
            Assert.Contains(trainers.Grants, g => g.GrantKey == Grants.AppointmentsWriteOwn);
            Assert.Contains(trainers.Grants, g => g.GrantKey == Grants.ClientsView);
            Assert.DoesNotContain(trainers.Grants, g => g.GrantKey == Grants.AppointmentsWriteAll);
            // Grupe prvog klijenta: korekcije, otpisi, override i *.write.past samo u grupi Vlasnik.
            bool Restricted(GrantGroupGrant g) => g.GrantKey.StartsWith("appointments.corrections.", StringComparison.Ordinal)
                || g.GrantKey == Grants.AppointmentsPolicyFeeWaive || g.GrantKey == Grants.AppointmentsPolicyUnitWaive
                || g.GrantKey == Grants.AppointmentsAvailabilityOverride || g.GrantKey.EndsWith(".write.past", StringComparison.Ordinal);
            GrantGroup owner = Assert.Single(groups, g => g.Name.StartsWith("Vlasnik", StringComparison.Ordinal));
            Assert.Contains(owner.Grants, Restricted);
            Assert.Null(owner.SystemKey);
            GrantGroup firstClientTrainer = Assert.Single(groups, g => g.Name.StartsWith("Trener #", StringComparison.Ordinal));
            Assert.DoesNotContain(firstClientTrainer.Grants, Restricted);
            Assert.DoesNotContain(firstClientTrainer.Grants, g => g.GrantKey == Grants.AppointmentsWriteAll || g.GrantKey == Grants.CheckoutManage);
            GrantGroup trainerFrontDesk = Assert.Single(groups, g => g.Name.StartsWith("Trener + recepcija", StringComparison.Ordinal));
            Assert.DoesNotContain(trainerFrontDesk.Grants, Restricted);
            Assert.Contains(trainerFrontDesk.Grants, g => g.GrantKey == Grants.CheckoutManage);
            List<Guid> usersWithGroups = await db.UserGrantGroups.Where(u => u.GrantGroup.OrganizationId == org).Select(u => u.UserId).Distinct().ToListAsync();
            Assert.Single(result.Users, u => !usersWithGroups.Contains(u.UserId));

            // Klijenti: GDPR suglasnost s datumom koji nije u budućnosti, svi s datumom rođenja.
            var clients = await db.Clients.Where(x => x.OrganizationId == org).ToListAsync();
            Assert.Equal(20, clients.Count);
            Assert.All(clients, x => Assert.True(x.GdprConsentGiven && x.GdprConsentDate <= result.LocalDate && x.DateOfBirth != null));
        }
        finally
        {
            await SchedulingWorld.DeleteOrganization(result.OrganizationId);
        }
    }

    [Fact]
    public async Task Full_ProducesEveryMembershipState_PackagesSchedulePastAttendanceAndCommissions_ThroughAClockJump_AndResetKeepsTheLevel()
    {
        using IServiceScope scope = SchedulingTestHost.CreateScope();
        IDemoSeedService seed = scope.ServiceProvider.GetRequiredService<IDemoSeedService>();
        ITestToolsService tools = scope.ServiceProvider.GetRequiredService<ITestToolsService>();

        DemoOrganizationResultDto result = await seed.CreateDemoOrganization(DemoSeedLevel.Full);
        DemoOrganizationResultDto reset = null;
        try
        {
            Assert.Equal(DemoSeedLevel.Full, result.Level);
            Assert.True(result.Skipped.Count == 0, string.Join(Environment.NewLine, result.Skipped));
            Assert.Equal(8, result.Users.Count);

            // Simulirani sat: skok 45–51 dan, novi "danas" je četvrtak (tekući tjedan ima prošle dane, sljedeći je budućnost).
            Assert.InRange(result.ClockAdvancedDays, 45, 51);
            Assert.Equal(DayOfWeek.Thursday, result.LocalDate.DayOfWeek);
            TestToolsOrganizationStatusDto status = await tools.GetStatus(result.OrganizationId);
            Assert.True(status.IsDemo);
            Assert.Equal(DemoSeedLevel.Full, status.DemoLevel);
            Assert.InRange(status.Offset.TotalDays, result.ClockAdvancedDays - 0.001, result.ClockAdvancedDays + 0.001);
            Assert.Equal(result.LocalDate, status.LocalDate);

            DemoSeedCountsDto c = result.Counts;
            Assert.Equal((3, 1, 6, 3, 4), (c.Companies, c.CompanyHolidays, c.Services, c.Packages, c.MembershipPlans));
            Assert.Equal((5, 1, 1, 1, 1, 1, 0), (c.MembershipsSold, c.MembershipsActivePaid, c.MembershipsInDebt, c.MembershipsPaused,
                c.MembershipsEnded, c.MembershipsStandingStill, c.MembershipsAwaitingPayment));
            Assert.Equal((2, 6), (c.ClientPackagesSold, c.PackageUnitsConsumed));
            Assert.Equal((6, 12, 12), (c.GroupAppointments, c.GroupAttendances, c.FutureAppointments));
            Assert.Equal(14, c.PastAppointments); // 6 odrađenih + 6 paketnih + 2 bez ishoda
            Assert.True(c.CheckoutsCompleted >= 12);
            Assert.True(c.CommissionEntries > 0);

            Guid org = result.OrganizationId;
            await using DatabaseContext db = DatabaseContext.GenerateContext(SchedulingTestHost.ConnectionString);
            Dictionary<string, Guid> clients = (await db.Clients.Where(x => x.OrganizationId == org).ToListAsync())
                .ToDictionary(x => x.FirstName, x => x.Id.Value);

            using (OrganizationClockContext.Use(org))
            {
                IClientMembershipService memberships = scope.ServiceProvider.GetRequiredService<IClientMembershipService>();
                async Task<ClientMembershipDto> MembershipOf(string firstName) =>
                    Assert.Single(await memberships.GetByClient(org, clients[firstName]));

                ClientMembershipDto active = await MembershipOf("Ivana");
                Assert.Equal((MembershipState.Active, MembershipStanding.Current, 0m), (active.State, active.Standing, active.OutstandingAmount));
                Assert.True((await memberships.GetPeriods(org, active.Id)).Count >= 2); // obnova tijekom skoka, zaostatak plaćen

                ClientMembershipDto debt = await MembershipOf("Nikola");
                Assert.Equal(MembershipStanding.Delinquent, debt.Standing);
                Assert.True(debt.OutstandingAmount > 0);
                Assert.True((await memberships.GetCharges(org, debt.Id)).Count(x => x.OutstandingAmount > 0) >= 2);

                ClientMembershipDto paused = await MembershipOf("Sara");
                Assert.Equal(MembershipState.Paused, paused.State);
                Assert.Equal(MembershipPauseSource.Client, Assert.Single(paused.Pauses).Source);

                ClientMembershipDto ended = await MembershipOf("Filip");
                Assert.Equal((MembershipState.Ended, (MembershipEndReason?)MembershipEndReason.Cancelled), (ended.State, ended.EndReason));
                Assert.True(ended.EndsOn < result.LocalDate);

                ClientMembershipDto standing = await MembershipOf("Ema");
                Assert.Equal(MembershipState.StandingStill, standing.State);
                Assert.NotNull(standing.StandingStillSince);
                Assert.Contains(standing.Pauses, p => p.Source == MembershipPauseSource.CompanyClosure && p.ActualEndsOn == null);

                IClientPackageService packages = scope.ServiceProvider.GetRequiredService<IClientPackageService>();
                ClientPackageDto remaining = Assert.Single(await packages.GetByClient(org, clients["Dario"]));
                Assert.Equal((10, 7, ClientPackageStatus.Active), (remaining.TotalEntryCount, remaining.RemainingSharedEntries, remaining.Status));
                ClientPackageDto usedUp = Assert.Single(await packages.GetByClient(org, clients["Lana"]));
                Assert.Equal((3, 0, ClientPackageStatus.Depleted), (usedUp.TotalEntryCount, usedUp.RemainingSharedEntries, usedUp.Status));
            }

            // Raspored: prošli grupni termini tekućeg tjedna imaju prisutnost, a ima i grupnih i individualnih termina sljedećeg tjedna.
            DateOnly today = result.LocalDate;
            DateOnly nextMonday = today.AddDays(4);
            var segments = await db.AppointmentSegments.Where(s => s.Appointment.OrganizationId == org)
                .Select(s => new { s.PlannedStart, s.Appointment.GroupId, s.Appointment.ClosedOutAt }).ToListAsync();
            DateTimeOffset todayStart = new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            Assert.Equal(2, segments.Count(s => s.GroupId != null && s.PlannedStart < todayStart && s.ClosedOutAt != null));
            Assert.Contains(segments, s => s.GroupId != null && s.PlannedStart >= new DateTimeOffset(nextMonday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

            // Reset stvara novu organizaciju iste razine.
            reset = await seed.ResetDemoOrganization(org);
            Assert.Equal(DemoSeedLevel.Full, reset.Level);
            Assert.Equal(DemoSeedLevel.Full, (await tools.GetStatus(reset.OrganizationId)).DemoLevel);
        }
        finally
        {
            await SchedulingWorld.DeleteOrganization(result.OrganizationId);
            if (reset != null)
                await SchedulingWorld.DeleteOrganization(reset.OrganizationId);
        }
    }

    [Fact]
    public async Task SeedExistingOrganization_OnlyAdds_NeverMovesTheClock_SellsWhatIsPossibleToday_AndListsTimeDependentStatesAsSkipped()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SeedExistingOrganization_OnlyAdds_NeverMovesTheClock_SellsWhatIsPossibleToday_AndListsTimeDependentStatesAsSkipped));
        IDemoSeedService seed = w.Resolve<IDemoSeedService>();

        // Preduvjet: bez aktivnog korisnika u Admin grupi seed odbija (nema u čije ime dodavati).
        BusinessRuleException noAdmin = await Assert.ThrowsAsync<BusinessRuleException>(() => seed.SeedExistingOrganization(w.OrganizationId));
        Assert.Equal(ErrorCodes.TestToolsNoAdminUser, noAdmin.Code);

        Guid adminGroupId;
        await using (IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            adminGroupId = await w.Resolve<IGrantGroupHandler>().CreateSystemAdminGroup(uow, w.OrganizationId);
            uow.Context.UserGrantGroups.Add(new UserGrantGroup { Id = Guid.NewGuid(), UserId = w.ActorUserId, GrantGroupId = adminGroupId });
            await uow.Context.SaveChangesAsync();
            await uow.CommitAsync();
        }

        Snapshot before = await Snapshot.Take(w);

        DemoSeedResultDto first = await seed.SeedExistingOrganization(w.OrganizationId);
        DemoSeedResultDto second = await seed.SeedExistingOrganization(w.OrganizationId);

        // Sat se ne pomiče (u ne-demo organizaciji bi pomak bio trajan).
        Assert.Equal(TimeSpan.Zero, (await w.Resolve<ITestToolsService>().GetStatus(w.OrganizationId)).Offset);
        foreach (DemoSeedResultDto run in new[] { first, second })
        {
            Assert.Equal(0, run.ClockAdvancedDays);
            // Preskočeno je samo ono što traži protok vremena (dug, završeno, stajanje; paketne sesije ovise o dobu dana).
            Assert.Contains(run.Skipped, s => s.StartsWith("Članarina s dugom", StringComparison.Ordinal));
            Assert.Contains(run.Skipped, s => s.StartsWith("Završeno članstvo", StringComparison.Ordinal));
            Assert.Contains(run.Skipped, s => s.StartsWith("Članstvo koje stoji", StringComparison.Ordinal));
            Assert.All(run.Skipped, s => Assert.True(
                s.StartsWith("Članarina s dugom", StringComparison.Ordinal) || s.StartsWith("Završeno članstvo", StringComparison.Ordinal)
                || s.StartsWith("Članstvo koje stoji", StringComparison.Ordinal) || s.StartsWith("Paket s potrošenim", StringComparison.Ordinal)
                || s.StartsWith("Potrošen paket", StringComparison.Ordinal), s));

            DemoSeedCountsDto c = run.Counts;
            Assert.Equal((3, 1, 1, 1, 0, 0, 0), (c.MembershipsSold, c.MembershipsActivePaid, c.MembershipsAwaitingPayment, c.MembershipsPaused,
                c.MembershipsInDebt, c.MembershipsEnded, c.MembershipsStandingStill));
            Assert.Equal(2, c.ClientPackagesSold);
            Assert.True(c.CheckoutsCompleted >= 4); // 2 paketa + 2 plaćene članarine
            Assert.True(c.CommissionEntries > 0);
            Assert.Equal(4, run.Users.Count);
        }
        Assert.NotEqual(first.RunTag, second.RunTag);

        Snapshot after = await Snapshot.Take(w);
        // Sve što je postojalo prije seeda postoji i dalje, nepromijenjeno (svako polje u otisku).
        Assert.Empty(before.Companies.Except(after.Companies));
        Assert.Empty(before.Services.Except(after.Services));
        Assert.Empty(before.GrantGroups.Except(after.GrantGroups));
        Assert.Empty(before.Users.Except(after.Users));
        Assert.Empty(before.WorkingHours.Except(after.WorkingHours));
        Assert.Empty(before.ServiceCompanies.Except(after.ServiceCompanies));
        Assert.Empty(before.Policies.Except(after.Policies));
        // ...a novo je dodano (dva pokretanja).
        Assert.Equal(before.Companies.Count + 4, after.Companies.Count);
        Assert.Equal(before.Services.Count + 12, after.Services.Count);
        // Po prolazu 3 nove grupe (Treneri, Recepcija, Sve ovlasti); postojeća Admin grupa ne dobiva članove.
        Assert.Equal(before.GrantGroups.Count + 6, after.GrantGroups.Count);
        Assert.Equal(before.Users.Count + 8, after.Users.Count);
        Assert.Equal(40, first.Counts.Clients + second.Counts.Clients);
        await using (DatabaseContext db = w.NewDb())
            Assert.Equal(new[] { w.ActorUserId }, db.UserGrantGroups.Where(g => g.GrantGroupId == adminGroupId).Select(g => g.UserId).ToArray());
    }

    [Fact]
    public async Task Reset_OnlyForDemoOrganizations_CreatesANewDemoOrganizationOfTheSameLevel_AndRetiresTheOldOneWithItsUsers()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Reset_OnlyForDemoOrganizations_CreatesANewDemoOrganizationOfTheSameLevel_AndRetiresTheOldOneWithItsUsers));
        IDemoSeedService seed = w.Resolve<IDemoSeedService>();

        BusinessRuleException notDemo = await Assert.ThrowsAsync<BusinessRuleException>(() => seed.ResetDemoOrganization(w.OrganizationId));
        Assert.Equal(ErrorCodes.TestToolsNotDemoOrganization, notDemo.Code);

        DemoOrganizationResultDto original = await seed.CreateDemoOrganization(DemoSeedLevel.Basic);
        DemoOrganizationResultDto reset = null;
        try
        {
            reset = await seed.ResetDemoOrganization(original.OrganizationId);

            Assert.NotEqual(original.OrganizationId, reset.OrganizationId);
            Assert.Equal(original.OrganizationId, reset.RetiredOrganizationId);
            Assert.Equal(DemoSeedLevel.Basic, reset.Level);
            Assert.Empty(reset.Skipped);

            ITestToolsService tools = w.Resolve<ITestToolsService>();
            TestToolsOrganizationStatusDto old = await tools.GetStatus(original.OrganizationId);
            TestToolsOrganizationStatusDto fresh = await tools.GetStatus(reset.OrganizationId);
            Assert.NotNull(old.RetiredAt);
            Assert.True(fresh.IsDemo);
            Assert.Equal(DemoSeedLevel.Basic, fresh.DemoLevel);
            Assert.Null(fresh.RetiredAt);
            Assert.Equal(TimeSpan.Zero, fresh.Offset);

            await using DatabaseContext db = w.NewDb();
            Assert.False(await db.Users.AnyAsync(u => u.OrganizationId == original.OrganizationId && u.IsActive));
            Assert.Equal(8, await db.Users.CountAsync(u => u.OrganizationId == reset.OrganizationId && u.IsActive));

            // Umirovljena demo organizacija se ne resetira ponovno.
            BusinessRuleException retired = await Assert.ThrowsAsync<BusinessRuleException>(() => seed.ResetDemoOrganization(original.OrganizationId));
            Assert.Equal(ErrorCodes.TestToolsNotDemoOrganization, retired.Code);
        }
        finally
        {
            await SchedulingWorld.DeleteOrganization(original.OrganizationId);
            if (reset != null)
                await SchedulingWorld.DeleteOrganization(reset.OrganizationId);
        }
    }

    /// <summary>Otisak postojećih redaka organizacije (svako relevantno polje kao string) za provjeru "samo dodaje".</summary>
    private sealed class Snapshot
    {
        public List<string> Companies { get; private init; }
        public List<string> Services { get; private init; }
        public List<string> GrantGroups { get; private init; }
        public List<string> Users { get; private init; }
        public List<string> WorkingHours { get; private init; }
        public List<string> ServiceCompanies { get; private init; }
        public List<string> Policies { get; private init; }

        public static async Task<Snapshot> Take(SchedulingWorld w)
        {
            Guid org = w.OrganizationId;
            await using DatabaseContext db = w.NewDb();
            return new Snapshot
            {
                Companies = (await db.Companies.AsNoTracking().Where(c => c.OrganizationId == org).ToListAsync())
                    .Select(c => $"{c.Id}|{c.Name}|{c.Address}|{c.TimeZone}|{c.IsActive}|{c.SortOrder}|{c.UpdatedAt}").ToList(),
                Services = (await db.Services.AsNoTracking().Where(s => s.OrganizationId == org).ToListAsync())
                    .Select(s => $"{s.Id}|{s.Name}|{s.ExecutionMode}|{s.DefaultDurationMinutes}|{s.DefaultPrice}|{s.IsActive}|{s.UpdatedAt}").ToList(),
                GrantGroups = (await db.GrantGroups.AsNoTracking().Include(g => g.Grants).Where(g => g.OrganizationId == org).ToListAsync())
                    .Select(g => $"{g.Id}|{g.Name}|{g.SystemKey}|{string.Join(",", g.Grants.Select(x => x.GrantKey).OrderBy(x => x))}").ToList(),
                Users = (await db.Users.AsNoTracking().Where(u => u.OrganizationId == org).ToListAsync())
                    .Select(u => $"{u.Id}|{u.Email}|{u.IsActive}|{u.PasswordHash}").ToList(),
                WorkingHours = (await db.WorkingHoursTemplates.AsNoTracking().Include(t => t.Intervals).Where(t => t.OrganizationId == org).ToListAsync())
                    .Select(t => $"{t.Id}|{t.EmployeeId}|{t.CompanyId}|{t.CycleType}|{t.AnchorDate}|" +
                                 string.Join(",", t.Intervals.OrderBy(i => i.CycleWeekIndex).ThenBy(i => i.DayOfWeek).ThenBy(i => i.StartTime)
                                     .Select(i => $"{i.CycleWeekIndex}/{i.DayOfWeek}/{i.StartTime}-{i.EndTime}"))).ToList(),
                ServiceCompanies = (await db.ServiceCompanies.AsNoTracking().Where(sc => sc.Service.OrganizationId == org).ToListAsync())
                    .Select(sc => $"{sc.ServiceId}|{sc.CompanyId}").ToList(),
                Policies = (await db.CancellationPolicies.AsNoTracking().Where(p => p.OrganizationId == org).ToListAsync())
                    .Select(p => $"{p.Id}|{p.Name}|{p.IsActive}|{p.IsOrganizationDefault}").ToList()
            };
        }
    }
}
