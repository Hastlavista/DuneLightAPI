using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;

namespace BlueDragon.DuneLight.Infrastructure.UnitOfWork;

/// <summary>
/// Jedan DbContext + jedna DB transakcija za trajanje jedne poslovne operacije koja piše u više tablica
/// preko više handlera (npr. termin + odbijanje ulaska iz paketa + audit log). Bez ovoga svaki handler
/// poziv otvara vlastiti context i sprema neovisno, pa djelomičan neuspjeh usred niza poziva ostavlja
/// nekonzistentno stanje (npr. termin spremljen, paket ne). Handler metode koje prime IUnitOfWork rade
/// nad <see cref="Context"/> umjesto da otvaraju vlastiti — <see cref="CommitAsync"/> commita transakciju;
/// DisposeAsync bez prethodnog CommitAsync (npr. zbog bačene iznimke) radi rollback.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    DatabaseContext Context { get; }

    Task CommitAsync();
}
