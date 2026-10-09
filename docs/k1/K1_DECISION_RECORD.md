# K1 — Brze dorade rasporeda i klijenata — Decision Record

> **K1 ZAKLJUČEN 2026-10-08** (nakon pregleda i dvije dorade, vidi dnevnik). Sljedeća faza (K2) samo na nalog.

> Faza otvorena 2026-10-08 iz povratnih informacija klijenta ([POVRATNE_INFORMACIJE_v1](../klijent/POVRATNE_INFORMACIJE_v1.md),
> sekcija "Odgovori klijenta"). Ovaj zapis ima prednost pred tim dokumentom za K1. Dnevnik odluka je na dnu.

## Opseg

| # | Stavka | Izvor |
|---|---|---|
| K1-1 | "Upiši odrađeno" za prošlost validira radno vrijeme (greška uz override, kao za buduće) | 2.3 |
| K1-2 | Označavanje dolaska (`ArrivedAt/ArrivedBy`) + trag u povijesti | 10, P-20 |
| K1-3 | Automatski broj člana; ručni upis uz jedinstvenost i raspon; utrka → `DUPLICATE_MEMBER_NUMBER` (bug c) | 15.3, P-15 |
| K1-4 | Šifrarnik razloga otkazivanja i izostanka + postavka obaveznosti | 12.3 |
| K1-5 | "Vrati cijeli termin" | 14.1 |
| K1-6 | Usluga → zadani resursi | P-7 |
| K1-7 | Generiranje grupa: neaktivna poslovnica (bug a), popis datuma preskočenih zbog praznika (bug d), praznik uz potvrdu (P-5) | 1.3, 2.2 |
| K1-8 | Obnova članarina: sve poslovnice opsega neaktivne → članstvo "stoji" (bug b) | 1.3, P-1 |
| K1-9 | Upozorenje pri dolasku/odradi kad sesija nije pokrivena | K1 nalog |

Izvan opsega K1: K2 (ovlasti), K3 (veze klijenata), dodaci (P-16), P1+ (olakšice P-10), P6 (popis pogođenih pri
deaktivaciji, limiti po zaposleniku).

## Zaključane odluke

### K1-1 Validacija prošlosti
- `POST api/appointments/complete` provjerava radnu snagu (radno vrijeme, odsutnost, pauza, praznik) i za prošli početak,
  isto kao za budući: tvrda greška; `OverrideAvailability` uz `appointments.write.all` → upozorenje. U skladu s ADR-0008
  ("Prošli termini su dopušteni, ali se validiraju"). Namjerna promjena ponašanja.

### K1-2 Dolazak
- `PATCH api/participations/{id}/arrival` (označi) i `DELETE api/participations/{id}/arrival` (poništi).
- Novi grant `appointments.arrival.mark` (granularnost po radnji: označavanje dolaska bez prava uređivanja termina);
  migracijom samo Admin grupama (ADR-0023).
- Dopušteno na Confirmed i Completed, i prije početka termina; ne na Cancelled/NoShow. Vrijeme = sat poslužitelja;
  `ArrivedBy` = korisnik.
- Prijelaz u NoShow ili Cancelled briše oznaku dolaska. Povijest sudjelovanja trajno bilježi da je dolazak bio označen
  (tko, kada) i da je obrisan zbog prijelaza u NoShow/Cancelled (trag za prigovore na naknadu).
- "Stigao" NIJE uvjet za "odrađeno"; trener smije kasnije staviti "nije se pojavio" (P-20). Bez financijskog utjecaja.

### K1-3 Broj člana
- Automatski = najveći postojeći + 1, pod lockom organizacije; brojevi se nikad ne ponavljaju (anonimizirani zadržavaju
  broj).
- Ručni upis dopušten (prijenos iz Excela) uz jedinstvenost i broj ≥ 1.
- Utrka na unique indeksu → `DUPLICATE_MEMBER_NUMBER` (ne 500).
- Ručni broj znatno veći od dosadašnjeg najvećeg (više od +1000) traži potvrdu: bez potvrde odbijeno s greškom i
  upozorenjem da će automatsko brojanje nastaviti od tog broja (isti obrazac kao `ConfirmWithoutCommission`).

### K1-4 Šifrarnik razloga
- Entitet po organizaciji: naziv, aktivan, redoslijed, vrijedi za: otkaz klijenta / otkaz studija / izostanak.
  Upravljanje grantom `catalog.cancellation-reasons.manage` (Admin migracijom).
