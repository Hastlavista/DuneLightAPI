# Povratne informacije klijenta na vodič za poslovnu validaciju — v1

> **Samo analiza i zapis (2026-10-08).** Ništa nije implementirano, kod i postojeće odluke nisu mijenjani.
> Izvor povratnih informacija: korisnikove bilješke sa sastanka s klijentom na vodič `dunelight-business-rules.pdf`
> (Documents/DuneLight/Poslovna logika NEW). Vodič je pisan **prije P1 i P2** i opisuje stari sustav. Ovdje je "trenutno
> ponašanje" uvijek prema **današnjem kodu** (nakon P1 i P2), uz dokaz (datoteka:redak).
>
> Kategorije: **PROVJERA** (odgovor iz koda) · **VEĆ RIJEŠENO** (odluka P1/P2/ADR) · **NOVA FUNKCIONALNOST** ·
> **PROTURJEČI POSTOJEĆOJ ODLUCI** · **NEJASNO** (treba pitati klijenta).
>
> Putanje su relativne na korijen repozitorija; `Svc` = `BlueDragon.DuneLight.Infrastructure/Services`,
> `Utl` = `BlueDragon.DuneLight.Infrastructure/Utils`, `Dto` = `BlueDragon.DuneLight.Core/DTOs`.

---

## 1.3 Deaktivacija zadnje aktivne poslovnice

**Vodič §1.3:** tvrda blokada (`LAST_ACTIVE_COMPANY`).
**Danas:** blokade nema. ADR-0021 (implementirano 2026-10-06): organizacija smije imati nula aktivnih poslovnica.
`CompanyHandler.Deactivate` samo postavlja `IsActive = false` (bez kaskade, bez upozorenja).
**Klijent traži:** da se i zadnja poslovnica može deaktivirati.
**Kategorija:** VEĆ RIJEŠENO (ADR-0021).

**Posljedice (organizacija bez aktivne poslovnice).** Za neaktivnu poslovnicu (`INACTIVE_COMPANY`) **prestaje raditi**:
- novi termin, ponavljajući termin, "Upiši odrađeno", dodavanje segmenta i promjena usluge ili zaposlenika segmenta
  (`EnsureStructuralEligibility`, `Svc/AppointmentService.cs:1012`); slobodni termini su prazni (`:768`);
- nova stavka cjenika (samo kreiranje, `Svc/PricingService.cs:276`), nova dodjela usluge poslovnici
  (`Svc/ServiceAvailabilityService.cs:98`), nova dodjela zaposlenika (`Svc/EmployeeService.cs:436`);
- kreiranje i izmjena grupe i osoblja predloška (`Svc/GroupService.cs:604, 1809`); nove sobe i resursi; pauze;
- otvaranje checkouta (`Svc/CheckoutService.cs:103`), prodaja paketa (`Svc/ClientPackageService.cs:84`), prodaja članarine
  (`Svc/ClientMembershipService.cs:221`; plan bez ijedne aktivne poslovnice → `MEMBERSHIP_PLAN_NO_ACTIVE_COMPANY`);
- matična poslovnica klijenta (`Svc/ClientService.cs:347`);
- onboarding: `HasCompany` = "postoji aktivna poslovnica", pa wizard ponovno traži poslovnicu.

**I dalje radi** (nalaz, nije odlučeno):
- **Postojeći budući termini i grupni termini ostaju netaknuti, bez upozorenja.** Na njima i dalje rade promjena vremena,
  sobe, resursa i izvora cijene, dodavanje klijenta, prijelazi statusa i otkazivanje.
- **Generiranje grupnih termina ne provjerava aktivnost poslovnice** (`GroupService.GenerateAppointmentsAttempt` ~`:1193`
  gleda samo `group.IsActive` i `slot.IsActive`).
- **Obnova članarina ne provjerava poslovnicu** (`Svc/MembershipRenewalService.cs:132, 147` gleda samo `Plan.IsActive`).
  Zaduženja se otvaraju, a pokriće (`MembershipCoverageRules.CoversCompany`) ne gleda `IsActive`.
- Zatvaranje već otvorenog checkouta ne provjerava poslovnicu ponovno.

Što se događa s budućim terminima, grupama i članarinama zatvorene poslovnice je otvorena politika deaktivacije kataloga
(ARCH §7.3, dug M, faza P6). Zadnja dva nalaza treba uključiti u tu odluku.

---

## 2.1 Zakazivanje izvan radnog vremena, promjena trenera — tko smije

**Kategorija:** PROVJERA.

**Odgovor iz koda.**
- Izvan radnog vremena (poslovnice ili zaposlenika), odsutnost, pauza i praznik poslovnice su danas **tvrda greška**:
  `OUTSIDE_WORKING_HOURS`, `EMPLOYEE_ABSENT`, `EMPLOYEE_ON_BREAK`, `COMPANY_CLOSED_HOLIDAY`
  (`Utl/AppointmentEligibilityHelper.cs:47-56`, klasifikacija `:33-43`).
- Prolazi samo ako zahtjev nosi **`OverrideAvailability = true`** I korisnik ima grant **`appointments.write.all`**. Tada
  termin nastaje, a odgovor vraća upozorenje (`*_WARNING` kodovi u `WarningCodes.cs`).
- **Nema zasebnog override granta.** Korisniku sa samo `appointments.write.own` zastavica se **tiho ignorira** (doc
  `Dto/Appointments/AppointmentDtos.cs:751-753`), pa dobiva tvrdu grešku i nema načina potvrditi.

**Isto za kreiranje, izmjenu i pomicanje?** Uglavnom da, uz razlike:

| Operacija | Endpoint | Provjera radnog vremena |
|---|---|---|
| Kreiranje | `POST api/appointments` | da, override uz `.all` (`AppointmentService.cs:846`) |
| Ponavljajući | `POST api/appointments/recurring` | da; bez overridea cijela serija pada s `RECURRING_CONFLICT` (`:570-683`) |
| Pomicanje (drag-drop) | `PATCH api/segments/{id}/time` | da (`AppointmentService.Segments.cs:49`) |
| Dodavanje segmenta | `POST api/appointments/{id}/segments` | da (`:248`) |
| Promjena trenera | `PATCH api/segments/{id}/employees` | da (`Segments.cs:94`) |
| Promjena usluge | `PATCH api/segments/{id}/service` | **samo ako se mijenja kraj** (`Segments.cs:67-70`) |
| Soba, resursi, izvor cijene | — | ne provjerava |
| Generiranje grupe | `POST` generiranje | grant `groups.manage`, **bez `.all`**; praznik se **tiho preskače** (`GroupService.cs:1246-1250`) |
| "Upiši odrađeno" za prošlost | `POST api/appointments/complete` | ne provjerava (vidi 2.3) |

**Promjena trenera.**
- Ide preko `PATCH api/segments/{id}/employees`.
- U own opsegu i stari i novi skup smiju sadržavati samo pozivatelja, pa promjena na drugu osobu u praksi traži
  `appointments.write.all`.
- Strukturna pravila (aktivan, dodijeljen poslovnici i usluzi) uvijek vrijede, bez overridea.
- Nakon odrade je zaključano (`SEGMENT_EXECUTION_HISTORY_LOCKED`, `Segments.cs:116-123`).
- Nema "pune izmjene" (PUT) ni pomicanja cijelog višesegmentnog termina, samo uske naredbe po segmentu (ADR-0014).

**Napomena za klijenta.** "Bilo tko s ovlastima" danas znači "tko ima `appointments.write.all`". Ako klijent želi da npr.
recepcija smije zakazivati za sve, ali ne i izvan radnog vremena (ili obrnuto), treba zaseban grant, npr.
`appointments.availability.override`. To je u duhu granularnosti po radnji (ARCH §7.3) i spada u fazu ovlasti (vidi 14.2).

---

## 2.2 Praznik — isti override kao radno vrijeme

**Kategorija:** PROVJERA (potvrđeno, uz jednu iznimku).

- **Individualni termini:** praznik je isti mehanizam kao radno vrijeme (`COMPANY_CLOSED_HOLIDAY`, ista zastavica, isti
  grant `appointments.write.all`; `AppointmentEligibilityHelper.cs:33-43`). Odgovara klijentovom zahtjevu, bez promjene.
- **Iznimka, grupe:** generiranje grupnih termina datum praznika **tiho preskače** (`GroupService.cs:1246-1250`; poziv s
  `holidayHit: false` na `:1572`). Nema upozorenja ni overridea. Ako klijent želi grupni termin na praznik, danas ga ne može
  generirati (jedino ručno kao individualni termin). → pitanje za klijenta (popis NEJASNO, P-5).

---

## 2.3 "Upiši odrađeno" za prošle datume — validirati radno vrijeme

**Danas:** `POST api/appointments/complete` (`AppointmentService.CompleteNow`, `:240-321`):
- Prošlost je dopuštena.
- Radno vrijeme, odsutnost, pauza i praznik provjeravaju se **samo za budući početak**:
  `if (plan.PlannedStart > DateTimeOffset.UtcNow) warnings.AddRange(await EnsureWorkforceAvailability(...))` (`:273-275`).
- Tvrde invarijante i dalje vrijede: strukturna pravila, preklapanje zaposlenika i klijenta, kapacitet sobe i resursa
  (`:264-291`).
- Običan `POST api/appointments` s prošlim datumom radno vrijeme provjerava. Preskače se samo ovaj put.

**Klijent traži:** upis u prošlost ostaje dopušten, ali se validira protiv radnog vremena.
**Kategorija:** NOVA FUNKCIONALNOST, **mala**. Nije u sukobu s odlukama, jer ADR-0008 već kaže "Prošli termini su
dopušteni, ali se validiraju; rad izvan radnog vremena/praznika samo uz eksplicitni override grant". Kod je ovdje blaži od
ADR-a.

**Prijedlog (za odluku):**
- (a) **Isto kao za budućnost (preporuka):** greška + `OverrideAvailability` uz `appointments.write.all`. Dosljedno s
  ADR-0008, a "Nastavi ipak?" na frontendu radi isto.
  - Posljedica: trener s own opsegom ne može upisati odrađeni termin izvan svog rasporeda bez nekog s `.all`.
- (b) **Za prošlost samo neblokirajuće upozorenje** (uvijek prolazi, uz upozorenje u odgovoru). Prošlost je činjenica, a
  roster za prošlost često nije ažuran. Odstupa od ADR-0008.
- (c) Kao (a), ali s zasebnim grantom za override (vidi 2.1 / 14.2).

Ovisi o: ništa. Mijenja karakterizacijski test za CompleteNow u prošlosti (namjerna promjena, zapisati).
Faza: "Brze dorade rasporeda" (vidi grupiranje).

