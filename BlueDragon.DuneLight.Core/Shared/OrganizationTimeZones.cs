using System;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Organization business timezone ids. Only IANA ids known to the platform's timezone database are accepted
/// (e.g. "Europe/Zagreb", "UTC") — Windows ids and arbitrary strings are rejected, so the stored value means the same
/// on every host.
/// </summary>
public static class OrganizationTimeZones
{
    /// <summary>Default for organizations that never configured a timezone (the studios this system serves).</summary>
    public const string Default = "Europe/Zagreb";

    public const int MaxLength = 64;

    public static bool IsSupported(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > MaxLength || timeZoneId != timeZoneId.Trim())
            return false;

        // IANA only: a Windows id ("Central European Standard Time") has no IANA -> Windows mapping of itself.
        if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _))
            return false;

        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);
    }
}
