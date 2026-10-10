#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Controllers.Catalog;
using BlueDragon.DuneLight.API.Controllers.Commissions;
using BlueDragon.DuneLight.API.Controllers.Organization;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-11 (naknadne odluke): paket "N dana" uključuje dan kupnje, upit rostera s preklapanjem, grantovi za čitanje
/// (catalog.cancellation-reasons.view, organization.settings.view, organization.branding.view, commissions.rules.view).
/// Provizije (neograničen paket, ograničenje paketne sesije): <see cref="T1CommissionCapTests"/>.
/// </summary>
public class T1FollowUpTests
{
    #region Paket "N dana"

    [Fact]
    public void PackageDayCount_PurchaseDayIsDayOne()
    {
        // Kupljen 1.10., "30 dana" → vrijedi do 30.10. uključivo (30 kalendarskih dana).
        Assert.Equal(new DateOnly(2026, 10, 30),
            PackageExpiryCalculator.CalculateValidUntilDate(PackageValidityType.DayCount, new DateOnly(2026, 10, 1), 30, null));
        // "1 dan" = samo dan kupnje.
        Assert.Equal(new DateOnly(2026, 10, 1),
            PackageExpiryCalculator.CalculateValidUntilDate(PackageValidityType.DayCount, new DateOnly(2026, 10, 1), 1, null));
        // Fiksni datum bez promjene.
        Assert.Equal(new DateOnly(2026, 12, 31),
            PackageExpiryCalculator.CalculateValidUntilDate(PackageValidityType.FixedDate, new DateOnly(2026, 10, 1), null, new DateOnly(2026, 12, 31)));
    }

    #endregion

    #region Roster: upit s from/to = preklapanje