---

## 2.4 Nejasna rečenica u vodiču §2.4

**Kategorija:** PROVJERA (ispravak dokumenta).

Naslov "Rezervacija poslovnice ne provjerava backend vs. frontend" je očito oštećen. Tijelo kaže da je provjera isključivo
na backendu i da je backend konačna instanca. Namjera je bila: **sva pravila zakazivanja provodi backend; frontend ih ne
duplicira niti zaobilazi.**

**Prijedlog formulacije za v2:**
> **2.4 Gdje se provjeravaju pravila.** Sva pravila iz ovog vodiča (radno vrijeme, praznici, dodjele, preklapanja,
> kapaciteti) provjerava poslužitelj (backend) pri svakom spremanju, bez obzira na to odakle zahtjev dolazi (aplikacija za
> osoblje, buduća online rezervacija, integracija). Sučelje može unaprijed prikazati upozorenje ili sakriti nedostupne
> termine, ali konačnu odluku uvijek donosi poslužitelj. Zato isto pravilo vrijedi i za korisnika koji bi zaobišao sučelje.

---

## 3.2 Limit po zaposleniku (npr. max 5 masaža dnevno)

**Danas:**
- `EmployeeServiceAssignment` ima samo `EmployeeId` i `ServiceId`, bez ikakvog limita.
- Prazan popis = sve usluge (`Utl/EmployeeServiceEligibility.cs`: `assignedServiceIds.Count == 0 || ...Contains(serviceId)`).

**Klijent traži:** koliko puta zaposlenik smije odraditi uslugu u periodu.
**Kategorija:** NOVA FUNKCIONALNOST, **srednja**.

**Mehanizam:** isti obrazac kao prozori limita članarine (Q17): kalendarski prozori (dan, tjedan pon–ned, mjesec) u zoni
poslovnice termina; termin pripada prozoru po vremenu termina. Brojanje mora biti pod subject lockom zaposlenika (već
postoji za preklapanja, `SchedulingConflictGuard`), da dva paralelna zakazivanja ne probiju limit.

**Status Q37 (prazan popis = sve):** **otvoreno, izvan P2, bez dodijeljene faze** (`docs/p2/P2_PLAN.md:717, 735`;
`P2_ZAVRSNI_PREGLED.md` "Zasebne odluke nakon P2").
- **Ovisnost:** ako limit živi na dodjeli usluge, zaposlenik s praznim popisom (smije sve) nema redak na koji bi se limit
  zakačio. Zato Q37 treba odlučiti prije ili zajedno s ovim, ili limit modelirati kao zaseban entitet
  (zaposlenik × usluga|sve × prozor × max) neovisan o dodjeli.

**Otvorena pravila (pitanja, P-3):**
- tvrda blokada ili upozorenje s overrideom;
- koji statusi se broje (Confirmed + Completed? NoShow?);
- broje li se grupni termini;
- po poslovnici ili ukupno;
- limit po usluzi ili za skupinu usluga.

Faza: Workforce (P6).

---

## 3.3a On-call raspored vanjskog suradnika

**Danas:** zaposlenik bez predloška i bez roster zapisa je "nedostupan" (vodič §3.3). Zakazati ga se može samo uz override
(`appointments.write.all`).
**Kategorija:** NEJASNO. Pitanja su u popisu (P-4).

Moguće interpretacije:
- (1) suradnik bez fiksnog rasporeda, zakazuje se kad se dogovori, sustav ga ne smije tretirati kao "izvan radnog
  vremena";
- (2) dežurstvo (on-call prozor) u kojem je pozivljiv;
- (3) drugačiji obračun (satnica ili po pozivu).

Za (1) bi bila dovoljna oznaka na zaposleniku "nema fiksnog rasporeda → provjera radnog vremena se ne primjenjuje" (mala).
(2) je nova vrsta roster zapisa (srednja). (3) pripada Payrollu.

---

## 3.3b Zamjena trenera na bolovanju jednim klikom

**Danas:**
- **Nema masovnog prebacivanja.** Postoji samo ručna promjena po segmentu (`PATCH api/segments/{id}/employees`, radi i na
  grupnim terminima).
- Kreiranje odsutnosti ne gleda termine (samo upozorenje `RosterEntryOverlap`).
- Deaktivacija zaposlenika vraća samo `EMPLOYEE_HAS_FUTURE_APPOINTMENTS` (`Svc/EmployeeService.cs:315-320`).
- Promjena trenera na predlošku grupe ne propagira se na generirane termine (`GroupService.cs:415-417`).

**Kategorija:** NOVA FUNKCIONALNOST, **srednja**.

**Sadržaj:**
- Naredba "zamijeni zaposlenika X zaposlenikom Y u rasponu [od, do]" s pregledom (dry-run) i odabirom termina.
- Primjenjuje postojeću promjenu zaposlenika po segmentu: strukturna pravila, preklapanje zamjene, radno vrijeme zamjene
  uz override.
- Ishod po terminu: prebačeno / preskočeno s razlogom.

**Otvoreno za odluku:**
- (1) **Cijena:** ako je izvor cijene "Employee" (cijena po zaposleniku postoji, vidi 20.1), zamjena mijenja cijenu
  klijentu. Zadržati staru, uzeti novu ili pitati?
- (2) Djelomično uspješna serija: sve ili ništa, ili po terminu.
- (3) Višezaposlenički segment: mijenja se samo X.
- (4) Obavijest klijentima (P4).
- (5) Odrađeni segmenti su zaključani i ne diraju se.

Provizija sama ide novom treneru, jer nastaje pri odradi po zaposlenicima segmenta.
Ovisi o: P5 (propagacija grupa) za "i budući generirani termini". Faza: Workforce (P6).

---

## 4.1 Termin s više zaposlenika i više klijenata

**Kategorija:** VEĆ RIJEŠENO (ADR-0005, ADR-0008, ADR-0009, ADR-0010, ADR-0028). Vodič to ne opisuje.

- **Više zaposlenika na segmentu:** podržano (`AppointmentSegmentDefinitionRequest.EmployeeIds`) kroz kreiranje, "Upiši
  odrađeno", dodavanje segmenta i promjenu zaposlenika. Duplikati se odbijaju.
  - Uz 2+ zaposlenika izvor cijene mora biti izričit (`PRICING_SOURCE_REQUIRED`, `Utl/SegmentPricingSource.cs:34-57`).
    Sustav nikad sam ne bira (ADR-0009).
  - Ograničenje: ponavljajući termin (`/recurring`) prima samo jedan `EmployeeId`.
- **Preklapanje:** svaki zaposlenik se provjerava zasebno, tvrda blokada (`Utl/SchedulingConflictGuard.cs:48-66`). Termin
  zauzima raspored svih.
  - Kapacitet sobe broji zaposlenike + klijente (`RoomPeopleCount.Of`).
- **Više klijenata (roditelj + dijete):** podržano (`Segments[].Participants[]`, naknadno `POST api/appointments/{id}/clients`).
  - Svaki klijent ima vlastiti Booking i Participation, svoj status, cijenu, paket, naplatu i pokriće.
  - Preklapanje klijenata se provjerava za svakog.
- **Provizije:** **svaki zaposlenik zarađuje neovisno, po svom pravilu, na punu cijenu sesije**. Nema podjele
  (`Svc/CommissionService.cs:718-760`).
  - Supervizor + trener na terminu od 50 € s pravilima 10 % → svaki dobiva 5 €.
- **Pokriće članarinom:** po sudjelovanju, svaki klijent zasebno (`AppointmentService.cs:885-895`).
- **Otvoreno:** roditelj plaća za dijete. Checkout traži istog klijenta (`CHECKOUT_ITEM_CLIENT_MISMATCH`), vidi 15.2.

Za klijenta potvrditi (P-6): je li "svaki zaposlenik punu proviziju" ispravno za par supervizor + trener, ili treba podjela?

---

## 4.3a Kapacitet sobe s paralelnim terminima

**Kategorija:** VEĆ RIJEŠENO (ADR-0008). Vodič je zastario.

- `AllowConcurrentBookings` više ne postoji.
- `Room.Capacity` je broj **osoba** istovremeno (zaposlenici + klijenti u statusu Confirmed/Completed na preklapajućim
  segmentima).
- Tvrda blokada `ROOM_CAPACITY_EXCEEDED` bez overridea (`SchedulingConflictGuard.FindCapacityViolations`,
  `Utl/IntervalCapacity.cs`).
- Smanjenje kapaciteta ne smije ostaviti postojeće stanje iznad limita (`CapacityChangeGuard`).

Za klijenta potvrditi (P-7): je li kapacitet u **osobama** (uključujući trenere) ono što misli, ili broj termina /
klijenata.

---

## 4.3b Resursi (npr. stolovi za masažu)

**Kategorija:** VEĆ RIJEŠENO u jezgri (ADR-0008), uz manju NOVU FUNKCIONALNOST (**mala**).

**Postoji:**
- `Resource` s `Capacity` (broj jedinica), vezan uz poslovnicu (`api/catalog/resources`).
- Segment traži `QuantityRequired` po resursu (`PUT api/segments/{id}/resources`); predložak grupe također.
- Zbroj preklapajućih ≤ kapacitet je **tvrda blokada** `RESOURCE_CAPACITY_EXCEEDED`, neovisno o sobi.
- Klijentov primjer (soba slobodna, stola nema → blokirano) radi već danas, **ako se resurs zada na terminu**.

**Nedostaje:**
- **Veza usluga → potrebni resursi** (npr. "Masaža" uvijek traži 1 stol), da se resurs automatski dodaje pri zakazivanju.
  Danas ga osoblje bira ručno, pa ga može zaboraviti.
  - Predložak: `ServiceResourceRequirement` (usluga, resurs ili tip resursa, količina).
  - Otvoreno je što kad poslovnica nema taj resurs.
- Veza resurs → soba (stol stoji u sobi X) ne postoji. Treba li, pitati klijenta (P-7).
- Zastarjeli komentari u kodu kažu da zakazivanje resurse "još ne koristi" (`Resource.cs`, `AppointmentSegmentResource.cs`,
  `AppointmentDtos.cs:722`). Popraviti uz prvu izmjenu tih datoteka.

Faza: "Brze dorade rasporeda" ili katalog (P6).

---

## 6.5 Promjena svega na pojedinom terminu

**Kategorija:** VEĆ RIJEŠENO (uglavnom; ADR-0005, ADR-0014). Vodič §6.5 je zastario.

