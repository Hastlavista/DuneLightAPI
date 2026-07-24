using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Auth;

public class LoginRequest
{
    [Required] public string OrganizationSlug { get; set; }
    [Required] public string Email { get; set; }
    [Required] public string Password { get; set; }
}
