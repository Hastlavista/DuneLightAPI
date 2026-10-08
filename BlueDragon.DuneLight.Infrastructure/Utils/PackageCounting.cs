using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO pravilo "brojenog" paketa za uslugu: zajednički fond s brojem ulazaka, ili unos usluge s brojem ulazaka.
/// Potrošnja brojenog paketa skida 1 jedinicu; neograničen paket (bez broja) ne skida ništa i nikad nije izvor kazne
/// politike otkazivanja (P1, D6). Isto pravilo nad entitetom (ledger) i nad DTO-om (odabir paketa za kaznu).
/// </summary>
public static class PackageCounting
{
    public static bool IsCounted(ClientPackage clientPackage, Guid serviceId) =>
        IsCounted(clientPackage.EntryMode, clientPackage.RemainingSharedEntries,
            clientPackage.ServiceEntries.Where(e => e.ServiceId == serviceId).Select(e => e.RemainingEntries));

    public static bool IsCounted(ClientPackageDto clientPackage, Guid serviceId) =>
        IsCounted(clientPackage.EntryMode, clientPackage.RemainingSharedEntries,
            clientPackage.ServiceEntries.Where(e => e.ServiceId == serviceId).Select(e => e.RemainingEntries));

    private static bool IsCounted(PackageEntryMode mode, int? remainingShared, IEnumerable<int?> serviceEntryRemaining) =>
        mode == PackageEntryMode.SharedPool
            ? remainingShared.HasValue
            : serviceEntryRemaining.FirstOrDefault() != null;
}
