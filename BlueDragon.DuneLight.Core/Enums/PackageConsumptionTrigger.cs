namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P1 (ADR-0017, D6) — zašto je jedinica paketa potrošena: izvršenje usluge (completion/check-in) ili posljedica
/// politike (kazna kasnog otkazivanja / izostanka, vezana uz ParticipationPolicyConsequence).</summary>
public enum PackageConsumptionTrigger
{
    ServiceCompletion,
    PolicyConsequence
}
