#nullable disable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// ADR-0023 — capability je editorska projekcija nad raw grantovima: spremanje prevodi odabire iz statičnog
/// CapabilityCatalog-a u grantove i sprema samo GrantGroupGrant; authoring-state se izvodi iz grantova (najveći opseg
/// čiji je skup grantova sadržan u grupi, ostatak su ručni grantovi).
/// </summary>
public class CapabilityAuthoringTests
{
    private static GrantGroupCapabilitySelectionRequest Select(string key, CapabilitySelectedScope scope) =>
        new() { CapabilityKey = key, SelectedScope = scope };

    private static GrantGroupCapabilityWriteRequest Request(string name, IEnumerable<GrantGroupCapabilitySelectionRequest> selections, params string[] manual) =>
        new() { Name = name, CapabilitySelections = selections.ToList(), ManualGrantKeys = manual.ToList() };

    [Fact]
    public async Task Save_PersistsOnlyTheMaterializedGrants_AndReadBackDerivesTheSameSelections()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Save_PersistsOnlyTheMaterializedGrants_AndReadBackDerivesTheSameSelections));
        IGrantGroupCapabilityAuthoringService authoring = w.Resolve<IGrantGroupCapabilityAuthoringService>();

        GrantGroupAuthoringDto created = await authoring.Create(w.OrganizationId, w.ActorUserId, Request("Recepcija",
            new[]
            {
                Select("clients.manage", CapabilitySelectedScope.Manage),
                Select("schedule.appointments.manage", CapabilitySelectedScope.Own)
            },
            Grants.CatalogServicesManage));

        Assert.Equal(new[] { Grants.AppointmentsView, Grants.AppointmentsWriteOwn, Grants.CatalogServicesManage, Grants.ClientsManage, Grants.ClientsView },
            created.GrantGroup.Grants);

        GrantGroupAuthoringDto state = await authoring.GetAuthoringState(w.OrganizationId, created.GrantGroup.Id);
        Assert.Equal(new[]
            {
                new GrantGroupCapabilitySelectionDto("clients.manage", CapabilitySelectedScope.Manage),
                new GrantGroupCapabilitySelectionDto("schedule.appointments.manage", CapabilitySelectedScope.Own)
            },
            state.CapabilitySelections.OrderBy(s => s.CapabilityKey));
        // catalog.services.manage without its view grants does not satisfy any scope of its capability -> manual.
        Assert.Equal(new[] { Grants.CatalogServicesManage }, state.ManualGrantKeys);
    }

    [Fact]
    public async Task Derivation_UsesThePersistedGrants_ViewPlusManuallyAddedManageReadsAsManage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Derivation_UsesThePersistedGrants_ViewPlusManuallyAddedManageReadsAsManage));
        IGrantGroupCapabilityAuthoringService authoring = w.Resolve<IGrantGroupCapabilityAuthoringService>();

        GrantGroupAuthoringDto created = await authoring.Create(w.OrganizationId, w.ActorUserId,
            Request("Klijenti", new[] { Select("clients.manage", CapabilitySelectedScope.View) }, Grants.ClientsManage));

        GrantGroupCapabilitySelectionDto selection = Assert.Single(created.CapabilitySelections);
        Assert.Equal(new GrantGroupCapabilitySelectionDto("clients.manage", CapabilitySelectedScope.Manage), selection);
        Assert.Empty(created.ManualGrantKeys);
    }

    [Fact]
    public async Task SystemAdminGroup_DerivesEveryCapabilityAtItsHighestScope()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SystemAdminGroup_DerivesEveryCapabilityAtItsHighestScope));

        System.Guid adminId;
        await using (IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            adminId = await w.Resolve<IGrantGroupHandler>().CreateSystemAdminGroup(uow, w.OrganizationId);
            await uow.CommitAsync();
        }

        GrantGroupAuthoringDto state = await w.Resolve<IGrantGroupCapabilityAuthoringService>().GetAuthoringState(w.OrganizationId, adminId);

        Assert.Equal(CapabilityCatalog.All.Select(c => c.Key).OrderBy(k => k), state.CapabilitySelections.Select(s => s.CapabilityKey).OrderBy(k => k));
        Assert.All(state.CapabilitySelections, s => Assert.Contains(s.SelectedScope,
            new[] { CapabilitySelectedScope.On, CapabilitySelectedScope.Manage, CapabilitySelectedScope.All }));
        // All never materializes Own, so the .own grants of own/all capabilities stay visible as manual grants.
        Assert.All(state.ManualGrantKeys, k => Assert.Contains(".own", k));
    }

    [Fact]
    public async Task Save_RejectsUnknownCapability_IllegalScope_DuplicateSelection_AndManualGrantAlreadyDerived()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Save_RejectsUnknownCapability_IllegalScope_DuplicateSelection_AndManualGrantAlreadyDerived));
        IGrantGroupCapabilityAuthoringService authoring = w.Resolve<IGrantGroupCapabilityAuthoringService>();

        async Task<string> Code(GrantGroupCapabilityWriteRequest request) =>
            (await Assert.ThrowsAsync<BusinessRuleException>(() => authoring.Create(w.OrganizationId, w.ActorUserId, request))).Code;

        Assert.Equal(ErrorCodes.CapabilityUnknown,
            await Code(Request("A", new[] { Select("does.not.exist", CapabilitySelectedScope.On) })));
        Assert.Equal(ErrorCodes.CapabilityScopeIllegal,
            await Code(Request("B", new[] { Select("clients.manage", CapabilitySelectedScope.All) })));
        Assert.Equal(ErrorCodes.DuplicateCapabilitySelection,
            await Code(Request("C", new[] { Select("clients.manage", CapabilitySelectedScope.View), Select("clients.manage", CapabilitySelectedScope.Manage) })));
        Assert.Equal(ErrorCodes.GrantAlreadyCapabilityDerived,
            await Code(Request("D", new[] { Select("clients.manage", CapabilitySelectedScope.View) }, Grants.ClientsView)));

        ValidationAppException unknownGrant = await Assert.ThrowsAsync<ValidationAppException>(() =>
            authoring.Create(w.OrganizationId, w.ActorUserId, Request("E", new GrantGroupCapabilitySelectionRequest[0], "no.such.grant")));
        Assert.Equal(ErrorCodes.GrantKeyUnknown, unknownGrant.Code);
    }
}
