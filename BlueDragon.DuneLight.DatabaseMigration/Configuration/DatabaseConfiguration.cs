using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using Microsoft.Extensions.Configuration;

namespace BlueDragon.DuneLight.DatabaseMigration.Configuration;

public class DatabaseConfiguration
{
    private static readonly Dictionary<string, ConfigurationModel> AvailableConfigurations =
        new Dictionary<string, ConfigurationModel>
        {
            {
                "Local", new ConfigurationModel
                {
                    Database = "PostgreSQL",
                    ConnectionString = @"Host=localhost;Database=postgres;Password=root1234;Username=postgres",
                    Description = "Local database",
                    PreviewOnly = false,
                    Timeout = TimeSpan.FromSeconds(180)
                }
            },
            {
                "Development", new ConfigurationModel
                {
                    Database = "PostgreSQL",
                    ConnectionString = @"Host=localhost;Database=dunelight;Password=postgres;Username=postgres",
                    Description = "Development database",
                    PreviewOnly = false,
                    Timeout = TimeSpan.FromSeconds(180)
                }
            },
            {
                "Production", new ConfigurationModel
                {
                    Database = "PostgreSQL",
                    ConnectionString = @"",
                    Description = "Production database",
                    PreviewOnly = false,
                    Timeout = TimeSpan.FromSeconds(180)
                }
            }
        };

    public static ConfigurationModel GetConfiguration(string configurationName)
    {
        return configurationName != null ?
            AvailableConfigurations.GetValueOrDefault(configurationName) : null;
    }

    public static ConfigurationModel CreateFromArgument(IConfiguration appConfiguration)
    {
        return new ConfigurationModel
        {
            Database = GetOrDefault(
                appConfiguration.GetSection(CommandLineArgument.Database),
                string.Empty,
                v => v),

            ConnectionString = GetOrDefault(
                appConfiguration.GetSection(CommandLineArgument.ConnectionString),
                string.Empty,
                v => v),

            PreviewOnly = GetOrDefault(
                appConfiguration.GetSection(CommandLineArgument.PreviewOnly),
                false,
                Convert.ToBoolean),

            Timeout = GetOrDefault(
                appConfiguration.GetSection(CommandLineArgument.Timeout),
                TimeSpan.FromSeconds(180),
                v => TimeSpan.FromSeconds(Convert.ToInt32(v)))
        };
    }

    private static T GetOrDefault<T>(IConfigurationSection section, T def, Func<string, T> valueExtractor)
    {
        return !section.Exists() ? def : valueExtractor(section.Value);
    }

    public static string[] GetPossibleConfigurationNames()
    {
        return AvailableConfigurations.Keys.ToArray();
    }
}
