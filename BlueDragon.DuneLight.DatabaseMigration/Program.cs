using System;
using BlueDragon.DuneLight.DatabaseMigration.Configuration;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using BlueDragon.DuneLight.DatabaseMigration.Utils;
using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.DatabaseMigration;

class Program
{
    static void Main(string[] args)
    {
        IConfiguration appConfiguration = new ConfigurationBuilder().AddCommandLine(args).Build();

        ConfigurationModel dbConfiguration = LoadConfiguration(appConfiguration);
        if (dbConfiguration == null)
        {
            ShowHelp();
            return;
        }

        using ServiceProvider serviceProvider = ServiceProviderGenerator.GenerateMigrationServiceProvider(dbConfiguration);
        UpdateDatabase(serviceProvider);
    }

    private static ConfigurationModel LoadConfiguration(IConfiguration appConfiguration)
    {
        string dbConfigurationName = appConfiguration.GetSection(CommandLineArgument.Configuration).Value;
        return DatabaseConfiguration.GetConfiguration(dbConfigurationName) ??
               DatabaseConfiguration.CreateFromArgument(appConfiguration);
    }

    private static void UpdateDatabase(IServiceProvider serviceProvider)
    {
        IMigrationRunner runner = serviceProvider.GetRequiredService<IMigrationRunner>();

        // MigrationRunner.MigrateUp() does not commit on its own — FluentMigrator's transaction is only
        // committed when the IMigrationScope it runs in is explicitly completed (same pattern as
        // System.Transactions.TransactionScope: an unmet Complete() means the Dispose() below rolls back).
        // Without this, every migration logs "X migrated" but the process exit silently discards all of it.
        using IMigrationScope scope = ((IMigrationScopeStarter)runner).BeginScope();
        runner.MigrateUp();
        scope.Complete();
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Wrong configuration!");
        Console.WriteLine("Possible configuration names are:");
        foreach (string configurationName in DatabaseConfiguration.GetPossibleConfigurationNames())
        {
            Console.WriteLine($"\t- {configurationName}");
        }

        Console.WriteLine("If you want to load configuration manually you need to provide at least 'database' and 'connection'");
    }
}
