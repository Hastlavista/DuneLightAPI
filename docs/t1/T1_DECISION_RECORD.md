# T1 — Sat sustava i testni alati — Decision Record

> Faza otvorena 2026-10-09 kao preduvjet frontend faze F1 (frontend `docs/f1/F1_DECISION_RECORD.md`, pitanja F1-Q2, F1-Q3,
> F1-Q5, F1-Q8). Plan: [T1_PLAN.md](T1_PLAN.md). Ovaj zapis ima prednost pred planom. Dnevnik odluka je na dnu.

## Zaključane odluke (2026-10-09)

### Sat sustava (TimeProvider)
- Zasebna mala backend faza prije F1; F1 koristi taj izvor vremena.
- **Jedan sat za cijeli sustav.** Obnova, zatvaranje termina, otkazni prozori, roster u prošlosti, validacija prošlosti (K1) i
  sve ostalo gledaju isti sat. Postojeći `POST /api/dev/time/membership-renewal-run` prelazi na novi sat i zaseban mehanizam
  se uklanja. Sva mjesta čitanja vremena se izlistaju i potvrdi se da su sva prebačena.
- **Pomak samo naprijed.** Pomak sata po organizaciji može se samo povećavati. Povratak na stvarno vrijeme ide zajedno s
  resetom testnih podataka (seed), jer bi podaci nastali "u budućnosti" inače postali nedosljedni.
- **Poslovi nakon skoka** (obnove, zaduženja, isteci, pauze koje završavaju) izvršavaju se sami, bez ručnog pokretanja, i
  ispravnim redoslijedom ako se preskoči više perioda.
- **Nikad u produkciji.** Uključuje se izričitom postavkom, ne samo imenom okruženja (klijent možda ne testira na
  Developmentu). Uključena postavka u produkciji → aplikacija odbija pokretanje s jasnom porukom.
- **Endpoint "trenutno vrijeme organizacije"** vraća stvarno vrijeme, pomaknuto vrijeme, pomak i zonu organizacije, te zonu
  poslovnice kad se razlikuje.
- Testovi koriste isti TimeProvider (fiksni sat), bez novih zasebnih mehanizama.
- Pravilo za frontend (vrijedi od F1): frontend nikad ne koristi sat preglednika za poslovnu logiku (frontend FE-ADR-0005).

### Seed (privremeni testni alat, uklanja se prije go-livea)
- Gumb u Managementu: **nova demo organizacija** (testni podaci i korisnici, može se resetirati) i **napuni postojeću
  organizaciju** (odabir s popisa).
- Punjenje postojeće organizacije **samo dodaje**: ne mijenja i ne briše postojeće poslovnice, radno vrijeme, usluge, grupe,
  cjenike, grupe ovlasti ni korisnike (korisnik ručno puni postojeću organizaciju postavkama klijenta).
- Bez zastavice "testna organizacija", bez zaštite od duplikata, bez detaljnog izvještaja; odgovor kaže koliko je čega dodano.
- Seed i simulirano vrijeme su pod istim prekidačem testnih alata; gašenje i uklanjanje je na popisu za go-live.

### Oblik 403
- `details.requiredGrants` je **lista** svih grantova koji nedostaju za radnju (korisnik odjednom vidi sve što mu treba).
- `details.reason` razlikuje barem `MissingGrant` i `OutOfScope` (uz `OutOfScope` koji opseg korisnik ima i koji je potreban);
  ostali slučajevi 403 se izlistaju s predloženom vrijednošću.
- Frontend prikaz je frontend odluka (FE-ADR-0003); oblik odgovora je ovdje.

### Swagger kao izvor frontend tipova
- Frontend tipove generira iz Swaggera; servisi ostaju ručni (FE-ADR-0004).
- Prije prvog generiranja na backendu se provjeri i po potrebi popravi: nullable polja označena kao nullable, enumi opisani kao
  stringovi (nazivi), ispravan format datuma i vremena; korisniku se javlja što je promijenjeno.
- Oblik 403 (`requiredGrants`, `reason`) i ostali odgovori s razlogom odbijanja opisani su u Swaggeru.
- Svaka promjena backend DTO-a uključuje ponovno generiranje frontend tipova u istoj promjeni (oba `CLAUDE.md`).

### Potvrda plana (2026-10-09)
- **T1-Q1:** povratak na stvarno vrijeme = "reset" demo organizacije: nova demo organizacija s pomakom 0 i svježim seedom, stara
  se deaktivira. U organizaciji koja nije demo (ručno punjena) pomak je **trajan** i ne vraća se; Management to jasno piše uz gumb
  za pomak (upozorenje prije potvrde).
- **T1-Q2:** pomak sata pokreće se samo iz Management portala. U aplikaciji je samo vidljiva traka sa simuliranim vremenom
  (FE-ADR-0005); korisnici studija ne mogu pomicati vrijeme.
- **T1-Q3:** oblik 403 i popravak Swaggera idu u T1.
- **T1-Q4:** `<Nullable>enable</Nullable>` za DTO-ove, uz uvjete: (1) popis svih request DTO polja koja time postaju implicitno
  obavezna, za svako odluka obavezno / `?`; korisniku se šalje samo popis polja gdje odluka nije sigurna, ostalo se riješi i
  zapiše ovdje; (2) provjeriti jesu li svi DTO-ovi iz Swaggera u Core; DTO-ovi u API projektu također dobivaju nullable (projekt
  ili `#nullable enable` po datoteci); (3) testovi koji padnu zbog nove obaveznosti označavaju se `CHANGED in T1`.
- **T1-Q5:** gornja granica jednog skoka 400 dana; veći skok se odbija s razlogom.
- **Dodatna provjera:** osim obnove članarina ne smije biti drugih poslova koji stvaraju zapise unaprijed ili po rasporedu bez
  pokretanja nakon skoka; ako postoje, pokreću se dan po dan nakon skoka kao obnova. Rezultat u T1 planu.

## Implementacija

Krenula 2026-10-09 nakon potvrde plana. Migracija `20261030000000` (`test_tool_organizations`, privremena).

### T1-1 Poslovni sat
| Stavka | Gdje |
|---|---|
| Poslovni sat | `Infrastructure/Time/BusinessTimeProvider` (singleton `TimeProvider`: izvor + pomak organizacije) |
| Organizacija toka izvršavanja | `Infrastructure/Time/OrganizationClockContext` (AsyncLocal); postavljaju je `API/Middleware/OrganizationClockMiddleware` (claim `organizationId`), `MembershipRenewalBackgroundService` (po organizaciji), `OutboxProcessorService` (organizacija poruke), testni alati |
| Pomak po organizaciji | `Infrastructure/Time/ClockOffsetStore` (učitava `test_tool_organizations` samo uz `TestTools:Enabled`) |
| Sistemski sat | `TimeProvider.System` izričito: istek JWT-a (`JwtService`, `PlatformJwtService`, `AuthService` odgovor), outbox `available_at`/`created_at`/lease/`processed_at` (`OutboxWriter`, `OutboxHandler`, `OutboxProcessorService`), `PlatformAccountBootstrapper`, `CreatedAt` registracije organizacije i prvog korisnika (`AuthService.Register`) |
| Čista pravila | `Utils` više ne čitaju sat: `ParticipationLifecycle.TrySetStatus`, `ParticipationPrice.Apply`, `MembershipTimelines.Audit`, `AvailabilityOverride.Audit/Entry`, `AppointmentLifecycle.Refresh/MarkExplicitlyCancelled`, `ParticipationEvents.Write*` primaju `now`/`occurredAt` |

**Izmjena prema planu (tehnička):** plan je predviđao scoped `TimeProvider` po organizaciji; handleri su DI singletoni pa scoped
ovisnost ne mogu primiti. Umjesto toga singleton `BusinessTimeProvider` + organizacija toka izvršavanja (AsyncLocal). Učinak je
isti: svaki poziv sata u zahtjevu ili pozadinskom poslu vidi sat svoje organizacije.

