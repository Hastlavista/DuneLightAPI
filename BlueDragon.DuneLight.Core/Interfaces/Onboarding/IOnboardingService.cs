using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Onboarding;

namespace BlueDragon.DuneLight.Core.Interfaces.Onboarding;

public interface IOnboardingService
{
    /// <summary>Jedan poziv koji sažima "checklist za početak" umjesto šest paralelnih upita s frontenda.
    /// <paramref name="userId"/> - vidi IOnboardingHandler.GetStatus.</summary>
    Task<OnboardingStatusDto> GetStatus(Guid organizationId, Guid userId);
}
