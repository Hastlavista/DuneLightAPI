using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Onboarding;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IOnboardingHandler
{
    /// <summary>Residual IsOwner Removal — <paramref name="userId"/> zamjenjuje staru User.IsOwner provjeru:
    /// "ima li NEKI drugi zaposlenik (osim trenutnog pozivatelja) profil" je stvarna poslovna namjera iza
    /// "Zaposlenici" koraka checkliste, ne "ima li Owner profil" (vidi OnboardingStatusDto.HasOtherEmployee).</summary>
    Task<OnboardingStatusDto> GetStatus(Guid organizationId, Guid userId);
}
