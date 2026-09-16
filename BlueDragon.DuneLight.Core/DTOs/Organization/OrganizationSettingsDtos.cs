using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Organization;

public class OrganizationSettingsDto
{
    public int CancellationCutoffMinutes { get; set; }
}

public class OrganizationSettingsUpdateRequest
{
    [Range(0, int.MaxValue, ErrorMessage = "Rok za otkazivanje ne smije biti negativan.")]
    public int CancellationCutoffMinutes { get; set; }
}
