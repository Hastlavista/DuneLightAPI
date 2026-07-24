using System;

namespace BlueDragon.DuneLight.DatabaseMigration.Models;

public class ConfigurationModel
{
    public string ConnectionString { get; set; }
    public string Database { get; set; }
    public string Description { get; set; }
    public bool PreviewOnly { get; set; }
    public TimeSpan? Timeout { get; set; }
}