    [Fact]
    public async Task RosterQuery_WithFrom_ReturnsEntriesOverlappingTheRange_IncludingOpenAndEarlierStartedAbsences()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RosterQuery_WithFrom_ReturnsEntriesOverlappingTheRange_IncludingOpenAndEarlierStartedAbsences));
        RosterType absence = await w.AddRosterType("Odsutnost", isAbsence: true);
        RosterType work = await w.AddRosterType("Rad", isAbsence: false);
        DateOnly from = new(2031, 3, 10);
        DateOnly to = new(2031, 3, 20);

        Guid openBefore = Guid.NewGuid(), multiDayBefore = Guid.NewGuid(), endedBefore = Guid.NewGuid(), workBefore = Guid.NewGuid(),
            workInside = Guid.NewGuid(), openAfter = Guid.NewGuid();
        await using (DatabaseContext db = w.NewDb())
        {
            RosterEntry Absence(Guid id, DateOnly dateFrom, DateOnly? dateTo) => new()
            {
                Id = id, OrganizationId = w.OrganizationId, EmployeeId = w.Employee.Id.Value, RosterTypeId = absence.Id.Value,
                DateFrom = dateFrom, DateTo = dateTo, CreatedAt = TestClock.UtcNow
            };
            RosterEntry Work(Guid id, DateOnly day) => new()
            {
                Id = id, OrganizationId = w.OrganizationId, EmployeeId = w.Employee.Id.Value, RosterTypeId = work.Id.Value,
                DateFrom = day, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0), DurationHours = 4m, CreatedAt = TestClock.UtcNow
            };
            db.RosterEntries.AddRange(
                Absence(openBefore, new DateOnly(2031, 3, 1), null),                         // otvorena, počela prije from → DA
                Absence(multiDayBefore, new DateOnly(2031, 3, 5), new DateOnly(2031, 3, 12)), // počela prije from, traje u rasponu → DA
                Absence(endedBefore, new DateOnly(2031, 3, 1), new DateOnly(2031, 3, 9)),     // završila prije from → NE
                Work(workBefore, new DateOnly(2031, 3, 9)),                                    // rad (DateTo null) prije from → NE
                Work(workInside, new DateOnly(2031, 3, 15)),                                   // rad u rasponu → DA
                Absence(openAfter, new DateOnly(2031, 3, 21), null));                         // počinje nakon to → NE
            await db.SaveChangesAsync();
        }

        PagedResult<RosterEntryDto> page = await w.Resolve<IRosterEntryService>().GetPaged(
            w.OrganizationId, new PagedRequest { Page = 1, PageSize = 50 }, w.Employee.Id.Value, null, from, to);

        // CHANGED in T1 (T1-11): prije se otvorena odsutnost koja je počela prije from nije vraćala.
        Assert.Equal(new[] { openBefore, multiDayBefore, workInside }.OrderBy(id => id), page.Items.Select(e => e.Id).OrderBy(id => id));
        Assert.Equal(3, page.TotalCount);
    }

    #endregion

    #region Grantovi za čitanje

    public static IEnumerable<object[]> ReadEndpoints() => new[]
    {
        new object[] { typeof(CancellationReasonsController), nameof(CancellationReasonsController.GetAll), Grants.CatalogCancellationReasonsView, Grants.CatalogCancellationReasonsManage },
        new object[] { typeof(OrganizationSettingsController), nameof(OrganizationSettingsController.GetSettings), Grants.OrganizationSettingsView, Grants.OrganizationSettingsManage },
        new object[] { typeof(OrganizationBrandingController), nameof(OrganizationBrandingController.GetBranding), Grants.OrganizationBrandingView, Grants.OrganizationBrandingManage },
        new object[] { typeof(CommissionRulesController), nameof(CommissionRulesController.GetList), Grants.CommissionsRulesView, Grants.CommissionsManage },
        new object[] { typeof(CommissionRulesController), nameof(CommissionRulesController.GetById), Grants.CommissionsRulesView, Grants.CommissionsManage },
        new object[] { typeof(CommissionsController), nameof(CommissionsController.GetSettings), Grants.CommissionsRulesView, Grants.CommissionsManage },
    };

    private sealed class FixedGrants : IGrantResolver
    {
        private readonly HashSet<string> _grants;
        public FixedGrants(params string[] grants) => _grants = grants.ToHashSet();
        public Task<GrantContext> Resolve(Guid organizationId, Guid userId) => Task.FromResult(new GrantContext(_grants));
    }

    private static async Task<IActionResult> Authorize(Type controller, string action, params string[] userGrants)
    {
        RequireGrantAttribute attribute = controller.GetMethod(action, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .GetCustomAttribute<RequireGrantAttribute>();
        Assert.NotNull(attribute);

        DefaultHttpContext http = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim("organizationId", Guid.NewGuid().ToString())
            }, "Test")),
            RequestServices = new ServiceCollection().AddSingleton<IGrantResolver>(new FixedGrants(userGrants)).BuildServiceProvider()
        };
        AuthorizationFilterContext context = new(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
        await attribute.OnAuthorizationAsync(context);
        return context.Result;
    }

    [Theory]
    [MemberData(nameof(ReadEndpoints))]
    public async Task ReadEndpoint_IsAllowedWithTheViewGrantOnly_AndWithManage(Type controller, string action, string viewGrant, string manageGrant)
    {
        Assert.Null(await Authorize(controller, action, viewGrant));
        Assert.Null(await Authorize(controller, action, manageGrant));
    }

    [Theory]
    [MemberData(nameof(ReadEndpoints))]
    public async Task ReadEndpoint_WithoutAnAcceptedGrant_Is403MissingGrant_ListingTheAcceptedGrants(Type controller, string action, string viewGrant, string manageGrant)
    {
        ObjectResult result = Assert.IsType<ObjectResult>(await Authorize(controller, action, Grants.ClientsView));
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        ErrorResponse error = Assert.IsType<ErrorResponse>(result.Value);
        Assert.Equal(ErrorCodes.Forbidden, error.Error.Code);
        ForbiddenDetails details = Assert.IsType<ForbiddenDetails>(error.Error.Details);
        Assert.Equal(ForbiddenReason.MissingGrant, details.Reason);
        Assert.Contains(viewGrant, details.RequiredGrants);
        Assert.Contains(manageGrant, details.RequiredGrants);
        Assert.Equal(GrantMatch.Any, details.Match);
    }

    [Fact]
    public async Task CancellationReasons_StillReadableByThoseWhoCancel()
    {
        foreach (string grant in new[] { Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll, Grants.GroupsAttendanceOwn, Grants.GroupsAttendanceAll })
            Assert.Null(await Authorize(typeof(CancellationReasonsController), nameof(CancellationReasonsController.GetAll), grant));
    }

    [Fact]
    public async Task WriteEndpoints_StillNeedManage_NotView()
    {
        Assert.NotNull(await Authorize(typeof(CommissionsController), nameof(CommissionsController.UpdateSettings), Grants.CommissionsRulesView));
        Assert.NotNull(await Authorize(typeof(CommissionRulesController), nameof(CommissionRulesController.Create), Grants.CommissionsRulesView));
        Assert.NotNull(await Authorize(typeof(OrganizationSettingsController), nameof(OrganizationSettingsController.UpdateTimeZone), Grants.OrganizationSettingsView));
        Assert.NotNull(await Authorize(typeof(OrganizationBrandingController), nameof(OrganizationBrandingController.UpdateColors), Grants.OrganizationBrandingView));
        Assert.NotNull(await Authorize(typeof(CancellationReasonsController), nameof(CancellationReasonsController.Create), Grants.CatalogCancellationReasonsView));
    }

    public static IEnumerable<object[]> ViewCapabilities() => new[]
    {
        new object[] { "catalog.cancellation-reasons.manage", Grants.CatalogCancellationReasonsView, Grants.CatalogCancellationReasonsManage },
        new object[] { "organization.settings.manage", Grants.OrganizationSettingsView, Grants.OrganizationSettingsManage },
        new object[] { "organization.branding.manage", Grants.OrganizationBrandingView, Grants.OrganizationBrandingManage },
        new object[] { "commissions.manage", Grants.CommissionsRulesView, Grants.CommissionsManage },
    };

    [Theory]
    [MemberData(nameof(ViewCapabilities))]
    public void Capability_ViewLevelGivesTheViewGrant_ManageIncludesIt(string capabilityKey, string viewGrant, string manageGrant)
    {
        CapabilityDefinition capability = CapabilityCatalog.Find(capabilityKey);
        Assert.NotNull(capability);
        Assert.Equal(CapabilityScopeModel.ViewManage, capability.ScopeModel);

        HashSet<string> view = TestSupport.Materializer.Materialize(capability.ScopeModel, CapabilitySelectedScope.View, capability.Grants);
        HashSet<string> manage = TestSupport.Materializer.Materialize(capability.ScopeModel, CapabilitySelectedScope.Manage, capability.Grants);
        Assert.Contains(viewGrant, view);
        Assert.DoesNotContain(manageGrant, view);
        Assert.Contains(viewGrant, manage);
        Assert.Contains(manageGrant, manage);
    }

    #endregion
}
