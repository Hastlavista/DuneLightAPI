namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>Razlog zašto DefaultRoleTemplateGrant retak postoji. Trenutno postoji samo jedan legitiman razlog —
/// vidi DefaultRoleTemplateGrant klasnu napomenu zašto ovo NIJE isto što i ručni/Advanced tenant grant.</summary>
public enum DefaultRoleTemplateGrantReason
{
    /// <summary>Predložak treba točno reproducirati legacy raw-grant skup, ali odabrani capability-scope-ovi
    /// (max "All"/"Manage") strukturno ne pokrivaju jedan "own"-variant grant — vidi CapabilityMaterializationService
    /// napomenu "All ne materijalizira Own".</summary>
    CompatibilityExtra
}
