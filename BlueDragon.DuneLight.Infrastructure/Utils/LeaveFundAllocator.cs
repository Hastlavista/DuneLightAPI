using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Čista mutacijska logika za trošenje/vraćanje dana fonda godišnjeg odmora — isti stil kao
/// ClientPackageEntryMutator. Deduct troši stariji (prvi u listi) fond prvo, automatski split preko više
/// fondova; blokira (LEAVE_FUND_EXCEEDED) ako zbroj svih dostupnih fondova ne pokriva traženo, BEZ
/// djelomične mutacije (provjera prije bilo kakve promjene) — vidi RosterEntryService.AllocateLeaveFund.
/// </summary>
public static class LeaveFundAllocator
{
    public static List<(LeaveFund Fund, int Days)> Deduct(List<LeaveFund> eligibleFundsOldestFirst, int requestedDays)
    {
        int totalAvailable = eligibleFundsOldestFirst.Sum(f => f.AllocatedDays - f.UsedDays);
        if (totalAvailable < requestedDays)
            throw new BusinessRuleException(
                ErrorCodes.LeaveFundExceeded,
                $"Nedovoljno dana godišnjeg odmora — dostupno {totalAvailable}, traženo {requestedDays}.",
                new { available = totalAvailable, requested = requestedDays });

        List<(LeaveFund Fund, int Days)> allocation = new();
        int remaining = requestedDays;

        foreach (LeaveFund fund in eligibleFundsOldestFirst)
        {
            if (remaining <= 0)
                break;

            int available = fund.AllocatedDays - fund.UsedDays;
            if (available <= 0)
                continue;

            int take = available < remaining ? available : remaining;
            fund.UsedDays += take;
            allocation.Add((fund, take));
            remaining -= take;
        }

        return allocation;
    }

    public static void Return(LeaveFund fund, int days)
    {
        fund.UsedDays -= days;
    }
}
