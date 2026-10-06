#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Controllers.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// Resource catalog authorization (Phase C): grant-only endpoints (catalog.resources.view / catalog.resources.manage,
/// mirroring catalog.rooms.*) and the catalog.resources.manage capability in the static CapabilityCatalog (ADR-0023).
/// </summary>
public class ResourceAuthorizationTests
{
    #region Endpoints

    public static IEnumerable<object[]> CatalogControllers() => new[]
    {
        new object[] { typeof(ResourcesController), Grants.CatalogResourcesView, Grants.CatalogResourcesManage },
        new object[] { typeof(RoomsController), Grants.CatalogRoomsView, Grants.CatalogRoomsManage },
    };

    [Theory]
    [MemberData(nameof(CatalogControllers))]
    public void EveryAction_RequiresExactlyTheViewOrManageGrant(Type controller, string viewGrant, string manageGrant)
    {
        List<MethodInfo> actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(7, actions.Count); // list, get, create, update, activate, deactivate, delete

        foreach (MethodInfo action in actions)
        {
            RequireGrantAttribute attribute = action.GetCustomAttribute<RequireGrantAttribute>();
            Assert.NotNull(attribute);
            string expected = action.GetCustomAttribute<HttpGetAttribute>() != null ? viewGrant : manageGrant;
            Assert.Equal(new[] { expected }, attribute.Grants);

            // Grant-only: no other authorization attribute (role/owner/assigned-company) on the action or controller.
            Assert.DoesNotContain(action.GetCustomAttributes().Concat(controller.GetCustomAttributes()),
                a => a is not RequireGrantAttribute && a.GetType().Name.Contains("Require", StringComparison.Ordinal));
            Assert.DoesNotContain(action.GetCustomAttributes().Concat(controller.GetCustomAttributes()),
                a => a.GetType().Name.StartsWith("Authorize", StringComparison.Ordinal));
        }
    }

    #endregion

    #region Grant and capability catalog

    [Fact]
    public void ResourceGrants_AreInTheCatalog()
    {
        List<string> catalog = Grants.Catalog.Select(g => g.Key).ToList();
        Assert.Contains("catalog.resources.view", catalog);
        Assert.Contains("catalog.resources.manage", catalog);
    }

    [Fact]
    public void ResourceCapability_ViewGrantsViewOnly_ManageGrantsBoth()
    {
        CapabilityDefinition capability = CapabilityCatalog.Find("catalog.resources.manage");
        Assert.NotNull(capability);
        Assert.Equal(CapabilityScopeModel.ViewManage, capability.ScopeModel);

        Assert.Equal(new HashSet<string> { Grants.CatalogResourcesView },
            TestSupport.Materializer.Materialize(capability.ScopeModel, CapabilitySelectedScope.View, capability.Grants));
        Assert.Equal(new HashSet<string> { Grants.CatalogResourcesView, Grants.CatalogResourcesManage },
            TestSupport.Materializer.Materialize(capability.ScopeModel, CapabilitySelectedScope.Manage, capability.Grants));
    }

    #endregion
}
