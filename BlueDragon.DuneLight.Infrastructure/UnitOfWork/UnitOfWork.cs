using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlueDragon.DuneLight.Infrastructure.UnitOfWork;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IDbContextTransaction _transaction;
    private bool _committed;

    private UnitOfWork(DatabaseContext context, IDbContextTransaction transaction)
    {
        Context = context;
        _transaction = transaction;
    }

    public static async Task<IUnitOfWork> Begin(string connectionString)
    {
        DatabaseContext context = DatabaseContext.GenerateContext(connectionString);
        IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        return new UnitOfWork(context, transaction);
    }

    public DatabaseContext Context { get; }

    public async Task CommitAsync()
    {
        await Context.SaveChangesAsync();
        await _transaction.CommitAsync();
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_committed)
            await _transaction.RollbackAsync();

        await _transaction.DisposeAsync();
        await Context.DisposeAsync();
    }
}
