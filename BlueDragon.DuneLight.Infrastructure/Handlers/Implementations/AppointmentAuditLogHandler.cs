using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class AppointmentAuditLogHandler : IAppointmentAuditLogHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public AppointmentAuditLogHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(AppointmentAuditLog entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.AppointmentAuditLog.Add(entry);
        await context.SaveChangesAsync();
    }
}
