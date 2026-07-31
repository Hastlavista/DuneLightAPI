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
/// termina, gdje se paket čita/piše preko istog IUnitOfWork-a kao i sam termin).
/// </summary>
public static class ClientPackageEntryMutator
{
    public static void Deduct(ClientPackage clientPackage, Guid serviceId)
    {
        if (clientPackage.EntryMode == PackageEntryMode.SharedPool)
        {
            if (clientPackage.RemainingSharedEntries.HasValue)
            {
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
                entry.RemainingEntries -= 1;
                if (clientPackage.ServiceEntries.All(e => e.RemainingEntries.HasValue && e.RemainingEntries <= 0))
                    clientPackage.Status = ClientPackageStatus.Depleted;
            }
        }
    }

    public static void Return(ClientPackage clientPackage, Guid serviceId)
    {
        if (clientPackage.EntryMode == PackageEntryMode.SharedPool)
        {
            if (clientPackage.RemainingSharedEntries.HasValue)
                clientPackage.RemainingSharedEntries += 1;
        }
        else
        {
            ClientPackageServiceEntry entry = clientPackage.ServiceEntries.FirstOrDefault(e => e.ServiceId == serviceId);
            if (entry != null && entry.RemainingEntries.HasValue)
                entry.RemainingEntries += 1;
        }

        if (clientPackage.Status == ClientPackageStatus.Depleted)
            clientPackage.Status = ClientPackageStatus.Active;
    }
}
