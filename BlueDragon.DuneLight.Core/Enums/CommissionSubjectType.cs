namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Komercijalni predmet jednog CommissionRule retka — vidi CommissionRule.cs. Svaka vrijednost ima točno
/// JEDAN odgovarajući tipizirani FK (ServiceId/ProductId/PackageId) koji MORA biti popunjen i mora se
/// slagati sa SubjectType (CHECK constraint u migraciji, isti obrazac kao CheckoutItemType).
/// </summary>
public enum CommissionSubjectType
{
    Service,
    Product,
    Package
}
