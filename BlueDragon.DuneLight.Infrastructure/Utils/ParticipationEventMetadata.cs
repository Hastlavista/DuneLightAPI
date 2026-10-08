using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P1 (ADR-0016, D3/D12) — JEDINO mjesto koje piše TRENUTNE metapodatke otkazivanja/izostanka sudjelovanja. Metapodaci uvijek
/// odgovaraju statusu (DB CHECK): prijelaz najprije briše sve stare metapodatke (<see cref="Clear"/>), pa novi događaj upisuje
/// svoje. Povijest je u audit logu, ledgeru posljedica, potrošnjama, provizijama i plaćanjima.
/// </summary>
public static class ParticipationEventMetadata
{
    public static void Clear(BookingSegmentParticipation participation)
    {
        ArgumentNullException.ThrowIfNull(participation);
        participation.CancellationInitiator = null;
        participation.CancelledAt = null;
        participation.CancelledBy = null;
        participation.CancellationReason = null;
        participation.IsLateCancellation = null;
        participation.CancellationPolicyId = null;
        participation.CancellationPolicyVersion = null;
        participation.AppliedCancellationWindowMinutes = null;
        participation.NoShowAt = null;
        participation.NoShowBy = null;
        participation.NoShowReason = null;
    }

    /// <summary>Otkazivanje (bilo koji initiator): tko, kada (jedan serverski timestamp događaja) i zašto.</summary>
    public static void SetCancelled(
        BookingSegmentParticipation participation, CancellationInitiator initiator, DateTimeOffset cancelledAt, Guid userId, string reason)
    {
        participation.CancellationInitiator = initiator;
        participation.CancelledAt = cancelledAt;
        participation.CancelledBy = userId;
        participation.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>Samo Client: klasifikacija i snapshot primijenjene politike (profil, verzija, prozor).</summary>
    public static void SetClientClassification(
        BookingSegmentParticipation participation, bool isLate, Guid policyId, int policyVersion, int windowMinutes)
    {
        participation.IsLateCancellation = isLate;
        participation.CancellationPolicyId = policyId;
        participation.CancellationPolicyVersion = policyVersion;
        participation.AppliedCancellationWindowMinutes = windowMinutes;
    }

    public static void SetNoShow(BookingSegmentParticipation participation, DateTimeOffset noShowAt, Guid userId, string reason)
    {
        participation.NoShowAt = noShowAt;
        participation.NoShowBy = userId;
        participation.NoShowReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
