#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.UnitTests.K1;

/// <summary>
/// K1-3 (P-15) — broj člana: automatski najveći + 1 pod lockom organizacije; ručni upis (prijenos iz Excela) uz jedinstvenost i
/// broj ≥ 1; skok veći od 1000 iznad najvećeg traži potvrdu; utrka na unique indeksu daje DUPLICATE_MEMBER_NUMBER, ne 500.
/// </summary>
public class MemberNumberTests
{
    private static ClientCreateRequest NewClient(int? memberNumber = null, bool confirmJump = false) => new()
    {
        MemberNumber = memberNumber,
        ConfirmMemberNumberJump = confirmJump,
        FirstName = "Broj",
        LastName = "Člana"
    };

    private static async Task<int> MaxMemberNumber(SchedulingWorld w)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.Clients.Where(c => c.OrganizationId == w.OrganizationId).MaxAsync(c => c.MemberNumber);
    }

    [Fact]
    public async Task WithoutNumber_TheNextNumberIsAssignedAutomatically()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WithoutNumber_TheNextNumberIsAssignedAutomatically));
        IClientService clients = w.Resolve<IClientService>();
        int max = await MaxMemberNumber(w);

        ClientDto first = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient());
        ClientDto second = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient());

        Assert.Equal(max + 1, first.MemberNumber);
        Assert.Equal(max + 2, second.MemberNumber);
    }

    [Fact]
    public async Task ConcurrentAutomaticNumbers_AreAllDistinct()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ConcurrentAutomaticNumbers_AreAllDistinct));
        int max = await MaxMemberNumber(w);

        // Svaki poziv u vlastitom DI scopeu (paralelno), lock organizacije serijalizira dodjelu.
        ClientDto[] created = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            using IServiceScope scope = SchedulingTestHost.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IClientService>().Create(w.OrganizationId, w.ActorUserId, NewClient());
        })));

        Assert.Equal(Enumerable.Range(max + 1, 6), created.Select(c => c.MemberNumber).OrderBy(n => n));
    }

    [Fact]
    public async Task ManualNumber_IsKept_MustBeFree_AndAutomaticCountingContinuesFromIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualNumber_IsKept_MustBeFree_AndAutomaticCountingContinuesFromIt));
        IClientService clients = w.Resolve<IClientService>();
        int manual = await MaxMemberNumber(w) + 10;

        ClientDto imported = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(manual));
        Assert.Equal(manual, imported.MemberNumber);

        BusinessRuleException duplicate = await Assert.ThrowsAsync<BusinessRuleException>(
            () => clients.Create(w.OrganizationId, w.ActorUserId, NewClient(manual)));
        Assert.Equal(ErrorCodes.DuplicateMemberNumber, duplicate.Code);

        ClientDto next = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient());
        Assert.Equal(manual + 1, next.MemberNumber);
    }

    [Fact]
    public async Task ZeroOrNegativeNumber_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ZeroOrNegativeNumber_IsRejected));
        IClientService clients = w.Resolve<IClientService>();

        await Assert.ThrowsAsync<ValidationAppException>(() => clients.Create(w.OrganizationId, w.ActorUserId, NewClient(0)));
        await Assert.ThrowsAsync<ValidationAppException>(() => clients.Create(w.OrganizationId, w.ActorUserId, NewClient(-5)));
    }

    [Fact]
    public async Task NumberFarAboveTheMaximum_NeedsConfirmation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(NumberFarAboveTheMaximum_NeedsConfirmation));
        IClientService clients = w.Resolve<IClientService>();
        int max = await MaxMemberNumber(w);

        // Do +1000 bez potvrde.
        ClientDto near = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(max + 1000));
        Assert.Equal(max + 1000, near.MemberNumber);

        int far = max + 1000 + 1001;
        BusinessRuleException jump = await Assert.ThrowsAsync<BusinessRuleException>(
            () => clients.Create(w.OrganizationId, w.ActorUserId, NewClient(far)));
        Assert.Equal(ErrorCodes.MemberNumberJumpNotConfirmed, jump.Code);
        Assert.Equal(max + 1000, await MaxMemberNumber(w)); // ništa nije spremljeno

        ClientDto confirmed = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(far, confirmJump: true));
        Assert.Equal(far, confirmed.MemberNumber);
    }

    [Fact]
    public async Task Update_WithoutNumberKeepsIt_ChangeMustBeFree()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_WithoutNumberKeepsIt_ChangeMustBeFree));
        IClientService clients = w.Resolve<IClientService>();
        ClientDto a = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient());
        ClientDto b = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient());

        ClientDto kept = await clients.Update(w.OrganizationId, w.ActorUserId, a.Id,
            new ClientUpdateRequest { FirstName = "Novo", LastName = "Ime" });
        Assert.Equal(a.MemberNumber, kept.MemberNumber);

        BusinessRuleException duplicate = await Assert.ThrowsAsync<BusinessRuleException>(() => clients.Update(
            w.OrganizationId, w.ActorUserId, a.Id, new ClientUpdateRequest { MemberNumber = b.MemberNumber, FirstName = "X", LastName = "Y" }));
        Assert.Equal(ErrorCodes.DuplicateMemberNumber, duplicate.Code);
    }
}
