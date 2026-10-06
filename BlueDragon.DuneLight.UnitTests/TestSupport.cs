using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Services;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Zajednički test-helperi. CapabilityMaterializationService je namjerno konkretna klasa (ne mock) —
/// čista/bez-stanja implementacija, isti obrazac kao produkcijski kod.</summary>
public static class TestSupport
{
    public static readonly ICapabilityMaterializationService Materializer = new CapabilityMaterializationService();
}
