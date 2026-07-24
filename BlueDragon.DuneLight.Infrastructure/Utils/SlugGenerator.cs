using System.Text.RegularExpressions;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

public static class SlugGenerator
{
    public static string Generate(string value)
    {
        string lowered = value.Trim().ToLowerInvariant();
        string normalized = Regex.Replace(lowered, @"[^a-z0-9]+", "-");
        return normalized.Trim('-');
    }
}
