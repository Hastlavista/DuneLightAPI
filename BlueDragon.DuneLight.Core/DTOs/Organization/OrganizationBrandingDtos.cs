using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Organization;

/// <summary>
/// Vizualni identitet organizacije (branding) — služi frontendu za prilagodbu izgleda (sidebar, login screen,
/// gumbi, linkovi, favicon). Logo/favicon su relativni URL-ovi na disk pohranjene datoteke. Boje su u HEX formatu.
/// </summary>
public class OrganizationBrandingDto
{
    public string Logo { get; set; }
    public string Favicon { get; set; }
    public string PrimaryColor { get; set; }
    public string SecondaryColor { get; set; }
}

/// <summary>Puni branding koji dobiva organizacija prilikom uređivanja u settingsima.</summary>
public class OrganizationBrandingResponse : OrganizationBrandingDto
{
    public string OrganizationName { get; set; }
    public string OrganizationSlug { get; set; }
}

/// <summary>
/// Ažurira boje (logo/favicon idu preko zasebnog upload endpointa). Obje boje su obavezne na svaki zahtjev —
/// namjerno nema djelomičnog ažuriranja (samo primarna ili samo sekundarna), da organizacija ne završi s
/// rastrgan izgledom (jedna boja custom, druga null pa pada natrag na platformski default).
/// </summary>
public class BrandingColorsUpdateRequest
{
    [Required(ErrorMessage = "Primarna boja je obavezna.")]
    [RegularExpression(@"^#([0-9A-Fa-f]{6})$", ErrorMessage = "Primarna boja mora biti u HEX formatu (npr. #1A73E8).")]
    [MaxLength(7)]
    public string PrimaryColor { get; set; }

    [Required(ErrorMessage = "Sekundarna boja je obavezna.")]
    [RegularExpression(@"^#([0-9A-Fa-f]{6})$", ErrorMessage = "Sekundarna boja mora biti u HEX formatu (npr. #185ABC).")]
    [MaxLength(7)]
    public string SecondaryColor { get; set; }
}

/// <summary>Odgovor nakon uspješnog uploada logo/favicona.</summary>
public class BrandingUploadResponse
{
    public string Url { get; set; }
}
