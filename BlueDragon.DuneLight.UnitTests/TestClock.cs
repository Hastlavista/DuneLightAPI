#nullable disable
using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.Time;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// T1 — fiksni sat testova: izvor ispod istog BusinessTimeProvidera koji koristi produkcija (testni DI kontejner i HTTP host).
/// Postavlja se jednom na stvarni trenutak pokretanja test runa (poravnat na sekundu) i ne prati zidni sat (1 ms po čitanju),
/// pa test i servisi gledaju isti "sada". Test koji treba drugi dan pomiče sat SAMO svoje organizacije (<see cref="RunForOrganizationOn"/>), kao
/// testni alat u Managementu, pa paralelni testovi ne smetaju jedni drugima.
/// </summary>
public static class TestClock
{
    // Svako čitanje pomiče sat za 1 ms (preciznost timestamptz je mikrosekunda): redoslijed događaja po vremenu (FIFO liste
    // čekanja, raspodjela uplata, trag dolaska) ostaje jednoznačan kao u stvarnom radu, a sat i dalje ne ovisi o zidnom satu.
    private static readonly FakeTimeProvider Fake = new(StartOfRun()) { AutoAdvanceAmount = TimeSpan.FromMilliseconds(1) };

    public static TimeProvider Source => Fake;

    public static DateTimeOffset UtcNow => Fake.GetUtcNow();

    /// <summary>
    /// Prolaz obnove članarina organizacije kao da je lokalni dan organizacije <paramref name="day"/> (podne u zoni organizacije):
    /// sat organizacije se pomakne za vrijeme prolaza i zatim vrati, pa ostatak testa radi na fiksnom satu (isto ponašanje kao
    /// raniji parametar "today", ali kroz jedan sat sustava).
    /// </summary>
    public static async Task<int> RunForOrganizationOn(this IMembershipRenewalService renewal, Guid organizationId, DateOnly day)
    {
        using IServiceScope scope = SchedulingTestHost.CreateScope();
        IClockOffsetStore offsets = scope.ServiceProvider.GetRequiredService<IClockOffsetStore>();
        OrganizationCalendar calendar = await scope.ServiceProvider.GetRequiredService<IOrganizationCalendarService>().GetCalendar(organizationId);

        TimeSpan previous = offsets.GetOffset(organizationId);
        offsets.Set(organizationId, calendar.ToInstant(day, TimeSpan.FromHours(12)) - UtcNow);
        try
        {
            using IDisposable clock = OrganizationClockContext.Use(organizationId);
            return await renewal.RunForOrganization(organizationId);
        }
        finally
        {
            offsets.Set(organizationId, previous);
        }
    }

    private static DateTimeOffset StartOfRun()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }
}
