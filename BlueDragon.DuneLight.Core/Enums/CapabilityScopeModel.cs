namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>Koje CapabilitySelectedScope vrijednosti su legalne za jednu CapabilityDefinition — vidi
/// CapabilityMaterializationService za točnu tablicu razrješavanja u raw grantove.</summary>
public enum CapabilityScopeModel
{
    /// <summary>Legalne vrijednosti: None/On. Jedan raw grant bez own/all/view podjele.</summary>
    None,

    /// <summary>Legalne vrijednosti: None/View/Manage.</summary>
    ViewManage,

    /// <summary>Legalne vrijednosti: None/Own/All. Own i All se MEĐUSOBNO ISKLJUČUJU — All ne materijalizira Own.</summary>
    OwnAll,

    /// <summary>Legalne vrijednosti: None/View/Own/All.</summary>
    ViewOwnAll
}
