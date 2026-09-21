namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>Odabrani opseg jedne capability-ja unutar predloška/GrantGroup-e. Koje vrijednosti su legalne ovisi
/// o CapabilityDefinition.ScopeModel — vidi taj enum za tablicu.</summary>
public enum CapabilitySelectedScope
{
    /// <summary>Capability nije odabran/aktivan. Legalan za SVAKI ScopeModel.</summary>
    None,

    View,
    Manage,
    Own,
    All,

    /// <summary>Jedina ne-None vrijednost za ScopeModel.None.</summary>
    On
}
