using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Shared.Exceptions;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P1 (D6) — eksplicitni paket za kaznu politike u naredbama nad VIŠE sudjelovanja (Booking-wide otkazivanje, izostanak
/// cijelog termina). Paket se bira po sudjelovanju, nikad jedan za sve: segmenti mogu imati različite usluge, a izostanak
/// termina obuhvaća više klijenata. Jedan ClientPackageId na takvoj naredbi se odbija (ne primjenjuje se tiho na sve, ni
/// tiho ignorira).
/// </summary>
public static class ParticipationPackageSelections
{
    /// <summary>Validira odabire (bez duplikata, samo sudjelovanja iz dosega naredbe) i vraća ParticipationId → ClientPackageId.</summary>
    public static IReadOnlyDictionary<Guid, Guid> Resolve(
        Guid? singleClientPackageId, IReadOnlyCollection<ParticipationPackageSelection> selections, IReadOnlyCollection<Guid> scopeParticipationIds)
    {
        if (singleClientPackageId.HasValue)
            throw new ValidationAppException(
                "Naredba nad više sudjelovanja bira paket po sudjelovanju (PackageSelections), ne jednim ClientPackageId.");

        Dictionary<Guid, Guid> byParticipation = new();
        foreach (ParticipationPackageSelection selection in selections ?? Array.Empty<ParticipationPackageSelection>())
        {
            if (selection?.ParticipationId == null || selection.ClientPackageId == null)
                throw new ValidationAppException("Odabir paketa mora imati ParticipationId i ClientPackageId.");
            if (!scopeParticipationIds.Contains(selection.ParticipationId.Value))
                throw new ValidationAppException(
                    $"Sudjelovanje {selection.ParticipationId} nije aktivno sudjelovanje obuhvaćeno ovom naredbom.");
            if (!byParticipation.TryAdd(selection.ParticipationId.Value, selection.ClientPackageId.Value))
                throw new ValidationAppException($"Paket za sudjelovanje {selection.ParticipationId} je naveden više puta.");
        }

        return byParticipation;
    }

    /// <summary>Zahtjev prijelaza za JEDNO sudjelovanje unutar naredbe nad više sudjelovanja: isti događaj i opcije, paket
    /// iz odabira tog sudjelovanja (ili bez paketa).</summary>
    public static BookingSetStatusRequest ForParticipation(
        BookingSetStatusRequest template, IReadOnlyDictionary<Guid, Guid> selections, Guid participationId) => new()
    {
        Status = template.Status,
        CancellationInitiator = template.CancellationInitiator,
        CancellationReason = template.CancellationReason,
        NoShowReason = template.NoShowReason,
        WaivePolicyConsequence = template.WaivePolicyConsequence,
        WaiverReason = template.WaiverReason,
        CorrectionReason = template.CorrectionReason,
        ClientPackageId = selections.TryGetValue(participationId, out Guid clientPackageId) ? clientPackageId : null
    };
}
