#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using BlueDragon.DuneLight.Infrastructure.Integrations;
using BlueDragon.DuneLight.Infrastructure.Outbox;
using BlueDragon.DuneLight.Infrastructure.Outbox.Handlers;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.Time;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Real-DI composition root for the Appointment/Booking characterization tests. It wires the PRODUCTION handler and
/// service classes (same classes Startup.cs registers) against the same local PostgreSQL every other DB-backed test in
/// this project uses (see LifecycleCleanupSafeFixesTests). Nothing here is mocked: the tests exercise the real
/// AppointmentService/BookingService/GroupService/... code paths, real transactions and real FOR UPDATE locks.
///
/// Registration is by reflection over the Infrastructure assembly instead of a hand-copied list, so a new
/// constructor dependency in production code does not silently drift from this container. Handlers register as
/// singletons and services as scoped (Startup.cs registers a couple of stateless services as singletons; the lifetime
/// difference is not observable here because every test resolves from its own scope).
/// </summary>
public static class SchedulingTestHost
{
    public const string ConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static readonly Lazy<ServiceProvider> Root = new(BuildRoot);

    public static IServiceScope CreateScope() => Root.Value.CreateScope();

    private static ServiceProvider BuildRoot()
    {
        ServiceCollection services = new();
        Assembly infrastructure = typeof(AppointmentHandler).Assembly;

        services.AddLogging();
        services.AddSingleton(new DatabaseSettings { ConnectionString = ConnectionString });
        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddSingleton<IOutboxWriter, OutboxWriter>();
        // T1-4: seed nove demo organizacije ide kroz pravu registraciju (AuthService → JwtService treba JwtSettings).
        services.AddSingleton(MultiSegmentHttpContractTests.Jwt);

        // T1: isti poslovni sat kao u produkciji (BusinessTimeProvider nad testnim izvorom sata); pomak po organizaciji je uključen
        // (TestTools), pa testovi pomiču sat samo svoje organizacije i ne smetaju paralelnim testovima.
        services.AddSingleton(new TestToolsSettings { Enabled = true });
        services.AddSingleton<IClockOffsetStore, ClockOffsetStore>();
        services.AddSingleton(sp => new BusinessTimeProvider(sp.GetRequiredService<IClockOffsetStore>(), TestClock.Source));
        services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<BusinessTimeProvider>());

        // Outbox message handlers are registered explicitly in Startup.cs; tests resolve them directly to run the
        // outbox -> Notification step without the background OutboxProcessorService.
        services.AddSingleton<BookingCancelledNotificationHandler>();
        services.AddSingleton<BookingNoShowNotificationHandler>();
        services.AddSingleton<WaitlistPromotedNotificationHandler>();

        // Handlers: I{Name} in *.Handlers.Interfaces implemented by {Name} in *.Handlers.Implementations.
        foreach (Type implementation in infrastructure.GetTypes().Where(t =>
                     t.IsClass && !t.IsAbstract && t.Namespace != null && t.Namespace.EndsWith(".Handlers.Implementations", StringComparison.Ordinal)))
        {
            Type contract = implementation.GetInterfaces().FirstOrDefault(i => i.Name == "I" + implementation.Name);
            if (contract != null)
                services.AddSingleton(contract, implementation);
        }

        // Business services: every interface a class in Infrastructure.Services implements (CommissionService and
        // PaymentService implement several, exactly as Startup.cs registers them).
        foreach (Type implementation in infrastructure.GetTypes().Where(t =>
                     t.IsClass && !t.IsAbstract && t.Namespace == "BlueDragon.DuneLight.Infrastructure.Services" && !t.Name.EndsWith("Service`1")))
        {
            foreach (Type contract in implementation.GetInterfaces().Where(i =>
                         i.Name.StartsWith("I", StringComparison.Ordinal) &&
                         i.Namespace != null && i.Namespace.StartsWith("BlueDragon.DuneLight", StringComparison.Ordinal)))
            {
                services.AddScoped(contract, implementation);
            }
        }

        // T1-4: seed dodaje praznik poslovnice kroz CompanyHolidayService; vanjski izvor praznika (samo Generate) testovi ne zovu.
        services.AddSingleton<IPublicHolidayApiClient, NoPublicHolidaysApiClient>();

        services.AddSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false, ValidateOnBuild = false });
    }
}

/// <summary>Testni izvor javnih praznika bez mreže: nikad ne vraća praznike.</summary>
internal sealed class NoPublicHolidaysApiClient : IPublicHolidayApiClient
{
    public Task<List<PublicHolidayResult>> GetPublicHolidays(int year, string countryCode) => Task.FromResult(new List<PublicHolidayResult>());
}
