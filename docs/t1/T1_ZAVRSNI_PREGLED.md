# T1 — Sat sustava i testni alati — završni pregled (2026-10-09)

> Odluke i implementacija: [T1_DECISION_RECORD.md](T1_DECISION_RECORD.md); ADR-0033 (poslovni sat), ADR-0034 (oblik 403),
> ADR-0035 (tipovi datuma i vremena).

## a) Što je napravljeno
| # | Stavka | Stanje |
|---|---|---|
| T1-1 | Jedan poslovni sat (`TimeProvider`), 252 čitanja prebačena, 0 izravnih čitanja sata | gotovo |
| T1-2 | Pomak sata organizacije iz Managementa (samo naprijed, ≤ 400 dana, obnova dan po dan); `TestTools:Enabled`, zabrana u Production | gotovo |
| T1-3 | `GET /api/organization/clock` | gotovo |
| T1-4 | Seed iz Managementa: "Osnova" i "Puni demo" (članarine u svim stanjima, paketi, raspored, provizije; stanja iz prošlosti kroz skok sata), reset iste razine, dopuna postojeće (samo dodaje, ne pomiče sat) | gotovo |
| T1-5 | 403 s `reason` i svim `requiredGrants`; own opseg 403 `OutOfScope` | gotovo |
| T1-6 | Core nullable + Swagger (nullable, obavezna polja, oblik grešaka, XML opisi); validacija nepromijenjena | gotovo |
| T1-7 | Tipovi: trenutak `DateTimeOffset`, dan `DateOnly`, vrijeme dana `TimeOnly`, trajanje `TimeSpan`; migracija `20261030000001` | gotovo |
| T1-8 | Pravila cijena: zamrznuta cijena (grupna prisutnost, ručni iznos), ponovno čitanje cjenika samo kad se mijenja dan/usluga/kontekst, `PARTICIPATION_PRICE_CHANGED`, `PRICE_NOT_DEFINED`, `PRICE_LIST_GAP`, `PRICE_LIST_SCHEDULED_KEEP_OLD_PRICE`, `PRICE_OVERLAP` s detaljima, audit ručnog iznosa | gotovo |
| T1-9 | Paketi i GDPR: `clients.packages.write.past`, bez budućeg datuma kupnje, odbijanje već isteklog, pokriće od dana kupnje, `client_audit_logs`, GDPR datum ne u budućnosti; migracija `20261030000002` | gotovo |

**Rezultat:** build bez grešaka (bez novih upozorenja u produkcijskom kodu); testovi **1375/1375** (85 novih; namjerno
promijenjeni označeni `CHANGED in T1`: 409 → 403 `NOT_OWNER`, tipovi datuma, pokriće paketa od dana kupnje). Migracije
`20261030000000` – `20261030000004` primijenjene lokalno (T1-10 i T1-11: uključiv kraj, ograničenje provizije, "N dana", read grantovi, roster upit; detalji u T1 recordu).

## b) Namjerno otvoreno
- Grace period: D+1 … D+G, potvrđeno bez promjene (dan dospijeća je redovni rok, grace su dodatni dani).
- Grupe ovlasti prvog klijenta su u seedu (Vlasnik, Trener, Trener + recepcija); grantovi izvještaja i blagajne dolaze s B1 / izvještajem.
- `Employee.EmploymentEndDate` bez učinka — P6.
- Nesigurni nullable nazivi preko navigacija (ostavljeni nullable) — T1 record T1-6.
- Uklanjanje testnih alata prije go-livea — ARCH §7.4.

## c) Ručno testiranje (Swagger / Management)
1. U `appsettings.Development.json` (ili varijabli okoline) `TestTools:Enabled = true`; okruženje ne smije biti Production.
2. Management prijava → `POST /api/management/test-tools/demo-organizations/basic` ("Osnova") → zapiši lozinke iz odgovora.
3. Prijava kao zaposlenik demo organizacije → `GET /api/organization/clock` (`isSimulated = false`).
4. `POST /api/management/organizations/{id}/test-tools/clock/advance` s `{ "days": 35 }` → odgovor `daysProcessed = 35`;
   `GET /api/organization/clock` → `isSimulated = true`, datum +35 (pomak u "Osnovi" je trajan za tu organizaciju; reset daje
   novu).
