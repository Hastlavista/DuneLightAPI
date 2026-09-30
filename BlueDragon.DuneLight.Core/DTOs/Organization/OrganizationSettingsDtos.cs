using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Organization;

public class OrganizationSettingsDto
{
    public int CancellationCutoffMinutes { get; set; }

    /// <summary>IANA vremenska zona poslovnog kalendara organizacije (npr. "Europe/Zagreb").</summary>
    public string TimeZone { get; set; }
}

public class OrganizationTimeZoneUpdateRequest
{
    /// <summary>IANA id (npr. "Europe/Zagreb", "UTC"); nepoznat ili Windows id se odbija.</summary>
    [Required]
    [MaxLength(64)]
    public string TimeZone { get; set; }
}

public class OrganizationSettingsUpdateRequest
{
    [Range(0, int.MaxValue, ErrorMessage = "Rok za otkazivanje ne smije biti negativan.")]
    public int CancellationCutoffMinutes { get; set; }
}
