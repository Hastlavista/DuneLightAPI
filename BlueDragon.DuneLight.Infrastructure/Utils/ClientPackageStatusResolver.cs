using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Efektivni status paketa u trenutku provjere. Cancelled/Depleted su perzistirani (Active je zadano stanje
/// kod kreiranja, Depleted se postavlja event-driven u ClientPackageEntryMutator), dok se Expired namjerno
/// NE perzistira (nema background joba) — uvijek se izvodi dinamički iz ValidUntilDate. Precedencija je
/// deterministička: Cancelled > Expired > Depleted > Active.
/// </summary>
public static class ClientPackageStatusResolver
{
    /// <param name="today">Današnji kalendarski datum u kalendaru organizacije (paket nije vezan uz poslovnicu).</param>
    public static ClientPackageStatus GetEffectiveStatus(ClientPackage clientPackage, DateOnly today)
    {
        if (clientPackage.Status == ClientPackageStatus.Cancelled)
            return ClientPackageStatus.Cancelled;

        if (clientPackage.ValidUntilDate < today)
            return ClientPackageStatus.Expired;

        if (clientPackage.Status == ClientPackageStatus.Depleted)
            return ClientPackageStatus.Depleted;

        return ClientPackageStatus.Active;
    }
}
