using System;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>P2 (Q18/Q53) — read model preskočenog člana grupe.</summary>
public static class GroupMembershipSkips
{
    public static GroupMembershipSkipDto ToDto(GroupOccurrenceMembershipSkip skip, DateTimeOffset plannedStart) => new()
    {
        Id = skip.Id,
        GroupId = skip.GroupId,
        AppointmentId = skip.AppointmentId,
        AppointmentSegmentId = skip.AppointmentSegmentId,
        PlannedStart = plannedStart,
        ClientId = skip.ClientId,
        ClientMembershipId = skip.ClientMembershipId,
        SkippedAt = skip.SkippedAt,
        Resolution = skip.Resolution,
        ResolvedAt = skip.ResolvedAt,
        ParticipationId = skip.ParticipationId
    };
}
