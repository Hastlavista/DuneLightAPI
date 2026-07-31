using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IAppointmentAuditLogHandler
{
    Task Add(AppointmentAuditLog entry);

    /// <summary>Kao <see cref="Add(AppointmentAuditLog)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task Add(IUnitOfWork uow, AppointmentAuditLog entry);
}
