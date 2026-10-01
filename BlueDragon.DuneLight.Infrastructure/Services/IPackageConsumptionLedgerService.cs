using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Phase D3B3A — JEDINA putanja koja troši/vraća entitlement paketa. Svaka promjena brojača ClientPackage-a ima svoj
/// PackageConsumption zapis na sudjelovanju Bookinga (ledger, Consumed -&gt; Reversed, nikad brisanje). Poziva se UNUTAR
/// pozivateljeve transakcije (IUnitOfWork) — isti obrazac kao ICommissionLedgerService/IPaymentLedgerService.
///
/// Konkurentnost: redak ClientPackage se zaključava (SELECT ... FOR UPDATE) prije čitanja brojača, pa dvije
/// istovremene potrošnje istog (zadnjeg) ulaska serijaliziraju — druga vidi svježe stanje i pada na
/// PACKAGE_NOT_ELIGIBLE; xmin token na paketu ostaje dodatna zaštita. Idempotentnost: najviše jedna aktivna potrošnja
/// po sudjelovanju (djelomični unique indeks) — Consume na već pokrivenom sudjelovanju istim paketom je no-op, a
/// ReverseActive bez aktivne potrošnje je no-op (ne može vratiti dvaput).
/// </summary>
public interface IPackageConsumptionLedgerService
{
    /// <summary>Phase M0: adresira se SUDJELOVANJE (izvršna jedinica), nikad Booking. Troši paket za to sudjelovanje na datum izvođenja usluge (F-08) — ako postavka organizacije
    /// (PackageConsumptionTiming) predviđa potrošnju na <paramref name="trigger"/> prijelazu; inače null bez ikakve
    /// promjene. Vraća aktivnu potrošnju (novu ili postojeću za isti paket).</summary>
    Task<PackageConsumption> Consume(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation, BookingExecutionContext execution,
        Guid clientPackageId, BookingStatus trigger);

    /// <summary>Poništava aktivnu potrošnju sudjelovanja (vraća ulazak u paket) — false ako aktivne potrošnje nema.</summary>
    Task<bool> ReverseActive(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation, PackageConsumptionReversalReason reason);
}
