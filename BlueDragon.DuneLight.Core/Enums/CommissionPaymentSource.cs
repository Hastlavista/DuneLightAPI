namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2F, Q28) — način plaćanja odrađene sesije za nadjačavanje pravila i osnovicu provizije: izravna naplata, pokriće
/// paketom ili pokriće članarinom (izvodi se iz ledgera pokrića u trenutku nastanka provizije).</summary>
public enum CommissionPaymentSource
{
    Direct,
    Package,
    Membership
}
