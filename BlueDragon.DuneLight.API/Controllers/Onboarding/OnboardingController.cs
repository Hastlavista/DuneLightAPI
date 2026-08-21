using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Onboarding;
using BlueDragon.DuneLight.Core.Interfaces.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Onboarding;

[ApiController]
[Route("api/onboarding-status")]
[Produces("application/json")]
public class OnboardingController : ControllerBase
{
    private readonly IOnboardingService _onboardingService;

    public OnboardingController(IOnboardingService onboardingService)
    {
        _onboardingService = onboardingService;
    }

    /// <summary>"Checklist za početak" na admin dashboardu — jedan poziv umjesto šest paralelnih.</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<OnboardingStatusDto>> GetStatus()
    {
        return Ok(await _onboardingService.GetStatus(this.CurrentOrganizationId()));
    }
}