5. Pomak unatrag (`to` u prošlosti) → 409 `TEST_CLOCK_BACKWARDS`; 401 dan → `TEST_CLOCK_ADVANCE_TOO_LARGE`.
6. `POST .../demo-organizations/{id}/reset` → nova organizacija s pomakom 0; stari korisnici se ne mogu prijaviti.
7. Korisnik "bez K2" pokuša ponovno otvoriti zatvoren termin → 403 s `details.reason = MissingGrant` i popisom grantova.
8. Trener (own opseg) pokuša mijenjati tuđi termin → 403 `NOT_OWNER`, `details.reason = OutOfScope`.

### Redoslijed ručnog testiranja po fazama (seed T1-4)
Svaka faza 1–5 počinje od **nove "Osnove"** (`POST /api/management/test-tools/demo-organizations/basic`; ili reset prethodne
"Osnove", koji stvara novu iste razine), da faze ne ovise jedna o drugoj. Faza 6 je "Puni demo".
1. **"Osnova" → usluge i cjenik:** provjeriti 2 poslovnice s radnim vremenom i praznikom, 4 zaposlenika (admin, recepcija,
   trener, korisnik bez ovlasti → 403 na svaku zaštićenu radnju), 20 klijenata s GDPR suglasnošću i rođendanima; zatim dodati
   prostorije, resurse, usluge (dodjela poslovnicama i zaposlenicima), stavke cjenika (preklapanje, rupa → `PRICE_NOT_DEFINED`).
2. **Raspored, termini, grupe, prisutnost:** individualni termini (radno vrijeme, praznik, sudari), "upiši odrađeno", grupa sa
   slotovima i članovima, generiranje, prisutnost i zatvaranje termina; trener samo svoje (own), recepcija sve.
3. **Checkout i plaćanja:** odrađeni termin u checkout, djelomična uplata, storno uplate, zatvaranje; politika otkazivanja s
   naknadom i plaćanje naknade.
4. **Paketi:** paket u katalogu, prodaja kroz checkout, trošenje jedinica odrađivanjem, potrošen paket, paket unatrag (grant).
5. **Članarine:** plan, prodaja, naplata zaduženja kroz checkout, pauza, otkaz; stanja koja traže protok vremena provjeriti
   pomakom sata (`clock/advance`) u toj demo organizaciji.
6. **"Puni demo" → sve zajedno, uključujući stanja nakon skoka sata:** `POST /api/management/test-tools/demo-organizations/full`.
   Odgovor: `clockAdvancedDays` (45–51) i `localDate` (četvrtak); `GET /api/organization/clock` → `isSimulated = true`. Provjeriti:
   članstva aktivno i plaćeno, s dugom (`standing = Delinquent`), pauzirano, završeno, "stoji" (zatvorena pop-up poslovnica,
   pauza `CompanyClosure`); paket 10 → 7 jedinica i potrošen probni paket; tekući tjedan s prošlim danima (odrađeni i plaćeni
   termini, prisutnost i izostanak na grupi, dva termina bez ishoda) i sljedeći tjedan u budućnosti; provizije
   (`GET /api/commissions/entries`) za usluge, grupne termine i prodaju paketa i članarina. Reset → nova "Puni demo" organizacija.
Dopuna postojeće organizacije (`POST /api/management/organizations/{id}/test-tools/seed`) ne pomiče sat: stanja koja traže
protok vremena navodi u `skipped`.

## d) Što frontend treba od T1 (F1)
| Područje | Što |
|---|---|
| "Sada" i "danas" | `GET /api/organization/clock` (`effectiveUtc`, `localDate`, `timeZone`, `companies[]` s vlastitom zonom, `isSimulated`, `offset`) — jedini izvor; traka "Simulirano vrijeme" kad je `isSimulated` (FE-ADR-0005) |
| 403 | `error.details`: `reason` (`MissingGrant` / `OutOfScope` / `CompanyNotAssigned`), `requiredGrants[]`, `match` (`All`/`Any`), `currentScope`/`requiredScope`, `companyId` (FE-ADR-0003). `NOT_OWNER` je sada 403, ne 409 |
| Tipovi | `swagger.json` (`/swagger/v1.0/swagger.json`, Development) — nullable i obavezna polja su točni; greške opisane kao `ErrorResponse` / `ForbiddenErrorResponse` (FE-ADR-0004) |
| Management | ekran testnih alata: stanje (`GET .../test-tools`, `offsetIsPermanent` → upozorenje prije pomaka u ne-demo organizaciji), pomak sata, nova demo organizacija, reset, dopuna postojeće |
| Uklonjeno | `POST /api/dev/time/membership-renewal-run` |
| Port | API po zadanom sluša na `5001` (`--port`), frontend `environment.development.ts` pokazuje na `6001` — uskladiti u F1-0 |
