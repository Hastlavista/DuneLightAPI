using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IScheduleBreakHandler
{
    Task Add(ScheduleBreak scheduleBreak);

    Task<ScheduleBreak> GetById(Guid organizationId, Guid id);

    /// <summary>Bare redak, BEZ Employee/Company navigacija — za pripremu mutacije.</summary>
    Task<ScheduleBreak> GetByIdLight(Guid organizationId, Guid id);

    Task UpdateScalar(ScheduleBreak scheduleBreak);

    Task Delete(ScheduleBreak scheduleBreak);

    Task<List<ScheduleBreak>> GetOverlappingForEmployee(Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    /// <summary>Sve pauze trenera unutar raspona — kandidati za preklapanje cijelog recurring niza odjednom,
    /// precizna provjera po occurrenceu radi se u servisu u memoriji.</summary>
    Task<List<ScheduleBreak>> GetForEmployeeInRange(Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Kao <see cref="GetForEmployeeInRange"/>, ali za više zaposlenika u jednom upitu — za available-slots,
    /// izbjegava upit po zaposleniku u petlji.</summary>
    Task<List<ScheduleBreak>> GetForEmployeesInRange(Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    Task<List<ScheduleBreak>> GetForSchedule(Guid organizationId, ScheduleBreakQuery query);

    /// <summary>Batch insert za recurring niz — jedan SaveChangesAsync za cijeli niz.</summary>
    Task AddRange(List<ScheduleBreak> scheduleBreaks);
}
