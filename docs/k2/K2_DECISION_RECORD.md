# K2 — Ovlasti (granularni grantovi) — Decision Record

> Faza otvorena 2026-10-09 iz povratnih informacija klijenta ([POVRATNE_INFORMACIJE_v1](../klijent/POVRATNE_INFORMACIJE_v1.md),
> 14.2, P-2, P-4, 12.2). Samo granularni grantovi na backendu; capability i ovisnosti grantova u editoru i dalje čekaju
> frontend (ARCHITECTURE.md §7.3). Ovaj zapis ima prednost pred tim dokumentom za K2. Dnevnik odluka je na dnu.

## Opseg

| # | Stavka | Izvor |
|---|---|---|
| K2-1 | Granularni grantovi za korekcije statusa (po izvornom statusu; grupna prisutnost; "vrati termin" iz K1-5) | 14.2, K1-5 |
| K2-2 | Zaseban grant za zakazivanje izvan radnog vremena / dostupnosti (override) | P-2 (2.1), K1-1, K1-7 |
| K2-3 | Zaseban grant za upis/izmjenu roster zapisa u prošlosti | P-4 |
| K2-4 | Razdvajanje oprosta naknade i oprosta jedinice paketa/kredita | 12.2 |

## Zaključane odluke

### Sustav je agnostičan prema ulogama
- Nema zakucanih uloga, predložaka uloga ni preporučene raspodjele grantova po ulogama u dokumentaciji. Sustav ne koristi
  samo fitness studio, pa nazivi radnih mjesta (recepcija, trener, voditelj...) nemaju mjesto ni u kodu ni u dokumentaciji.
- Sve grantove ima samo Admin grupa prvog korisnika koji otvara organizaciju (odgovorna osoba). Organizacija dalje sama
  slaže grupe kroz capabilityje i dodjeljuje ih zaposlenicima.
- Stavka 5 naloga ("dokumentiraj preporučenu raspodjelu po ulogama") se zato ne radi.

### Novi grantovi (prijedlog prihvaćen 2026-10-09)
`appointments.corrections.completed`, `appointments.corrections.no-show`, `appointments.corrections.cancelled`,
`appointments.availability.override`, `roster.entries.write.past`, `appointments.policy.fee.waive`,
`appointments.policy.unit.waive`. `appointments.policy.override` se gasi. Nijedan grant ne širi opseg (own/all).

### K2-1 Korekcije
- Dok termin nije zatvoren, promjena statusa je normalno označavanje i ne treba grant. Svaka promjena automatski poništava
  posljedice prethodnog statusa (naknada, potrošena jedinica, provizija). To je ispravak činjenice, ne otpis, i bilježi se u audit.
- Nakon zatvaranja korekcija traži grant po izvornom statusu (`corrections.completed` / `no-show` / `cancelled`).
- Ponovno otvaranje zatvorenog termina ili grupe traži isti grant kao korekcija (inače se pravilo zaobilazi).
- Pravilo "zatvoren" je isto za individualni i grupni termin (definicija niže, potvrđeno 2026-10-09).
- Razlog: korekcija prije zatvaranja neobavezan (audit bilježi prijelaz i poništenu posljedicu); nakon zatvaranja obavezan.
- Sudjelovanje koje je ostalo Confirmed označava se i nakon zatvaranja bez granta; audit bilježi da je označeno nakon zatvaranja.
- Provizija: nakon korekcije se prilagođava vidljivom stavkom s razlogom i vezom na korekciju (storno / novi zapis), nikad tihim
  prepisivanjem; provizija nikad ne ostaje za sudjelovanje koje više nije Completed.

### Zatvoren termin (isto za individualni i grupni)
- Termin je **zatvoren** kad je ručno zatvoren ili je prošao trenutak automatskog zatvaranja, osim ako je nakon toga ponovno otvoren.
- **Ručno:** naredba "Zatvori termin" za oba oblika. Za grupu je to close-out: provizija sesije i istek liste čekanja samo kod
  prvog ručnog zatvaranja (ponovljeno ne zarađuje ništa).
