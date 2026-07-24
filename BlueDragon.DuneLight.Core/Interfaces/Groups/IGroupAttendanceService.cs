using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Groups;

namespace BlueDragon.DuneLight.Core.Interfaces.Groups;

public interface IGroupAttendanceService
{
    Task<GroupAttendanceListDto> GetAttendance(Guid organizationId, Guid appointmentId);

    /// <summary>Trener smije čekirati samo na svom terminu (isto vlasništvo kao kod termina) — Admin bez ograničenja.</summary>
    Task<GroupAttendanceListDto> SetAttendance(
        Guid organizationId, Guid userId, bool isAdmin, Guid appointmentId, SetGroupAttendanceRequest request);
}
