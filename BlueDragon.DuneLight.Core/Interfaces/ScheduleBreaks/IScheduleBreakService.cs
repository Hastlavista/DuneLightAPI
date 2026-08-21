using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;

namespace BlueDragon.DuneLight.Core.Interfaces.ScheduleBreaks;

public interface IScheduleBreakService
{
    Task<ScheduleBreakDto> Create(Guid organizationId, Guid userId, bool hasFullScope, ScheduleBreakCreateRequest request);

    Task<ScheduleBreakDto> Update(Guid organizationId, Guid userId, bool hasFullScope, Guid id, ScheduleBreakUpdateRequest request);

    /// <summary>Pravo brisanje — nema statusa "otkazano" za pauzu. Kod ponavljajuće pauze dira samo taj occurrence.</summary>
    Task Delete(Guid organizationId, Guid userId, bool hasFullScope, Guid id);

    Task<List<ScheduleBreakDto>> CreateRecurring(Guid organizationId, Guid userId, bool hasFullScope, RecurringScheduleBreakCreateRequest request);

    Task<List<ScheduleBreakDto>> GetList(Guid organizationId, ScheduleBreakQuery query);

    /// <summary>Lagani cell-DTO oblik — koristi ga AppointmentsController za sastavljanje ScheduleFeedDto.Breaks.</summary>
    Task<List<ScheduleBreakCellDto>> GetScheduleCells(Guid organizationId, ScheduleBreakQuery query);

    Task<ScheduleBreakDto> GetById(Guid organizationId, Guid id);
}
