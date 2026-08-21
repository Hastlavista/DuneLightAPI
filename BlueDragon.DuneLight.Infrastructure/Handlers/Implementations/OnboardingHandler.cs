using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Onboarding;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class OnboardingHandler : IOnboardingHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public OnboardingHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<OnboardingStatusDto> GetStatus(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        return new OnboardingStatusDto
        {
            HasLocation = await context.Companies.AnyAsync(c => c.OrganizationId == organizationId && c.IsActive),
            HasEngagementType = await context.EngagementTypes.AnyAsync(e => e.OrganizationId == organizationId && e.IsActive),
            HasService = await context.Services.AnyAsync(s => s.OrganizationId == organizationId && s.IsActive),
            HasOwnerProfile = await context.Employees.AnyAsync(e => e.OrganizationId == organizationId && e.User.IsOwner),
            HasOtherEmployee = await context.Employees.AnyAsync(e => e.OrganizationId == organizationId && !e.User.IsOwner),
            HasClient = await context.Clients.AnyAsync(c => c.OrganizationId == organizationId)
        };
    }
}
