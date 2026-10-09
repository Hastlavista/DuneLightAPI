using System;
using System.Threading;

namespace BlueDragon.DuneLight.Infrastructure.Time;

/// <summary>
/// T1 — organizacija čiji poslovni sat vrijedi u trenutnom toku izvršavanja (AsyncLocal). Postavlja je middleware iz tokena
/// (tenant zahtjev), pozadinski servisi po organizaciji i testni alati za ciljnu organizaciju. Bez postavljene organizacije
/// poslovni sat je stvarni sat (registracija, Management, platforma).
/// </summary>
public static class OrganizationClockContext
{
    private static readonly AsyncLocal<Guid?> CurrentOrganization = new();

    public static Guid? OrganizationId => CurrentOrganization.Value;

    /// <summary>Postavlja organizaciju do Dispose (vraća prethodnu vrijednost).</summary>
    public static IDisposable Use(Guid? organizationId)
    {
        Guid? previous = CurrentOrganization.Value;
        CurrentOrganization.Value = organizationId;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Guid? _previous;
        private bool _disposed;

        public Restore(Guid? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;
            CurrentOrganization.Value = _previous;
            _disposed = true;
        }
    }
}
