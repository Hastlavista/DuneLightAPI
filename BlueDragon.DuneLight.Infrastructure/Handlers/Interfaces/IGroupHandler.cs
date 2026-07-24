using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGroupHandler
{
    /// <summary>group.Slots mora biti popunjen prije poziva — cascade insert.</summary>
    Task Add(Group group);

    /// <summary>Puni graf (Service, Location, DefaultTrainer, Slots, Members.Client) — za prikaz/detalj.</summary>
    Task<Group> GetById(Guid organizationId, Guid id);

    /// <summary>Samo osnovni redak, bez navigacijskih kolekcija — za pripremu mutacije.</summary>
    Task<Group> GetByIdLight(Guid organizationId, Guid id);

    Task<List<Group>> GetAll(Guid organizationId, bool? isActive);

    Task UpdateScalar(Group group);

    Task<GroupSlot> GetSlotById(Guid groupId, Guid slotId);
    Task<int> CountActiveSlots(Guid groupId);
    Task AddSlot(GroupSlot slot);
    Task UpdateSlot(GroupSlot slot);

    Task<GroupMember> GetActiveMember(Guid groupId, Guid clientId);
    Task<GroupMember> GetMemberById(Guid groupId, Guid memberId);
    Task<int> CountActiveMembers(Guid groupId);
    Task AddMember(GroupMember member);
    Task UpdateMember(GroupMember member);

    /// <summary>Grupe (aktivne i povijesne) čiji je klijent član — za dopunu Klijent detalja.</summary>
    Task<List<GroupMember>> GetMembershipsByClient(Guid organizationId, Guid clientId);

    /// <summary>Već postojeći (GroupSlotId, StartsAt) parovi u zadanom rasponu — idempotentna provjera generiranja.</summary>
    Task<HashSet<(Guid GroupSlotId, DateTimeOffset StartsAt)>> GetExistingSlotOccurrences(
        List<Guid> groupSlotIds, DateTimeOffset from, DateTimeOffset to);

    Task AddAppointments(List<Appointment> appointments);

    Task<List<Appointment>> GetAppointmentsForGroup(Guid organizationId, Guid groupId, DateTimeOffset from, DateTimeOffset to);
}
