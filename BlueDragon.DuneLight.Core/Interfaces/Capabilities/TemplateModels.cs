using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.Interfaces.Capabilities;

/// <summary>Jedan capability-odabir unutar razrješenog predloška, s dovoljno podataka (ScopeModel + Grants) da
/// ICapabilityMaterializationService odmah izračuna raw grant skup — bez dodatnog upita u bazu.</summary>
public record TemplateCapabilitySelection(
    Guid CapabilityDefinitionId,
    string CapabilityKey,
    int CapabilityVersion,
    CapabilityScopeModel ScopeModel,
    IReadOnlyList<CapabilityGrantRoleEntry> Grants,
    CapabilitySelectedScope SelectedScope);

/// <summary>Jedan template-vlasnički compatibility-extra raw grant — vidi DefaultRoleTemplateGrant klasnu napomenu.</summary>
public record TemplateCompatibilityGrant(string GrantKey, DefaultRoleTemplateGrantReason Reason);

/// <summary>Puni razrješeni graf jedne DefaultRoleTemplate verzije (predložak + odabrane capability-je +
/// compatibility-extra grantove) — dovoljno za materijalizaciju BEZ dodatnih upita u bazu.</summary>
public record ResolvedDefaultRoleTemplate(
    Guid TemplateId,
    string TemplateKey,
    int TemplateVersion,
    string DisplayNameHr,
    IReadOnlyList<TemplateCapabilitySelection> Selections,
    IReadOnlyList<TemplateCompatibilityGrant> CompatibilityGrants);
