using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Infrastructure.UnitOfWork;

public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> Begin();
}
