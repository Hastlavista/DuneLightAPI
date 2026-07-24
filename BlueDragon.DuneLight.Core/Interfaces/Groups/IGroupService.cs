using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Groups;

namespace BlueDragon.DuneLight.Core.Interfaces.Groups;

public interface IGroupService
{
    Task<GroupDto> Create(Guid organizationId, Guid userId, GroupCreateRequest request);
    Task<GroupDto> Update(Guid organizationId, Guid userId, Guid id, GroupUpdateRequest request);
    Task<GroupDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task<GroupDetailDto> GetById(Guid organizationId, Guid id);
    Task<List<GroupDto>> GetAll(Guid organizationId, bool? isActive);

    Task<GroupDto> AddSlot(Guid organizationId, Guid userId, Guid groupId, GroupSlotCreateRequest request);
    Task<GroupDto> UpdateSlot(Guid organizationId, Guid userId, Guid groupId, Guid slotId, GroupSlotUpdateRequest request);
    Task<GroupDto> RemoveSlot(Guid organizationId, Guid userId, Guid groupId, Guid slotId);

    Task<GroupDto> AddMember(Guid organizationId, Guid userId, Guid groupId, GroupMemberAddRequest request);
    Task<GroupDto> RemoveMember(Guid organizationId, Guid userId, Guid groupId, Guid memberId);

    /// <summary>Idempotentno generira grupne termine za zadani raspon — GroupId=null generira za sve aktivne grupe.</summary>
    Task<GenerateGroupAppointmentsResult> GenerateAppointments(Guid organizationId, Guid userId, GenerateGroupAppointmentsRequest request);

    /// <summary>Grupe (aktivne i povijesne) čiji je klijent član — za dopunu Klijent detalja.</summary>
    Task<List<ClientGroupMembershipDto>> GetMembershipsByClient(Guid organizationId, Guid clientId);
}
