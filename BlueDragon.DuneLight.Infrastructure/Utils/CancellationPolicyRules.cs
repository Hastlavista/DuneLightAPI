using System;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared.Exceptions;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P1 (ADR-0016/ADR-0017) — čista pravila politike otkazivanja (bez I/O): klasifikacija kasnog otkazivanja (D3),
/// izračun naknade (D4) i valjanost pravila verzije. Zamjenjuje BookingCancellationPolicy (jedan prozor organizacije).
/// </summary>
public static class CancellationPolicyRules
{
    /// <summary>D3 — klijentsko otkazivanje je kasno kad je do PlannedStart segmenta preostalo STROGO manje od prozora.
    /// Točno na granici je na vrijeme; apsolutno trajanje između UTC instanata (bez lokalnog sata/DST aritmetike); prozor 0
    /// znači da je svako valjano otkazivanje prije početka na vrijeme.</summary>
    public static bool IsLateCancellation(DateTimeOffset segmentStartsAt, DateTimeOffset cancelledAt, int windowMinutes)
    {
        if (windowMinutes < 0)
            throw new ArgumentOutOfRangeException(nameof(windowMinutes));
        return segmentStartsAt - cancelledAt < TimeSpan.FromMinutes(windowMinutes);
    }

    /// <summary>D4 — naknada događaja: osnovica je Participation.Amount (konačna cijena) u trenutku događaja;
    /// finalFee = Round(min(rawFee, base), 2, AwayFromZero), WasFeeCapped = rawFee &gt; base. Samo decimal aritmetika.</summary>
    public static (decimal FinalFee, bool WasCapped) CalculateFee(decimal baseAmount, CancellationFeeType feeType, decimal? feeValue)
    {
        decimal rawFee = feeType switch
        {
            CancellationFeeType.None => 0m,
            CancellationFeeType.Fixed => feeValue ?? throw new InvalidOperationException("Fixed naknada nema iznos."),
            CancellationFeeType.Percentage => baseAmount * (feeValue ?? throw new InvalidOperationException("Postotna naknada nema postotak.")) / 100m,
            _ => throw new ArgumentOutOfRangeException(nameof(feeType))
        };
        decimal finalFee = Math.Round(Math.Min(rawFee, baseAmount), 2, MidpointRounding.AwayFromZero);
        return (finalFee, rawFee > baseAmount);
    }

    /// <summary>Valjanost pravila nove verzije (D3/D4): prozor &gt;= 0; None bez vrijednosti; Fixed &gt;= 0 i Percentage
    /// 0–100, oba s najviše 2 decimale (osnovica je numeric(10,2), pa finalFee nikad ne prelazi osnovicu).</summary>
    public static void Validate(CancellationPolicyRulesRequest rules)
    {
        if (rules == null)
            throw new ValidationAppException("Pravila politike su obavezna.");
        if (rules.CancellationWindowMinutes < 0)
            throw new ValidationAppException("Prozor otkazivanja ne smije biti negativan.");
        ValidateEvent(rules.LateCancellation, "kasno otkazivanje");
        ValidateEvent(rules.NoShow, "izostanak");
    }

    private static void ValidateEvent(CancellationPolicyEventRuleDto rule, string label)
    {
        if (rule?.FeeType == null || rule.PackageAction == null)
            throw new ValidationAppException($"Pravilo za {label} mora imati FeeType i PackageAction.");

        switch (rule.FeeType.Value)
        {
            case CancellationFeeType.None:
                if (rule.FeeValue.HasValue)
                    throw new ValidationAppException($"Pravilo za {label}: FeeType None ne smije imati iznos.");
                break;
            case CancellationFeeType.Fixed:
                if (rule.FeeValue is not { } amount || amount < 0m || amount > 99999999.99m || HasMoreThanTwoDecimals(amount))
                    throw new ValidationAppException($"Pravilo za {label}: fiksna naknada mora biti iznos >= 0 s najviše 2 decimale.");
                break;
            case CancellationFeeType.Percentage:
                if (rule.FeeValue is not { } percent || percent < 0m || percent > 100m || HasMoreThanTwoDecimals(percent))
                    throw new ValidationAppException($"Pravilo za {label}: postotak mora biti između 0 i 100 s najviše 2 decimale.");
                break;
            default:
                throw new ValidationAppException($"Pravilo za {label}: nepoznat FeeType.");
        }
    }

    private static bool HasMoreThanTwoDecimals(decimal value) => decimal.Round(value, 2) != value;

    /// <summary>P2 (Q31, pregled 2D #8) — dimenzija članarine za događaj: izričit izbor organizacije u verziji politike ima
    /// prednost; bez izbora događaj BEZ naknade (FeeType None ili vrijednost 0) ne kažnjava ni članove (kredit se vraća,
    /// ReturnCreditChargeFee uz naknadu 0), a događaj s naknadom zadržava default ForfeitCredit (kredit umjesto naknade).
    /// Studio koji ne kažnjava kasni otkaz tako ne kažnjava nesvjesno samo članove.</summary>
    public static CancellationMembershipAction MembershipActionFor(
        CancellationMembershipAction? requested, CancellationFeeType feeType, decimal? feeValue) =>
        requested ?? (IsWithoutFee(feeType, feeValue)
            ? CancellationMembershipAction.ReturnCreditChargeFee
            : CancellationMembershipAction.ForfeitCredit);

    public static bool IsWithoutFee(CancellationFeeType feeType, decimal? feeValue) =>
        feeType == CancellationFeeType.None || feeValue is 0m;
}
