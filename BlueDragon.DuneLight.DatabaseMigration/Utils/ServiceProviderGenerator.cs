using BlueDragon.DuneLight.DatabaseMigration.Configuration;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Logging;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.DatabaseMigration.Utils;

public class ServiceProviderGenerator
{
    public static ServiceProvider GenerateMigrationServiceProvider(ConfigurationModel configuration)
    {
        return new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(runnerBuilder => runnerBuilder
                .AddPostgres()
                .ConfigureGlobalProcessorOptions(options =>
                {
                    options.ConnectionString = configuration.ConnectionString;
                    options.PreviewOnly = configuration.PreviewOnly;
                    options.Timeout = configuration.Timeout;
                })
                .WithMigrationsIn(typeof(DatabaseConfiguration).Assembly))
            .Configure<SelectingProcessorAccessorOptions>(processorAccessorOptions =>
            {
                processorAccessorOptions.ProcessorId = configuration.Database;
            })
            .Configure<AssemblySourceOptions>(assemblyOptions =>
            {
                string target = typeof(DatabaseConfiguration).Assembly.Location;
                assemblyOptions.AssemblyNames = new string[] { target };
            })
            .AddLogging(loggingBuilder => loggingBuilder.AddFluentMigratorConsole())
            .Configure<FluentMigratorLoggerOptions>(loggerOptions =>
            {
                loggerOptions.ShowElapsedTime = true;
                loggerOptions.ShowSql = true;
            })
            .AddSingleton<ILoggerProvider, LogFileFluentMigratorLoggerProvider>()
            .Configure<LogFileFluentMigratorLoggerOptions>(loggerFileOptions =>
            {
                loggerFileOptions.OutputFileName = "sqlscript.sql";
            })
            .BuildServiceProvider();
    }
}
