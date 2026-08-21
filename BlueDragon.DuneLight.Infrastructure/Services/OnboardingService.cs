using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Onboarding;
using BlueDragon.DuneLight.Core.Interfaces.Onboarding;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class OnboardingService : IOnboardingService
{
    private readonly IOnboardingHandler _onboardingHandler;

    public OnboardingService(IOnboardingHandler onboardingHandler)
    {
        _onboardingHandler = onboardingHandler;
    }

    public Task<OnboardingStatusDto> GetStatus(Guid organizationId)
    {
        return _onboardingHandler.GetStatus(organizationId);
    }
}
