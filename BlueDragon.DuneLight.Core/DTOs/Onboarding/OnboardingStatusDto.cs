namespace BlueDragon.DuneLight.Core.DTOs.Onboarding;

public class OnboardingStatusDto
{
    public bool HasCompany { get; set; }
    public bool HasEngagementType { get; set; }
    public bool HasService { get; set; }

    /// <summary>Ima li organizacija zaposlenika koji NIJE trenutni pozivatelj (Residual IsOwner Removal —
    /// zamjenjuje staru !User.IsOwner provjeru). Trenutni pozivateljev vlastiti profil frontend već zna izravno
    /// preko CurrentEmployeeService.hasProfile(), pa za to više ne postoji poseban DTO stupac.</summary>
    public bool HasOtherEmployee { get; set; }
    public bool HasClient { get; set; }
}
