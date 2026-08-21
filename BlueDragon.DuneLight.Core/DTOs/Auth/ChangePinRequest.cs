using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Auth;

public class ChangePinRequest
{
    /// <summary>Potvrda LOZINKOM, ne starim PIN-om — sigurnije, i pokriva prvo postavljanje PIN-a (kad ga korisnik još nema).</summary>
    [Required]
    public string CurrentPassword { get; set; }

    [Required]
    public string NewPin { get; set; }
}
