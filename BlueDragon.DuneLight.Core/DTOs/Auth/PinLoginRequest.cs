using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Auth;

public class PinLoginRequest
{
    [Required] public string OrganizationSlug { get; set; }
    [Required] public string Email { get; set; }
    [Required] public string Pin { get; set; }
}