- Postavka organizacije po događaju (otkaz klijenta / otkaz studija / izostanak): odabir razloga opcionalan (default)
  ili obavezan. Obavezan vrijedi samo ako za taj događaj postoji barem jedna aktivna šifra.
- Postojeći slobodni tekst ostaje uz šifru.
- Vrijedi i za otkaz cijelog termina i grupe, ne samo pojedinog sudionika.
- Sudjelovanje pamti i naziv razloga u trenutku otkaza (snapshot).
- Bez utjecaja na politiku naplate (P-10 / P1+).

### K1-5 Vrati cijeli termin
- Vraćaju se na Confirmed samo sudjelovanja otkazana otkazom cijelog termina. Ona koja je klijent otkazao ranije ostaju
  otkazana.
- Sve ili ništa, uz ponovnu provjeru preklapanja i kapaciteta.
- Grant zasad `appointments.write.all` (u K2 prelazi na grant korekcije).
- Vraćena sudjelovanja ponovno prolaze evaluaciju pokrića članarinom i cijene (kao nova rezervacija u tom trenutku,
  uključujući limite); razlog promjene se zapisuje na sudjelovanju.
- Upisi na listi čekanja istekli zbog otkaza termina NE vraćaju se automatski; odgovor vraća njihov popis za recepciju.
- Vraćanje se bilježi u povijesti termina i sudjelovanja (tko, kada, opcionalni razlog).

### K1-6 Usluga → zadani resursi
- Veza (usluga, resurs, količina). Resurs pripada poslovnici, pa se primjenjuju samo zadani resursi poslovnice termina.
- Primjena pri kreiranju, "Upiši odrađeno", ponavljajućem terminu, dodavanju segmenta i promjeni usluge segmenta: ako
  zahtjev NE šalje `Resources` (null), uzimaju se zadani; ako ih šalje (i prazno), vrijedi poslano.
- Promjena usluge segmenta bez poslanih resursa: resursi prethodne usluge se ZAMJENJUJU zadanima nove usluge.
- Frontend unaprijed prikazuje zadane resurse (backend daje upit).
- Predlošci grupa: zadani se kopiraju pri kreiranju predloška. Kasnija promjena zadanih resursa ne mijenja postojeće
  predloške ni generirane termine (P5).
- Kapacitet resursa ostaje tvrda blokada.

### K1-7 Generiranje grupa
- Grupe neaktivne poslovnice se preskaču i vraćaju na popisu preskočenih s razlogom. Generiranje jedne takve grupe →
  `INACTIVE_COMPANY`.
- Bez potvrde se datum praznika preskače, a odgovor vraća popis preskočenih datuma u istom obliku kao preskočene grupe
  (jedan prikaz za frontend).
- S postojećim `OverrideAvailability` generira se i na praznik, uz upozorenje `COMPANY_CLOSED_HOLIDAY`. Grant ostaje
  `groups.manage` (u K2 prelazi na grant za rad izvan radnog vremena).

### K1-8 Članstvo "stoji" (sve poslovnice opsega neaktivne)
1. Gleda se opseg poslovnica verzije plana koja vrijedi za članstvo (ne poslovnica prodaje).
2. Dok je barem jedna poslovnica opsega aktivna, obnova ide normalno.
3. Kad su sve neaktivne, obnova ne otvara nove periode ni zaduženja i članstvo "stoji" (vidljivo, s razlogom). Tekući
   period ostaje kakav jest.
4. Mehanika: **sustavna pauza** (`membership_pauses` dobiva izvor `Client | CompanyClosure`). Otvara je obnova na granici
   perioda (bez kraja), a zatvara obnova kad vidi aktivnu poslovnicu opsega.
   - **Plan od datuma kupnje:** pauza dana koja uvijek pomiče granice (i kad plan ne produljuje pauzom), pa novi period
     počinje na dan ponovne aktivacije.
   - **Kalendarski plan:** preskočeni periodi. Nakon ponovne aktivacije obnova nastavlja od 1. sljedećeg mjeseca, bez
     zaduženja za ostatak tekućeg mjeseca (aktivacija 1. u mjesecu otvara taj mjesec). Recepcija može za pojedinog člana
     ručno otvoriti period od tog dana kroz postojeći Q47 tok (uz potvrdu, puni iznos).
5. Vrijeme stajanja ne broji se u minimalnu obvezu i ne troši klijentov limit pauza (ni dane ni broj).
6. Klijentova pauza u tijeku i sustavna pauza postoje neovisno. Dani pod sustavnom pauzom ne troše klijentov limit;
   klijentova pauza nakon ponovne aktivacije nastavlja do svog kraja.
