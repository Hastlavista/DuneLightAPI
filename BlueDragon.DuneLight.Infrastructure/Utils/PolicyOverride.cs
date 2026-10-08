using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P1 (ADR-0018, D10/D12) — JEDINA provjera iznimke od politike otkazivanja: otpis posljedice (u trenutku događaja ili
/// naknadno) i korekcija koja poništava posljedicu sa stvarnim učinkom traže razlog i raw grant
/// appointments.policy.override. Grant nikad ne širi own opseg (pristup sudjelovanju provjerava pozivatelj). Isti obrazac
/// kao GroupCapacityOverride.
/// </summary>
public static class PolicyOverride
{
    /// <summary>Otpis posljedice: razlog obavezan + appointments.policy.override.</summary>
    public static Task EnsureWaiverAllowed(IGrantResolver grantResolver, Guid organizationId, Guid userId, string waiverReason) =>
        EnsureAllowed(grantResolver, organizationId, userId, waiverReason,
            "Otpis posljedice politike zahtijeva razlog.",
            "Otpis posljedice politike zahtijeva ovlast appointments.policy.override.");

    /// <summary>Korekcija koja poništava posljedicu sa stvarnim učinkom: razlog korekcije obavezan + appointments.policy.override.</summary>
    public static Task EnsureReversalAllowed(IGrantResolver grantResolver, Guid organizationId, Guid userId, string correctionReason) =>
        EnsureAllowed(grantResolver, organizationId, userId, correctionReason,
            "Korekcija poništava posljedicu politike (naknadu ili potrošenu jedinicu paketa) — razlog korekcije je obavezan.",
            "Korekcija koja poništava posljedicu politike zahtijeva ovlast appointments.policy.override.");

    private static async Task EnsureAllowed(
        IGrantResolver grantResolver, Guid organizationId, Guid userId, string reason, string missingReasonMessage, string forbiddenMessage)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationAppException(missingReasonMessage);
        GrantContext grants = await grantResolver.Resolve(organizationId, userId);
        if (!grants.Has(Grants.AppointmentsPolicyOverride))
            throw new ForbiddenAppException(forbiddenMessage);
    }
}
