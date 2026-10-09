#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.K1;

/// <summary>
/// K1-7 — generiranje grupnih termina: praznik uz potvrdu (P-5), grupe neaktivnih poslovnica se preskaču i navode (bug a),
/// izričito tražena takva grupa je INACTIVE_COMPANY.
/// </summary>
public class GroupGenerationTests
{
    [Fact]
    public async Task Holiday_WithOverride_IsGeneratedWithAWarning()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Holiday_WithOverride_IsGeneratedWithAWarning));
        ServiceEntity svc = await w.AddGroupService();
        await w.AddCompanyHoliday(w.Company, SchedulingWorld.FutureDay);
        GroupDto group = await w.CreateGroup(svc, capacity: 5);

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay, overrideAvailability: true);

        Assert.Equal(1, result.CreatedCount);
        Assert.Empty(result.Skipped);
        Assert.Contains(Assert.Single(result.Created).Warnings, x => x.Code == WarningCodes.CompanyClosedHoliday);
    }

    [Fact]
    public async Task GroupOfAnInactiveCompany_RequestedExplicitly_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupOfAnInactiveCompany_RequestedExplicitly_IsRejected));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.SetCompanyActive(w.Company, false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveCompany, () => w.GenerateOccurrences(group, SchedulingWorld.FutureDay));
        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task GenerateAll_SkipsGroupsOfInactiveCompanies_AndListsThem()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GenerateAll_SkipsGroupsOfInactiveCompanies_AndListsThem));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.SetCompanyActive(w.Company, false);

        GenerateGroupAppointmentsResult result = await w.Groups.GenerateAppointments(w.OrganizationId, w.ActorUserId,
            new GenerateGroupAppointmentsRequest { FromDate = SchedulingWorld.Day(SchedulingWorld.FutureDay), ToDate = SchedulingWorld.Day(SchedulingWorld.FutureDay) });

        Assert.Equal(0, result.CreatedCount);
        GroupGenerationSkipDto skipped = Assert.Single(result.Skipped);
        Assert.Equal(GroupGenerationSkipReason.CompanyInactive, skipped.Reason);
        Assert.Equal(group.Id, skipped.GroupId);
        Assert.Null(skipped.Date);
        Assert.Equal(0, await w.CountAppointments());
    }
}