7. Dok sustavna pauza traje, klijent ne može zadati novu pauzu, ali može otkazati članstvo (otkazni rok i minimalna
   obveza kao inače; stajanje ne ulazi u obvezu).
8. Termini tijekom stajanja nisu pokriveni.
9. Admin popis članstava koja stoje zbog zatvorenih poslovnica, s razlogom (za ručni otkaz ako je zatvaranje trajno).

### K1-9 Upozorenje "nije pokriveno"
- Upozorenje (ne blokada) na označavanju dolaska i na prijelazu u Completed (individualno, grupna prisutnost, "Upiši
  odrađeno"):
  - `PARTICIPATION_NOT_COVERED`: klijent nema prihvatljiv paket za sesiju, sesija nije pokrivena članarinom i dug > 0;
  - `PARTICIPATION_PACKAGE_AVAILABLE`: postoji prihvatljiv paket koji nije odabran/potrošen ("klijent ima paket X,
    odaberite ga pri odradi");
  - kad članarina postoji, ali ne pokriva (limit, dug, usluga nije u planu…), upozorenje nosi razlog pokrića iz
    `MembershipCoverage`.
- Sesija plaćena novcem ili cijene 0 ne upozorava.

## Implementacija (2026-10-08)

ADR-0031. Migracije `20261028000000` (K1Arrival), `20261028000001` (K1ServiceDefaultResources), `20261028000002`
(K1CancellationReasons) i `20261028000003` (K1MembershipCompanyClosure), primijenjene lokalno. Build bez grešaka; svi testovi
prolaze (1271/1271; novih 37 u `UnitTests/K1/`).

| Stavka | Kod | API |
|---|---|---|
| K1-1 | `AppointmentService.CompleteNow` | — |
| K1-2 | `BookingService.MarkArrival/ClearArrival`, brisanje u jezgri prijelaza | `PATCH/DELETE api/participations/{id}/arrival` |
| K1-3 | `ClientService`, `ClientHandler.Add` (advisory lock) | `MemberNumber` opcionalan, `ConfirmMemberNumberJump` |
| K1-4 | `CancellationReasonService` (jedina provjera `ResolveForEvent`), `ParticipationEventMetadata` | `api/catalog/cancellation-reasons`, `PUT api/organization/settings/cancellation-reasons` |
| K1-5 | `AppointmentService.Restore` | `POST api/appointments/{id}/restore` |
| K1-6 | `ServiceAvailabilityService` (zadani resursi), `AppointmentService` (`DefaultResourcesOf`), `GroupService.BuildTemplate` | `GET/PUT api/catalog/services/{id}/default-resources` |
| K1-7 | `GroupService.GenerateAppointmentsAttempt` | `GenerateGroupAppointmentsResult.Skipped` |
| K1-8 | `MembershipRenewalService` (Start/EndCompanyClosure), `MembershipPeriodCalendar` (razmak), `MembershipTimelines.PeriodPauseSpans`, `MembershipLifecycleRules.PauseUsage`, `ClientMembershipService` | `GET api/memberships/standing-still`, `ClientMembershipDto.StandingStillSince`, `MembershipPauseDto.Source` |
| K1-9 | `ParticipationCoverageWarnings` | `BookingDto.Warnings`, `GroupAttendanceListDto.Warnings`, `AppointmentDto.Warnings` |

**Namjerne promjene ponašanja** (testovi označeni `CHANGED in K1`):
- "Upiši odrađeno" za prošlost više ne preskače radno vrijeme.
- Generiranje grupe navodi preskočene praznike (i grupe neaktivnih poslovnica).
- `Resources` na zahtjevima zakazivanja i predlošcima grupa je nullable (izostavljeno = zadano).

### Implementacijski izbori koji nisu bili izričito potvrđeni
1. **Dolazak:** grant `appointments.arrival.mark` vrijedi za bilo koji termin (nema own/all podjele). Ponovno označavanje ne
   mijenja prvi zapis. Trag u povijesti je `AppointmentAuditLog` tipa "Arrival" (OldValue = vrijeme|tko, NewValue =
   `Cleared:Manual|NoShow|Cancelled`).
2. **Broj člana:** izmjena bez broja zadržava postojeći. Skok se mjeri prema trenutnom najvećem broju organizacije.
3. **Šifrarnik:**
   - šifra se ne briše, samo deaktivira;
   - aktivni naziv je jedinstven (case-insensitive);
   - popis šifri vide i korisnici s `appointments.write.*` / `groups.attendance.*` (forma otkaza);
   - System otkazivanje nikad ne nosi šifru;
   - šifra otkaza cijelog termina sprema se i na termin.
4. **Vrati termin:**
   - pokriće članarinom (i cijena po 2E) se ponovno evaluira kroz jezgru prijelaza, ali se cjenik ne razrješava ponovno
     (iznos ostaje snapshot);
   - meki kapacitet grupe se ne provjerava ponovno (vraća se točno prijašnje stanje);
   - upozorenje navodi sve istekle upise liste čekanja tog termina s razlogom `APPOINTMENT_CANCELLED`.
5. **Zadani resursi:**
   - samo aktivni resursi poslovnice termina;
   - novi zadani resurs mora biti aktivan, količina 1..kapacitet;
   - upit je dostupan i uz `appointments.write.*`;
   - promjena usluge na istu uslugu bez resursa zadržava resurse;
   - izmjena predloška grupe bez resursa zadržava postojeće;
   - ponavljajući termin dobiva zadane resurse i provjeru kapaciteta za cijeli niz — sudar resursa vraća
     `RESOURCE_CAPACITY_EXCEEDED`, ne `RECURRING_CONFLICT`.
6. **Generiranje grupa:** `SkippedCount` broji preskočene occurrence (već generirane + praznike), ne grupe neaktivnih poslovnica.
7. **Stajanje (K1-8):**
   - Datum ponovne aktivacije je dan prolaza obnove (dnevni prolaz).
   - Aktivacija istog dana kad je stajanje počelo poništava sustavnu pauzu (razlog `Withdrawn`).
   - Plan od datuma kupnje: zatvoreno stajanje je preskočeni razmak u matematici perioda, pa tekući period ne produljuje.
   - Termini u stajanju imaju razlog pokrića `Paused`; stanje članstva je `Paused`, a `CurrentPeriod` je null dok stoji.
   - Raniji povratak (Q47) za sustavnu pauzu dopušten je samo kod kalendarskog plana nakon ponovne aktivacije.
   - Otkaz tijekom stajanja zatvara sustavnu pauzu na dan završetka; stajanje koje još nije počelo poništava se kao svaka
     zakazana pauza.
8. **Upozorenje "nije pokriveno":**
   - računa se nakon commita (čitanje), za odradu individualnog sudjelovanja, grupnu prisutnost, check-in gosta i
     "Upiši odrađeno";
   - za dolazak na Confirmed sudjelovanje s prihvatljivim paketom vraća `PARTICIPATION_PACKAGE_AVAILABLE`.

## Dnevnik odluka tijekom implementacije

| Datum | Pitanje | Odgovor | Posljedica |
|---|---|---|---|
| 2026-10-08 | Odgovori klijenta P-1 … P-20 | Zapisano u POVRATNE_INFORMACIJE_v1 "Odgovori klijenta"; nalog: implementirati samo K1, pitati prije svake poslovne odluke. | Otvoren K1. |
| 2026-10-08 | Bug b: koja poslovnica i što znači "zaustaviti"? | Sve poslovnice opsega verzije plana neaktivne → članstvo "stoji" (ne otvara periode ni zaduženja); ponovna aktivacija nastavlja bez naknadnog zaduživanja; stajanje se ne broji u obvezu ni limit pauza; admin popis. Zamjenjuje raniju formulaciju "tretira se kao deaktiviran plan" (usklađeno s P-1). | K1-8 |
| 2026-10-08 | Bug b: mehanika | Sustavna pauza (izvor CompanyClosure). Kalendarski plan NE automatski Q47 nego od 1. sljedećeg mjeseca (ručni Q47 ostaje); od datuma kupnje od dana aktivacije; neovisno o klijentovoj pauzi (test); u stajanju nema nove pauze, otkaz da. | K1-8 |
| 2026-10-08 | Bug a: grupa neaktivne poslovnice | Preskoči i navedi na popisu preskočenih (isti oblik kao praznici); jedna grupa → `INACTIVE_COMPANY`. | K1-7 |
| 2026-10-08 | P-5 + bug d: praznik kod generiranja | Bez potvrde preskoči + popis; s `OverrideAvailability` generiraj uz upozorenje; bez zasebne zastavice; grant `groups.manage` do K2. | K1-7; bilješka K2 |
| 2026-10-08 | P-15: sljedeći broj člana | MAX + 1 pod lockom; nikad ponavljanje; ručni broj > najveći + 1000 traži potvrdu (obrazac ConfirmWithoutCommission). | K1-3 |
| 2026-10-08 | 10 / P-20: pravila dolaska | Uske naredbe; novi grant `appointments.arrival.mark` (Admin); Confirmed/Completed, i prije početka; NoShow/Cancelled briše oznaku uz trajni trag u povijesti; bez financijskog utjecaja. | K1-2 |
| 2026-10-08 | 12.3: opseg šifrarnika | Entitet + postavka obaveznosti po događaju (default opcionalno; obavezno samo ako postoji aktivna šifra); tekst ostaje; i za otkaz termina/grupe; snapshot naziva; bez utjecaja na politiku. | K1-4 |
| 2026-10-08 | 14.1: koja sudjelovanja | Samo otkazana otkazom termina; sve ili ništa; `appointments.write.all` do K2; ponovna evaluacija pokrića i cijene; istekli waitlist upisi se ne vraćaju (popis u odgovoru); povijest. | K1-5; bilješka K2 |
| 2026-10-08 | P-7: primjena zadanih resursa | Kad zahtjev ne šalje resurse; promjena usluge zamjenjuje; upit za frontend; predlošci kopiraju pri kreiranju, bez propagacije (P5). | K1-6; bilješka P5 |
| 2026-10-08 | 12.3: je li uz šifru razloga slobodni tekst za otkaz studija (Business) i dalje obavezan? | Šifra ILI tekst (oba smiju). Otpis i korekcija i dalje traže tekst (šifrarnik vrijedi samo za otkaz i izostanak). | K1-4; mijenja P1 pravilo "Business traži razlog" (razlog = tekst ili šifra). |
| 2026-10-08 | K1-8: otkaz članstva dok stoji (rok i obveza nisu izračunljivi) | Završava odmah — krajem zadnjeg otvorenog perioda ili danas ako je kasnije — bez otkaznog roka i preostale obveze (stajanje nije klijentov izbor). Poseban razlog završetka ("otkaz tijekom zatvaranja poslovnice"). Zaduženja otvorenih perioda ostaju (povrat je P3). Isto kad recepcija otkazuje u ime klijenta: isti grant `clients.memberships.cancel`, ne end-override. Nakon ponovne aktivacije otkaz opet po redovnim pravilima. | K1-8 |
| 2026-10-08 | Pregled K1 | Stajanje kao preskočeni razmak prihvaćeno. Izbor 1 (vrati termin) potvrđen uz pravilo: ako se vraćanjem promijeni pokriće, vrijedi 2E logika nad povijesnom cijenom (pokriveno → dug 0; nepokriveno → pogodnost za članove; zaštićene cijene ostaju) — tako i radi (jezgra prijelaza → `SyncParticipation` → `PriceParticipation`), test `Restore_ReevaluatesMembershipCoverage_OverTheHistoricalPrice`. Izbor 2 (dolazak bez own/all) potvrđen za sada. Izbor 3: aktivacija poslovnice se nigdje ne bilježi (samo `UpdatedAt`, koji mijenja svaka izmjena); dodavanje traži stupac, migraciju i pravilo kad je u opsegu više poslovnica → ostaje dan prolaza obnove (dnevni prolaz, razlika najviše dan), zabilježeno za kasnije. Dodan test K1-9 za grupnu prisutnost. | Bez promjene koda osim testova. |
| 2026-10-08 | Pregled ostalih izbora | Potvrđeno: šifra razloga (deaktivacija, jedinstven aktivni naziv), šifra na otkazanom terminu, promjena usluge mijenja zadane resurse, `PARTICIPATION_PACKAGE_AVAILABLE`. Dorada 1: sudar resursa u ponavljajućem nizu (`RESOURCE_CAPACITY_EXCEEDED`) nosi `details.conflicts` kao `RECURRING_CONFLICT` — po datumu: razlog, resurs, kapacitet, najveća zauzetost (`RecurringResourceConflictDetail`). Dorada 2: stajanje se prikazuje različito od klijentove pauze — stanje članstva `StandingStill` (uz `StandingStillSince`), razlog nepokrivenosti `MembershipStandingCompanyClosed` (i razlog pogodnosti); mehanika ostaje. **K1 zaključen**; K2 samo na nalog. | Testovi `Recurring_ResourceClash_…`, `StandingStill_IsShownDifferently…`; 1275/1275. |
| 2026-10-08 | Poruka "nije pokriveno" | Bez prihvatljivog paketa, bez pokrića i dug > 0; prihvatljiv neodabran paket → `PARTICIPATION_PACKAGE_AVAILABLE`; razlog pokrića članarine; plaćeno/0 bez upozorenja. | K1-9 |
