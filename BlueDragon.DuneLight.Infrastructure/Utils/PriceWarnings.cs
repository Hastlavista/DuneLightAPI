using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// T1-8 — upozorenja pravila cijena koja se vraćaju uz uspješnu naredbu (nikad ne odbijaju):
/// <list type="bullet">
/// <item>PRICE_NOT_DEFINED — cijena sudjelovanja uzeta iz zadane cijene usluge uz rupu u cjeniku (vidi
/// <see cref="ResolvePriceResponse.PriceNotDefinedReason"/>); jedno upozorenje po (usluga, dan, iznos, razlog);</item>
/// <item>PRICE_NOT_DEFINED_OCCURRENCES — isto, zbirno za generiranje grupnih termina (broj termina, datumi);</item>
/// <item>PARTICIPATION_PRICE_CHANGED — segmentna naredba je promijenila iznos sudjelovanja.</item>
/// </list>
/// Upozorenje o rupi prati samo cijenu koja je stvarno upisana na sudjelovanje (pozivatelj ga dodaje samo kad sudjelovanje
/// dobiva tu cijenu).
/// </summary>
public static class PriceWarnings
{
    /// <summary>Detalji rupe u cjeniku za razriješenu cijenu usluge; null kad rupe nema (ili predmet nije usluga).</summary>
    public static WarningPriceNotDefinedDetails NotDefinedDetails(ResolvePriceResponse resolved) =>
        resolved?.PriceNotDefinedReason is PriceNotDefinedReason reason && resolved.SubjectType == PricingSubjectType.Service
            ? new WarningPriceNotDefinedDetails
            {
                ServiceId = resolved.SubjectId,
                ServiceName = resolved.SubjectName,
                Date = resolved.Date,
                UsedAmount = resolved.Price,
                Source = resolved.Source,
                Reason = reason
            }
            : null;

    /// <summary>Dodaje PRICE_NOT_DEFINED za razriješenu cijenu (ako ima rupu), bez ponavljanja istog (usluga, dan, iznos, razlog).</summary>
    public static void AddNotDefined(List<WarningDto> warnings, ResolvePriceResponse resolved)
    {
        AddNotDefinedDetails(warnings, NotDefinedDetails(resolved));
    }

    /// <summary>Dodaje PRICE_NOT_DEFINED s gotovim detaljima, bez ponavljanja istog (usluga, dan, iznos, razlog).</summary>
    public static void AddNotDefinedDetails(List<WarningDto> warnings, WarningPriceNotDefinedDetails details)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        if (details == null)
            return;
        bool duplicate = warnings.Any(w => w.Code == WarningCodes.PriceNotDefined && w.Details is WarningPriceNotDefinedDetails d && SameItem(d, details));
        if (!duplicate)
            warnings.Add(new WarningDto(WarningCodes.PriceNotDefined, details));
    }

    /// <summary>Zbirni PRICE_NOT_DEFINED_OCCURRENCES za više termina (ključ = termin): broj termina s rupom, njihovi dani cjenika
    /// (uzlazno, bez ponavljanja) i stavke bez ponavljanja. Null kad nijedan termin nema rupu.</summary>
    public static WarningDto NotDefinedOccurrences(IEnumerable<(Guid OccurrenceKey, ResolvePriceResponse Resolved)> priced)
    {
        List<(Guid Key, WarningPriceNotDefinedDetails Details)> gaps = priced
            .Select(p => (p.OccurrenceKey, NotDefinedDetails(p.Resolved)))
            .Where(p => p.Item2 != null)
            .ToList();
        if (gaps.Count == 0)
            return null;

        List<WarningPriceNotDefinedDetails> items = new();
        foreach (WarningPriceNotDefinedDetails details in gaps.Select(g => g.Details).OrderBy(d => d.Date).ThenBy(d => d.ServiceId))
            if (!items.Any(i => SameItem(i, details)))
                items.Add(details);

        return new WarningDto(WarningCodes.PriceNotDefinedOccurrences, new WarningPriceNotDefinedOccurrencesDetails
        {
            OccurrenceCount = gaps.Select(g => g.Key).Distinct().Count(),
            Dates = gaps.Select(g => g.Details.Date).Distinct().OrderBy(d => d).ToList(),
            Items = items
        });
    }

    /// <summary>PARTICIPATION_PRICE_CHANGED za jedno sudjelovanje čiji se iznos promijenio.</summary>
    public static WarningDto ParticipationPriceChanged(
        Guid participationId, Guid clientId, decimal oldAmount, decimal newAmount, ParticipationPriceChangeReason reason) =>
        new(WarningCodes.ParticipationPriceChanged, new WarningParticipationPriceChangedDetails
        {
            ParticipationId = participationId,
            ClientId = clientId,
            OldAmount = oldAmount,
            NewAmount = newAmount,
            Reason = reason
        });

    private static bool SameItem(WarningPriceNotDefinedDetails a, WarningPriceNotDefinedDetails b) =>
        a.ServiceId == b.ServiceId && a.Date == b.Date && a.UsedAmount == b.UsedAmount && a.Reason == b.Reason;
}
