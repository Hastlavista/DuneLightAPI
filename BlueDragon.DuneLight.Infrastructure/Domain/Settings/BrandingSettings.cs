namespace BlueDragon.DuneLight.Infrastructure.Domain.Settings;

/// <summary>
/// Konfiguracija za pohranu branding datoteka (logo/favicon) na disk servera. Spremljene datoteke služe se
/// javno preko API-ja (query odgovara <see cref="StoragePath"/> + relativnoj putanji unutar njega), a u bazi
/// se čuva samo relativni javni URL (npr. /uploads/branding/moja-firma/logo-a1b2.png).
/// </summary>
public class BrandingSettings
{
    /// <summary>
    /// Fizička mapa na disku servera gdje se spremaju branding datoteke — koristi je BrandingFileStorage.
    /// Relativan put (npr. "wwwroot/branding", default za dev na Windowsu i za produkciju na Hetzneru) rješava se
    /// nasuprot ContentRootPath-u aplikacije, radi identično na Windowsu i Linuxu. Ako se ikad poželi izmjestiti
    /// izvan app foldera (npr. zbog deploy procesa koji briše/zamjenjuje cijeli folder), ovdje se može staviti
    /// apsolutan put (npr. "/var/dunelight/storage/branding" na Linuxu) — Path.Combine ga tada koristi kakav jest.
    /// </summary>
    public string StoragePath { get; set; } = "wwwroot/branding";

    /// <summary>Javni URL prefiks pod kojim se datoteke serviraju na GET endpointu. Npr. "/uploads/branding".</summary>
    public string PublicBasePath { get; set; } = "/uploads/branding";

    /// <summary>Maksimalna dopuštena veličina datoteke u bajtovima.</summary>
    public long MaxFileSizeBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// Dozvoljene ekstenzije (bez točke, malim slovima). Namjerno bez "svg" — SVG serviran s image/svg+xml
    /// izvršava embedded &lt;script&gt; ako se URL otvori direktno u browseru, a logo/favicon su javno dostupni
    /// bez autentifikacije (vidi BrandingFileController) pa bi to bio stored-XSS vektor preko Owner/Admin uploada.
    /// </summary>
    public string[] AllowedExtensions { get; set; } = { "png", "jpg", "jpeg", "gif", "webp", "ico", "avif" };
}