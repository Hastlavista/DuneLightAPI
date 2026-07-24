using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Provjerava ima li zaposlenik zakazane buduće termine — koristi se za upozorenje (ne zabranu) pri deaktivaciji.</summary>
public class FutureAppointmentsProvider : IFutureAppointmentsProvider
{
    private readonly IAppointmentHandler _appointmentHandler;

    public FutureAppointmentsProvider(IAppointmentHandler appointmentHandler)
    {
        _appointmentHandler = appointmentHandler;
    }

    public Task<bool> HasFutureAppointments(Guid organizationId, Guid employeeId)
    {
        return _appointmentHandler.HasFutureScheduledForEmployee(organizationId, employeeId);
    }
}
