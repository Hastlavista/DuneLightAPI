using System;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Core.Interfaces;

/// <summary>
/// Provjerava ima li zaposlenik zakazane buduće termine, radi upozorenja (ne zabrane) pri
/// deaktivaciji. Implementacija (FutureAppointmentsProvider) upitava modul Termina; koristi
/// je EmployeeService.SetActive/Delete.
/// </summary>
public interface IFutureAppointmentsProvider
{
    Task<bool> HasFutureAppointments(Guid organizationId, Guid employeeId);
}