- **Automatski:** kraj poslovnog dana (ponoć u zoni organizacije) od najkasnijeg od: kraj zadnjeg segmenta, upis termina,
  ponovno otvaranje. Izvedeno pri provjeri, bez noćnog posla; ne zarađuje proviziju i ne ističe listu čekanja.
- Zaključava samo promjene iz terminalnog statusa (Completed, NoShow, Cancelled).
- **Ponovno otvaranje:** razlog obavezan, opseg kao inače, grant korekcije za svaki terminalni status prisutan na terminu.
  Otvara do ručnog zatvaranja ili kraja tog dana. Tko/kada/zašto se bilježi.
- Eksplicitno otkazan termin se ne otvara ovom naredbom nego kroz "Vrati termin" (uvijek `corrections.cancelled`).

### K2-4 Otpis
- Otpis (`fee.waive` / `unit.waive`) traži razlog uvijek. Zahtjev za otpis bez potrebnog granta odbija se u cijelosti s
  porukom koji grant nedostaje (nema tihog izvršenja bez otpisa ni tihog otpisa).
- Korekcija koja poništava naknadu ili jedinicu ne traži grant za otpis; posljedica postaje Reversed, nikad Waived (izmjena
  ADR-0018/D12, novi ADR).
- Korekcija ≠ otpis. Korekcija "no-show → completed" znači "nije bio no-show" i uklanja naknadu. Otpis (`policy.*.waive`) znači
  "bio je no-show, ali opraštamo". Obje radnje traže razlog i bilježe se. Korekcija nije zaobilaznica za otpis: posljedica
  se u auditu vidi kao korekcija statusa (Reversed), ne kao otpis (Waived).

### K2-2 Override radnog vremena
- Neovisan je o opsegu. Provjeravaju se obje stvari: opseg (smije li dirati taj termin) i `appointments.availability.override`
  (smije li izvan radnog vremena). Own opseg + override = svoj termin izvan radnog vremena da, tuđi ne.
- Svaki override bilježi tko ga je napravio.

### K1-5 "Vrati termin"
- Smije se i u own opsegu, uz `appointments.corrections.cancelled`; nema posebnog granta.
- Vraćeni termin prolazi sve provjere kao novi: kapacitet grupe, radno vrijeme (izvan njega treba i override), pokriće
  članstvom/paketom s upozorenjem. Nema upisa u punu grupu kroz "vrati termin".

### K2-3 Roster u prošlosti
- `roster.entries.write.past` zasad bez vremenske granice; svaki upis unatrag bilježi tko ga je napravio i kada.
- Granica (npr. N dana) kao postavka organizacije je otvorena tema u ARCHITECTURE.md §7.3 (utjecaj na provizije).