| Što | Danas | Napomena |
|---|---|---|
| Vrijeme | `PATCH api/segments/{id}/time` | Ponovno računa cijenu (ručna ostaje) i pokriće |
| **Trajanje** | isti endpoint, `PlannedEnd` je slobodan | Trajanje usluge je samo zadana vrijednost pri kreiranju (`AppointmentService.cs:197`) |
| Cijena | `PATCH api/participations/{id}/price` | Samo individualni i Confirmed (`BookingService.cs:328-347`); inače pri odradi (`Amount`) |
| Tko izvodi | `PATCH api/segments/{id}/employees` | |
| Usluga | `PATCH api/segments/{id}/service` | |
| Klijenti | dodaj `POST .../clients`, ukloni `DELETE api/participations/{id}` (samo netaknuto) ili otkaži | |
| Tko je došao | status po sudjelovanju (`/status`, `/no-show`, `/cancel`, `/confirm`) | |

**Danas NIJE moguće:**
- promjena zaposlenika nakon odrade (`SEGMENT_EXECUTION_HISTORY_LOCKED`);
- cijena nakon odrade (samo korekcijom na Confirmed pa ponovno);
- cijena grupnog sudjelovanja prije check-ina;
- bilo kakva izmjena na eksplicitno otkazanom terminu;
- pomicanje cijelog višesegmentnog termina odjednom;
- promjena poslovnice (ADR-0014);
- dodavanje segmenata ili klijenata na grupni termin kroz ove endpointe;
- uređivanje stvarnog početka i kraja (`ActualStart/End`, otvoreno ARCH §7.3).

Za klijenta (P-8): treba li išta od toga (posebno promjena trenera ili cijene na već odrađenom terminu, bez korekcije
statusa)?

---

## 6.6 Promjena usluge → pitati za buduće termine

**Danas:**
- `ServiceCatalogService.Update` (`:72-97`) samo mijenja `DefaultPrice` i `DefaultDurationMinutes`.
- `PricingService.Update` piše samo povijest stavke.
- Na postojeće termine nema propagacije, upozorenja ni brojača.
- Cijena se snima na sudjelovanje pri nastanku i ponovno računa samo kad se mijenja njegov segment (`RepriceSegment`,
  `Segments.cs:484-492`). Ondje se ručni iznos čuva, ali **već plaćeno se ne štiti**.

**Kategorija:** NOVA FUNKCIONALNOST, **srednja**.

**Što se može ponovno upotrijebiti (2E):**
- zaštite iz `MembershipCoverageService.PriceParticipation` (`:618-629`): `ManualAmount` (`IsAmountManuallyOverridden`)
  i `AlreadyPaid` (aktivna novčana alokacija);
- obrazac `RepriceFuture` (`:698-734`: budući Confirmed, `FOR UPDATE SKIP LOCKED`, `PriceStale` za nezaključane);
- zapis stare i nove cijene uz razlog.

**Razlika:** 2E računa od snimljenog `BaseAmount`, a ovdje treba **ponovno razriješiti cjenik** pa preko toga ponovno
primijeniti pogodnost (Q1).

**Otvoreno za odluku:**
- (1) Okidači: promjena usluge, ali i stavke cjenika (poslovnica / sve / zaposlenik).
- (2) Trajanje: produljenje mijenja `PlannedEnd` budućih termina i može izazvati preklapanja ili kapacitete. Treba
  izvještaj "N prebačeno, M u sukobu", bez djelomičnog tihog stanja.
- (3) Stavke u otvorenom checkoutu: već postoji upozorenje `CHECKOUT_ITEM_PRICE_CHANGED`, a osvježavanje stavke je
  otvoreno (ARCH §7.3).
- (4) Sesije pokrivene paketom ili članarinom: pokrivena sesija ima cijenu iz cjenika i dug 0, pa promjena cijene ne
  mijenja dug.
- (5) Grupni termini: isti obrazac "samo ovaj / ovaj i budući" kao P5 (dug E). Predlažem zajednički mehanizam.

Faza: zajedno s P5 ("Propagacija izmjena kataloga i grupa").

---

## 10. Odvojen korak "klijent je fizički stigao"

**Kategorija:** PROVJERA → NOVA FUNKCIONALNOST, **mala**.

**Odgovor iz koda:** odvojenog koraka nema.
- Statusi sudjelovanja su `Confirmed, Completed, Cancelled, NoShow` (`Core/Enums/ParticipationStatus.cs`). Enum izričito
  kaže da "Arrived" **nije** status nego metapodatak.
- Stupci `ArrivedAt` / `ArrivedBy` postoje na `BookingSegmentParticipation` (`:65-69`, DB CHECK u
  `Migration_2026_10_25_Baseline09_Scheduling.cs:163-178`), ali **ih ništa ne zapisuje**. Nisu ni u DTO-u.
- Jedino se čitaju u `Utl/ParticipationHistory.cs:37-38` (dolazak = "dirnuto", pa se sudjelovanje ne smije fizički
  obrisati). ARCH §7.4 to vodi kao dug.
- Grupni check-in = `Completed` (`GroupAttendanceService.cs:56`).

**Prijedlog:** naredba "označi dolazak" i "poništi dolazak" (uska, ADR-0014) koja upisuje metapodatak, bez financijskog
efekta.

**Za odluku:**
- (1) Grant: recepcija vs. trener, own/all.
- (2) Pravila:
  - sprječava li dolazak NoShow (klijent je stigao → ne može biti izostanak);
  - je li dolazak preduvjet za Completed;
  - smije li se označiti prije početka (npr. 15 min ranije).
- (3) Grupe: dolazak po članu, a trener kasnije zatvara.
- (4) Kasni dolazak: samo zapis vremena?

Faza: "Brze dorade rasporeda".

---

## 12.2 Povrat ulaska u paket kod otkazivanja (posebno grupe)

**Vodič §12.2:** povrat je ručan izbor osoblja pri otkazivanju. **Zastarjelo.**

**Danas (P1):**
- Ulazak se troši **samo pri odradi** (`PackageConsumptionTiming.OnCompletion`, `Utl/PackageConsumptionPolicy.cs:8-10`).
  Otkazivanje Confirmed sudjelovanja ne troši ništa, pa **nema što vratiti**.
