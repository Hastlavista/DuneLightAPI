namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q19) — kanal prodaje: Staff = recepcija (aktivno odmah od datuma početka); Online kasnije (aktivno tek
/// nakon uspješnog prvog plaćanja). Pravilo aktivacije se razrješava po kanalu.</summary>
public enum MembershipSaleChannel
{
    Staff,
    Online
}
