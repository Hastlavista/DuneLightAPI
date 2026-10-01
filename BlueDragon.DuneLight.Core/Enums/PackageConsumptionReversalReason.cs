namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Phase D3B3A — zašto je potrošnja paketa poništena. Odgovara postojećim putanjama vraćanja ulaska:
/// Cancellation/NoShow (otkazivanje/izostanak uz eksplicitan ili grupni povrat ulaska) i CompletionCorrection
/// (administrativna korekcija Completed -&gt; Confirmed, individualna ili grupna).
/// </summary>
public enum PackageConsumptionReversalReason
{
    Cancellation,
    NoShow,
    CompletionCorrection
}
