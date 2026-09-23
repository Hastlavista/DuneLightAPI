using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BlueDragon.DuneLight.Core.Services;

/// <summary>
/// FAZA 3 (v2 template-upgrade, Part K) — deterministički otisak TRENUTNOG (CURRENT) provenance stanja jedne
/// GrantGroup, korišten da GetDiff/Preview/Apply otkriju "netko je drugdje promijenio stanje između review-a i
/// primjene" (stale-state zaštita). NAMJERNO odvojeno u Core (bez EF-a) da bude jedinično testabilno bez
/// DbContext-a — GrantGroupTemplateUpgradeService poziva OVU istu metodu iz sva tri toka (GetDiff/Preview/Apply)
/// da drift između njih bude strukturno nemoguć. Poredak ulaza NIKAD ne utječe na token (uvijek sortirano po
/// stabilnom ključu), i token NIKAD ne ovisi o vremenskim/insercijskim poljima (AppliedAt i sl.) — vidi Part K.
/// </summary>
public static class TemplateUpgradeStateToken
{
    public static string Compute(
        string templateKey,
        int currentTemplateVersion,
        IEnumerable<(Guid CapabilityDefinitionId, string SelectedScope)> snapshots,
        IEnumerable<string> templateCompatibilityGrantKeys,
        IEnumerable<string> manualGrantKeys)
    {
        string snapshotsPart = string.Join(",", snapshots
            .OrderBy(s => s.CapabilityDefinitionId)
            .Select(s => $"{s.CapabilityDefinitionId}:{s.SelectedScope}"));

        string compatPart = string.Join(",", templateCompatibilityGrantKeys.OrderBy(k => k, StringComparer.Ordinal));
        string manualPart = string.Join(",", manualGrantKeys.OrderBy(k => k, StringComparer.Ordinal));

        string canonical = string.Join("|", new[]
        {
            templateKey ?? string.Empty,
            currentTemplateVersion.ToString(CultureInfo.InvariantCulture),
            snapshotsPart,
            compatPart,
            manualPart
        });

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }
}