**Popis mjesta:** 252 čitanja u 61 datoteci prebačena su (3 agenta po skupinama datoteka + statična pravila i sistemska mjesta
ručno). Završni grep `DateTime(Offset)?.(Utc)?Now|DateTime.Today` nad API/Core/Infrastructure: **0** pogodaka. SQL `now()` ni
defaulti vremena u migracijama ne postoje. Jedino izravno čitanje izvan DI je `TimeProvider.System` na sistemskim mjestima iz
tablice gore. Napomene agenata (namjerne, bez promjene ponašanja): `GroupService.LockMembershipScope` je prije uspoređivao s
`DateTimeOffset.UtcNow` unutar EF upita (vrijeme baze), sada s parametrom poslovnog sata; `Refresh` unutar jezgre prijelaza
dobiva trenutak događaja (`eventAt`) umjesto novog čitanja sata (razlika u milisekundama, samo u auditu).

**Jedan sat za sve:** `POST /api/dev/time/membership-renewal-run`, `[DevelopmentOnly]` i parametar `today` u
`IMembershipRenewalService.RunForOrganization` su uklonjeni. Drugi dan se postiže samo pomakom sata organizacije.

**Testovi:** `UnitTests/TestClock` — `FakeTimeProvider` (paket `Microsoft.Extensions.TimeProvider.Testing`) postavljen jednom na
početak test runa, ispod istog `BusinessTimeProvidera` u testnom DI kontejneru i HTTP hostu; testni kod "sada" čita iz
`TestClock.UtcNow` (131 mjesto prebačeno). Sat se pomiče 1 ms po čitanju: potpuno zamrznut sat davao je iste vremenske oznake i
rušio 4 testa redoslijeda po vremenu (FIFO lista čekanja, raspodjela uplate, trag dolaska). Testovi koji su zadavali "today"
obnovi koriste `TestClock.RunForOrganizationOn(org, dan)`: pomak sata SAMO te organizacije za vrijeme prolaza (isti mehanizam
kao testni alat), pa paralelni testovi ne smetaju jedni drugima.

### T1-2 Pomak sata (Management)
- `POST /api/management/organizations/{id}/test-tools/clock/advance` (`Days` ili `To`), `GET .../test-tools` (stanje, `IsDemo`,
  `OffsetIsPermanent` za upozorenje u Managementu); `ManagementTestToolsController` s `[TestToolsOnly]`.
- `TestToolsService.AdvanceClock`: samo naprijed (`TEST_CLOCK_BACKWARDS`), najviše 400 dana (`TEST_CLOCK_ADVANCE_TOO_LARGE`),
  točno jedno od `Days`/`To` (`TEST_CLOCK_ADVANCE_INVALID`); za svaki preskočeni lokalni dan sat se postavi na početak dana
  (zadnji dan na cilj), pomak se spremi i pokrene prolaz obnove; odgovor: broj dana i obrađenih članstava.
- `TestToolsSettings` (`TestTools:Enabled`, default `false`); `TestToolsStartupGuard`: uključeno u `Production` → aplikacija se
  ne pokreće; isključeno → `TestToolsOnlyControllers` uklanja kontrolere (404).

### T1-3 Vrijeme organizacije
`GET /api/organization/clock` (`[Authorize]`, bez granta): `RealUtc`, `EffectiveUtc`, `Offset`, `IsSimulated`, `TimeZone`,
`LocalDate`, `LocalTime`, `Companies` (poslovnice s drugačijom zonom). `OrganizationClockService`.

### T1-5 Oblik 403
- `ForbiddenAppException` se stvara samo tvorničkim metodama (`MissingGrant(s)`, `MissingAnyGrant`, `OutOfScope`) i nosi
  `ForbiddenDetails` (`reason`, `requiredGrants`, `match`, `currentScope`/`requiredScope`, `companyId`); middleware ih piše u
  `error.details` (enumi kao nazivi). `[RequireGrant]` i `[RequireGrantOrAssignedCompany]` više ne vraćaju prazan 403, nego isti
  oblik (`ForbiddenResults`).
- Popis svih 403 i `reason`:

| Mjesto | `reason` | `requiredGrants` / `match` |
|---|---|---|
| `[RequireGrant]` | `MissingGrant` | grantovi atributa / `Any` (jedan = `All`) |
| `[RequireGrantOrAssignedCompany]` bez `companyId` u ruti | `MissingGrant` | grantovi atributa |
| `[RequireGrantOrAssignedCompany]` nedodijeljena poslovnica | `CompanyNotAssigned` | grantovi atributa + `companyId` |
| `AppointmentClosure` korekcija na zatvorenom terminu | `MissingGrant` | grant izvornog statusa / `All` |
| `AppointmentClosure` ponovno otvaranje | `MissingGrant` | SVI nedostajući `appointments.corrections.*` / `All`; bez terminalnih statusa bilo koji / `Any` |
| `AvailabilityOverride`, `GroupCapacityOverride`, `RosterEntryService` (prošlost), "vrati termin" | `MissingGrant` | jedan grant / `All` |
| `PolicyOverride` otpis | `MissingGrant` | grant učinka / `All`; prije poznatog učinka oba / `Any` |
| Business otkazivanje bez `appointments.write.all` | `MissingGrant` | `appointments.write.all` |
| `AppointmentService` / `BookingService` bez ikakvog opsega | `MissingGrant` | own i all grantovi (i grupni za grupu) / `Any` |
| Own opseg na tuđem resursu (`AppointmentOwnership`, roster, fond godišnjeg, pauze) | `OutOfScope` | all grant područja; `currentScope=Own`, `requiredScope=All` |

- **Namjerna promjena ponašanja (CHANGED in T1):** own opseg na tuđem resursu je do sada bio **409** `NOT_OWNER`
  (`BusinessRuleException`); sada je **403** s `reason = OutOfScope` (kod ostaje `NOT_OWNER`, da frontend prijevodi ostanu).
  Razlog: odluka F1-Q5 traži `OutOfScope` kao vrstu 403 odbijanja. 30 karakterizacijskih testova ažurirano s oznakom
  `CHANGED in T1` (`SchedulingAssert.OutOfScope`, HTTP 409 → 403).
- `InactiveUser` i `PermissionSafety` nisu 403: neaktivan korisnik dobiva 401 (autentikacija), zaštita `permissions.manage` 409.

### T1-4 Seed (Management)
- **Dvije razine nove demo organizacije** (obje kroz stvarnu registraciju `IAuthService.Register`: organizacija, osnivač, Admin
  grupa sa svim grantovima, zadani tipovi rostera, zadana politika otkazivanja; zatim `MarkDemo` s razinom i seed):
  - `POST /api/management/test-tools/demo-organizations/basic` — **"Osnova"**: minimum za organizaciju koja radi (testiranje
    postavljanja studija od nule);
  - `POST /api/management/test-tools/demo-organizations/full` — **"Puni demo"**: sve iz "Osnove" + katalog, prodaje, raspored i
    stanja nakon skoka sata;
  - stara ruta `POST /api/management/test-tools/demo-organizations` zadržana je radi kompatibilnosti i znači "Puni demo".
  Odgovor: organizacija, `Level`, `RunTag`, korisnici s lozinkama (samo u ovom odgovoru), broj dodanog po vrsti, `Skipped`,
  `ClockAdvancedDays` (za koliko je seed pomaknuo sat) i `LocalDate` (poslovni "danas" organizacije na kraju seeda).
