namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Komercijalni predmet jednog CommissionRule retka — vidi CommissionRule.cs. Svaka vrijednost ima točno
/// JEDAN odgovarajući tipizirani FK (ServiceId/ProductId/PackageId/MembershipPlanId) koji MORA biti popunjen i mora se
/// slagati sa SubjectType (CHECK constraint u migraciji, isti obrazac kao CheckoutItemType).
/// </summary>
public enum CommissionSubjectType
{
    Service,
    Product,
    Package,

    /// <summary>P2 (2F, Q39/Q42) — plan članarine; samo pravila za prodaju (prva prodaja članarine).</summary>
    MembershipPlan,

    /// <summary>P2 (2F, Vagaro) — opće pravilo zaposlenika za SVE individualne usluge (bez predmeta); pravilo za uslugu ima
    /// prednost. Iznos je u razinama (commission_rule_tiers): u P2 točno jedna razina bez praga, faza Payroll dodaje razine po prometu.</summary>
    AllServices
}
