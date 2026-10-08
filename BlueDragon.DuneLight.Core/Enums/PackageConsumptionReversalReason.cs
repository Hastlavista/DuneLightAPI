namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Phase D3B3A — zašto je potrošnja paketa poništena. Cancellation/NoShow/CompletionCorrection: korekcija statusa koja
/// poništava potrošnju izvršenja usluge (prema ciljnom statusu). P1: PolicyConsequenceReversed (posljedica politike je
/// poništena korekcijom) i PolicyConsequenceWaived (posljedica je otpisana) vraćaju jedinicu potrošenu kao kaznu.
/// </summary>
public enum PackageConsumptionReversalReason
{
    Cancellation,
    NoShow,
    CompletionCorrection,
    PolicyConsequenceReversed,
    PolicyConsequenceWaived
}
