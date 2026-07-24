using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IAppointmentAuditLogHandler
{
    Task Add(AppointmentAuditLog entry);
}
