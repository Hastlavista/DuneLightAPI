using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Učinak posljedice politike koji otpis uklanja: jedinica (paket ili kredit članarine) ILI naknada — nikad oboje
/// (jedinica je kazna umjesto naknade, P1 D6 / P2 Q26).</summary>
public enum PolicyWaiverEffect
{
    None,
    Fee,
    Unit
}

/// <summary>
/// P1 (ADR-0018, D10) + K2 (ADR-0032, 12.2) — JEDINA provjera otpisa posljedice politike otkazivanja (u trenutku događaja ili
/// naknadno). Razlog je uvijek obavezan; grant ovisi o učinku: naknada → appointments.policy.fee.waive, jedinica paketa ili
/// kredit članarine → appointments.policy.unit.waive (posljedica bez učinka: bilo koji od njih). Zahtjev bez potrebnog granta
/// se odbija u cijelosti (403 s grantom koji nedostaje) — nema tihog izvršenja bez otpisa ni tihog otpisa. Grant nikad ne širi
/// own opseg. Korekcija statusa koja poništava posljedicu NIJE otpis (Reversed, ne Waived) i ne traži ove grantove.
/// </summary>
public static class PolicyOverride
{
    /// <summary>Provjera prije transakcije (oblik zahtjeva): razlog + barem jedan grant otpisa. Točan grant provjerava
    /// <see cref="EnsureWaiverAllowed"/> kad je učinak poznat.</summary>
    public static async Task EnsureWaiverRequestAllowed(IGrantResolver grantResolver, Guid organizationId, Guid userId, string waiverReason)
    {
        AppointmentClosure.EnsureReason(waiverReason, "Otpis posljedice politike zahtijeva razlog.");
        GrantContext grants = await grantResolver.Resolve(organizationId, userId);
        if (!grants.HasAny(Grants.AppointmentsPolicyFeeWaive, Grants.AppointmentsPolicyUnitWaive))
            throw ForbiddenAppException.MissingAnyGrant(
                "Otpis posljedice politike zahtijeva ovlast appointments.policy.fee.waive ili appointments.policy.unit.waive.",
                Grants.AppointmentsPolicyFeeWaive, Grants.AppointmentsPolicyUnitWaive);
    }

    /// <summary>Točna provjera za poznat učinak posljedice.</summary>
    public static void EnsureWaiverAllowed(GrantContext grants, PolicyWaiverEffect effect)
    {
        string required = effect switch
        {
            PolicyWaiverEffect.Fee => Grants.AppointmentsPolicyFeeWaive,
            PolicyWaiverEffect.Unit => Grants.AppointmentsPolicyUnitWaive,
            _ => null
        };
        if (required == null)
        {
            if (!grants.HasAny(Grants.AppointmentsPolicyFeeWaive, Grants.AppointmentsPolicyUnitWaive))
                throw ForbiddenAppException.MissingAnyGrant(
                "Otpis posljedice politike zahtijeva ovlast appointments.policy.fee.waive ili appointments.policy.unit.waive.",
                Grants.AppointmentsPolicyFeeWaive, Grants.AppointmentsPolicyUnitWaive);
            return;
        }
        if (!grants.Has(required))
            throw ForbiddenAppException.MissingGrant(effect == PolicyWaiverEffect.Fee
                ? $"Otpis naknade politike zahtijeva ovlast {required}."
                : $"Otpis jedinice paketa ili kredita članarine zahtijeva ovlast {required}.", required);
    }

    /// <summary>Učinak POSTOJEĆE posljedice (naknadni otpis): kredit članarine ili aktivna potrošnja paketa → jedinica; inače
    /// naknada &gt; 0 → naknada.</summary>
    public static PolicyWaiverEffect EffectOf(BookingSegmentParticipation participation, ParticipationPolicyConsequence consequence)
    {
        if (consequence.MembershipCreditForfeited || PolicyConsequences.ActiveConsumptionOf(participation, consequence) != null)
            return PolicyWaiverEffect.Unit;
        return consequence.CalculatedFeeAmount > 0m ? PolicyWaiverEffect.Fee : PolicyWaiverEffect.None;
    }
}
