using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Auth;

public class RegisterRequest
{
    [Required] public string OrganizationName { get; set; }
    [Required] public string Email { get; set; }
    [Required] public string Password { get; set; }
}
