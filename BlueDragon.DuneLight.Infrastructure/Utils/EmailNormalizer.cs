namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// ADR-0020 — jedino pravilo za email klijenta i korisničkog računa: sprema se trimana vrijednost (prazno nakon trima =
/// nema emaila), a jedinstvenost i prijava uspoređuju bez obzira na velika/mala slova (DB: lower(email) unique indeksi
/// ux_clients_organization_email i ux_users_organization_email).
/// </summary>
public static class EmailNormalizer
{
    public static string Normalize(string email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    /// <summary>Ključ usporedbe — isti oblik kao lower(email) u bazi.</summary>
    public static string ComparisonKey(string email) => Normalize(email)?.ToLowerInvariant();
}
