using System;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Čista mutacijska logika za odbijanje/vraćanje jednog ulaska na ClientPackage — izvučena iz
/// ClientPackageService da je mogu dijeliti i standalone poziv (ClientPackageService.DeductEntry/ReturnEntry,
/// s optimistic-concurrency retryem) i poziv unutar zajedničke transakcije (AppointmentService kod naplate
/// termina, gdje se paket čita/piše preko istog IUnitOfWork-a kao i sam termin). Budući da AppointmentService
/// zaobilazi ClientPackageService, sve provjere podobnosti (cancelled/expired/depleted/coverage) moraju biti
/// ovdje — ovo je jedino zajedničko grlo za obje putanje poziva.
/// </summary>
public static class ClientPackageEntryMutator
{
    public static void Deduct(ClientPackage clientPackage, Guid serviceId, DateTimeOffset now)
    {
        EnsureEligible(clientPackage, now);

        if (clientPackage.EntryMode == PackageEntryMode.SharedPool)
        {
            // ServiceEntries je i kod SharedPool-a snapshot pokrivenosti (vidi ClientPackageServiceEntry) —
            // provjera pokrivenosti mora ići preko njega, ne preko trenutnog Package.Services.
            bool covered = clientPackage.ServiceEntries.Any(e => e.ServiceId == serviceId);
            if (!covered)
                throw new BusinessRuleException(ErrorCodes.PackageServiceNotCovered, "Odabrani paket ne pokriva ovu uslugu.");

            if (clientPackage.RemainingSharedEntries.HasValue)
            {
                if (clientPackage.RemainingSharedEntries <= 0)
                    throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Paket nema preostalih ulazaka.");

                clientPackage.RemainingSharedEntries -= 1;
                if (clientPackage.RemainingSharedEntries <= 0)
                    clientPackage.Status = ClientPackageStatus.Depleted;
            }
        }
        else
        {
            ClientPackageServiceEntry entry = clientPackage.ServiceEntries.FirstOrDefault(e => e.ServiceId == serviceId);
            if (entry == null)
                throw new BusinessRuleException(ErrorCodes.PackageServiceNotCovered, "Odabrani paket ne pokriva ovu uslugu.");

            if (entry.RemainingEntries.HasValue)
            {
                if (entry.RemainingEntries <= 0)
                    throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Paket nema preostalih ulazaka za ovu uslugu.");

                entry.RemainingEntries -= 1;
                if (clientPackage.ServiceEntries.All(e => e.RemainingEntries.HasValue && e.RemainingEntries <= 0))
                    clientPackage.Status = ClientPackageStatus.Depleted;
            }
        }
    }

    public static void Return(ClientPackage clientPackage, Guid serviceId, DateTimeOffset now)
    {
        if (clientPackage.EntryMode == PackageEntryMode.SharedPool)
        {
            if (clientPackage.RemainingSharedEntries.HasValue)
            {
                // Ne smije premašiti izvorni kupljeni fond (TotalEntryCount) — bez ovoga bi ponovljeni/
                // konkurentni return mogao "napuhati" paket iznad onoga što je klijent kupio.
                int max = clientPackage.TotalEntryCount ?? int.MaxValue;
                clientPackage.RemainingSharedEntries = Math.Min(clientPackage.RemainingSharedEntries.Value + 1, max);
            }
        }
        else
        {
            ClientPackageServiceEntry entry = clientPackage.ServiceEntries.FirstOrDefault(e => e.ServiceId == serviceId);
            if (entry != null && entry.RemainingEntries.HasValue)
            {
                int max = entry.TotalEntries ?? int.MaxValue;
                entry.RemainingEntries = Math.Min(entry.RemainingEntries.Value + 1, max);
            }
        }

        // Depleted -> Active je dopušteno vraćanjem ulaska, ali samo ako paket u međuvremenu nije istekao.
        // Cancelled se ovdje nikad ne dira (status je Depleted, ne Cancelled), pa se otkazan paket vraćanjem
        // ulaska ne "odotkazuje".
        if (clientPackage.Status == ClientPackageStatus.Depleted && clientPackage.ExpiryDate >= now)
            clientPackage.Status = ClientPackageStatus.Active;
    }

    private static void EnsureEligible(ClientPackage clientPackage, DateTimeOffset now)
    {
        if (clientPackage.Status == ClientPackageStatus.Cancelled)
            throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Paket je otkazan.");
        if (clientPackage.ExpiryDate < now)
            throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Paket je istekao.");
    }
}
