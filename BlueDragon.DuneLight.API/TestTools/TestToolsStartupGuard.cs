using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using Microsoft.Extensions.Hosting;

namespace BlueDragon.DuneLight.API.TestTools;

/// <summary>T1 — testni alati nikad u produkciji: uključena postavka u okruženju Production zaustavlja pokretanje.</summary>
public static class TestToolsStartupGuard
{
    public static void Ensure(TestToolsSettings settings, IHostEnvironment environment)
    {
        if (settings.Enabled && environment.IsProduction())
            throw new InvalidOperationException(
                "Testni alati (TestTools:Enabled = true: simulirani pomak sata i seed) uključeni su u okruženju Production. " +
                "Aplikacija se ne pokreće: isključite TestTools:Enabled ili pokrenite izvan Production okruženja.");
    }
}
