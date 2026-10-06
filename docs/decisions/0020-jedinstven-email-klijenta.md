# ADR-0020: Jedinstven email klijenta i korisničkog računa unutar organizacije (case-insensitive)

- **Status:** Prihvaćeno, implementirano 2026-10-06
- **Datum:** 2026-10-06
- **Zapis segmenta:** [Foundation cleanup](../foundation-cleanup/FOUNDATION_CLEANUP_DECISION_RECORD.md)

## Kontekst
Stari sustav je dopuštao isti email kod više klijenata (Business Rules vodič §15.2). Target Architecture v1 §6 i sažetak
arhitekta kažu da duplikat emaila NIJE dopušten, ali u kodu nema ni provjere u `ClientService` ni unique indeksa na
`clients.email`. Korisnički računi (`users`) imaju jedinstven email u organizaciji (`EMAIL_ALREADY_IN_USE`,
`uq_users_organization_id_email`), ali osjetljivo na velika/mala slova — `Ana@x.hr` i `ana@x.hr` su danas dva računa, a
prijava traži točno slovo-po-slovo podudaranje.

## Razmotrene opcije
1. **Dopustiti duplikate klijenata** (staro ponašanje) - jednostavno, ali otežava identifikaciju klijenta i notifikacije.
2. **Jedinstven email po organizaciji, samo za klijente** - usko, ali ostavlja nedosljedno pravilo za korisničke račune.
3. **Jedinstven, case-insensitive i trimani email po organizaciji za klijente I korisničke račune** - jedno pravilo.

## Odluka
Opcija 3:
- Email se prije spremanja i usporedbe **trima** (prazno nakon trima = nema emaila). Sprema se trimana vrijednost.
- Usporedba je **neovisna o velikim/malim slovima** (`lower(email)`).
- **Klijent:** email je jedinstven unutar organizacije; klijent bez emaila je dopušten. Duplikat → `409
  CLIENT_EMAIL_ALREADY_IN_USE` (novi, specifičan kod).
- **Korisnički račun:** email je jedinstven unutar organizacije case-insensitive; duplikat → `409 EMAIL_ALREADY_IN_USE`
  (postojeći kod). Prijava (Login, PinLogin) traži korisnika po emailu case-insensitive i trimano.

## Posljedice
- Provjera u servisima (kreiranje i izmjena klijenta; kreiranje korisnika) + DB unique indeksi po
  (`organization_id`, `lower(email)`) — za klijente samo gdje email nije NULL; postojeći
  `uq_users_organization_id_email` zamijenjen funkcijskim indeksom.
- Anonimizirani klijenti imaju `NULL` email, pa ne ulaze u provjeru (anonimizacija oslobađa email).
- Razvojna baza se smije ponovno izgraditi (ADR-0003); seed ne smije imati duplikate.
- Platformski računi (`PlatformAccount`) nisu tenant korisnici i nisu dio ove odluke.

## Implementacija (2026-10-06)
- `Infrastructure/Utils/EmailNormalizer.cs` — jedino pravilo (trim; ključ usporedbe = lowercase).
- Klijenti: `ClientService.Create/Update` normaliziraju email i provjeravaju `IClientHandler.IsEmailTaken`; utrka dva
  istovremena upisa (unique violation na `ux_clients_organization_email`) prevodi se u `CLIENT_EMAIL_ALREADY_IN_USE`.
- Korisnici: `AuthHandler.EmailExists / GetUserByCredentials / GetUserByPinCredentials` uspoređuju `lower(email)`;
  `AuthService.Register` i `EmployeeService.CreateWithLogin` spremaju trimani email; utrka na
  `ux_users_organization_email` u `CreateWithLogin` prevodi se u `EMAIL_ALREADY_IN_USE`.
- Migracija `Migration_2026_10_24_UniqueEmailCaseInsensitive` (`20261024000000`).
- Testovi: `UniqueEmailTests` (10).
- Izvan opsega: kontakt email zaposlenika (`Employee.Email`) na običnom `POST/PUT /api/employees` nije login email i
  nije normaliziran; `PlatformAccount` email.