- Ručni "vrati ulazak" (`ReturnPackageEntry`) je **uklonjen** (P1 promjena #14).
- Kazna: politika otkazivanja po događaju (kasni otkaz, izostanak) ima `PackageAction = ConsumeUnit`. Sustav tada
  **automatski** skida ulazak umjesto naknade, po pravilima (P1 D6; `CancellationPolicyVersion`).
  - Odabir paketa za kaznu: jedan odgovarajući se bira sam; više → `PACKAGE_SELECTION_REQUIRED`.
- Korekcija iz Completed automatski vraća ulazak (`BookingService.cs:902-917`).
- **Iznimka (override):** grant `appointments.policy.override` + obavezan razlog. Može u trenutku događaja
  (`WaivePolicyConsequence`) ili naknadno (`POST api/participations/{id}/policy-consequence/waive`). Uklanja cijelu
  posljedicu i vraća skinuti ulazak (P1 D10, `Utl/PolicyOverride.cs`).
- **Grupe:**
  - `Attended = false` → politika izostanka (D9).
  - Klijentov otkaz člana grupe ide istom matricom.
  - Otkaz cijelog grupnog termina = Business, **bez posljedice** (`AppointmentService.cs:432-451`).
  - Uklanjanje člana ili odznačavanje predloška = System, bez posljedice.

**Kategorija:** VEĆ RIJEŠENO (P1 D6, D9, D10, D12).

**Što eventualno nedostaje (manje):**
- **Zatvaranje grupnog termina ne radi automatski izostanak** za neevidentirane (samo upozorenje
  `GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS`, D9). Ako klijent pod "automatski" misli da se neoznačeni članovi tretiraju kao
  izostanak, to je promjena odluke D9 (pitanje P-9).
- Jedan grant pokriva i oprost naknade i oprost ulaska. Ako klijent želi razdvojiti, ide u granularne grantove (14.2).
- Grupni check-in s više odgovarajućih paketa baca opću `ValidationAppException` umjesto `PACKAGE_SELECTION_REQUIRED`
  (`BookingService.cs:1054-1061`). Sitna nedosljednost kodova.
- Automatski oprost kad se mjesto popuni s liste čekanja i djelomični oprost: P1 dug.

---

## 12.3 Pravila otkazivanja i naplate; "jednom mjesečno"; razlozi otkazivanja

**Pravila definira vlasnik — VEĆ RIJEŠENO (P1, ADR-0015 – ADR-0017).**
- Imenovani profili politike s nepromjenjivim verzijama.
- Dodjela po usluzi + poslovnici, usluzi ili poslovnici, uz zadanu politiku organizacije. Razrješava samo
  `ICancellationPolicyResolver`.
- Po verziji:
  - otkazni rok (`CancellationWindowMinutes`);
  - po događaju (kasni otkaz, izostanak): `FeeType` (None / Fixed / Percentage) + iznos, `PackageAction`, `MembershipAction`
    (P2).
- Naknada je ograničena na cijenu sesije.
- Politika se primjenjuje **samo na otkaz klijenta**. Otkaz studija (Business) traži `appointments.write.all` i razlog i
  nema posljedice.

**NOVO 1 — "svaki mjesec jednom smije otkazati":** **NOVA FUNKCIONALNOST, srednja + NEJASNO.**
- P1 to izričito ostavlja kao dug "Allowances (first late cancellation free, N per period, counters)"
  (`P1_DECISION_RECORD.md:302, 368`).
- Vjerojatno značenje: N besplatnih **kasnih** otkaza (i/ili izostanaka) po klijentu po razdoblju.
- Mehanizam: brojač iskorištenih olakšica pod lockom klijenta. Kad je olakšica slobodna, posljedica nastaje kao "oproštena
  olakšicom" (trag ostaje, kao Waived, ali bez granta).
- Prozori kao Q17 (kalendarski, zona poslovnice).
- Pitanja: P-10.

**NOVO 2 — šifrarnik razloga otkazivanja:** **NOVA FUNKCIONALNOST, mala.**
- Danas su razlozi slobodan tekst (`CancellationReason`, `NoShowReason`, `WaiverReason`, `CorrectionReason`, max 500) i
  nema entiteta ni enuma.
- Prijedlog: `CancellationReasonCode` po organizaciji (naziv, aktivan, redoslijed, za koje initiatore vrijedi) + opcionalna
  napomena.
- Otvoreno:
  - je li odabir obavezan (danas je razlog obavezan samo za Business);
  - utječe li razlog na politiku (npr. "bolest uz potvrdu" → bez naknade). To bi bila veća promjena resolvera, pitanje
    P-10.

---

## 12.4 Lista čekanja s prioritetom (VIP ispred FIFO)

**Danas:**
- Strogi FIFO po `JoinedAt`, pa `Id` (`Svc/WaitlistService.cs:296-299`). `WaitlistEntry` nema polje prioriteta.
- Ako prvi na listi ima sukob u rasporedu, promocija **staje** i kasniji ga ne preskaču (`:305-311`).
- Oznake klijenata (`ClientTag`) postoje, ali se nigdje ne koriste u pravilima.

**Kategorija:** NOVA FUNKCIONALNOST, **srednja**. Već je na popisu otvorenih odluka (ARCH §7.3 "Waitlist prioritet po
tagovima i auto/manual promocija").

**Ovisi o:**
- odluci kako se prioritet definira (oznaka, članarina, ručno);
- tome je li promocija i dalje automatska.

**Bilješke:**
- Pravilo "FIFO blokira" treba preispitati uz prioritete.
- Oznaka kao poslovni signal kosi se s P1 D13 ("tagovi se ne koriste u razrješavanju politike"). To nije proturječje (D13
  se odnosi na politiku otkazivanja), ali je prvo poslovno korištenje oznaka. Preporuka: prioritet na posebnoj postavci,
  ne na imenu oznake.

Pitanja P-11. Faza: P5 (grupe) ili zasebno uz sustav pogodnosti (oznake, Q1).

---

## 13.2 Naplata i posljedice po statusu; provizija za izostanak; otkaz cijele grupe

**Naplata i posljedica po statusu — VEĆ RIJEŠENO (P1).**
- Događaji kasni otkaz i izostanak, svaki s naknadom ili jedinicom paketa ili posljedicom za članarinu.
- Dug Cancelled / NoShow = naknada aktivne posljedice (ADR-0017).

**Provizija za izostanak — provjera Q38.**
- Postavka `CommissionLateCancellationMode { Never, WhenFeePaid }`, default `Never`
  (`OrganizationSettings.cs:62`).
- Uz `WhenFeePaid` provizija nastaje **samo na plaćenu P1 naknadu** (osnovica = naknada, `CommissionService.cs:923-964`).
  **Ne** nastaje kao da je usluga odrađena.
- Nema provizije ako je kazna podmirena jedinicom paketa ili kreditom članarine. Grupe su isključene (`:947`).
- Provizija za **odrađeno** je normalno pravilo zaposlenik × usluga (ADR-0030).

**Kategorija:** VEĆ RIJEŠENO za "provizija iz naplaćene naknade". **NOVA FUNKCIONALNOST (mala)** ako vlasnik želi da
zaposlenik dobije proviziju za izostanak **kao da je odrađeno** (osnovica = cijena sesije, neovisno o naplati).
- To bi bila treća vrijednost Q38 postavke, npr. `AsCompleted`.
- Pitanje P-12.

**Otkaz cijele grupe, a trener je bio tamo — NEJASNO.**
- Danas otkaz cijelog grupnog termina = nema provizije, jer grupna provizija nastaje samo pri zatvaranju, a otkazan termin
  se ne može zatvoriti (`AppointmentService.cs:370-371`).
- Povezano je s otvorenim dugom B/D8: zatvoren prazan ili sve-izostanak grupni termin **danas zarađuje punu fiksnu
  proviziju** (`CommissionService.cs:784-836`). Dakle, "trener je bio tamo, nitko nije došao" već se plaća, ako se termin
  zatvori umjesto otkaže.
- Pitanje P-13.
- Faza: Payroll (tamo je već dug B).

---

## 14.1 Svi statusi moraju biti povratni

**Vodič §14.1:** individualni "Otkazano" nema povratka. **Zastarjelo.**
**Kategorija:** VEĆ RIJEŠENO (P1 D12, ADR-0018).

- Jedna matrica za individualne i grupne termine: **svaki prijelaz u drugi status je dopušten** uz guardove ciljnog
  događaja (`BookingService.cs:702-710`).
  - Klijentov otkaz samo prije početka.
  - Izostanak samo nakon početka.
  - NoShow → Cancelled samo Business.
- Uključeno je i **Cancelled → Confirmed** za individualne (`PATCH api/participations/{id}/confirm`).
- Korekcija reverzira sve efekte (plaćanja s check-ina, potrošnju paketa, proviziju, posljedicu politike), a reaktivacija
  ponovno provjerava preklapanja i kapacitete.
- Ručne uplate više ne blokiraju korekciju (vodič §14.2 je i tu zastario).

**Manji nedostatak (NOVA, mala):**
- Otkazan cijeli termin vraća se samo potvrđivanjem sudjelovanja jedno po jedno. Termin se tada sam vraća u Scheduled
  (`Utl/AppointmentLifecycle.cs:95-112`). Nema naredbe "vrati cijeli termin".
- Grupna prisutnost (`Attended` bool) ne može vratiti na Confirmed kroz endpoint prisutnosti, nego kroz `/confirm`.

---

## 14.2 Administrator bira tko smije što vratiti

**Danas:**
- Korekcija je obična promjena statusa pod `appointments.write.own` ili `appointments.write.all`. **Zasebnog granta za
  korekcije nema.**
- Dodatno se traži `appointments.policy.override` + razlog **samo** kad korekcija poništava aktivnu posljedicu sa stvarnim
  efektom (`BookingService.cs:485-492`).

**Kategorija:** NOVA FUNKCIONALNOST, **srednja**. U skladu s principom granularnosti po radnji (ARCH §7.3), novi grantovi
idu migracijom samo Admin grupama (ADR-0023).

**Prijedlog podjele (za odluku):**
- `appointments.corrections.completed` — vrati odrađeno (reverzira naplatu s check-ina, paket, proviziju);
- `appointments.corrections.no-show` — vrati izostanak;
- `appointments.corrections.cancelled` — vrati otkazano;
- postojeći `appointments.policy.override` ostaje za poništavanje posljedice sa stvarnim efektom.
- Own/all: korekcija ostaje vezana uz own/all opseg termina (grant nikad ne širi opseg).

**Otvoreno:**
- (1) Granularnost po izvornom statusu (prijedlog) ili po vremenu (npr. "korekcije starije od X dana" ili "nakon
  zatvorenog obračuna" zaseban grant, veza s Payrollom).
- (2) Vrijedi li i za grupnu prisutnost.

Faza: "Ovlasti" (ARCH §7.3), zajedno s override grantom iz 2.1.

---

## 15.2 Jedinstven email klijenta; veze među klijentima

**Jedinstven email — VEĆ RIJEŠENO (ADR-0020, 2026-10-06).** Vodič §15.2 je zastario.
- Email je trimani, case-insensitive i jedinstven u organizaciji; prazan je dopušten. Duplikat →
  `409 CLIENT_EMAIL_ALREADY_IN_USE`.
- Implementacija: `Utl/EmailNormalizer.cs`, `ClientService.cs:96-97, 150-151, 318-335`.
- DB: `ux_clients_organization_email (organization_id, lower(email)) WHERE email IS NOT NULL`.

**Veze među klijentima — NOVA FUNKCIONALNOST, srednja do velika.**
- Danas nema nikakve veze (`Client.cs`: samo matična poslovnica, trener i oznake).
- Checkout traži istog klijenta za termin i zaduženje članarine (`CheckoutService.cs:171-172, 306-307`,
  `CHECKOUT_ITEM_CLIENT_MISMATCH`).
- Povezano s otvorenom temom **platitelj ≠ član** (ARCH §7.3, `P2_ZAVRSNI_PREGLED.md`) i s P4 (kamo idu obavijesti).

**Prijedlog modela (za odluku):**
- `ClientRelationship` (klijent A, klijent B, vrsta: roditelj/skrbnik, partner…, zastavice: "prima obavijesti umjesto",
  "smije plaćati za").
- Kontakt za obavijesti se izvodi: vlastiti email, ili email skrbnika ako dijete nema svoj.
- Plaćanje za drugoga: checkout platitelja smije sadržavati stavke povezanog klijenta uz zastavicu.
- Utjecaj:
  - prihod i dug ostaju po sudjelovanju klijenta korisnika;
  - provizija na prodaju se ne mijenja;
  - P3 povrati idu platitelju.

Pitanja P-14. Faza: "Klijenti — veze i platitelj", **prije P4** (obavijesti trebaju znati primatelja).

---

## 15.3 Automatski broj člana

**Danas:**
- `Client.MemberNumber` je `int`, obavezan i jedinstven u organizaciji. Provjeru radi servis (`ClientService.cs:309-314`,
  `DUPLICATE_MEMBER_NUMBER`), a postoji i DB unique indeks.
- Unosi se ručno. Postoji **prijedlog** `GET api/clients/next-member-number` (MAX + 1, `ClientHandler.cs:223-235`), ali
  se ne dodjeljuje sam.

**Kategorija:** NOVA FUNKCIONALNOST, **mala**.

**Nalazi uz put:**
- `[Required] int` ne sprječava izostavljanje (binda 0). Nema provjere raspona (0 i negativni se primaju).
- Utrka dva upisa na unique indeksu daje 500, a ne `DUPLICATE_MEMBER_NUMBER` (za razliku od emaila).

**Prijedlog:**
- `MemberNumber` u zahtjevu postaje opcionalan. Ako izostane, sustav dodjeljuje sljedeći broj pod lockom organizacije
  (MAX + 1 unutar transakcije, ili brojač na organizaciji).
- Ručni unos ostaje dopušten (prijenos Excel brojeva) uz provjeru jedinstvenosti i pozitivnosti (≥ 1).
- Unique povreda se prevodi u `DUPLICATE_MEMBER_NUMBER`.
- Format: ostaje cijeli broj bez prefiksa (najjednostavnije, kompatibilno s Excelom).
- Ako klijent želi prefiks ili poslovnicu u broju (npr. `ZG-00123`), to je promjena tipa → pitanje P-15.

Faza: "Brze dorade".

---

## 17.2 Paket vs. članstvo

**Kategorija:** VEĆ RIJEŠENO (P2, ADR-0025 – ADR-0028). Potvrđeno.

- Članarina je zasebna domena, nije paket (ADR-0025; `ClientMembership.cs:11`).
- Planovi imaju nepromjenjive verzije (`MembershipPlanService`).
- Članstvo (`ClientMembershipService`) ima periode i zaduženja koje otvara samo obnova (`MembershipRenewalService` +
  hosted service, ADR-0027). Zaduženje se naplaćuje kroz checkout stavku `MembershipCharge`.
- Pokriće sudjelovanja i limiti: `MembershipCoverageService`, ADR-0028.
- Cjenovna pogodnost: ADR-0029.
- Vodič §25.1 ("nema koncepta članarine") je zastario.

---

## 17.3 Valjanost paketa: vrijedi do 30.8., termin 5.9. rezerviran 30.8.

**Danas:**
- Valjanost se provjerava na **datum izvođenja** (lokalni datum planiranog početka segmenta u zoni poslovnice), nikad na
  datum rezervacije ni trenutak odrade.
- `Utl/PackageValidity.cs:7-24`: `serviceDate <= ValidUntilDate`. Isto u `ClientPackageHandler.cs:126-127`,
  `BookingService.cs:988-993`, `ClientPackageEntryMutator.cs:103-104`; ADR-0012.
- **Klijentov primjer:** paket **se ne može iskoristiti**. Termin 5.9. je nakon isteka, ne nudi se kao odgovarajući, a
  odrada s tim paketom → `PACKAGE_NOT_ELIGIBLE`.
- Obrnuto: termin 30.8. odrađen (upisan) tek 2.9. **može** se platiti paketom.

**Kategorija:** NEJASNO (treba klijentova odluka). Ako odabere drugo od današnjeg, to je PROTURJEČI ADR-0012 ("valjanost na
DATUM IZVRŠENJA").

**Opcije:**
- (a) **Datum termina (danas, ADR-0012)** — jednostavno i predvidljivo; paket ne "proteže" valjanost.
- (b) **Datum rezervacije** — termin rezerviran unutar valjanosti smije se odraditi iz paketa i kasnije.
  - Traži da se pri rezervaciji zapiše odabrani paket ("rezervacija paketa"), što danas ne postoji (sudjelovanje nema
    `ClientPackageId` prije odrade).
  - Usko povezano s 18.1 (trošenje pri rezervaciji). Ako se odabere 18.1 "pri rezervaciji", (b) dolazi gotovo besplatno.
- (c) **Postavka** (organizacije ili definicije paketa): "valjanost se provjerava na datum termina | datum rezervacije", uz
  eventualni grace ("još N dana nakon isteka za termine rezervirane prije isteka").

---

## 18.1 Admin bira kada se skida ulazak (rezervacija ili odrada)

**Danas:**
- Samo pri odradi. `PackageConsumptionTiming` postoji kao postavka organizacije, ali s jedinom vrijednošću `OnCompletion`
  (`Core/Enums/PackageConsumptionTiming.cs`; ADR-0012).
- Na rezervaciji se ništa ne troši ni rezervira (`P2_PLAN.md:23-24`). P2 pakete nije mijenjao.

**Kategorija:** PROTURJEČI POSTOJEĆOJ ODLUCI — **ADR-0012** (paket se troši na odradu; enum je namjerno jednovrijednosni) i
P1 D6 / D7 / D12 grade na toj pretpostavci.

**Utjecaj ako se uvede "pri rezervaciji":**
1. **Odabir paketa pri rezervaciji.**
   - Sudjelovanje danas nema odabrani paket prije odrade, pa treba novi trenutak potrošnje (`Trigger = Booking`) i tko bira
     paket.
   - Individualni: izričito, a sustav ne bira (Q30 auto-primjena je odgođena za online booking).
   - Grupe: generiranje termina automatski stvara sudjelovanja za sve članove, pa bi masovno trošilo pakete (odabir jedan /
     više paketa, `PACKAGE_SELECTION_REQUIRED` u batchu).
2. **Otkazivanje pa povrat.**
   - Ulazak se vraća automatski pri pravovremenom otkazu.
   - Kod kasnog otkaza ili izostanka P1 `PackageAction = ConsumeUnit` znači "ne vraćaj". Semantika politike se mijenja iz
     "skini kao kaznu" u "zadrži već skinuto".
   - D6 pravilo "jedinica ILI naknada" ostaje, ali s drugim tokom.
3. **Isključivost novac/paket** (`SettlementExclusivityPolicy`): sudjelovanje je pokriveno paketom od rezervacije, pa je
   novac blokiran od tada.
   - Predujam (prepayment) uplaćen prije rezervacije paketa tada blokira paket.
4. **Korekcije (D12):** Completed → Confirmed više ne vraća ulazak (ostaje rezerviran), a Cancelled → Confirmed ga ponovno
   skida. Matricu efekata treba prepisati za paket.
5. **Promjena vremena ili usluge segmenta:** ponovna provjera valjanosti i usluge paketa, uz moguću zamjenu ili oslobađanje.
6. **Valjanost (17.3):** prirodno postaje "na datum rezervacije" ili oboje.
7. **Izvještaji:** "potrošeno" ≠ "odrađeno". Treba razlikovati rezervirane i iskorištene jedinice.
8. **Zanimljivost:** članarina (P2) već "claima" pokriće pri nastanku sudjelovanja (ADR-0028), a paket na odradi. Danas
   postoji ta asimetrija; "pri rezervaciji" bi je uklonio za pakete.

**Opcije za odluku:**
- (a) **Zadržati OnCompletion** (ADR-0012), bez promjene.
- (b) **Postavka organizacije** `OnBooking | OnCompletion` — enum je za to pripremljen. Jedna semantika po studiju; svi
  efekti 1–7 vrijede za cijelu organizaciju.
- (c) **Po definiciji paketa** (snapshot na prodanom paketu) — najfleksibilnije, ali dva toka žive istovremeno, pa svaka
  P1 / korekcijska putanja mora podržati oba. Najveći trošak testiranja.
- (d) **"Rezervacija jedinice" bez potrošnje** — pri rezervaciji se jedinica drži (preostalo za novu rezervaciju se
  smanjuje, sprječava overbooking paketa), a potrošnja ostaje na odradi. Rješava čest stvarni problem ("klijent rezervira
  12 termina s paketom od 10"), uz manji utjecaj na P1.

**Veličina:** (b) velika, (c) vrlo velika, (d) srednja. Zahtijeva novi ADR koji zamjenjuje dio ADR-0012.

---

## 19. Jedan termin s više usluga: dio paketom, dio gotovinom

**Danas:**
- Jedan termin smije imati **više segmenata (usluga)**. Svaki klijent ima **zasebno sudjelovanje po segmentu**.
- Isključivost paket/novac vrijedi **po sudjelovanju**, ne po terminu ni bookingu (ADR-0012; `BookingService.cs:968-969`:
  "Paket i novac su isključivi SAMO unutar ovog sudjelovanja … druga sudjelovanja istog Bookinga se namiruju neovisno").
- Test `ParticipationNativeAddressingTests.PackageConsumption_TargetsOneParticipation_AndLeavesTheSiblingUncovered`.
- **Dakle "masaža iz paketa + dodatna usluga gotovinom" radi već danas**, ako su to dva segmenta.

**Kategorija:**
- **VEĆ RIJEŠENO** za scenarij više usluga.
- **PROTURJEČI** (ADR-0012 isključivost po sudjelovanju; P1 D6 "jedinica ILI naknada"; otvoren dug O / Decision Log #32)
  samo ako klijent misli **jednu uslugu** plaćenu djelomično paketom, a djelomično novcem (nadoplata za nadogradnju).

**Utjecaj (ako je nadoplata):**
- Dug sesije = cijena − vrijednost jedinice paketa. Treba definirati vrijednost jedinice, a Vagaro odluka 2026-10-08 je
  izračun vrijednosti jedinice paketa upravo **ukinula** (P2 record).
- Mijenja `SettlementExclusivityPolicy`, P1 D6 / D7 (surplus) i osnovicu provizije.

**Opcije za nadoplatu:**
- (a) Ne podržavati, nego modelirati kao dodatni segment "Nadoplata — nadogradnja" (radi danas, bez promjene);
- (b) Fiksni iznos nadoplate po usluzi uz paket (cjenik "uz paket": novac = nadoplata, paket = jedinica);
- (c) Opća djelomična vrijednost paketa (dug O), velika.

Pitanje P-16.

---

## 20.1 Različite cijene po zaposleniku

**Kategorija:** VEĆ RIJEŠENO (cjenik po zaposleniku; ADR-0009). Vodič §20.1 je zastario.

**Hijerarhija danas** (`Svc/PriceResolutionService.cs:14-47`), samo aktivne stavke važeće na datum segmenta, a unutar
razine pobjeđuje najnoviji `ValidFrom`:
1. zaposlenik + poslovnica;
2. zaposlenik + sve poslovnice;
3. poslovnica;
4. sve poslovnice;
5. zadana cijena usluge.

**Napomene:**
- `PriceListItem.EmployeeId` je dopušten samo za usluge (`PricingService.cs:296-308`).
- Koji zaposlenik određuje cijenu bira **izvor cijene segmenta** (`SegmentPricingMode` Standard/Employee +
  `PricingEmployeeId`):
  - 1 zaposlenik → automatski njegova cijena;
  - 2+ → izbor je obavezan, sustav nikad ne bira sam (ADR-0009, ADR-0011).
- `PricingEmployeeId` utječe **samo na cijenu**, ne na proviziju ni "primarnog" zaposlenika.
- **Najbolja cijena (Q1)** se primjenjuje **iznad** razriješene osnovne cijene (uključujući cijenu po zaposleniku). Jedna
  prilagodba, najniža pobjeđuje (`Utl/PriceAdjustmentResolver.cs`); danas je jedini izvor cjenovna pogodnost članarine
  (ADR-0029).
  - Cijena po zaposleniku nije "prilagodba" nego osnovica, pa ne konkurira u Q1.

Za klijenta potvrditi (P-17): treba li cijena po zaposleniku po **razini/klasi** (senior, junior) umjesto po osobi? To je
nova razina (klasa zaposlenika), povezana s klasama iz Payrolla.

---

# Na kraju

## 1. Tablica sažetka

| Stavka | Tema | Kategorija | Veličina |
|---|---|---|---|
| 1.3 | Deaktivacija zadnje poslovnice | VEĆ RIJEŠENO (ADR-0021) | — (nalazi za P6) |
| 2.1 | Override radnog vremena, promjena trenera | PROVJERA | (opc. grant, mala) |
| 2.2 | Praznik = isti override | PROVJERA (grupe preskaču praznik) | — |
| 2.3 | Validacija prošlih "odrađeno" | NOVA FUNKCIONALNOST | mala |
| 2.4 | Rečenica u vodiču | PROVJERA (ispravak teksta) | — |
| 3.2 | Limit usluga po zaposleniku | NOVA FUNKCIONALNOST | srednja |
| 3.3a | On-call vanjski suradnik | NEJASNO | (mala – srednja) |
| 3.3b | Zamjena trenera jednim klikom | NOVA FUNKCIONALNOST | srednja |
| 4.1 | Više zaposlenika / klijenata | VEĆ RIJEŠENO (ADR-0005/0008/0009/0010/0028) | — |
| 4.3a | Kapacitet sobe | VEĆ RIJEŠENO (ADR-0008) | — |
| 4.3b | Resursi s količinom | VEĆ RIJEŠENO + NOVA (veza usluga → resurs) | mala |
| 6.5 | Izmjena pojedinog termina | VEĆ RIJEŠENO (uz ograničenja) | — |
| 6.6 | Propagacija izmjena usluge na buduće | NOVA FUNKCIONALNOST | srednja |
| 10 | Korak "stigao" | PROVJERA → NOVA FUNKCIONALNOST | mala |
| 12.2 | Povrat ulaska, override | VEĆ RIJEŠENO (P1 D6/D9/D10/D12) | — |
| 12.3 | Pravila otkazivanja | VEĆ RIJEŠENO (P1) | — |
| 12.3 | N besplatnih kasnih otkaza | NOVA FUNKCIONALNOST + NEJASNO | srednja |
| 12.3 | Šifrarnik razloga | NOVA FUNKCIONALNOST | mala |
| 12.4 | Prioritet liste čekanja | NOVA FUNKCIONALNOST | srednja |
| 13.2 | Naplata po statusu | VEĆ RIJEŠENO (P1) | — |
| 13.2 | Provizija za izostanak "kao odrađeno" | NOVA FUNKCIONALNOST (proširenje Q38) | mala |
| 13.2 | Provizija kod otkaza cijele grupe | NEJASNO | (mala) |
| 14.1 | Svi statusi povratni | VEĆ RIJEŠENO (P1 D12) + mala dorada (vrati cijeli termin) | mala |
| 14.2 | Granularni grantovi korekcija | NOVA FUNKCIONALNOST | srednja |
| 15.2 | Jedinstven email | VEĆ RIJEŠENO (ADR-0020) | — |
| 15.2 | Veze među klijentima, platitelj | NOVA FUNKCIONALNOST | srednja – velika |
| 15.3 | Automatski broj člana | NOVA FUNKCIONALNOST | mala |
| 17.2 | Paket vs. članstvo | VEĆ RIJEŠENO (ADR-0025 – 0028) | — |
| 17.3 | Valjanost paketa vs. datum | NEJASNO (drugačije od danas = PROTURJEČI ADR-0012) | mala – srednja |
| 18.1 | Kada se skida ulazak | PROTURJEČI (ADR-0012, P1 D6/D12) | srednja – vrlo velika (ovisno o opciji) |
| 19 | Više usluga: paket + gotovina | VEĆ RIJEŠENO (po segmentu); nadoplata = PROTURJEČI (ADR-0012, dug O) | — / velika |
| 20.1 | Cijena po zaposleniku | VEĆ RIJEŠENO (ADR-0009, cjenik) | — |

## 2. Pitanja za klijenta (NEJASNO i potvrde)

- **P-1 (1.3)** Kad zatvorite (zadnju) poslovnicu, što s budućim terminima, grupama koje se i dalje generiraju i
  članarinama koje se i dalje obnavljaju i naplaćuju: otkazati automatski, upozoriti ili ostaviti?
- **P-2 (2.1)** Treba li "smije zakazati izvan radnog vremena" biti zasebna ovlast (npr. recepcija smije zakazivati za sve
  trenere, ali ne izvan radnog vremena), ili je u redu da to ide uz pravo "upravlja svim terminima"?
- **P-3 (3.2)** Limit po zaposleniku:
  - Je li prekoračenje zabrana ili upozorenje koje admin može potvrditi?
  - Broje li se samo zakazani i odrađeni, ili i izostanci?
  - Ulaze li grupni termini?
  - Je li limit po poslovnici ili ukupno, za jednu uslugu ili skupinu usluga?
  - Koja razdoblja: dan, tjedan, mjesec?
- **P-4 (3.3a)** Vanjski suradnik "on-call":
  - Nema li fiksni raspored i zakazuje se kad se dogovori (pa ga sustav ne smije upozoravati na radno vrijeme)?
  - Ili ima dežurne prozore u kojima se smije zvati?
  - Plaća li se drugačije (po pozivu, satnica, dežurstvo)?
  - Treba li ga vidjeti klijent kod online rezervacije?
- **P-5 (2.2)** Grupni termini na dan praznika se danas preskaču pri generiranju. Treba li moći generirati grupni termin
  i na praznik (uz potvrdu)?
- **P-6 (4.1)** Kad termin vode dvije osobe (supervizor + trener), dobiva li svaka punu proviziju po svom pravilu (danas),
  ili se provizija dijeli?
- **P-7 (4.3)** Kapacitet sobe je danas broj osoba istovremeno, uključujući trenere. Je li to vaš pojam kapaciteta? Treba
  li resurs (stol) biti vezan uz sobu ili uslugu, tako da ga sustav sam doda (npr. svaka masaža traži stol)?
- **P-8 (6.5)** Trebate li mijenjati trenera ili cijenu na **već odrađenom** terminu bez vraćanja statusa? Trebate li
  pomicati cijeli višeuslužni termin odjednom?
- **P-9 (12.2)** Kad se zatvori grupni termin, treba li neoznačene polaznike automatski tretirati kao izostanak (s
  naknadom ili skidanjem ulaska po pravilima)? Danas ostaju neriješeni uz upozorenje.
- **P-10 (12.3)** "Jednom mjesečno smije otkazati":
  - Znači li to N besplatnih **kasnih** otkaza po klijentu po razdoblju (pravovremeni su ionako besplatni)? Vrijedi li i za
    izostanke?
  - Kalendarski mjesec ili zadnjih 30 dana?
  - Po klijentu ukupno ili po usluzi / članarini?
  - Prenosi li se neiskorišteno?
  - Treba li razlog otkaza (npr. "bolest") automatski osloboditi naknade, ili je razlog samo evidencija?
- **P-11 (12.4)** Prioritet liste čekanja:
  - Što daje prioritet (oznaka "VIP", aktivna članarina, ručno)?
  - Ima li više razina?
  - Ostaje li unutar iste razine FIFO?
  - Ostaje li promocija automatska ili recepcija potvrđuje?
  - Smije li VIP koji se upiše kasnije preteći nekoga tko je već dugo na listi?
- **P-12 (13.2)** Za izostanak: želite li da zaposlenik dobije proviziju kao da je termin odrađen (iz cijene termina), ili
  samo iz naknade koju je klijent stvarno platio (danas moguće postavkom)?
- **P-13 (13.2)** Kad se otkaže cijela grupa, a trener je došao: treba li ga platiti uvijek, ovisno o razlogu otkaza (npr.
  "nitko nije došao" da, "trener bolestan" ne), ili odlučuje osoba koja otkazuje? Trebaju li treneri za grupu u kojoj nitko
  nije došao, a termin je zatvoren, dobiti punu proviziju (danas da)?
- **P-14 (15.2)** Veze među klijentima:
  - Koje vrste (roditelj–dijete, partneri, obitelj)?
  - Smije li dijete biti bez emaila, a obavijesti idu roditelju?
  - Plaća li roditelj termine i članarine djeteta na svojoj blagajni?
  - Treba li jedan račun za cijelu obitelj?
  - Smije li roditelj vidjeti povijest djeteta?
- **P-15 (15.3)** Je li broj člana dovoljan kao redni broj (1, 2, 3…) ili treba prefiks ili poslovnicu (npr. `ZG-00123`)?
  Unosite li još brojeve iz Excela ručno?
- **P-16 (19)** Mislite li dvije različite usluge na istom terminu (jedna iz paketa, druga gotovinom — radi danas) ili
  **istu** uslugu dijelom iz paketa, a dijelom nadoplatom (npr. nadogradnja s 60 na 90 min)? Ako ovo drugo, je li nadoplata
  fiksan iznos po usluzi?
- **P-17 (20.1)** Cijena po zaposleniku: po osobi (postoji) ili po razini (senior/junior), da ne morate unositi cijenu za
  svakog trenera?
- **P-18 (17.3)** Paket vrijedi do 30.8., termin je 5.9.: smije li se odraditi iz paketa ako je rezerviran do 30.8.? Ako da,
  bez ograničenja ili samo N dana nakon isteka?
- **P-19 (18.1)** Želite li da se ulazak skida već pri rezervaciji, ili vam je zapravo važno da klijent ne može rezervirati
  više termina nego što ima ulazaka (to se može riješiti "rezervacijom jedinice" bez promjene trenutka potrošnje)?
- **P-20 (10)** Kad recepcija označi dolazak: smije li trener kasnije još staviti "nije se pojavio"? Je li "stigao" uvjet za
  "odrađeno"? Želite li vidjeti tko kasni (vrijeme dolaska)?

## 3. Proturječne stavke — odluke za vlasnika projekta

1. **18.1 Trenutak potrošnje paketa** — proturječi ADR-0012 (`OnCompletion`) i P1 D6 / D12.
   - Opcije:
     - (a) zadržati;
     - (b) postavka organizacije `OnBooking | OnCompletion`;
     - (c) po definiciji paketa;
     - (d) **rezervacija jedinice bez promjene trenutka potrošnje**.
   - Preporuka za razmatranje: (d), ako je stvarni problem overbooking paketa (P-19). Inače (b).
   - Svaka opcija osim (a) traži novi ADR koji zamjenjuje dio ADR-0012 i izmjenu P1 korekcijske matrice za paket.
2. **17.3 Valjanost paketa** — ako klijent ne prihvati "datum termina", proturječi ADR-0012.
   - Opcije:
     - (a) datum termina (danas);
     - (b) datum rezervacije (traži zapis paketa pri rezervaciji, prirodno uz 18.1 b/c/d);
     - (c) postavka + grace N dana.
   - Odluku donijeti zajedno s 18.1.
3. **19 Nadoplata unutar jedne usluge** — proturječi ADR-0012 (isključivost po sudjelovanju) i P1 D6; dug O. Vagaro odluka
   je ukinula vrijednost jedinice paketa.
   - Opcije:
     - (a) ne podržavati, nego dodatni segment "Nadoplata" (radi danas);
     - (b) fiksna nadoplata po usluzi uz paket;
     - (c) opća djelomična vrijednost paketa.
   - Prvo P-16. Ako je odgovor "dvije usluge", proturječja nema.
4. **2.3 (blaže, nije proturječje ADR-u):** kod je danas blaži od ADR-0008. Izbor (a) / (b) / (c) iz stavke 2.3 je odluka
   o tome hoće li se kod uskladiti s ADR-om ili ADR s praksom.
5. **12.2 / P-9 (uvjetno):** ako klijent želi automatski izostanak pri zatvaranju grupe, to mijenja P1 D9 (9A "No automatic
   NoShow").

## 4. Prijedlog grupiranja novih funkcionalnosti u faze

Postojeći redoslijed (ARCH §1): P3 Client Credit → P4 Notifications → P5 Group propagacija → P6 Workforce/katalog,
plus Payroll nakon P2. Prijedlog uklapanja:

| Faza | Sadržaj | Ovisnosti | Napomena |
|---|---|---|---|
| **K1 Brze dorade rasporeda i klijenata** (mala, može odmah) | 2.3 validacija prošlosti; 10 označi dolazak (`ArrivedAt/By`); 15.3 auto broj člana (+ popravak utrke i raspona); 12.3 šifrarnik razloga; 14.1 "vrati cijeli termin"; 4.3b usluga → zadani resursi | samo odluke iz P-15, P-20 | Neovisno o ostalom; dobro za ručno testiranje s frontendom. |
| **K2 Ovlasti** | 14.2 grantovi korekcija; 2.1 grant za override dostupnosti; razdvajanje oprosta naknade i jedinice (12.2) | ARCH §7.3 tema "Ovlasti" (capability, ovisnosti grantova) | Prirodno uz frontend test ovlasti. |
| **K3 Klijenti — veze i platitelj** | 15.2 veze, primatelj obavijesti, platitelj ≠ član (P2 otvoreno) | **prije P4** (obavijesti); dodir s P3 (povrat platitelju) | Srednja – velika. |
| **P1+ Proširenja politike** | 12.3 olakšice (N besplatnih kasnih otkaza); 13.2 Q38 `AsCompleted`; P-9 auto-izostanak grupe (ako se odluči) | P1 resolver i ledger; prozori kao Q17 | Olakšice su P1 dug; dobro odvojeno od paketa. |
| **Paketi v2** | 18.1 trenutak potrošnje / rezervacija jedinice; 17.3 valjanost; 19 nadoplata (ako se odluči) | odluke iz §3 (novi ADR); P1 korekcijska matrica; P3 ako ide i povrat | Ne miješati s P1+, jer obje diraju `ParticipationPolicyService`. Prvo Paketi v2 ili prvo P1+, ne paralelno. |
| **P5 Propagacija (proširena)** | dug E (grupe "ovaj i budući") + 6.6 promjena usluge/cjenika na buduće termine + 12.4 prioritet liste čekanja | 2E obrazac `RepriceFuture`; ARCH §7.3 checkout osvježavanje stavke | Jedan zajednički mehanizam "pregled → primijeni na buduće" za grupe i katalog. |
| **P6 Workforce/katalog** | 3.3b zamjena trenera; 3.2 limiti po zaposleniku (**prvo Q37**); 3.3a on-call (nakon P-4); 1.3 / dug M politika deaktivacije (uklj. generiranje grupa i obnovu za neaktivnu poslovnicu) | Q37; P4 za obavijesti o zamjeni; P5 za buduće generirane termine | |
| **Payroll** | 13.2 provizija kod otkaza grupe i prazne grupe (dug B); podjela provizije više zaposlenika (P-6, ako se traži); klase zaposlenika i cijena po razini (P-17) | ADR-0030 | Već planirano. |

**Predloženi redoslijed:** K1 → K2 → (P3) → K3 → P4 → P5 proširena → P6 → Paketi v2 / P1+ (prema odlukama iz §3) →
Payroll. K1 i K2 se mogu raditi i prije P3, jer ne ovise o njemu.

## 5. Tvrdnje u vodiču zastarjele zbog P1/P2 (i ranijih faza) — za v2 vodiča

| § vodiča | Tvrdnja u vodiču | Danas | Izvor |
|---|---|---|---|
| 1.3, 43 #1 | Zadnja aktivna poslovnica se ne može deaktivirati | Može; nula aktivnih je dopušteno | ADR-0021 |
| 2.1, 7.1 #9–12, 42 #4–6 | Radno vrijeme, odsutnost, pauza, praznik = "upozorenje" | Tvrda greška; prolazi samo `OverrideAvailability` + `appointments.write.all` (own: zastavica se ignorira) | `AppointmentEligibilityHelper.cs` |
| 2.2 | Praznik isto kao radno vrijeme | Za individualne da; generiranje grupe praznik tiho preskače | `GroupService.cs:1246-1250` |
| 2.4 | Oštećena rečenica | Zamijeniti formulacijom iz stavke 2.4 | — |
| 4.3, 5.1, 42 #2–3, 43 #10–11, 44 | `AllowConcurrentBookings` da/ne, "bez limita" | `Room.Capacity` u osobama, tvrda blokada | ADR-0008 |
| 5 | Resursi se ne spominju | `Resource.Capacity` + `QuantityRequired` po segmentu, tvrda blokada | ADR-0008 |
| 6.5, 43 | Trajanje se ne može mijenjati po terminu | `PlannedEnd` slobodan pri kreiranju i promjeni vremena | `AppointmentService.cs:197`, `Segments.cs:44` |
| 7.1 | Checklist provjera | Dodati kapacitet sobe i resursa, blokadu duga članarine (`appointments.membership-block.override`) i P1/P2 | ADR-0008, ADR-0028 |
| 8.1 | "Puna izmjena (PUT)" i "brzo pomicanje" | Nema PUT-a; uske naredbe po segmentu (vrijeme, usluga, zaposlenici, soba, resursi, izvor cijene), klijenti, napomena | ADR-0014 |
| 9.1, 13.3 | Booking ima status; termin Zakazano/Odrađeno/Otkazano | Status je na sudjelovanju (Participation, po usluzi); termin ima izveden status `Scheduled/Cancelled/Closed`; Booking ima samo izveden sažetak (uklj. `Mixed`) | ADR-0005, ADR-0006 |
| 12.2, 42 #14, 43 #19 | Povrat ulaska je ručni izbor osoblja | Nema ručnog povrata; troši se samo pri odradi; kazna jedinicom po politici; oprost uz `appointments.policy.override` | P1 D6, D10 |
| 12.3, 12.5, 43 #20–21, 44 | Kasni otkaz je samo evidencija; nema naknada | Politike s verzijama, rokom, naknadom, jedinicom paketa, akcijom članarine; dodjela po usluzi i poslovnici; posljedica u ledgeru | ADR-0015 – ADR-0017 |
| 13.2, 42 #15, 43 #22 | Izostanak: ništa automatski, nema naplate | Politika izostanka (naknada ili jedinica); provizija na plaćenu naknadu uz Q38 postavku | P1, P2 Q38 |
| 14.1, 43 #23, 44 | Individualni "Otkazano" nema povratka; asimetrija | Jedna matrica, svaki status povratan uz guardove | P1 D12, ADR-0018 |
| 14.2, 42 #24, 43 #24 | Ručna uplata blokira korekciju | Više ne blokira; uplata ostaje kao settlement (surplus) | P1 D12, D7 |
| 15.2, 43 #25 | Duplikat emaila klijenta dopušten | Jedinstven, case-insensitive | ADR-0020 |
| 15.3 | Nema auto-generiranja | Postoji prijedlog sljedećeg broja (`next-member-number`), ali nije automatski | `ClientsController.cs:46-52` |
| 19, 43 #28 | Isključivost po rezervaciji (bookingu) | Po sudjelovanju (usluzi); više usluga u terminu može miješati paket i novac | ADR-0012 |
| 20.1, 43 #29 | Poslovnica > sve > zadano | + razina zaposlenika (zaposlenik+poslovnica > zaposlenik > poslovnica > sve > zadano); izvor cijene segmenta | `PriceResolutionService.cs` |
| 20.2 | "Nema cijene po klijentu/članstvu" | Cjenovna pogodnost članarine, najbolja cijena (Q1); pokriće članarinom | ADR-0029, ADR-0028 |
| 22.1, 22.3, 42 #12, 43 #32 | Kapacitet grupe tvrda blokada bez overridea | Meki kapacitet: bez overridea odbijeno, uz `OverrideCapacity` + `groups.capacity.override` dopušteno (dodavanje člana `GroupService.cs:632-648, 703-719`; booking na terminu `BookingService.cs:144, 524`); soba i resursi i dalje tvrdi | ADR-0008 |
| 25.1, 43 #34, 44 | Nema članarine | Članarine (P2): planovi, članstva, periodi, zaduženja kroz checkout, pokriće, pogodnost | ADR-0025 – ADR-0029 |
| 35.1–35.2 | Provizija: pravilo zaposlenik/usluga, postotak samo individualno | Vagaro model: pravilo za uslugu > opće pravilo zaposlenika, "Bez provizije" izričit, verzije po datumu, objašnjenje izbora; nema pravila = nema provizije | ADR-0030 |
| 36 | Provizija na prodaju pripada osobi koja zatvori blagajnu | Zaposleniku odabranom na stavci ("Sold By"); prva prodaja članarine; naknadna dodjela uz razlog | ADR-0030 |
| 38.1, 38.2 | Nove organizacije dobivaju predloške Admin/Trener/Recepcija | Samo Admin grupa (svi grantovi); nema predložaka ni uloga | ADR-0019, ADR-0023 |
| 41.2 | Dug isključuje otkazane | Dug Cancelled/NoShow = naknada aktivne posljedice (može biti > 0) | ADR-0017 |
| 44 "Nije podržano" | Penali, članarina, kapacitet sobe | Sva tri sada postoje | P1, P2, ADR-0008 |

Nisu zastarjele (i dalje točne): §31 deaktivacija zaposlenika samo upozorava; §35.3 grupna provizija nema reverziju; §37
nema vanjske dostave obavijesti; §27 nema općeg voida zatvorenog checkouta (povrat je P3); §41.3 dashboard po poslovnici.

---

# Odgovori klijenta (2026-10-08)

> Odgovori klijenta na P-1 … P-20 i korisnikove potvrde. Odluke donesene tijekom pripreme i implementacije K1 su u
> [K1 decision record](../k1/K1_DECISION_RECORD.md) (dnevnik odluka), koji za K1 ima prednost pred ovim dokumentom.

## Prihvaćene preporuke

| Pitanje | Odgovor | Gdje |
|---|---|---|
| P-1 (1.3) | Pri zatvaranju poslovnice sustav ništa ne otkazuje sam; prikazuje popis pogođenih budućih termina, grupa i članarina. Generiranje grupa i obnova članarina za neaktivnu poslovnicu se zaustavljaju. | K1 (bugovi a, b); popis pogođenih pri deaktivaciji → P6 |
| P-2 (2.1) | Zaseban grant za zakazivanje izvan radnog vremena. | K2 |
| P-3 (3.2) | Limit po zaposleniku: upozorenje uz override; broje se zakazani i odrađeni; po usluzi; prozori dan i tjedan. | P6, nakon Q37 |
| P-5 (2.2) | Grupni termin na praznik smije se generirati uz potvrdu (isti override kao individualni). | K1 |
| P-6 (4.1) | Dvoje na terminu: svaki zaposlenik po svom pravilu (bez promjene); dijeljenje provizije kasnije uz Payroll. | Payroll |
| P-7 (4.3) | Kapacitet sobe = osobe istovremeno uključujući trenere (bez promjene). Usluga automatski zauzima zadane resurse (masaža → stol). | K1 |
| P-8 (6.5) | Izmjena odrađenog termina samo nakon korekcije statusa (bez promjene). | — |
| P-9 (12.2) | Neoznačeni polaznici pri zatvaranju grupe ostaju neriješeni uz upozorenje (P1 D9 bez promjene). | — |
| P-12 (13.2) | Provizija za izostanak samo iz plaćene naknade (postojeća Q38 postavka, bez promjene). | — |
| P-13 (13.2) | Kod otkaza cijele grupe osoba koja otkazuje odlučuje kvačicom "plati trenera". | Payroll |
| P-15 (15.3) | Broj člana = redni broj koji sustav dodjeljuje automatski; ručni upis dopušten (prijenos iz Excela) uz provjeru jedinstvenosti. | K1 |
| P-18 (17.3) | Zatvoreno: termin nakon isteka paketa nije pokriven paketom, naplaćuje se (ADR-0012 bez promjene). | — |
| P-19 (18.1) | Zatvoreno: potrošnja paketa pri odradi (ADR-0012 bez promjene). | — |
| P-20 (10) | Trener smije kasnije staviti "nije se pojavio"; "stigao" NIJE uvjet za "odrađeno"; bilježi se vrijeme i tko je označio dolazak. | K1 |

Posljedica za proturječne stavke (§3 gore): **18.1 i 17.3 zatvoreni bez promjene ADR-0012**; 19 razriješen kroz dodatke (P-16).

## Odgovori koji mijenjaju ili dopunjuju

### P-4 — on-call suradnik
**Odgovor:** suradnik ima roster kao ostali zaposlenici; kad ga se pozove izvan rostera, naknadno se upiše roster zapis da
je radio. Način plaćanja suradnika → Payroll.

**Provjera (dopušta li roster upis rada unatrag i tko to smije):**
- **Da, bez ograničenja datuma.** `RosterEntryService` (Create `:73-144`, Update `:146-224`, Delete) nigdje ne uspoređuje
  datum s danas i nema zaključavanja prošlih razdoblja. Validacija: aktivan zaposlenik i tip, oblik zapisa
  (`ValidateAndCompute` `:570-599`), dosljednost overridea istog dana (`:604`). Preklapanje zapisa je samo upozorenje
  (`ROSTER_ENTRY_OVERLAP`).
- **Tko:** `POST/PUT/DELETE api/roster-entries` traže `roster.entries.write.own` ili `roster.entries.write.all`
  (`RosterEntriesController.cs:42, 51, 59`). Own opseg smije pisati samo vlastite zapise (`ValidateOwnership` `:443-451`)
  — dakle i suradnik sam može upisati svoj rad unatrag, ako ima own grant. Svaka promjena je u `RosterAuditLog`.
- **Učinci upisa unatrag:**
  - postojeći termini se ne revalidiraju;
  - izvještaji sati koriste stvarni zapis umjesto pretpostavljenog predloška (`ApplyPlannedSource` `:352-368`);
  - odsutnost koja troši fond godišnjeg troši fondove prihvatljive **danas**, ne na datum odsutnosti (`:488`).
- **Veza s K1 (2.3):** nakon K1 "Upiši odrađeno" za prošlost provjerava radno vrijeme. Za poziv izvan rostera redoslijed
  je: prvo upisati roster zapis rada, zatim odrađeni termin (prolazi bez overridea). Inače "Upiši odrađeno" traži override
  (`appointments.write.all`, nakon K2 zaseban grant).
- **Otvoreno (nije pitano; P6/Payroll):** zaključavanje rostera za zatvoreno obračunsko razdoblje; treba li upis tuđeg rada
  unatrag poseban grant.

### P-17 — cijena po razini zaposlenika
**NE sada**, tek ako klijenti zatraže. Cijena po zaposleniku (osoba) ostaje kako jest.

### P-11 — prioritet liste čekanja
**ODGOĐENO, bez faze.** Preduvjet je zaseban **sustav oznaka**: definira što koja oznaka (tag) radi i na što utječe
(prioritet liste čekanja, cjenovne pogodnosti (Q1, kandidat `ClientTag` u `PriceAdjustmentResolver`), …). Buduća tema;
12.4 čeka nju.

### P-10 — "jednom mjesečno smije otkazati"
**Svaka organizacija sama definira značenje** → konfigurabilno pravilo olakšica u P1 politici. Faza **P1+**. Model se
predlaže prije implementacije. Model mora pokriti:
- broj besplatnih kasnih otkaza;
- vrijedi li i za izostanak;
- razdoblje (kalendarsko / klizno);
- opseg (po klijentu / po usluzi / po članarini);
- prenosi li se neiskorišteno;
- oslobađa li razlog otkaza (šifrarnik iz K1) naknade.

### P-16 — nadoplata → dodaci (add-ons) — POTVRĐENO
1. **Ostaje današnji način:** različite usluge u istom terminu plaćaju se različito (paket, gotovina…), jer je
   isključivost po sudjelovanju.
2. **Dodaje se: dodaci (add-ons) uz uslugu, kao u Vagaru.** Ono što se "usput uzme dodatno" je zasebna stavka s vlastitom
   cijenom, naplaćuje se normalno, neovisno o tome čime je pokrivena osnovna usluga.
   - Bez djelomičnog plaćanja iste usluge paketom: P1 D6 / ADR-0012 bez promjene, pa proturječja nema.
   - **Ne implementirati bez potvrde modela.**

**Postoji li danas išta slično:** ne.
- Nema oznake dodatka ni veze roditelj–dijete među uslugama: `Service` je ravan entitet; varijante trajanja su zasebne
  usluge.
- Najbliže postojeće:
  - (a) **dodatni segment termina** — vlastita usluga, vrijeme, cijena i sudjelovanje, naplata neovisna o osnovnom
    segmentu;
  - (b) **proizvod na istom checkoutu** (`CheckoutItemType.Product`) — vezan samo uz checkout klijenta, ne uz termin
    (`CheckoutService.AddProductItem :403-479`). Checkout smije miješati sesiju, proizvod, paket i zaduženje članarine
    istog klijenta.

**Kako Vagaro modelira dodatke** (iz općeg poznavanja proizvoda, nije provjereno u njihovoj dokumentaciji):
- Dodatak je stavka kataloga usluga označena kao dodatak, vezana uz usluge uz koje se smije dodati.
- Ima vlastitu cijenu i trajanje. Bira se uz osnovnu uslugu pri rezervaciji (osoblje ili online), a trajanje se nastavlja
  na osnovnu uslugu kod istog izvođača.
- Ne rezervira se samostalno, a ima vlastitu proviziju.

**Prijedlog modela (za potvrdu):**
- `Service.Kind = Standard | AddOn` (dodatak se ne rezervira sam i ne koristi u grupnim predlošcima bez osnovne usluge) +
  `ServiceAddOn` (osnovna usluga, dodatak, redoslijed) — dopuštene kombinacije.
- Na terminu je dodatak **segment** s vlastitom uslugom, `PlannedStart` = kraj osnovnog segmenta, istim zaposlenicima
  (zadano) i `ParentSegmentId` → osnovni segment. Zato automatski dobiva vlastitu cijenu (cjenik, uklj. po zaposleniku),
  vlastito sudjelovanje i settlement (novac neovisno o paketu ili članarini osnovne usluge, isključivost bez promjene),
  proviziju (pravilo za uslugu dodatka) i provjere preklapanja i kapaciteta.
- Pravila za odluku:
  - dodatak bez osnovnog segmenta nije dopušten;
  - otkaz ili izostanak osnovnog sudjelovanja kaskadira na dodatke istog klijenta?
  - promjena vremena osnovnog segmenta pomiče dodatke.
  - Paket i članarina: prijedlog je da dodatak nije prihvatljiv za paket i ne ulazi u pokriće plana, osim ako ga plan ili
    paket izričito sadrži.
- **Veličina:** srednja. **Faza:** zasebna mala faza "Dodaci" nakon K1 (ili uz K2); online booking ga kasnije samo koristi.

### P-14 — veze među klijentima: NOSITELJ + PODRAČUNI — POTVRĐENO u cijelosti
- Podračun je puni klijent sa svim opcijama (termini, paketi, članarine…) i smije biti bez emaila.
- Obavijesti za podračun idu nositelju; nositelj vidi povijest podračuna.
- Podračun može biti dijete ili partner, ali je u odnosu na nositelja uvijek podračun (vrsta veze kao oznaka).
- **Nositelj smije plaćati termine, pakete i članarine podračuna na svojoj blagajni** — rješava P2 otvorenu temu
  "platitelj ≠ član" (ARCH §7.3).
- Jedan račun za obitelj — kasnije, uz fiskalizaciju.
- **Faza K3, prije P4.**

## Bilješke za kasnije faze (iz odgovora tijekom pripreme K1)
- **K2:**
  - (1) Kad se uvede zaseban grant za rad izvan radnog vremena (P-2), `OverrideAvailability` kod generiranja grupa
    (praznik i radno vrijeme) prelazi s `groups.manage` na taj grant.
  - (2) "Vrati cijeli termin" (14.1) prelazi s `appointments.write.all` na odgovarajući grant korekcije (14.2).
  - (3) P-4 (pregled K1): zaseban grant (ili odobrenje) za upis i izmjenu roster zapisa u prošlosti — roster će utjecati na
    Payroll, a suradnik danas može sam sebi upisati rad unatrag. Do K2 bez promjene.
- **P-16 dodaci:** smjer potvrđen (pregled K1) — dodatak kao segment s vezom na osnovnu uslugu. Faza i detalji kasnije; ne
  implementirati.
- **K1-8 (kasnije, po potrebi):** bilježenje trenutka aktivacije poslovnice, da kraj stajanja članstva bude točno taj dan, a
  ne dan dnevnog prolaza obnove.
- **P5 (proširena):** promjena zadanih resursa usluge NE mijenja postojeće predloške grupa ni generirane termine (dio
  "ovaj i budući").
- **P6:** popis pogođenih budućih termina, grupa i članarina pri deaktivaciji poslovnice (P-1); limiti po zaposleniku (P-3).
