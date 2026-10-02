using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1G — JEDINO pravilo podobnosti zaposlenika za uslugu (zaključano): zaposlenik BEZ ijedne eksplicitne dodjele
/// usluge (EmployeeServiceAssignment) smije izvoditi SVE usluge; s jednom ili više dodjela — samo dodijeljene. Isto
/// pravilo vrijedi za termine, grupe (osoblje predložaka, generiranje) i čitanja (slobodni termini); upitna verzija je
/// IEmployeeHandler.CanEmployeePerformService. Ispravlja legacy ponašanje "prazno = nijedna usluga".
/// </summary>
public static class EmployeeServiceEligibility
{
    public static bool CanPerform(IReadOnlyCollection<Guid> assignedServiceIds, Guid serviceId)
    {
        ArgumentNullException.ThrowIfNull(assignedServiceIds);
        return assignedServiceIds.Count == 0 || assignedServiceIds.Contains(serviceId);
    }
}