### K2-4 Gašenje `appointments.policy.override`
- Gasi se. Popis mjesta i zamjena (#1–#11) potvrđen 2026-10-09: otpis u trenutku događaja i naknadni otpis → `fee.waive` /
  `unit.waive` po učinku posljedice (oboje → oba); korekcija → grant korekcije (samo zatvoren termin); capability se dijeli na
  dva; nova migracija briše stari ključ iz SVIH grupa i nove dodaje samo Admin grupama (ne-Admin grupe gube ovlast).

## Implementacija (2026-10-09)

ADR-0032, migracija `20261029000000` (stupci zatvaranja na `appointments`, gašenje `appointments.policy.override` u svim
grupama, novi grantovi Admin grupama).

| Stavka | Gdje |
|---|---|
| Zatvorenost (izvedena), pravilo korekcije i ponovnog otvaranja | `Utils/AppointmentClosure` |
| Korekcija na zatvorenom terminu, audit `StatusCorrectedAfterClose` / `MarkedAfterClose` | `BookingService.EnsureClosedAppointmentRules` (jezgra prijelaza; vrijedi i za grupnu prisutnost i "vrati termin") |
| Zatvori / otvori termin | `POST api/appointments/{id}/close` (`appointments.write.own/all`), `POST .../reopen` (`appointments.corrections.*`) |
| Otpis po učinku | `Utils/PolicyOverride` (`EffectOf`, `EnsureWaiverAllowed`); u trenutku događaja u `ParticipationPolicyService.CreateConsequence` |
| Override radne snage + audit | `Utils/AvailabilityOverride` (kreiranje, niz, "Upiši odrađeno", segmenti, AddSegment, "vrati termin", generiranje grupa) |
| Roster u prošlosti | `RosterEntryService.EnsurePastAllowed` (Create, Update stari i novi datum, Delete) |
| Storno provizije s razlogom | `ICommissionLedgerService.ReverseForIndividualServiceCorrection(..., reason)` |

**Tehnički izbori bez zasebnog pitanja** (u duhu potvrđenih pravila; promijeniti na zahtjev):
- `OverrideAvailability` zatražen bez granta → **403** (prije: tiho ignoriran uz own opseg). Isti obrazac kao
  `groups.capacity.override` i pravilo "bez tihog izvršenja" iz točke 3.
- Kraj poslovnog dana računa se u zoni **poslovnice termina** (efektivna zona; bez vlastite zone = zona organizacije), kao datum
  sesije kod provizija.
- Opseg za ponovno otvaranje i "vrati termin": `appointments.write.own/all`, a za grupni termin i `groups.attendance.own/all`
  (kao otpis i prisutnost).
- "U prošlosti" za roster = zapis počinje prije današnjeg dana u zoni organizacije; današnji dan nije prošlost.
- Override se bilježi samo kad je stvarno nešto zaobišao (postoje upozorenja dostupnosti); zatraženi override bez učinka ne.
- `PolicyConsequences.HasRealEffect` uklonjen (zamijenio ga je `PolicyOverride.EffectOf`).
- Test suite: akter `SchedulingWorld` ima `appointments.availability.override` od početka (modelira osoblje s punim ovlastima
  nad rasporedom); testovi odbijanja koriste korisnika bez grantova.

**Poznato ponašanje (potvrđeno 2026-10-09, ne ispravlja se):** `AutoClosesAt` se ne sprema, nego izvodi pri svakom čitanju iz
zone poslovnice termina. Promjena zone poslovnice (ili organizacije, kad poslovnica nema svoju) pomiče `AutoClosesAt` i
zatvorenost svih postojećih termina te poslovnice. Ručno zatvaranje (`closed_at`) se ne mijenja.

**Pregled pokrivenosti odbijanja (2026-10-09):** za svaki novi grant postoji test u kojem korisnik bez njega dobiva 403 s
nazivom granta u poruci (`K2PermissionsTests.Refused`):

| Grant | Test |
|---|---|
| `appointments.corrections.completed` | `ClosedAppointment_CorrectionNeedsTheGrantOfTheOriginalStatus_…` |
| `appointments.corrections.no-show` | `ClosedAppointment_AConfirmedParticipationIsStillMarked_…`, `Reopen_WithACompletedAndANoShowParticipation_NeedsBothCorrectionGrants` |
| `appointments.corrections.cancelled` | `Restore_NeedsCorrectionsCancelled_…` (termin otkazan, još nije zatvoren) |
| `appointments.availability.override` | `Override_WithoutTheGrant_IsRefused_…`, `GroupGeneration_OverrideWithoutTheGrant_IsRefused_GroupsManageIsNotEnough_…` |
| `appointments.policy.fee.waive` | `WaiverAfterTheEvent_OfAFee_NeedsFeeWaive_UnitWaiveIsNotEnough` |
| `appointments.policy.unit.waive` | `WaiverAtEventTime_OfAPackageUnit_NeedsUnitWaive_FeeWaiveIsNotEnough_…` |
| `roster.entries.write.past` | `RosterEntry_StartingBeforeToday_NeedsWritePast_…` |

**Rezultat:** build bez grešaka; testovi 1290/1290 (15 novih u `UnitTests/K2/K2PermissionsTests.cs`; namjerno promijenjeni
karakterizacijski testovi označeni `CHANGED in K2`).

## Dnevnik odluka tijekom implementacije

| Datum | Pitanje | Odgovor | Posljedica |
|---|---|---|---|
| 2026-10-09 | Prijedlog je uz grantove imao tablicu preporučene raspodjele po tipičnim ulogama (Admin, Voditelj, Recepcija, Trener). Što su te uloge? | Sustav mora biti agnostičan: nema zakucanih uloga ni ičega sličnog, jer ga neće koristiti samo fitness centri. Admin sa svim grantovima ima samo prvi korisnik koji otvara organizaciju; organizacija si kroz capabilityje sama slaže tko što radi. | Tablica po ulogama se izbacuje iz K2; nema raspodjele po ulogama u dokumentaciji. |
| 2026-10-09 | Treba li svaka korekcija grant? | Ne. Slobodno dok termin nije zatvoren (ispravak činjenice, poništava posljedice, audit); nakon zatvaranja grant po izvornom statusu; ponovno otvaranje traži isti grant. "Zatvoren" isto za individualni i grupni; definiciju poslati prije implementacije. Korekcija ≠ otpis, obje traže razlog. | K2-1; definicija "zatvoren" na potvrdi. |
| 2026-10-09 | Je li override neovisan o opsegu? | Da: opseg I `appointments.availability.override`; own + override = samo svoj termin; svaki override se bilježi. | K2-2 |
| 2026-10-09 | "Vrati termin" u own opsegu? | Da, uz `corrections.cancelled`; vraćeni termin prolazi sve provjere kao novi; nema upisa u punu grupu. | K1-5 mijenja grant |
| 2026-10-09 | Gasi li se `appointments.policy.override`? | Da, uz popis svih mjesta upotrebe i zamjenski grant za svako. | K2-4; popis na potvrdi |
| 2026-10-09 | Roster unatrag | Bez vremenske granice zasad; bilježi se tko i kada; granica kao postavka organizacije otvorena u ARCH §7.3. | ARCH §7.3 |
| 2026-10-09 | Definicija zatvorenog termina i tablica zamjena `policy.override` (#1–#11) | Potvrđeno kako je predloženo, uz dopune: (1) provizija se nakon korekcije zatvorenog termina prilagođava vidljivom stavkom s razlogom i vezom na korekciju, nikad ne ostaje za sudjelovanje koje nije Completed; (2) označavanje Confirmed sudjelovanja nakon zatvaranja bez granta, audit bilježi "nakon zatvaranja", granica kasnog označavanja kao otvorena tema u ARCH §7.3; (3) otpis bez granta odbija se u cijelosti s porukom koji grant nedostaje. Razlog: korekcija prije zatvaranja neobavezan, nakon zatvaranja obavezan; otpis uvijek; ponovno otvaranje obavezan. #9: nova migracija briše stari ključ iz svih grupa (ne-Admin grupe gube ovlast i ne dobivaju nove; zapisati u ADR). "AvailabilityOverride" audit i kod generiranja grupa. | Sekcija "Zatvoren termin" niže; implementacija krenula. |
| 2026-10-09 | Grupna provizija je po sesiji; korekcija polaznika je ne mijenja. Što s grupom? | Ostaje po sesiji (ADR-0030, M1G), bez promjene modela. Individualno: storno dobiva razlog s vezom na korekciju (StatusVersion). Dug B se NE odlučuje u K2: pitanje klijentu P-21 + referenca u PAYROLL_QUESTIONS 3.4 (odgovor prije Payrolla). | P-21 dodan; PAYROLL_QUESTIONS 3.4 |
| 2026-10-09 | Kako ručno testirati automatsko zatvaranje (razvojni alat ne pomiče stvarni sat)? | SQL skript koji dosljedno pomiče sva vremena termina (u `P2_ZAVRSNI_PREGLED.md` c) točka 12); bez `dev/appointments/{id}/backdate` i bez TimeProvidera sada. TimeProvider ostaje tehnički dug: riješiti prije frontenda / online bookinga, ujedinjuje razvojni pomak vremena. | Plan c) 12; ARCHITECTURE §7.4 |
| 2026-10-09 | Redoslijed nakon K2 | K3 → P3 → P4 → P5 → P6 → Paketi v2 / P1+ → Payroll (K3 prije P3: kredit mora znati platitelja). | ARCH §1, POVRATNE_INFORMACIJE §4 ažurirani |
