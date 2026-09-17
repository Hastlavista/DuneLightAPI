using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueDragon.DuneLight.Infrastructure.Outbox;

/// <summary>Iste konvencije kao Startup.ConfigureJsonOptions (camelCase, enumi kao stringovi, bez null polja) —
/// primijenjeno ovdje neovisno o ASP.NET Core JsonOptions jer Outbox payload/Notification.Data serijalizacija
/// mora raditi i izvan HTTP request pipelinea (pozadinski worker, vidi spec section 3).</summary>
public static class OutboxJsonOptions
{
    public static readonly JsonSerializerOptions Instance = Create();

    private static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
