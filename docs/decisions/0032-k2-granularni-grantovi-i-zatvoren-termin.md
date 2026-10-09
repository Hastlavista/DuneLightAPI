# ADR-0032: K2 — granularni grantovi (korekcije, zatvoren termin, otpis naknade/jedinice, rad izvan radnog vremena, roster u prošlosti)

- **Status:** Prihvaćeno, implementirano 2026-10-09 (migracija `20261029000000`)
- **Datum:** 2026-10-09
- **Izvor:** [K2 Decision Record](../k2/K2_DECISION_RECORD.md), [povratne informacije klijenta](../klijent/POVRATNE_INFORMACIJE_v1.md)
  (14.2, P-2, P-4, 12.2)
- **Mijenja:** ADR-0018 / P1 D12 (korekcija koja poništava posljedicu), ADR-0008 (tko smije override radne snage), ADR-0031
  (grant za "vrati termin" i override kod grupa)

## Kontekst
Klijent želi birati tko smije što vratiti (14.2), zakazivati izvan radnog vremena bez prava na sve termine (P-2), ograničiti
upis rada unatrag (P-4) i razdvojiti oprost naknade od oprosta ulaska (12.2). Do sada je korekcija bila obična promjena
statusa (`appointments.write.*`), override radne snage je išao uz `appointments.write.all` (kod grupa uz `groups.manage`), a jedan
grant `appointments.policy.override` pokrivao je otpis i korekciju sa stvarnim učinkom.

Sustav je agnostičan prema ulogama (nema uloga, predložaka ni preporučene raspodjele po ulogama); organizacija sama slaže
GrantGroup-e kroz capabilityje, a nove grantove migracijom dobivaju samo Admin grupe (ADR-0023).

## Razmotrene opcije
1. **Grant korekcije za svaku promjenu terminalnog statusa** — jednostavno, ali trener ne bi mogao ispraviti vlastitu grešku
   pri označavanju istog dana.
2. **Grant korekcije tek nakon zatvaranja termina**, uz jednu definiciju "zatvoren" za oba oblika termina.
3. Odobravanje (workflow) umjesto granta za roster unatrag — traži novi model (status zapisa, tko odobrava).

## Odluka
Opcija 2, a za roster zaseban grant (ne opcija 3).

### Novi grantovi (samo Admin grupe; `appointments.policy.override` se gasi)
| Grant | Radnja |
|---|---|
| `appointments.corrections.completed` / `.no-show` / `.cancelled` | Korekcija iz tog statusa na zatvorenom terminu; ponovno otvaranje; `.cancelled` i "vrati termin" |
| `appointments.availability.override` | `OverrideAvailability` (radno vrijeme, odsutnost, pauza, praznik) na svim tokovima, uključujući generiranje grupa |
| `appointments.policy.fee.waive` | Otpis posljedice koja naplaćuje naknadu |
| `appointments.policy.unit.waive` | Otpis posljedice koja troši jedinicu paketa ili kredit članarine |
| `roster.entries.write.past` | Upis, izmjena i brisanje roster zapisa koji počinje prije današnjeg dana organizacije |

Nijedan grant ne širi own/all opseg.

### Zatvoren termin (isto za individualni i grupni, `Utils/AppointmentClosure`)
- Zatvoren = ručno zatvoren (`appointments.closed_at`) ILI je prošao trenutak automatskog zatvaranja: ponoć (zona poslovnice termina)
  nakon poslovnog dana najkasnijeg od kraja zadnjeg segmenta, upisa termina i ponovnog otvaranja.
- Izvodi se pri provjeri — nema noćnog posla. Automatsko zatvaranje ne zarađuje proviziju i ne ističe listu čekanja.
- Ručno: `POST api/appointments/{id}/close` (za grupu = close-out; provizija sesije i istek liste samo prvi put, `closed_out_at`).
- Zaključava samo prijelaze IZ terminalnog statusa: takva korekcija traži grant po izvornom statusu i razlog (audit
  `StatusCorrectedAfterClose`). Confirmed sudjelovanje se označava i nakon zatvaranja bez granta (audit `MarkedAfterClose`).
- Ponovno otvaranje: `POST api/appointments/{id}/reopen` — razlog obavezan, opseg kao inače, grant korekcije za svaki terminalni
  status prisutan na terminu. Briše ručno zatvaranje; termin je otvoren do ručnog zatvaranja ili kraja tog dana.
- Eksplicitno otkazan termin vraća se samo kroz "vrati termin" (uvijek `corrections.cancelled`).

### Korekcija ≠ otpis (mijenja D12)
- Korekcija statusa ispravlja činjenicu: posljedica postaje `Reversed`, nikad `Waived`, i ne traži grant otpisa. Prije zatvaranja
  ne traži ništa (razlog opcionalan), nakon zatvaranja grant korekcije i razlog.
- Otpis (`Waived`) znači "bilo je, ali opraštamo": uvijek razlog i grant po učinku (naknada → fee, jedinica/kredit → unit).
  Učinak je isključiv (jedinica je kazna umjesto naknade). U trenutku događaja grant se provjerava nakon izračuna posljedice;
  bez granta cijela naredba pada (403 s imenom granta), bez tihog izvršenja.

### Ostalo
- **Override radne snage** je neovisan o opsegu: provjeravaju se opseg i grant. Zatražen bez granta → 403 (prije: tiho
  ignoriran). Svaki override koji je stvarno nešto zaobišao bilježi audit `AvailabilityOverride` (tko, kada, kodovi).
- **"Vrati termin"** smije i own opseg uz `corrections.cancelled`; prolazi provjere kao novi upis (radna snaga uz override,
  preklapanja i kapaciteti, meki kapacitet grupe bez overridea, pokriće).
- **Provizija:** individualna provizija se stornira korekcijom iz Completed uz razlog
  `Korekcija statusa X -> Y (StatusVersion n): razlog`; povratak u Completed stvara novi zapis. Grupna provizija ostaje po
  sesiji (ADR-0030) — korekcija polaznika je ne mijenja; prazna / sve-NoShow sesija je pitanje klijentu P-21 (prije Payrolla).
- **Roster u prošlosti:** bez vremenske granice (otvoreno u ARCHITECTURE §7.3); `RosterAuditLog` bilježi tko i kada.

## Posljedice
- Migracija `20261029000000`: stupci `closed_at/by`, `reopened_at/by`, `reopen_reason` na `appointments`; `appointments.policy.override`
  obrisan iz SVIH grupa — **ne-Admin grupe gube tu ovlast i ne dobivaju nove** (nema produkcijskih podataka, ADR-0003); novi
  grantovi samo Admin grupama.
- `AppointmentDto`: `IsClosed`, `ClosedAt/By`, `AutoClosesAt`, `ReopenedAt/By`, `ReopenReason`. `AppointmentRestoreRequest.OverrideAvailability`.
- Novi kodovi grešaka: `APPOINTMENT_NOT_CLOSED` (409), `CORRECTION_REASON_REQUIRED` (400).
- Karakterizacijski testovi s oznakom `CHANGED in K2`: korekcija posljedice prije zatvaranja (`CancellationPolicyEngineTests`,
  `CancellationPolicyHttpContractTests`), override bez granta (`AppointmentCreateCharacterizationTests`,
  `AppointmentWorkforceAvailabilityCharacterizationTests`). Akter test suite (`SchedulingWorld`) ima `availability.override`.
- Otvoreno (ARCHITECTURE §7.3): granica za roster unatrag i granica za kasno označavanje kao postavke organizacije.
