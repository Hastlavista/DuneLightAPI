using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Onboarding;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IOnboardingHandler
{
    Task<OnboardingStatusDto> GetStatus(Guid organizationId);
}
