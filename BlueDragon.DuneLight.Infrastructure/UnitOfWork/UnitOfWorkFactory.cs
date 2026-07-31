using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;

namespace BlueDragon.DuneLight.Infrastructure.UnitOfWork;

public class UnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly DatabaseSettings _databaseSettings;

    public UnitOfWorkFactory(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public Task<IUnitOfWork> Begin()
    {
        return UnitOfWork.Begin(_databaseSettings.ConnectionString);
    }
}
