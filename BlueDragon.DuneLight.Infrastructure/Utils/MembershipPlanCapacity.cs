using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P2 (pregled 2B, #8) — broj članstava koja će na datum biti na planu, za provjeru kapaciteta pri prodaji i promjeni plana
/// (kapacitet postoji da se ne proda više članstava nego što termini mogu primiti; promjena plana ne smije biti zaobilazni put).
/// Broje se: članstva na planu koja na datum još traju i ne odlaze (klijentova promjena na drugi plan koja stupa na snagu do tog
/// datuma), plus SVA zakazana dolaženja na plan klijentovom promjenom (konzervativno, bez obzira stupaju li prije ili poslije
/// datuma — dolazak nakon datuma ionako zauzima mjesto kasnije). Čista funkcija; zahtijeva graf iz ClientMembershipHandlera.
/// </summary>
public static class MembershipPlanCapacity
{
    public static int CountOnPlanAt(Guid planId, DateOnly date, IEnumerable<ClientMembership> candidates, Guid? excludeMembershipId = null)
    {
        int count = 0;
        foreach (ClientMembership membership in candidates.Where(m => m.VoidedAt == null && m.Id != excludeMembershipId))
        {
            bool clientChange = membership.PendingSource == MembershipPendingChangeSource.ClientPlanChange && membership.PendingPlanVersion != null;
            DateOnly? changeOn = clientChange ? MembershipTimelines.PendingEffectiveOn(membership) : null;

            if (membership.MembershipPlanId == planId)
            {
                bool stillOnDate = membership.EndsOn == null || membership.EndsOn.Value >= date;
                bool departsByDate = clientChange && membership.PendingPlanVersion.MembershipPlanId != planId && changeOn <= date;
                if (stillOnDate && !departsByDate)
                    count++;
            }
            else if (clientChange && membership.PendingPlanVersion.MembershipPlanId == planId
                     && (membership.EndsOn == null || membership.EndsOn.Value >= changeOn.Value))
            {
                count++;
            }
        }

        return count;
    }
}
