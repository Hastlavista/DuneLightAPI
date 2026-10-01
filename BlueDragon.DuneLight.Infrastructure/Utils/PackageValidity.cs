using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3B3A (F-08) — JEDINO pravilo valjanosti paketa za izvođenje usluge. Valjanost se procjenjuje na DATUM
/// IZVOĐENJA usluge (planirani početak segmenta sudjelovanja), kao lokalni datum u efektivnoj zoni poslovnice
/// (OrganizationCalendar) — nikad po trenutnom satu, nikad po UTC datumu niti zoni hosta. Isto pravilo koriste
/// eligibility upit (ClientPackageHandler.GetEligibleForService) i potrošnja (ClientPackageEntryMutator.Deduct kroz
/// IPackageConsumptionLedgerService) — prije D3B3A eligibility je gledao datum termina, a skidanje ulaska "sada".
///
/// ClientPackage.ExpiryDate je instant kraja (uključivog) dana isteka. Paket vrijedi za uslugu lokalnog datuma D ako
/// mu istek pada na D ili kasnije, tj. ExpiryDate &gt;= lokalna ponoć dana D — izraženo kao instant
/// (<see cref="ValidityCutoff"/>) da ga može koristiti i SQL upit.
/// </summary>
public static class PackageValidity
{
    /// <summary>UTC instant lokalne ponoći dana izvođenja usluge u kalendaru poslovnice.</summary>
    public static DateTimeOffset ValidityCutoff(OrganizationCalendar companyCalendar, DateTimeOffset serviceStartsAt)
    {
        ArgumentNullException.ThrowIfNull(companyCalendar);
        return companyCalendar.StartOfDay(companyCalendar.LocalDate(serviceStartsAt));
    }

    public static bool IsValidOn(ClientPackage clientPackage, DateTimeOffset validityCutoff) =>
        clientPackage.ExpiryDate >= validityCutoff;
}
