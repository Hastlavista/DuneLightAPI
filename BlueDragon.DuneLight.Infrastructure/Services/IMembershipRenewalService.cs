using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Pravila duga organizacije (Q15, 2C).</summary>
public readonly record struct MembershipDebtRules(int GraceDays, MembershipDebtBehavior Behavior, int? AutoEndAfterUnpaidPeriods);

/// <summary>
/// P2 (faza 2C) — JEDINA putanja koja otvara periode članstva i stvara zaduženja perioda (obnova). Idempotentna (unique
/// članstvo + početak perioda): sustiže sve periode koji su počeli do danas, a pri prodaji otvara prvi period i kad počinje u
/// budućnosti. Na granici obnove redom: automatski završetak nakon N neplaćenih perioda (postavka, default isključeno),
/// primjena zakazanih uvjeta (klijentov prelazak na neaktivan plan se ne primjenjuje, uz trajnu oznaku), završetak ako plan nije
/// aktivan (PlanDeactivated). Usklađuje kraj tekućeg perioda s izračunom (pauza produljuje period). Poziva se unutar
/// pozivateljeve transakcije pod zaključanim članstvom.
/// </summary>
public interface IMembershipRenewalService
{
    Task CatchUp(IUnitOfWork uow, ClientMembership membership, DateOnly today, MembershipDebtRules rules, Guid? userId);

    /// <summary>Obnova svih članstava organizacije; "danas" je lokalni datum zone organizacije (Q22) po poslovnom satu (T1: jedan
    /// sat, drugi dan samo pomakom sata organizacije). Svako članstvo u vlastitoj transakciji; greška jednog ne zaustavlja ostala.
    /// Vraća broj obrađenih.</summary>
    Task<int> RunForOrganization(Guid organizationId);

    Task<MembershipDebtRules> GetDebtRules(Guid organizationId);
}