- Razina se pamti u `test_tool_organizations.demo_level` (`Basic` / `Full`; migracija `T1DemoLevel`, 2026-10-30 #3); status
  testnih alata vraća `DemoLevel`. Demo organizacija stvorena prije razina (bez zapisa) tretira se kao "Puni demo".
- `POST /api/management/test-tools/demo-organizations/{id}/reset` — samo demo organizacija koja nije umirovljena
  (`TEST_TOOLS_NOT_DEMO_ORGANIZATION`); prvo nova demo organizacija **iste razine**, zatim umirovljenje stare (`retired_at` + svi
  korisnici neaktivni), pa neuspjeh seeda ne ostavlja bez demo organizacije. Organizacija nema vlastitu oznaku aktivnosti, zato
  "deaktivacija" = deaktivacija svih njenih korisnika (prijava i tokeni prestaju vrijediti).
- `POST /api/management/organizations/{id}/test-tools/seed` — dopuna postojeće organizacije, **samo dodaje i nikad ne pomiče sat**
  (u ne-demo organizaciji bi pomak bio trajan): akter je najstariji aktivni korisnik Admin grupe (`TEST_TOOLS_NO_ADMIN_USER` ako ga
  nema); postavke organizacije, postojeće poslovnice, radno vrijeme, usluge, cjenici, grupe ovlasti (uključujući članstvo Admin
  grupe), korisnici i zadana politika se ne diraju.
- Sve kroz postojeće aplikacijske servise (`DemoSeedService`; `DemoSeedHandler` samo za čitanje aktera), unutar
  `OrganizationClockContext` ciljne organizacije; nikad izravan upis poslovnih podataka u bazu. Nazivi i emailovi nose oznaku
  prolaza (`#a1b2c3`), pa ponovljeni prolaz ne sudara jedinstvene nazive.

**"Osnova"** (`Basic`): 2 poslovnice (radno vrijeme 07–21) i jedan praznik poslovnice (Centar, "danas" + 30 dana); 1 vrsta
angažmana; grupe ovlasti "Treneri" (own opseg: `appointments.write.own`, `clients.view` i potrebni pregledi) i "Recepcija bez K2
ovlasti" (all opseg, sve osim K2 grantova); 4 zaposlenika s loginom: admin (Admin grupa), trener, recepcija i **korisnik bez
ijedne grupe ovlasti** (prijava radi, svaka zaštićena radnja 403); 20 klijenata s GDPR suglasnošću (datum nikad u budućnosti) i
datumom rođenja (jedan rođendan "danas", jedan za tri dana). Bez usluga, cjenika, prostorija/resursa, paketa, planova članarina,
politika i razloga otkazivanja, grupa, termina i checkouta. Grupe "za prvog klijenta" nisu u seedu: mapiranje još nije spremno,
dolaze kasnije, nakon potvrde korisnika.

**"Puni demo"** (`Full`): sve iz "Osnove" (zaposlenici dobivaju usluge), i:
- katalog: 4 sobe, 4 resursa, 6 usluga (5 individualnih, 1 grupna; zadani resurs reformera), 3 paketa (10 treninga / mjesečni
  pilates / probni 3 treninga), 4 stavke cjenika, 4 plana članarine (Basic, Premium kalendarski s početnom naknadom, Flex s
  pauzom do 30 dana, Pop-up s opsegom samo na trećoj, maloj poslovnici "Studio Pop-up"), politika otkazivanja s naknadom
  (dodijeljena Centru), 4 razloga otkazivanja;
- pravila provizija (ADR-0010 + ADR-0030), bez datuma važenja: opće pravilo za individualne usluge svakog zaposlenika
  (10 % / 15 % / 10 %), pravilo usluge s prednošću (trener, personalni trening, 10 €), grupni trening (trener, 15 € po grupnom
  terminu), prodaja paketa (recepcija, 10 %) i prodaja članarina (recepcija, 10 € po planu);
- grupa sa 6 članova (pon/sri/pet 18:00) i generiranim terminima **tekućeg i sljedećeg tjedna**;
- **tijek s jednim skokom sata** (isti mehanizam kao alat za pomak vremena: `ITestToolsService.AdvanceClock` → prolaz obnove dan
  po dan; nikad ručno postavljeni datumi). Duljina skoka = najmanje 45 dana (dulje od mjesečnog perioda + 7 dana grace perioda),
  do prvog **četvrtka**, dakle 45–51 dan; novi "danas" je četvrtak, pa tekući tjedan ima prošle dane (pon–sri), a sljedeći je u
  budućnosti. Primjer: seed pokrenut 2026-10-09 skače 48 dana, "danas" organizacije = 2026-11-26. Organizacija zato završava sa
  simuliranim vremenom; povratak na stvarno vrijeme je reset (nova organizacija).
  - **Faza 1 (početni sat):** prodaja paketa kroz checkout (paket 10 treninga i probni paket 3 treninga; prodavač = recepcija,
    plaćeno gotovinom, zatvaranje izdaje paket i proviziju na prodaju); prodaja članarina: aktivna (Basic, plaćena kroz checkout),
    s dugom (Premium, neplaćena), pauzirana (Flex, plaćena; pauza od ponedjeljka tekućeg tjedna nakon skoka, 21 dan), otkazana
    (Basic, plaćena; otkaz → završava krajem prvog perioda), Pop-up (plaćena u pop-up poslovnici, koja se zatim deaktivira kroz
    `ICompanyService.SetActive`); zakazivanje svega što će nakon skoka biti prošlost tekućeg tjedna: generiranje grupe, 6
    individualnih termina (pon–sri, 9:00 i 12:00), 6 paketnih sesija (pon–sri, 16:00 i 17:00), 2 termina bez ishoda (uto, sri).
  - **Skok sata** (`AdvanceClock`, `Days` = duljina skoka): obnova stvara periode i zaduženja, grace ističe (dug), otkazano članstvo
    završava, Pop-up članstvo na granici perioda "stoji" (K1-8 sustavna pauza `CompanyClosure`, otvara je obnova).
  - **Faza 2 (novi "danas"):** odrađivanje prošlih individualnih termina (`SetParticipationStatus` → Completed) i naplata svakog
    kroz checkout; odrađivanje paketnih sesija s odabranim paketom (troši jedinicu); prisutnost na prošlim grupnim terminima (5
    članova došlo i platilo gotovinom, 1 izostanak → politika izostanka) i zatvaranje termina (provizija grupnog termina); plaćanje
    zaostataka aktivne i pauzirane članarine nastalih obnovom tijekom skoka; 12 budućih individualnih termina (sljedeća 4 dana).
  - **Rezultat:** članstva — aktivno i plaćeno, s dugom (`Standing = Delinquent`, više neplaćenih zaduženja), pauzirano
    (klijentova pauza), završeno (`EndReason = Cancelled`), stoji (`StandingStill`, otvorena pauza `CompanyClosure`); paket s
    preostalim jedinicama (10 → 7) i potrošen paket (3 → 0, `Depleted`); provizije za odrađene usluge, grupne termine, prodaju
    paketa i prve prodaje članarina; 14 checkouta.
- Brojevi u odgovoru, uz katalog i raspored: `MembershipsSold` i stanja članstava na kraju seeda (čitaju se iz servisa:
  `MembershipsActivePaid`, `MembershipsAwaitingPayment`, `MembershipsInDebt`, `MembershipsPaused`, `MembershipsEnded`,
  `MembershipsStandingStill`), `ClientPackagesSold`, `PackageUnitsConsumed`, `CheckoutsCompleted`, `CommissionRules`,
  `CommissionEntries` (zapisi provizija seedanih zaposlenika), `GroupAttendances`, `CompanyHolidays`.

**Dopuna postojeće organizacije** (puni sadržaj bez pop-up poslovnice i plana; sat se ne pomiče): katalog, 4 zaposlenika (uz novu
grupu "Sve ovlasti" umjesto postojeće Admin grupe), klijenti, pravila provizija, grupa s terminima +1…+14 dana, 12 budućih i 8
prošlih individualnih termina (6 "upiši odrađeno" plaćeno gotovinom, 2 bez ishoda), paketi kroz checkout, članarine ostvarive
danas: aktivna i plaćena, neplaćena (čeka plaćanje, još nije dug), pauzirana od danas (14 dana). Paketne sesije samo kao "upiši
odrađeno" današnjih termina (8–11 h) koji su već prošli (paket vrijedi od danas). U `Skipped` s razlogom: članarina s dugom,
završeno članstvo i članstvo koje stoji (traže protok vremena; postojeća poslovnica se ne zatvara) te, ovisno o dobu dana, paket
s potrošenim jedinicama i potrošen paket.

- Termini, prodaje i generiranje grupe su neobavezni dijelovi: ako ih pravilo odbije (npr. sudar u postojećoj organizaciji),
  navode se u `Skipped` (u novoj demo organizaciji `Skipped` je prazan; testovi to provjeravaju).
- **Poznato ograničenje (odlučeno):** seed nije jedna transakcija (svaka naredba servisa commita sama, a skok sata je niz
  prolaza obnove); prekid ostavlja djelomične podatke — tada napraviti novu demo organizaciju (ili reset).
- Ostali tehnički izbori: osnivač demo organizacije nema profil zaposlenika (zaposlenici su zasebni korisnici); članarine i paketi
  se prodaju klijentima koji nisu članovi grupe, da dug ne preskače članove grupe; testni host ima izvor javnih praznika bez mreže
  (`NoPublicHolidaysApiClient`), jer seed dodaje praznik kroz `CompanyHolidayService`.

### T1-6 Točnost Swaggera
- `Core` ima `<Nullable>enable</Nullable>` i XML dokumentaciju (opisi DTO-ova u Swaggeru); CS8618 je isključen jer su DTO-ovi
  podatkovni ugovori koje puni System.Text.Json (ponašanje se ne mijenja). Swashbuckle: `SupportNonNullableReferenceTypes`,
  `NonNullableReferenceTypesAsRequired`; `ErrorResponsesOperationFilter` opisuje 400/404/409 (`ErrorResponse`) i 403
  (`ForbiddenErrorResponse`) na svakoj operaciji.
- **Validacija zahtjeva se ne mijenja:** `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true`, pa nijedno polje
  ne postaje implicitno obavezno pri bindanju (uvjet T1-Q4 (1): "ne smije proći tiho" — ne događa se uopće). Bez toga bi 122
  polja request DTO-ova bez `[Required]` počela vraćati `VALIDATION_ERROR` umjesto dosadašnjih kodova servisa (npr.
  `PAYMENT_VOID_REASON_REQUIRED`), što bi promijenilo kodove koje frontend prevodi. Anotacije su zato samo ugovor:
  - request: ne-nullable = polje bez kojeg servis odbija naredbu (`[Required]` ili validacija u servisu), `?` = neobavezno;
  - response: ne-nullable = svaki producent ga uvijek postavlja (provjereno u mapperima/servisima i shemi baze).
- Opseg: 3 agenta po mapama DTO-ova, ~180 svojstava označeno `?`; Swagger prije/poslije: nullable svojstava 873 → 619,
  obaveznih u shemi 1309. Enumi su već bili stringovi (JsonStringEnumConverter), datumi ispravni (`DateOnly` → `date`,
  `DateTimeOffset` → `date-time`, `TimeOnly` → `time`; `TimeSpan` → `date-span`, string).
- DTO-ovi su svi u Core (API projekt ih nema nakon uklanjanja razvojnog kontrolera), pa `#nullable` u API nije potreban.
- Testovi: nijedan nije pao zbog nove obaveznosti (validacija nepromijenjena), pa nema oznaka `CHANGED in T1` iz ovog razloga.
- **Nesigurna polja (ostavljena nullable, sigurniji smjer za frontend):** nazivi preko navigacija u mapperima s `?.` (npr.
  `AppointmentDto.CompanyName`, segment `ServiceName`/`EmployeeName`/`RoomName`, `BookingDto.ClientName`, nazivi u pravilima
  provizija i dodjelama politika, `ServiceDto.ColorHex`). U praksi su vjerojatno uvijek popunjeni; mogu se suziti kad se
  provjeri da handler uvijek učitava navigaciju. Ostavljena ne-nullable uz napomenu: `CheckoutItemDto.Description`,
  `MembershipPlanUpdateNotAppliedDto.Reason`, `CommissionRuleEvaluationDto.Applied` (jsonb), `ParticipationPriceAdjustmentDto.Candidates`.
- **Nalazi koji se NE mijenjaju u T1 (promjena ugovora, otvorena tema):** poslovni datumi tipizirani kao `DateTimeOffset`
  umjesto `DateOnly` (`ClientDto.DateOfBirth`, datumi zaposlenika, `CompanyHolidayDto.Date`, `AvailabilityDto.Date`, datumi
  roster zapisa, `WorkingHoursTemplate.AnchorDate`, `OperationalDashboardDto.Date`, `AvailableSlotsQuery.Date`,
  `Recurring*.EndDate`, `GenerateGroupAppointmentsRequest.FromDate/ToDate`); lokalno vrijeme dana kao `TimeSpan` umjesto
  `TimeOnly` (slotovi); `AuthResponse.TokenExpiration` `DateTime?` umjesto instanta; `[Required]` na ne-nullable value tipovima
  nema učinka (nedostajuća vrijednost postaje `Guid.Empty`/0); `NotificationDto.Data` je jsonb kao string.

### T1-8 Pravila cijena
Odluke: dnevnik "Grupna prisutnost danas ponovno računa cijenu…", "Odrađivanje s ručnim iznosom…", "Rupa u cjeniku", "T1-7 nejasna
polja: cjenik" (dopuna 2). Bez migracije (nema promjene sheme).

**Provjera prije promjene (stavka 1) — kako se cijena postavljala:**
- (a) stalni polaznici pri generiranju: `GroupService.GenerateAppointmentsAttempt` po segmentu kandidata
  `ResolveForServiceStart(usluga predloška, poslovnica grupe, zaposlenik izvora predloška, početak segmenta)` → `AtSuggested` —
  cijena važeća za dan termina u trenutku generiranja. Ispravno, bez promjene.
- (b) kasniji upis na već generiran termin — svi razrješavaju cijenu za dan termina u trenutku upisa: gost
  (`BookingService.AddGroupGuest`, `CheckInNewGuest`), promocija liste čekanja (`WaitlistService.PromoteOnSegment`), dodavanje
  preskočenog člana (`GroupMembershipSkipService`, BackfillForClient/Organization), upis člana u generirane termine
  (`GroupService.JoinFutureOccurrences` iz `AddMember`/`ChangeMemberSegmentTemplates`). Ispravno, bez promjene.
- Problem je bio samo check-in: `BookingService.ResolveCoverage` (i grana pokrivena članarinom) je pri prisutnosti ponovno
  razrješavao TRENUTNI cjenik i prepisivao cijenu iz upisa.

**Što je promijenjeno:**

| Stavka | Gdje | Promjena |
|---|---|---|
| 1 Grupna prisutnost | `BookingService.ResolveCoverage` (+ grana članarine), `ApplyCheckInPrice`, `Utils/ParticipationPrice.Stored` | Check-in koristi spremljenu cijenu sudjelovanja (iznos = spremljena predložena cijena, osnovica/izvor/način/zaposlenik izvora netaknuti); ručni iznos uz check-in = ručni iznos uz spremljenu osnovicu. 2E (`MembershipCoverageService.PriceOnCompletion` iz spremljenog `BaseAmount`) nepromijenjen. **CHANGED in T1** |
| 2 Ručni iznos pri odrađivanju | `BookingService.ApplyIndividualCompletion` → `ApplyManualAmount` | Upisuje se ručni iznos, predložena cijena i osnovica ostaju spremljene (prije: osvježene iz cjenika). **CHANGED in T1** |
| 3 Segmentne naredbe | `AppointmentService.Segments` (`SegmentTarget.Reprice`/`PriceReason`, `RewriteSegment`), `IPricingService.PriceListDay` | Cjenik se čita samo kad se promijeni nešto o čemu cijena ovisi; promjena iznosa sudjelovanja → upozorenje `PARTICIPATION_PRICE_CHANGED` po sudjelovanju |
| 4 Audit ručnog iznosa | `BookingService.AuditAmount` | Novi audit `ManualAmount` (OldValue = predložena cijena, NewValue = upisani iznos, ChangedBy/ChangedAt) uz postojeći `Amount` (stari → novi iznos) |
| 6 Preklapanje | `PricingService.EnsureNoOverlap`, `PriceOverlapDetails` | `PRICE_OVERLAP` s details (stavka, ValidFrom/ValidTo, predmet, poslovnica, zaposlenik) i porukom s datumima |
| 7 Rupa u cjeniku | `PricingService.ResolvePrice` (`ResolvePriceResponse.PriceNotDefinedReason`, `SubjectName`), `Utils/PriceWarnings` | Upozorenje u odgovorima naredbi koje upisuju cijenu |
| 8 Spremanje cjenika | `PricingService.SaveWarnings`, `IPriceListItemHandler.GetScheduledServiceStarts`, `PriceListItemDto.Warnings` | `PRICE_LIST_GAP`, `PRICE_LIST_SCHEDULED_KEEP_OLD_PRICE`; spremanje se nikad ne odbija |

**Segmentne naredbe i cjenik (stavka 3), nakon promjene:**

| Naredba | Čita cjenik | Napomena |
|---|---|---|
| `ChangeSegmentService` | da, kad se usluga stvarno mijenja | ista usluga (samo trajanje/resursi) → ne |
| `ChangeSegmentPricingSource` | da, uvijek | kontekst cijene |
| `ChangeSegmentEmployees` | samo kad se promijeni efektivni zaposlenik izvora cijene | **CHANGED in T1** (prije: uvijek) |
| `ChangeSegmentTime` | samo kad novi početak pada na drugi dan cjenika (lokalni datum u zoni poslovnice) | **CHANGED in T1** (prije: uvijek) |
| `ChangeSegmentRoom`, `ChangeSegmentResources` | ne | kao i prije |
| `AddSegment`, `AddClient` | samo za NOVA sudjelovanja (nastanak) | postojeća se ne diraju |
| `RemoveSegment`, `RemoveParticipation` | ne | — |
| `GroupService.UpdateSegmentTemplate` | ne | ne mijenja cijene postojećih sudjelovanja |

Promjena vremena ili usluge i dalje ponovno evaluira pokriće članarinom (`ReevaluateParticipation`, 2D) — to nije čitanje
cjenika; ako bi time iznos sudjelovanja promijenio, upozorenje ga također javlja (razlog = vrsta naredbe).

**Upozorenja (`WarningCodes`):**
- `PRICE_NOT_DEFINED` (`WarningPriceNotDefinedDetails`: serviceId, serviceName, date, usedAmount, source, reason
  `NoPriceListItemForDate` | `ZeroDefaultPrice`) — slučaj 1: izvor `Default`, a usluga ima aktivne stavke u kontekstu razrješavanja
  (učitani kandidati: poslovnica ili sve poslovnice, bez zaposlenika ili zaposlenik izvora), bez obzira na iznos; slučaj 2: korištena
  zadana cijena je 0 € (bez oznake besplatne usluge, upozorava se svaki 0 €; stavka cjenika s 0 € je izričita i ne upozorava).
  Vraća se samo kad sudjelovanje stvarno dobiva tu cijenu, bez ponavljanja po (usluga, dan, iznos, razlog): kreiranje termina,
  "upiši odrađeno" (`CompleteNow`), `/recurring` (po terminu), `AddSegment`, `AddClient`, gost (`AddGroupGuest`, check-in gosta),
  promocija liste čekanja (u odgovoru naredbe koja je oslobodila mjesto: prijelaz/otkaz bookinga, izmjena/uklanjanje člana grupe).
- `PRICE_NOT_DEFINED_OCCURRENCES` (`WarningPriceNotDefinedOccurrencesDetails`: occurrenceCount, dates, items) — JEDNO zbirno
  upozorenje u `GenerateGroupAppointmentsResult.Warnings` (novo polje) i u odgovoru upisa člana u već generirane termine
  (`AddMember`, `ChangeMemberSegmentTemplates`). Broje se termini u kojima je barem jedno sudjelovanje dobilo cijenu s rupom.
- `PARTICIPATION_PRICE_CHANGED` (`WarningParticipationPriceChangedDetails`: participationId, clientId, oldAmount, newAmount,
  reason `Service` | `PricingSource` | `Employees` | `Time`) — jedno po sudjelovanju čiji se iznos promijenio.
- `PRICE_LIST_GAP` (`WarningPriceListGapDetails`: fromDate, toDate) — rupa između spremljene stavke i susjedne aktivne stavke
  istog predmeta i konteksta (poslovnica, zaposlenik).
- `PRICE_LIST_SCHEDULED_KEEP_OLD_PRICE` (`WarningPriceListScheduledKeepDetails`: count, validFrom, validTo) — broj Confirmed
  sudjelovanja usluge s početkom od sada, u poslovnici stavke (null = sve) i za zaposlenika stavke kao izvor cijene, čiji je dan
  cjenika unutar važenja stavke; zadržavaju spremljenu cijenu (P5). Samo za aktivnu stavku usluge.

**Audit ručnog iznosa (stavka 4):** prije je postojao samo `Amount` (stari → novi iznos, tko, kada) i samo kod
`SetParticipationPrice`; odrađivanje s ručnim iznosom nije imalo audit iznosa. Dodano: `ManualAmount` (predložena cijena →
upisani iznos) kod `SetParticipationPrice` (kad je upisan iznos), kod odrađivanja s iznosom (individualno i grupni check-in) i
`Amount` kod odrađivanja s iznosom kad se iznos promijenio. Vrijednosti u invariant formatu (iz baze npr. "50.00").

**Ručni iznos i provizija (stavka 5, bez promjene):** osnovica provizije za odrađeno (`CommissionService.GenerateForIndividualServiceCompletion`)
uz ručni iznos je UVIJEK ručni iznos (`SessionPriceAmount` = ručni iznos, `ListPriceAmount` = spremljena osnovica, `IsManualPrice`),
neovisno o "oduzmi popuste" / "oduzmi popuste članstva": 50 € / 40 € → osnovica 40 €, uz "oduzmi popuste" i bez njega (test za sve 4
kombinacije, provizija 10 % = 4 €). Ručni iznos VIŠI od osnovice (70 € uz 50 €) je također osnovica (70 €, i uz uključene prekidače) —
ne tretira se kao "negativni popust". **Nalaz (ne mijenja se, za odluku):** provizija MOŽE premašiti naplaćeni iznos: (1) fiksno
pravilo (Fixed) se ne ograničava osnovicom (npr. fiksno 10 € uz ručni iznos 5 €); (2) sesija pokrivena članarinom bez "oduzmi
popuste članstva" ima osnovicu cijenu sesije, a naplaćeno na sesiji je 0 € (Vagaro, ADR-0030); (3) paket je način plaćanja
(osnovica = cijena sesije). Uz postotno pravilo (0–100 %) i ručni iznos provizija nikad ne premašuje naplaćeni iznos.

**Izvještaj provizija (stavka 9):** test — zarada u P1, storno i ponovna zarada (korekcija Completed → Confirmed → Completed) u
P2: P1 nepromijenjen (5 / 0 / 5), P2 pokazuje ponovnu zaradu i storno (5 / 5 / 0).

**Testovi:** novi `UnitTests/T1/T1PricingRulesTests.cs` (16 testova). Nijedan postojeći karakterizacijski test nije pao, pa nijedno
postojeće očekivanje nije mijenjano; promijenjeno ponašanje označeno je `CHANGED in T1` u novim testovima (grupna prisutnost,
ručni iznos pri odrađivanju, `ChangeSegmentTime` unutar dana, `ChangeSegmentEmployees` bez promjene izvora cijene).

### T1-9 Paketi i GDPR
Odluke: dnevnik "T1-7: paket `PurchaseDate`", "T1-7: klijent `GdprConsentDate`", "Kupnja paketa unatrag". Migracija
`20261030000002` `T1PackagesBackdatingGdpr` (primijenjena lokalno).

**Ručni upis paketa** (`ClientPackageService.Create`, `POST api/clients/{id}/packages`; "danas" = poslovni dan u zoni poslovnice
prodaje, bez nje organizacije):
- `PurchaseDate` nakon današnjeg dana → 400 `PACKAGE_PURCHASE_DATE_IN_FUTURE` (poruka s oba datuma); ni grant za prošlost to ne otvara.
- `PurchaseDate` prije današnjeg dana → grant `clients.packages.write.past` (bez granice unatrag), inače 403 `MissingGrant`
  (`ForbiddenAppException.MissingGrant`). Grant je u `Grants.Catalog` ("Paket unatrag", modul clients), capability
  `On("clients.packages.write.past", Sensitive)` uz `clients.packages.manage`, u `CheckedInCodeOnly` testa konzistentnosti
  (provjerava se u servisu, ne u `[RequireGrant]`), a migracija ga dodaje samo grupama `system_key = 'admin'` (idempotentno).
  Demo seed: grupa "Recepcija bez K2" ga namjerno nema (kao ostale osjetljive grantove).
- `ValidUntilDate` izračunat iz `PurchaseDate` prije današnjeg dana → 409 `PACKAGE_EXPIRED_AT_ISSUE` (`BusinessRuleException`) (details `purchaseDate`,
  `validUntilDate`, `today`); bez iznimke za uvoz povijesti. Isto vrijedi i za današnju kupnju paketa s fiksnim datumom isteka koji je
  prošao. Granica: istječe danas → upisuje se.
- Upis unatrag piše povijest klijenta `PackageIssuedBackdated` (`NewValue = clientPackageId=…;purchaseDate=yyyy-MM-dd`, tko, kada)
  u istom `SaveChanges` kao i paket. Današnji upis nema zapisa.
- Checkout (provjereno, bez promjene): `CheckoutService.Complete` → `IssueClientPackage` uzima `PurchaseDate` = lokalni dan trenutka
  dovršetka checkouta (`TimeProvider`) u zoni poslovnice checkouta — uvijek današnji dan, ne prolazi kroz `Create`.
- Ručni upis bez checkouta ne stvara proviziju (provizija na prodaju paketa nastaje samo u checkoutu) — test.

**Pokriće od `PurchaseDate`** (CHANGED in T1): jedno pravilo `PackageValidity.IsValidOn` = `PurchaseDate <= dan usluge <=
ValidUntilDate` (dan usluge = lokalni datum početka segmenta u zoni poslovnice termina); isti uvjet u SQL-u
`ClientPackageHandler.GetEligibleForService` i u potrošnji (`ClientPackageEntryMutator.EnsureEligible`, poruka razlikuje "kupljen
nakon usluge" od "istekao"; kod ostaje `PACKAGE_NOT_ELIGIBLE`). Vrijedi i za kaznu politike koja troši jedinicu.

**Bez retroaktivnog pokrića — kako je danas (stavka C, provjereno):**
- Upis paketa (ručno ili checkout) ne pokreće nikakvu ponovnu procjenu postojećih sudjelovanja: nema poziva sinkronizacije pokrića
  (članarina ima `SyncParticipation`, paket nema ništa slično), nema outbox događaja ni automatske potrošnje. Odrađena / plaćena /
  dužna sudjelovanja ostaju nepromijenjena (test).
- Paket se troši samo na događaju sudjelovanja: odrađivanje s izričitim `ClientPackageId` (individualno), grupni check-in
  (izričit paket ili jedini prihvatljiv paket) i P1 kazna koja troši jedinicu.
- Izričite naredbe "prebaci prošli termin na paket" nema. Jedini put je korekcija statusa (K2): Completed → Confirmed (na
  zatvorenom terminu grant `appointments.corrections.completed` + razlog) pa ponovno Completed s `ClientPackageId`, i to samo ako
  sudjelovanje nema aktivno novčano namirenje (`SettlementExclusivityPolicy`). Nova naredba nije dodana; otvorena tema u ARCH §7.3.
- Napomena: grupni check-in koji se označava naknadno (termin prošao, sudjelovanje još Confirmed) može automatski izabrati jedini
  prihvatljiv paket, pa i paket upisan unatrag ako dan termina nije prije `PurchaseDate` — to je postojeće pravilo označavanja, ne
  retroaktivna promjena odrađenog termina.

**Povijest klijenta** (nova tablica `client_audit_logs`, entitet `ClientAuditLog`, `IClientAuditLogHandler.GetByClient`): id,
organization_id, client_id, change_type, old_value, new_value, reason, changed_at, changed_by — obrazac `client_membership_audit_log`.
Zapis upisuje handler promjene u istom contextu i `SaveChanges` (`ClientHandler.Add/Update/Anonymize`, `ClientPackageHandler.Add`).
Vrste (`ClientAuditChangeTypes`): `GdprConsent` (vrijednost `given=true;date=2026-10-01`) i `PackageIssuedBackdated`.

**GDPR suglasnost** (`ClientService`): datum nakon današnjeg dana organizacije → 400 `GDPR_CONSENT_DATE_IN_FUTURE`; "datum obavezan
kad je suglasnost dana" ostaje (generički `VALIDATION_ERROR`). Svaka promjena zastavice i/ili datuma ide u povijest (staro → novo,
tko, kada): kreiranje s danom suglasnošću (staro `given=false;date=`), izmjena, i anonimizacija koja briše suglasnost (razlog
"Anonimizacija"). Kreiranje bez suglasnosti i izmjena bez promjene ne pišu zapis.

**Uvoz klijenata:** alat za uvoz ne postoji. Budući uvoz paketa s prošlim datumima trebao bi isti grant (`clients.packages.write.past`)
i izričitu odluku o već isteklim paketima (danas se odbijaju s `PACKAGE_EXPIRED_AT_ISSUE`).

**Testovi:** novi `UnitTests/T1/T1PackagesGdprTests.cs` (12 testova). `CHANGED in T1`: `SchedulingWorld.AddClientPackage` — fiksni
`PurchaseDate` 2025-01-01 → 2020-01-01 (prije svih dana suite, `PastDay` 2020-03-02; bez toga 30 testova pokrića pada zbog donje
granice); `PackageCatalogDateTests.Sell` i `PackageValidityCalendarTests.Sale_UsesTheSaleCompanysLocalDate…` (izričit dan kupnje)
prodaju uz sat postavljen na dan kupnje. `T1DemoSeedTests` dopunjen (grupa "bez K2" nema novi grant). Ostala očekivanja nepromijenjena.

## Dnevnik odluka tijekom implementacije

| Datum | Pitanje | Odgovor | Posljedica |
|---|---|---|---|
| 2026-10-09 | F1-Q2 Kada uvesti TimeProvider? | Zasebna mala backend faza prije F1, s vlastitim decision recordom; dopune 1–7 (jedan sat, samo naprijed, poslovi nakon skoka, nikad u produkciji, endpoint vremena, pravilo za frontend, testovi). | Ova faza; plan na potvrdi. |
| 2026-10-09 | F1-Q3 Seed? | Endpoint pokretan iz Management portala; pojednostavljeno: nova demo organizacija (s resetom) ili dopuna postojeće (samo dodaje); bez zastavica i zaštite od duplikata; isti prekidač kao simulirano vrijeme; uklanja se prije go-livea. | T1-4. |
| 2026-10-09 | F1-Q5 403 s nazivom granta? | Opcija A (grant u `details`), uz dopune: lista `requiredGrants`, `reason` (`MissingGrant`, `OutOfScope`, ostali izlistani), prikaz na frontendu, prijevodi grantova na jednom mjestu, FE-ADR + backend dokument. | T1-5; frontend FE-ADR-0003. |
| 2026-10-09 | Potvrda plana T1 (T1-Q1 – T1-Q5) | Q1 da (+ upozorenje u Managementu da je pomak trajan u ne-demo organizaciji); Q2 da (samo Management, u aplikaciji samo traka); Q3 da; Q4 da uz uvjete (popis novih obaveznih polja, DTO-ovi u API projektu, `CHANGED in T1`); Q5 da (400 dana); dodatna provjera drugih zakazanih poslova. "Kreni s implementacijom." | Sekcija "Potvrda plana"; implementacija krenula. |
| 2026-10-09 | (tehnički, bez pitanja) Scoped TimeProvider po organizaciji nije moguć (handleri su singletoni) | Singleton `BusinessTimeProvider` + `OrganizationClockContext` (AsyncLocal) | Isti učinak; plan §1.1 ispravljen. |
| 2026-10-09 | (tehnički, bez pitanja) Potpuno zamrznut testni sat ruši 4 testa redoslijeda po vremenu | Fiksni početak + 1 ms po čitanju | `TestClock`; redoslijed jednoznačan kao u stvarnom radu. |
| 2026-10-09 | (tehnički, na potvrdu) `OutOfScope` za own opseg na tuđem resursu znači promjenu dosadašnjeg 409 `NOT_OWNER` | 403 s `reason = OutOfScope`, kod `NOT_OWNER` ostaje | 30 testova `CHANGED in T1`; frontend dobiva 403 umjesto 409 za taj slučaj. |
| 2026-10-09 | (tehnički, na potvrdu) Implicitno obavezna polja nakon uključivanja nullable | Isključeno (`SuppressImplicitRequired...`): validacija i kodovi grešaka nepromijenjeni, anotacije samo za Swagger | Nijedno polje ne postaje obavezno; popis nesigurnih u T1-6. |
| 2026-10-09 | (tehnički) Seed: admin zaposlenik u postojećoj organizaciji | Nova grupa "Sve ovlasti" po prolazu umjesto učlanjenja u postojeću Admin grupu (pravilo "grupe ovlasti se ne diraju") | Test provjerava da Admin grupa ne dobiva članove. |
| 2026-10-09 | Potvrda tehničkih izbora T1 (409 → 403 `OutOfScope`; implicitna obaveznost isključena; seed) | 1 i 2 potvrđeni. Seed bez transakcije prihvaćen kao poznato ograničenje (pri prekidu napraviti novu demo organizaciju); grupa "Sve ovlasti" potvrđena. **Članarine i paketi MORAJU biti u seedu**, kroz prave servise: aktivna, s dugom, pauzirana, istekla, članstvo koje stoji (zatvorena poslovnica), paket s preostalim jedinicama, potrošen paket, nekoliko checkouta s provizijama; stanja koja traže prošlost složiti prodajom "u prošlosti" i skokom sata kroz isti mehanizam kao pomak vremena, ne ručnim datumima. | T1-4 proširenje. |
| 2026-10-09 | Tipovi datuma i vremena u API-ju (nalaz T1-6) | **Odlučeno sada, prije F1:** trenutak → `DateTimeOffset`; dan u kalendaru → `DateOnly`; vrijeme dana bez datuma → `TimeOnly`; trajanje → `TimeSpan` (samo tu). Razlog: dan kao `DateTimeOffset` se pri pretvorbi zone pomakne na prethodni dan. Napraviti: popis polja koja nisu po pravilu (korisniku samo nejasna), promjena ugovora, baze i testova (`CHANGED in T1`), ponovno generiranje frontend tipova, ADR, ukloniti temu iz ARCH §7.4. | T1-7 (nova stavka T1). |
| 2026-10-09 | T1-7 nejasna polja: cjenik `ValidFrom/ValidTo` | `DateOnly`, oba kraja uključena; dan = lokalni datum početka termina u zoni poslovnice termina; migracija i za povijest cjenika. Dopune: (1) potvrditi da je cijena zamrznuta na terminu i da promjena cjenika ne mijenja zakazane termine (ako se cijena danas računa ponovno pri checkoutu ili čitanju — javiti prije promjene); (2) bez preklapanja stavki za isti kontekst na isti dan, odbijanje navodi stavku s kojom se preklapa, test granice (ValidTo = ValidFrom druge); (3) rupa u cjeniku: razlog (usluga, datum), nikad tiho 0 €; (4) termin preko ponoći pripada danu početka; promjena cijene unutar dana namjerno nije podržana (otvorena tema ARCH §7.3, uz P-17). | T1-7 |
| 2026-10-09 | T1-7: izvještaj provizija `From/To` | `DateOnly`, oba kraja uključena, granice dana u zoni organizacije. Dopune: test da storno ide u razdoblje u kojem je nastao (zarađeno u listopadu, storno u studenom → listopad nepromijenjen), isto za ponovno zarađenu nakon korekcije; zapisati razliku zona (izvještaj = zona organizacije, cjenik = zona poslovnice) i otvorenu temu ARCH §7.3. | T1-7 |
| 2026-10-09 | T1-7: paket `PurchaseDate` | `DateOnly`, dan kupnje u zoni poslovnice prodaje; valjanost od tog dana; trenutak prodaje ostaje u checkoutu/plaćanju i `CreatedAt`. Dopune: (1) javiti pokriva li K1 validacija prošlosti kupnju unatrag, inače predložiti grant (kao `roster.entries.write.past`) na potvrdu prije implementacije; (2) paket ne pokriva termine prije `PurchaseDate` ni kod kupnje unatrag (test); (3) `PurchaseDate` ne smije biti nakon današnjeg dana po satu organizacije (razlog); (4) potvrditi da provizija na prodaju paketa ide u razdoblje trenutka prodaje (checkout). | T1-7 |
| 2026-10-09 | T1-7: klijent `GdprConsentDate` | `DateOnly`, datum s obrasca. Dopune: ne u budućnosti (po satu organizacije); svaka promjena zastavice i datuma u auditu (staro → novo, tko, kada); otvorena tema ARCH §7.3: povijest pristanka, verzija teksta, trenutak pristanka kod online prijave (potvrditi s pravnikom). | T1-7 |
| 2026-10-09 | Grupna prisutnost danas ponovno računa cijenu iz trenutnog cjenika | **Zamrznuti**: prisutnost koristi spremljenu cijenu (CHANGED in T1). Cijena se zamrzava po sudjelovanju u trenutku upisa (stalni polaznici pri generiranju, kasniji upis = cijena važeća za datum termina u trenutku upisa; potvrditi kako je danas). Cjenik se ponovno čita samo kad se promijeni nešto o čemu cijena ovisi (usluga, kontekst cijene); dvorana, resurs, zaposlenik ili vrijeme unutar istog dana ne mijenjaju cijenu; kad se cijena promijeni, odgovor navodi sudjelovanje, staru → novu cijenu i razlog; izlistati segmentne naredbe koje čitaju cjenik. Izmjena stavke cjenika u odgovoru navodi koliko zakazanih sudjelovanja zadržava staru cijenu (primjena na zakazane = P5). Test: promjena cjenika između upisa i prisutnosti → naplata po cijeni iz upisa. | T1-8 |
| 2026-10-09 | Odrađivanje s ručnim iznosom danas osvježava osnovicu iz cjenika | **Zadržati spremljenu osnovicu** (CHANGED in T1). Provjeriti i javiti: ručni iznos niži od osnovice = popust ("oduzmi popuste"), provizija nikad veća od naplaćenog (test 50 € / 40 €); ručni iznos viši od osnovice — opisati kako se računa provizija (bez promjene); audit upisa ručnog iznosa (tko, kada, predložena cijena, upisani iznos). Otvorena tema ARCH §7.3: grant i/ili razlog za ručni iznos različit od predloženog. | T1-8 |
| 2026-10-09 | Rupa u cjeniku | Zadana cijena usluge ostaje valjan izvor, bez odbijanja. Upozorenje `PRICE_NOT_DEFINED` (usluga, datum, korištena cijena, izvor): (1) usluga ima stavke cjenika, ali nijedna ne pokriva datum → zadana cijena, bez obzira na iznos; (2) 0 € iz zadane cijene, osim izričito besplatne usluge (predložiti najjeftiniji način; ako je velik posao, upozorenje za svaki 0 €). Zbirno pri generiranju grupa (broj termina, datumi). Pri spremanju stavke cjenika koja ostavlja rupu između dvije stavke iste usluge upozorenje (od–do rupe), spremanje se ne odbija. Testovi. | T1-8 |
| 2026-10-09 | Kupnja paketa unatrag | Grant `clients.packages.write.past` (bez granice; bez granta 403 `MissingGrant`; migracija samo Admin grupama; u mapiranje UI → grant). Checkout uvijek današnji dan. Dopune: bez retroaktivnog pokrića (paket upisan unatrag ne mijenja odrađene/naplaćene/dug termine; javiti kako je danas; izričito prebacivanje po terminu samo ako postoji, inače otvorena tema); istekao pri upisu (`ValidUntilDate` prošao) → odbija se s razlogom (uvoz povijesti samo uz posebnu odluku); ručni upis bez checkouta ne stvara proviziju (test); audit upisa unatrag (tko, kada, `PurchaseDate`); uvoz klijenata je zaseban alat (javiti kako se uklapa). | T1-9 |
| 2026-10-09 | (na odluku) Fond godišnjeg: vrijedi li i na dan `ExpiresAt`? | Otvoreno. Prije T1-7 `ExpiresAt` je bio UTC ponoć tog dana, pa fond na dan isteka više nije vrijedio; agent T1-7 je to promijenio u "vrijedi zaključno", što je vraćeno na prijašnje ponašanje (nema tihe promjene pravila) dok korisnik ne odluči. | `LeaveFundYearCalculator.IsExpired`, `LeaveFundHandler.GetEligible`. |
| 2026-10-09 | Port 5001/6001 | Usklađuje se u F1-0. | Frontend F1 plan. |
| 2026-10-09 | F1-Q8 Ručni modeli ili Swagger? | Generirati tipove iz Swaggera, servisi ručni; provjera zastarjelosti, točnost Swaggera (nullable, enumi, datumi), generirane datoteke se ne uređuju, greške u shemi, prijelaz odjednom na početku F1. | T1-6; frontend FE-ADR-0004; pravilo u oba `CLAUDE.md`. |
| 2026-10-09 | (tehnički, bez pitanja) T1-7: gdje se određuje dan cjenika termina | Jedno mjesto: `IPricingService.ResolveForServiceStart` (lokalni datum početka u zoni poslovnice termina); svi pozivatelji (termin, booking, grupa, lista čekanja, preskakanje člana grupe) ga koriste; `ResolvePriceRequest.Date` bez vrijednosti = danas u zoni poslovnice (bez nje organizacije) | ADR-0035; implementirano. |
| 2026-10-09 | (tehnički, bez pitanja) T1-7: migracija postojećih podataka | Dvokoračna pretvorba (UPDATE na UTC ponoć lokalnog dana preko join-a zone, zatim `ALTER TYPE date`) — zadržava NOT NULL i poziciju stupca; klijent/zaposlenik zona organizacije, paket zona poslovnice checkouta (inače organizacije), cjenik i povijest zona poslovnice stavke (inače organizacije); fond godišnjeg UTC jer je dan uvijek spremljen kao UTC ponoć | `20261030000001` T1DateTypes, primijenjena lokalno. |
| 2026-10-09 | (tehnički, bez pitanja) T1-7: fond godišnjeg na dan isteka | `ExpiresAt` je dan, fond se smije trošiti zaključno s tim danom (danas po kalendaru organizacije), `IsExpired` = danas > `ExpiresAt`. Prije: istjecao je u ponoć UTC na početku tog dana (rubna razlika od jednog dana, prati dosadašnji opis "nakon ovog datuma") | Bez testa koji je mijenjao očekivanje. |
| 2026-10-09 | (tehnički, bez pitanja) T1-7: dopune odluka koje nisu u opsegu ovog koraka | Ne implementirano u T1-7 (sljedeći koraci, T1-8/T1-9): odbijanje preklapanja ne navodi stavku s kojom se preklapa; `PurchaseDate` / `GdprConsentDate` bez provjere budućnosti i bez audita; upozorenje o rupi u cjeniku | Popis za sljedeći korak. |
| 2026-10-09 | (tehnički, bez pitanja) T1-8: `ChangeSegmentService` s istom uslugom | Cjenik se ne čita (usluga se nije promijenila, samo trajanje/resursi) — primjena načela "samo kad se promijeni nešto o čemu cijena ovisi"; `ChangeSegmentPricingSource` čita uvijek (po odluci) | Promijeniti na zahtjev. |
| 2026-10-09 | (tehnički, bez pitanja) T1-8: oblik upozorenja | `PARTICIPATION_PRICE_CHANGED` jedno po sudjelovanju; zbirno upozorenje grupa je zaseban kod `PRICE_NOT_DEFINED_OCCURRENCES` (drukčiji details od `PRICE_NOT_DEFINED`, frontend prevodi po kodu); rupa se javlja samo kad sudjelovanje dobiva cijenu (segment bez sudionika / termin grupe bez članova ne upozorava); `PRICE_LIST_GAP` samo za rupe uz spremljenu stavku | — |
| 2026-10-09 | (tehnički, bez pitanja) T1-8: upozorenje o rupi i na upisu člana u generirane termine | Osim generiranja, zbirno upozorenje vraćaju i `AddMember`/`ChangeMemberSegmentTemplates` (isti nastanak cijene); `GroupMembershipSkipService` (pozadinsko dodavanje nakon plaćanja) nema odgovor s upozorenjima | — |
| 2026-10-09 | (tehnički, bez pitanja) T1-8: audit ručnog iznosa | Bez promjene sheme: novi `ChangeType = "ManualAmount"` (OldValue = predložena cijena, NewValue = upisani iznos) uz postojeći `Amount` | Postojeći `Amount` zapisi nepromijenjeni. |
| 2026-10-09 | Nalaz T1-8 (stavka 5, bez promjene): provizija može premašiti naplaćeni iznos | Fiksno pravilo nije ograničeno osnovicom; sesija pokrivena članarinom (bez "oduzmi popuste članstva") i paketom ima osnovicu cijenu sesije. Uz postotno pravilo i ručni iznos ne može. | Za odluku korisnika (nije mijenjano). |
| 2026-10-09 | (tehnički, bez pitanja) T1-9: brisanje klijenta i povijest | `client_audit_logs.client_id` je FK s `ON DELETE CASCADE`: postojeće trajno brisanje klijenta (bez budućih aktivnosti) ostaje moguće i briše i njegovu povijest; `RESTRICT` bi blokirao brisanje svakog klijenta kojem je ikad dana suglasnost | Promijeniti na zahtjev (npr. zadržati povijest bez FK). |
| 2026-10-09 | (tehnički, bez pitanja) T1-9: anonimizacija i GDPR audit | Anonimizacija briše suglasnost, pa i ona piše `GdprConsent` zapis (razlog "Anonimizacija") — primjena odluke "svaka promjena zastavice i datuma u auditu" | — |
| 2026-10-09 | (tehnički, bez pitanja) T1-9: istekao pri upisu i današnja kupnja | Pravilo `ValidUntilDate < danas` vrijedi za svaki ručni upis, pa i današnji upis paketa s fiksnim datumom isteka koji je prošao (prije se upisivao već istekao); checkout nije mijenjan | Promijeniti na zahtjev. |
| 2026-10-09 | (tehnički, bez pitanja) T1-9: fiksni dan kupnje u testnom svijetu | `SchedulingWorld.AddClientPackage` `PurchaseDate` 2025-01-01 → 2020-01-01 (prije `PastDay`), `CHANGED in T1` — posljedica donje granice pokrića, ne promjena očekivanja testova | — |
| 2026-10-09 | (tehnički, bez pitanja) T1-9: naziv tablice | `client_audit_logs` (kako je zadano), iako su postojeće audit tablice u jednini (`client_membership_audit_log`) | — |
| 2026-10-09 | T1-4 proširenje (zahtjev korisnika, preko koordinatora): dvije razine nove demo organizacije | "Osnova" (poslovnice s radnim vremenom i jednim praznikom, zaposlenici, korisnici s grupama admin / Recepcija / Trener i jedan bez ovlasti, klijenti s GDPR suglasnošću i rođendanima; bez usluga, cjenika, paketa, planova, grupa, termina, checkouta) i "Puni demo" (sve + članarine u svim stanjima, paketi, grupe i raspored tekućeg i sljedećeg tjedna s prisutnošću prošlih dana, checkouti s provizijama, stanja iz prošlosti skokom sata). Obje demo, obje s resetom iste razine; dopuna postojeće ostaje (samo dodaje, ne pomiče sat, vremenska stanja u `Skipped`). Grupe "za prvog klijenta" ne sada (mapiranje nije spremno), kasnije uz potvrdu. | Rute `.../demo-organizations/basic` i `/full` (stara ruta = full), stupac `demo_level`, sekcija T1-4, ručni redoslijed u `T1_ZAVRSNI_PREGLED.md` c). |
| 2026-10-09 | (tehnički, bez pitanja) T1-4: duljina skoka "Punog demoa" | Najmanje 45 dana (mjesečni period + grace 7 dana), do prvog četvrtka (45–51 dan): tekući tjedan ima prošle dane s prisutnošću, sljedeći je budućnost. Sve što nakon skoka mora biti prošlost zakazuje se prije skoka (tada budućnost), ishodi se upisuju nakon skoka. | `ClockAdvancedDays`, `LocalDate` u odgovoru. |
| 2026-10-09 | (tehnički, bez pitanja) T1-4: aktivna i pauzirana članarina nakon skoka | Obnova tijekom skoka stvara neplaćena zaduženja (bez plaćanja bi bile u dugu); u fazi 2 klijent plaća zaostatak kroz checkout, pa su "uredne". Pauza pauzirane članarine zakazana je prije skoka (počinje u tekućem tjednu nakon skoka). | Stanja se na kraju čitaju iz servisa i broje u odgovoru. |
| 2026-10-09 | (tehnički, bez pitanja) T1-4: dopuna postojeće organizacije i paketne sesije | Paket vrijedi od dana kupnje, a sat se ne pomiče, pa se jedinice troše samo "upiši odrađeno" današnjim terminima koji su već prošli (8–11 h); ako ih nema dovoljno, stavka ide u `Skipped`. | Test prihvaća oba ishoda. |
