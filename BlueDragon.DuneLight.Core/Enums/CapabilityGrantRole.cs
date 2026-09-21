namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>Uloga jednog raw grant-ključa unutar jedne CapabilityDefinition — određuje pri kojem
/// CapabilitySelectedScope se taj grant materijalizira (vidi CapabilityMaterializationService).</summary>
public enum CapabilityGrantRole
{
    /// <summary>Materijalizira se kod SelectedScope=Own (OwnAll/ViewOwnAll modeli).</summary>
    PrimaryOwn,

    /// <summary>Materijalizira se kod SelectedScope=All (OwnAll/ViewOwnAll modeli). NIKAD kod Own.</summary>
    PrimaryAll,

    /// <summary>Materijalizira se kod SelectedScope=View ILI Own ILI All (ViewManage/ViewOwnAll modeli) — "bazni" pogled.</summary>
    PrimaryViewOnly,

    /// <summary>Materijalizira se kod SelectedScope=Manage (ViewManage modeli).</summary>
    PrimaryManage,

    /// <summary>Materijalizira se kod SelectedScope=On (None model) — jedini raw grant bez podjele opsega.</summary>
    PrimaryNoScope,

    /// <summary>Obavezni prateći grant koji ide uz aktivno stanje capability-ja (npr. sistemski preduvjet), a nije
    /// primarni nositelj odabranog opsega. Ne koristi se za opcionalne/frontend-only ovisnosti.</summary>
    MandatorySupporting
}
