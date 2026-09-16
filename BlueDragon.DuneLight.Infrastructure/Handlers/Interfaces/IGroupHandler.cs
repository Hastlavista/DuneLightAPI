using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGroupHandler
{
    /// <summary>group.Slots mora biti popunjen prije poziva — cascade insert.</summary>
    Task Add(Group group);

    /// <summary>Puni graf (Service, Company, DefaultTrainer, Slots, Members.Client) — za prikaz/detalj.</summary>
    Task<Group> GetById(Guid organizationId, Guid id);

    /// <summary>Samo osnovni redak, bez navigacijskih kolekcija — za pripremu mutacije.</summary>
    Task<Group> GetByIdLight(Guid organizationId, Guid id);

    Task<List<Group>> GetAll(Guid organizationId, bool? isActive);

    Task UpdateScalar(Group group);

    /// <summary>Kao <see cref="UpdateScalar(Group)"/>, ali unutar zajedničke transakcije s audit logom — vidi IUnitOfWork.</summary>
    Task UpdateScalar(IUnitOfWork uow, Group group);

    Task<GroupSlot> GetSlotById(Guid organizationId, Guid groupId, Guid slotId);
    Task<int> CountActiveSlots(Guid groupId);
    Task AddSlot(GroupSlot slot);
    Task UpdateSlot(GroupSlot slot);

    Task<GroupMember> GetActiveMember(Guid organizationId, Guid groupId, Guid clientId);
    Task<GroupMember> GetMemberById(Guid organizationId, Guid groupId, Guid memberId);
    Task<int> CountActiveMembers(Guid groupId);
    Task AddMember(GroupMember member);

    /// <summary>Kao <see cref="AddMember(GroupMember)"/>, ali unutar zajedničke transakcije s audit logom — vidi IUnitOfWork.</summary>
    Task AddMember(IUnitOfWork uow, GroupMember member);

    Task UpdateMember(GroupMember member);

    /// <summary>Kao <see cref="UpdateMember(GroupMember)"/>, ali unutar zajedničke transakcije s audit logom — vidi IUnitOfWork.</summary>
    Task UpdateMember(IUnitOfWork uow, GroupMember member);

    /// <summary>Grupe (aktivne i povijesne) čiji je klijent član — za dopunu Klijent detalja.</summary>
    Task<List<GroupMember>> GetMembershipsByClient(Guid organizationId, Guid clientId);

    /// <summary>Ima li klijent ijedan redak članstva (aktivan ili povijesni) — group_members.client_id nema
    /// ON DELETE, pa ovo koristi ClientFutureActivityProvider da blokira tvrdo brisanje umjesto da pusti
    /// da padne na sirovoj FK grešci.</summary>
    Task<bool> HasAnyMembershipForClient(Guid organizationId, Guid clientId);

    /// <summary>Ima li klijent ijedno TRENUTNO aktivno članstvo (IsActive = true) — povijesno/napušteno
    /// članstvo se namjerno ne broji. Koristi ClientService.Anonymize.</summary>
    Task<bool> HasActiveMembershipForClient(Guid organizationId, Guid clientId);

    /// <summary>Već postojeći (GroupSlotId, StartsAt) parovi u zadanom rasponu — idempotentna provjera generiranja.</summary>
    Task<HashSet<(Guid GroupSlotId, DateTimeOffset StartsAt)>> GetExistingSlotOccurrences(
        List<Guid> groupSlotIds, DateTimeOffset from, DateTimeOffset to);

    Task AddAppointments(List<Appointment> appointments);

    Task<List<Appointment>> GetAppointmentsForGroup(Guid organizationId, Guid groupId, DateTimeOffset from, DateTimeOffset to);

    /// <summary>Ima li grupa ijedan generirani termin (bilo kada) ili ijedan članski redak (aktivan ili
    /// povijesni) — vidi spec section 42/43. Grupa bez ijedne reference smije se trajno obrisati.</summary>
    Task<bool> IsReferenced(Guid organizationId, Guid id);

    Task Delete(Group group);
}
