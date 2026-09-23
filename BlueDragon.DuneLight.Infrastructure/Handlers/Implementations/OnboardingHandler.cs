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

    public async Task<OnboardingStatusDto> GetStatus(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        return new OnboardingStatusDto
        {
            HasCompany = await context.Companies.AnyAsync(c => c.OrganizationId == organizationId && c.IsActive),
            HasEngagementType = await context.EngagementTypes.AnyAsync(e => e.OrganizationId == organizationId && e.IsActive),
            HasService = await context.Services.AnyAsync(s => s.OrganizationId == organizationId && s.IsActive),
            // Residual IsOwner Removal — "je li netko DRUGI (osim mene) već dobio profil" je uvijek bila stvarna
            // namjera ovog "Zaposlenici" koraka, ranije provedena kroz !User.IsOwner kao (netočnu) zamjensku
            // varijablu za "nije trenutni pozivatelj". Vlastiti profil (bivši HasOwnerProfile) frontend već zna
            // izravno preko CurrentEmployeeService.hasProfile() pa ovdje više ne treba poseban stupac.
            HasOtherEmployee = await context.Employees.AnyAsync(e => e.OrganizationId == organizationId && e.UserId != userId),
            HasClient = await context.Clients.AnyAsync(c => c.OrganizationId == organizationId)
        };
    }
}
