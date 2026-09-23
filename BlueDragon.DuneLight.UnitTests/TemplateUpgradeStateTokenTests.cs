using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Services;
using Xunit;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Layer 1 — TemplateUpgradeStateToken determinizam/nezavisnost-o-poretku/osjetljivost-na-promjenu
/// (vidi backend test plan §3, Part K). EF-free — testira se izravno bez ijedne baze/servisa.</summary>
public class TemplateUpgradeStateTokenTests
{
    private static readonly Guid CapA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CapB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CapC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void SameInput_ProducesSameToken()
    {
        string token1 = Compute();
        string token2 = Compute();

        Assert.Equal(token1, token2);
    }

    [Fact]
    public void ShuffledInputOrder_ProducesSameToken()
    {
        string token1 = TemplateUpgradeStateToken.Compute(
            "admin", 1,
            new (Guid, string)[] { (CapA, "View"), (CapB, "Manage"), (CapC, "Own") },
            new[] { "compat.b", "compat.a" },
            new[] { "manual.b", "manual.a" });

        string token2 = TemplateUpgradeStateToken.Compute(
            "admin", 1,
            new (Guid, string)[] { (CapC, "Own"), (CapA, "View"), (CapB, "Manage") },
            new[] { "compat.a", "compat.b" },
            new[] { "manual.a", "manual.b" });

        Assert.Equal(token1, token2);
    }

    [Fact]
    public void ChangingAnySnapshotScope_ChangesToken()
    {
        string original = Compute();
        string changed = TemplateUpgradeStateToken.Compute(
            "admin", 1,
            new (Guid, string)[] { (CapA, "Manage"), (CapB, "Manage") }, // CapA View -> Manage
            new[] { "compat.a" },
            new[] { "manual.a" });

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void ChangingTemplateVersion_ChangesToken()
    {
        string v1 = Compute();
        string v2 = TemplateUpgradeStateToken.Compute(
            "admin", 2,
            new (Guid, string)[] { (CapA, "View"), (CapB, "Manage") },
            new[] { "compat.a" },
            new[] { "manual.a" });

        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void ChangingCompatibilityGrantKeys_ChangesToken()
    {
        string original = Compute();
        string changed = TemplateUpgradeStateToken.Compute(
            "admin", 1,
            new (Guid, string)[] { (CapA, "View"), (CapB, "Manage") },
            new[] { "compat.a", "compat.extra" },
            new[] { "manual.a" });

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void ChangingManualGrantKeys_ChangesToken()
    {
        string original = Compute();
        string changed = TemplateUpgradeStateToken.Compute(
            "admin", 1,
            new (Guid, string)[] { (CapA, "View"), (CapB, "Manage") },
            new[] { "compat.a" },
            new[] { "manual.a", "manual.extra" });

        Assert.NotEqual(original, changed);
    }

    private static string Compute() => TemplateUpgradeStateToken.Compute(
        "admin", 1,
        new (Guid, string)[] { (CapA, "View"), (CapB, "Manage") },
        new[] { "compat.a" },
        new[] { "manual.a" });
}
