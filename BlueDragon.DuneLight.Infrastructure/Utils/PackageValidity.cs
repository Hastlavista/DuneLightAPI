using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3B3A/D3B3A.1 — JEDINO pravilo valjanosti paketa, isključivo usporedba KALENDARSKIH datuma: paket vrijedi za
/// izvođenje usluge kad je ClientPackage.PurchaseDate &lt;= lokalni datum izvođenja &lt;= ClientPackage.ValidUntilDate (oba kraja
/// uključena). Lokalni datum izvođenja je datum planiranog početka segmenta sudjelovanja u efektivnoj zoni poslovnice termina
/// (Company.TimeZone ?? Organization.TimeZone, OrganizationCalendar) — nikad trenutni sat, UTC datum ni zona hosta. Isto
/// pravilo koriste eligibility upit (ClientPackageHandler.GetEligibleForService) i potrošnja (ClientPackageEntryMutator.Deduct
/// kroz IPackageConsumptionLedgerService).
/// T1-9 (CHANGED in T1): donja granica PurchaseDate — paket ne pokriva termine prije dana kupnje, ni kad je upisan unatrag
/// (prije: samo gornja granica ValidUntilDate).
/// </summary>
public static class PackageValidity
{
    /// <summary>Lokalni datum izvođenja usluge u kalendaru poslovnice termina.</summary>
    public static DateOnly ServiceDate(OrganizationCalendar companyCalendar, DateTimeOffset serviceStartsAt)
    {
        ArgumentNullException.ThrowIfNull(companyCalendar);
        return companyCalendar.LocalDate(serviceStartsAt);
    }

    public static bool IsValidOn(ClientPackage clientPackage, DateOnly serviceDate) =>
        serviceDate >= clientPackage.PurchaseDate && serviceDate <= clientPackage.ValidUntilDate;
}
