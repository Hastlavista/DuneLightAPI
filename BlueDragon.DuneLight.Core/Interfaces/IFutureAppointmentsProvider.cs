using System;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Core.Interfaces;

/// <summary>
/// Mjesto pripremljeno za budući modul Termina: provjerava ima li zaposlenik zakazane
/// buduće termine, radi upozorenja (ne zabrane) pri deaktivaciji. Dok Termini ne postoje,
/// koristi se placeholder implementacija koja uvijek vraća false.
/// </summary>
public interface IFutureAppointmentsProvider
{
    Task<bool> HasFutureAppointments(Guid organizationId, Guid employeeId);
}
