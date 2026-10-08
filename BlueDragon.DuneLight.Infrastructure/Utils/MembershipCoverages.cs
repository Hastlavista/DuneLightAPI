using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P2 (faza 2D) — čitanje projekcije pokrića članarinom sudjelovanja (BookingSegmentParticipation.MembershipCoverage,
/// AutoInclude). Bez projekcije (klijent bez članarine) sve vraća false/null — ponašanje kao prije P2.
/// </summary>
public static class MembershipCoverages
{
    /// <summary>Članarina pokriva USLUGU: aktivno (zauzimajuće) sudjelovanje s aktivnim claimom. Claim zadržan uz otkazano /
    /// izostalo sudjelovanje (Q26) nije pokriće usluge — o dugu tada odlučuje posljedica politike.</summary>
    public static bool CoversService(BookingSegmentParticipation participation) =>
        ParticipationOccupancy.Occupies(participation.Status)
        && participation.MembershipCoverage?.Status == MembershipCoverageStatus.Covered;

    /// <summary>Q27.2 — aktivno sudjelovanje s terminom iza horizonta: pokriće se još ne zna, pa nema duga ni naplate.</summary>
    public static bool IsPending(BookingSegmentParticipation participation) =>
        ParticipationOccupancy.Occupies(participation.Status)
        && participation.MembershipCoverage?.Status == MembershipCoverageStatus.PendingEvaluation;

    public static ParticipationMembershipCoverageDto ToDto(ParticipationMembershipCoverage coverage) => coverage == null
        ? null
        : new ParticipationMembershipCoverageDto
        {
            ClientMembershipId = coverage.ClientMembershipId,
            Status = coverage.Status,
            Reason = coverage.Reason,
            ChangedByEvent = coverage.ChangedByEvent,
            LimitWindow = coverage.LimitWindow,
            LimitServiceId = coverage.LimitServiceId,
            LimitMaxUses = coverage.LimitMaxUses,
            LimitUsed = coverage.LimitUsed,
            ExpectedPeriodStartsOn = coverage.ExpectedPeriodStartsOn,
            EvaluatedAt = coverage.EvaluatedAt,
            LastPriceChangeOldAmount = coverage.LastPriceChangeOldAmount,
            LastPriceChangeNewAmount = coverage.LastPriceChangeNewAmount,
            LastPriceChangeEvent = coverage.LastPriceChangeEvent,
            LastPriceChangeAt = coverage.LastPriceChangeAt,
            PriceProtectedReason = coverage.PriceProtectedReason,
            PriceStale = coverage.PriceStale
        };

    private static readonly System.Text.Json.JsonSerializerOptions EvaluationJson = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>2E — primijenjena prilagodba i evaluacija kandidata (snapshot); null kad nikad nije evaluirana (bez članarine).</summary>
    public static ParticipationPriceAdjustmentDto PriceAdjustmentOf(BookingSegmentParticipation participation) =>
        participation.AdjustmentType == null && participation.AdjustmentEvaluation == null
            ? null
            : new ParticipationPriceAdjustmentDto
            {
                AppliedType = participation.AdjustmentType,
                AppliedSourceId = participation.AdjustmentSourceId,
                BaseAmount = participation.BaseAmount,
                AdjustmentAmount = participation.AdjustmentAmount,
                Candidates = participation.AdjustmentEvaluation == null
                    ? new()
                    : System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<PriceAdjustmentCandidateDto>>(
                        participation.AdjustmentEvaluation, EvaluationJson)
            };
}
