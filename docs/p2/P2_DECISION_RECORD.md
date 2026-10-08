# DuneLight P2 — Memberships — Decision Record

> Status: **P2 ZAKLJUČEN 2026-10-08** — dizajn zatvoren 2026-10-07, faze 2A–2F implementirane i svi implementacijski izbori potvrđeni;
> završni pregled: [P2_ZAVRSNI_PREGLED.md](P2_ZAVRSNI_PREGLED.md). Nova faza kreće samo na korisnikov nalog.
> Zamijenjene odluke su označene **Zamijenjeno** uz pokazivač na odluku koja vrijedi; kasniji zapis u dnevniku ima prednost pred ranijim.
> Plan, nalazi iz koda i otvorena pitanja: [P2_PLAN.md](P2_PLAN.md). Ovaj dokument sadrži samo zaključano.

## Izvori
- Target Architecture v1 §15: Membership je zaseban domen, nije podtip paketa, ima vlastiti ponavljajući lifecycle i naplatu,
  kasnije može dati cjenovnu pogodnost, pravo na usluge ili pokriće settlementa.
- Decision Log #28 (OPEN): jedan najprioritetniji adjustment, bez automatskog slaganja; redoslijed membership/tag/promo neodlučen.
- Stari sustav (Business Rules vodič §25): koncept članarine ne postoji.
- Korisnikov spec od 2026-10-07 (sažet niže).

## Zaključano (korisnikov spec, 2026-10-07)

### Osnovno
1. Membership je ZASEBNA domena, nije podtip paketa.
2. Razlika prema paketu: članarina se automatski obnavlja po periodima.
3. Online booking i Stripe nisu u P2, ali dizajn im ne smije zatvoriti put.
4. Računi i fiskalizacija (HR, Fiskalizacija 2.0) nisu u P2, ali se račun/fiskalni zapis kasnije mora moći vezati na dugovanje.

### Što članarina daje
- Pravo na usluge (pokriće): plan pokriva Participation za navedene usluge, neograničeno ili N po periodu; pokrivena
  Participation u settlementu ima iznos 0 i referencu na stavku ledgera (tumačenje "iznos 0" — vidi P2_PLAN Q9).
- Cjenovna pogodnost za članove; prioritet/kombiniranje s tagom, grupom klijenata i promo kodom NIJE odlučen (Q1). → **Odlučeno** Q1
  (najbolja cijena, jedna prilagodba).

### Model
- MembershipPlan (organizacija): naziv, cijena, interval (mjesečno/godišnje), početna naknada, pokrivene usluge + limiti,
  način obnove, max broj aktivnih članarina, pravila pauze, ponašanje kod duga.
- ClientMembership: klijent, plan, status, datum sljedeće obnove, datum otkaza s efektom. → **Precizirano** (2B, ADR-0026): status se
  ne sprema nego izvodi; obnova i kraj se računaju iz perioda.
- MembershipPeriod: početak/kraj, krediti perioda, veza na dugovanje.
- Charge (dugovanje) po periodu: čeka / plaćeno / neuspjelo / otpisano; način plaćanja (gotovina, transakcija, kartica na
  recepciji; kasnije Stripe). U P2 recepcija zatvara ručno. → **Precizirano** Q16: spremljen je samo `Open | WrittenOff | Voided`,
  plaćenost se izvodi iz alokacija; "neuspjelo" nema smisla bez Stripea (dolazi sa Stripeom).

### Obnova i naplata su odvojene
- Obnova = scheduler otvara novi period s novim pravom (domena Membership); naplata = za period nastaje Charge.
- Status članstva se IZVODI iz statusa dugovanja + grace pravila, ne sprema se kao polje "plaćeno".
- DuneLight je vlasnik rasporeda obnove; Stripe će kasnije samo naplaćivati Charge.

### Pravila pokrića
1. Ako Participation mogu pokriti i paket i članarina — članarina prva.
2. Potrošnja pripada periodu u koji pada TERMIN, ne datumu rezervacije.
3. Ledger: potrošnja −1, otkaz Participationa = storno +1 s referencom; nikad brisanje; stanje = zbroj unosa. → **Precizirano** Q6/Q26/Q31:
   kasni otkaz i izostanak uz ForfeitCredit NE vraćaju kredit.
4. Neiskorišteni krediti: reset na kraju perioda (rollover kasnije).
5. Otkaz članarine djeluje na kraju tekućeg perioda. → **Precizirano** (minimalna obveza i otkazni rok): max(kraj perioda, kraj
   obveze, kraj perioda nakon otkaznog roka).
6. Promjena plana usred perioda: samo od sljedećeg ciklusa, bez proporcionalnog obračuna.
7. Sva pravila pokrića su deterministička i na backendu.

### Konfigurabilno
1. Datum obnove po planu: od datuma kupnje ILI 1. u mjesecu. → **Precizirano** (2A): kalendarski samo za mjesečni interval.
2. Ponašanje kod duga nakon grace perioda (organizacija): a) nastavi pokrivati, b) ne pokrivaj (normalan settlement),
   c) blokiraj rezervaciju. Default b) ili a) — ne blokirati. → **Odlučeno** Q15: default grace 7 dana i b) "ne pokrivaj".
3. Pauza / zamrzavanje: pravila definira organizacija (max trajanje, koliko puta, produljuje li period). → **Zamijenjeno** Q5/Q12:
   pravila pauze su na PLANU (max dana / max periodi ukupno u 12 mjeseci, max puta, produljenje).
4. Limiti korištenja (obavezno u P2): po planu i po usluzi, N unutar prozora dnevno/tjedno/mjesečno/kvartalno; kombiniraju se (AND);
   neograničeno = bez limita perioda, prozorski limiti i dalje vrijede.
5. Opcionalni max broj aktivnih članarina po planu (zasebno od kapaciteta termina).

### Deaktivacija plana
- Nova prodaja nije moguća; postojeća članstva traju do kraja tekućeg perioda i NE obnavljaju se. → **Precizirano** (2C): završavaju na
  obnovi ako je plan tada još neaktivan; ponovna aktivacija ili klijentov prelazak na aktivan plan nastavlja članstvo.

### Izvan P2 (ne zatvarati put)
Stripe (kartice, SCA, retry), računi i fiskalizacija, online booking i samostalno otkazivanje, rollover kredita, proporcija pri
promjeni plana, penali za raniji izlazak, ranije otvaranje rezervacija za članove.

### Odluke Q1, Q6, Q9, Q23 (2026-10-07)
- **Q6 Trenutak potrošnje:** kredit se troši **na rezervaciji** (Claim −1 kad Participation postane Confirmed).
  - Rezervacija u period koji još nije otvoren: claim se veže na **očekivani period**; ako se taj period ne otvori (otkaz,
    dug, deaktiviran plan), Participation prelazi na normalan settlement (model: P2_PLAN §10.1).
  - Otkaz na vrijeme vraća kredit; otkaz unutar otkaznog prozora i NoShow **ne vraćaju** kredit. Otkazni prozor je postavka
    organizacije (izvor prozora: vidi P2_PLAN §10.2 / Q25). → **Zamijenjeno** Q25: prozor je P1 klasifikacija politike, ne zasebna
    postavka.
  - Promjena vremena preko granice perioda = storno u starom periodu + novi claim u novom, s provjerom limita.
  - **Paketi ostaju na `OnCompletion`**; P2 ih ne mijenja.
- **Q9 Cijena pokrivene sesije:** `Amount` = cjenik (retail), `MonetaryDue = 0`, u skladu s ADR-0012. "Iznos 0" iz spec-a znači
  MonetaryDue. Provizija na članarinske sesije je zasebno pitanje (P2_PLAN §10.5); ne implementira se dok se ne odluči. → **Odlučeno**
  Vagaro modelom (ADR-0030): osnovica je cijena sesije, uz "oduzmi popuste članstva" pokrivena sesija → 0.
- **Q1 Prioritet pogodnosti:** **najbolja cijena pobjeđuje**; jedna prilagodba, bez slaganja (Decision Log #28).
  - Na stavci se zapisuje primijenjena prilagodba (tip + izvor) i lista kandidata koji su izgubili (za izvještaje i kasniji
    prelazak na fiksni prioritet bez migracije).
  - Kod iste cijene odlučuje deterministički fiksni redoslijed tipova (prijedlog: P2_PLAN §10.4).
  - Dizajn mora omogućiti kasniji prikaz klijentu koja je pogodnost primijenjena i zašto; sam prikaz nije u P2.
  - Fiksni prioritet po organizaciji ostaje moguća kasnija postavka nad istom arhitekturom; ne implementira se sada.
- **Q23 Scope:** **plan bira poslovnice** u kojima članstvo vrijedi; pokriće se provjerava po poslovnici termina.

### Odluke Q25, Q26, Q27, Q4 (2026-10-07)
- **Q25 Otkazni prozor:** kredit prati **P1 klasifikaciju** (`ICancellationPolicyResolver`, `IsLateCancellation`). Jedan prozor za
  naknadu i kredit; zamjenjuje raniji prijedlog zasebne postavke. Je li ponašanje kredita kod kasnog otkaza fiksno ili dio
  P1 politike: otvoreno (Q31). → **Odlučeno** Q31 (dimenzija P1 politike `MembershipAction`).
- **Q26 Kazna kod pokrivene sesije:** kasni otkaz ili NoShow → **kredit propada, bez P1 naknade** (P1 D6: jedinica ILI naknada).
  - "Neograničen" za ovo pravilo = plan **bez kredita perioda** (bez N po periodu), bez obzira na dnevne/tjedne limite. Kod
    takvog plana primjenjuje se P1 naknada (osnovica retail cijena, Q9). Kasni otkaz/NoShow i dalje troši mjesto u limitima prozora.
  - Sesija nepokrivena zbog duga (postavka "ne pokrivaj") je nepokrivena: vrijede normalna P1 pravila, uključujući naknadu.
- **Q27 Horizont pokrića:** **tekući + sljedeći period**.
  - Kad scheduler naknadno primjenjuje pokriće, a limit nema mjesta za sve: pokrivaju se redom po vremenu termina
    (tie-breaker: vrijeme rezervacije); ostale idu na sljedeći izvor (Q4); klijent i recepcija dobivaju obavijest.
  - Rezervacija koja čeka evaluaciju pokrića ne smije stvoriti dug/naplatu dok se pokriće ne evaluira.
  - Ako članstvo ima zakazan kraj, period nakon kraja ne postoji: rezervacije u njemu su nepokrivene od početka (+ warning),
    bez pending claima.
  - Ponavljajuće serije: pokriće se primjenjuje period po period (prijedlog: P2_PLAN §11.4).
- **Q4 Limit iskorišten:** default **prolazi + warning**; org postavka "odbij" (`MEMBERSHIP_LIMIT_EXCEEDED`).
  - Sesija ide na **sljedeći izvor pokrića** (članarina → paket → normalan settlement); warning kaže što se dogodilo. Isto vrijedi
    za scheduler.
  - P2: jedna org postavka; model ostavlja mjesto za nadjačavanje po planu, po tipu limita i po kanalu (recepcija vs online)
    — ne implementira se sada.

### Odluke Q28 – Q31 (2026-10-07)
- **Q28 Model provizije: KONAČNO (Vagaro model, 2026-10-07).** Zamjenjuje SVE ranije dogovore o Allocated /
  "postotku od naplaćenog".
  - **Ukida se:** metoda PostotakOdNaplaćenog / Allocated i sva pravila vezana uz nju: zaključavanje perioda za obračun provizije
    (Q35), periodični obračun (Q34), Allocated za neograničene pakete (Q36), godišnji planovi po mjesečnim isječcima, Allocated za
    grupne treninge, korekcijski unosi provizije nakon zaključavanja. Zaključavanje perioda članarine ostaje za samu članarinu
    (naplata, krediti), ali NE za proviziju. Q32 – Q36 time otpadaju.
  - **Trenutak nastanka se ne mijenja:** provizija se računa na Completed, kao danas. Nema odgođenog obračuna.
  1. ~~Pravilo ostaje trener × usluga: `Percentage | Fixed | None` + vrijednost. `None` je eksplicitan izbor; postojeće dodjele bez
     pravila migriraju na eksplicitno `None` (opseg "dodjele": P2_PLAN §13.2).~~ **Zamijenjeno** preciziranjem niže ("Pravilo
     provizije nije obavezno"): osnovno pravilo je `Percentage | Fixed`, `None` postoji samo kao nadjačavanje, nema migracije.
     → **Ponovno zamijenjeno** (Vagaro, 2026-10-08, ADR-0030): `None` ("Bez provizije") je izričit izbor na pravilu USLUGE;
     nadjačavanja više nema; uz pravilo usluge postoji opće pravilo zaposlenika (sve individualne usluge).
  > ⚠️ **Točke 2, 3, 4 i 6 su zamijenjene** odlukom "kopiramo Vagaro model" (2026-10-08, ADR-0030): prekidači su "oduzmi popuste"
  > (bez popusta za članove) i "oduzmi popuste članstva" (popust za članove; pokrivena sesija → 0); prekidač i jedinična vrijednost
  > paketa su ukinuti (paket = cijena sesije); nadjačavanje po izvoru plaćanja je ukinuto; izvor pokrića se i dalje zapisuje uz
  > proviziju (informativno). Točke 5 i 7 vrijede (datum važenja, grupe fiksno po terminu).
  2. Osnovicu za `Percentage` određuju postavke organizacije (kao Vagaro payroll settings):
     "Oduzmi popuste od osnovice" (promo, tag, cijena za članove, uključuje cjenovnu pogodnost iz Q1) i
     "Oduzmi pokriće članarinom od osnovice". Svi isključeni = osnovica je **cijena sesije** bez oduzimanja popusta i pokrića, a
     cijena sesije je ručni iznos ako je upisan (i kad je viši od cjenika), inače cjenik = današnje ponašanje (ispravljeno 2026-10-08). **Default: oba isključena**
     (nijedan postojeći iznos se ne mijenja). Pokriće oduzeto i članarina pokriva cijelu sesiju → postotna provizija 0.
  3. Paket uz uključeno "oduzmi pokriće": osnovica = cijena paketa / broj jedinica (poznato pri kupnji); paket bez broja jedinica
     (neograničen) → osnovica 0, kao članarina. Zaseban ili zajednički prekidač za paket: otvoreno (P2_PLAN §13.3).
  4. Nadjačavanje po izvoru plaćanja (direktno / paket / članarina) na pravilu trener × usluga, s vlastitim načinom i vrijednošću
     (tipično: osnovno 20 % od cijene, za članarinu fiksno 5 € po sesiji).
  5. Povijest pravila s datumom od kad vrijedi; obračun koristi pravilo važeće u trenutku sesije.
  6. Svaka sesija trajno zapisuje izvor pokrića (tip + ID članarine/paketa), za nadjačavanje i izvještaje.
  7. Grupni treninzi ostaju kakvi jesu (fiksno po terminu); ništa od ovoga se na njih ne primjenjuje.
  - Faza **2F unutar P2**. Nema novog mehanizma obračuna, samo proširenje postojećeg (osnovica + nadjačavanje po izvoru). Treba
    ADR koji proširuje ADR-0010, bez promjene trenutka nastanka provizije.
- **Q29 Opseg poslovnica plana:** eksplicitno polje `CompanyScope: AllCompanies | SelectedCompanies` (+ lista).
  `SelectedCompanies` s praznom listom = validacijska greška; prazna lista NIKAD ne znači "sve". `AllCompanies` uključuje i
  buduće poslovnice. Ako su sve odabrane poslovnice deaktivirane: warning na planu, postojeća članstva po grandfathering
  pravilu kao kod paketa, nova prodaja nije moguća. Isti obrazac za druge domene je zasebna odluka (P2_PLAN §12.2).
- **Q30 Paket kao fallback kod Individual termina:** **eksplicitno na Completed**, paketi se ne mijenjaju. Precizira Q4: za
  Individual termine paket u lancu nije automatski, nego ga recepcija bira pri Completed; za Group vrijedi postojeći check-in.
  Warning pri rezervaciji: "limit članarine iskorišten, klijent ima prihvatljiv paket". Predodabir jedinog paketa je posao UI-a,
  backend ostaje eksplicitan. Tema za fazu online bookinga: automatska primjena paketa za Individual općenito.
- **Q31 Kredit kod kasnog otkaza/NoShowa:** **dimenzija P1 politike** `MembershipAction: ForfeitCredit | ReturnCreditChargeFee`
  po događaju, default `ForfeitCredit`, konzistentno s `PackageAction`.
  1. `ForfeitCredit` na planu bez kredita perioda → primjenjuje se P1 naknada (Q26).
  2. `ReturnCreditChargeFee` = storno cijelog claima (kredit + mjesto u limitima prozora) + P1 naknada. `ForfeitCredit` = claim
     ostaje (kredit i mjesto u limitu potrošeni).
  3. Waiver za članarinu = storno cijelog claima, bez naknade (kao otkaz na vrijeme).
  4. ~~`PercentOfCollected` provizija: pravilo nazivnika~~ (ukinuto Vagaro odlukom Q28; provizija kod kasnog otkaza/NoShowa je
     otvoreno pitanje, P2_PLAN §13.4). → **Odlučeno** Q38 (postavka organizacije, default Never).
  - **Precizirano** pregledom 2D (izbor 8, 2026-10-08): default `ForfeitCredit` vrijedi samo za događaj S naknadom; događaj bez
    naknade (FeeType None ili 0) dobiva `ReturnCreditChargeFee`, osim ako je organizacija izričito odabrala ForfeitCredit.

### Odluke o provizijama Q38 – Q42 i preciziranja Q28 (2026-10-07)
- **Ručno upisana cijena** = konačna cijena direktne naplate, NE popust (ne utječe na prekidač "oduzmi popuste"). Uz sesiju se
  zapisuje i cijena iz cjenika (`BaseAmount`), da izvještaji mogu pokazati ručna sniženja.
- **Pravilo provizije nije obavezno** (precizira Q28.1): provizija se dodjeljuje naknadno; **nema pravila = nema provizije** (kao
  danas). Dodjela usluge zaposleniku ne traži pravilo provizije, ni sada ni nakon Q37. **Nema migracije** na "bez provizije";
  postojeće stanje ostaje (**Q41 otpada**). "BezProvizije" postoji **samo kao vrijednost nadjačavanja po načinu plaćanja**
  (npr. osnovno 20 %, za članarinu bez provizije); osnovno pravilo ostaje `Percentage | Fixed`. → **Zamijenjeno** (Vagaro,
  2026-10-08): nadjačavanja nema; "Bez provizije" je izričit izbor na pravilu usluge. "Nema pravila = nema provizije" vrijedi.
- ~~**Prekidač za paket:** zaseban `commission_deduct_package_coverage` ("oduzmi pokriće paketom"), default isključen.~~
  **Zamijenjeno** (Vagaro, 2026-10-08): ukinut; sesija pokrivena paketom ima osnovicu cijenu sesije.
- **Q38 Provizija kod kasnog otkaza/izostanka:** postavka organizacije, **default: nikad** provizija za otkazane i neodrađene
  sesije (kao danas). Uz drugu vrijednost postavke provizija nastaje kad je P1 naknada plaćena (osnovica = naknada) i poništava se
  kod storna plaćanja ili oprosta (waiver) naknade.
- **Q39 Provizija na prodaju članarine:** u 2F, na **prvu prodaju**. Odvojen model od provizije za uslugu. Storno plaćanja
  prvog zaduženja poništava proviziju. Provizija na obnove = kasnija zasebna odluka.
  - **Korisnik provizije bira se proizvoljno pri naplati** (korisnikovo preciziranje, 2026-10-07): trener, recepcionar koji
    naplaćuje ili bilo koji drugi zaposlenik. Nije automatski ni prodavač ni naplatitelj (zamjenjuje "zaposleniku koji je prodao,
    ne onome koji je naplatio"). → **Zamijenjeno** općim pravilom provizije na prodaju (niže) i preciziranjem prije 2F
    (2026-10-08): korisnik se pamti na članstvu, stavke ga mijenjaju dok je checkout otvoren (do evaluacije prve prodaje).
  - "Storno plaćanja prvog zaduženja poništava proviziju" vrijedi, ali u P2 nema okidača (provizija nastaje na Complete, a storno
    uplate postoji samo u otvorenom checkoutu); okidač je povrat iz P3 i Q51(a).
- **Q40 Razina postavki osnovice:** samo organizacija; nadjačavanje po treneru moguće kasnije.
- **Q42 Osnovica provizije na prvu prodaju:** stvarno plaćeno (nakon popusta) za **prvi period + početnu naknadu**. Provizija
  nastaje kad su **oba zaduženja konačna** (plaćena ili otpisana; otpisano = 0 u osnovici), da oproštena početna naknada ne
  blokira proviziju.

### Odluke Q2, Q3, Q10 (2026-10-07)
- **Q2 Cjenovna pogodnost članarine:** samo na sesije koje klijent stvarno plaća (usluge izvan plana, sesije iznad limita na
  normalnoj naplati). NE na sesije pokrivene članarinom NI na sesije pokrivene paketom.
- **Q3 Prvi period i način obnove** (svaki plan bira način obnove):
  1. **Od datuma kupnje:** period od datuma početka do istog dana sljedećeg mjeseca (npr. 28.10.–28.11.), puni iznos, bez
     proporcije. Ako dan ne postoji u mjesecu (29., 30., 31.), obnova je zadnji dan tog mjeseca, a zatim se vraća na izvorni dan
     gdje postoji.
  2. **Kalendarski mjesec:** prvi period traje do kraja tekućeg mjeseca, **puni iznos**, **puni krediti** (bez proporcije);
     dnevni/tjedni limiti nepromijenjeni.
  3. Pri prodaji se bira datum početka (danas ili kasniji, npr. 1. sljedećeg mjeseca kod kalendarskih planova). Ograničenje
     unaprijed: P2_PLAN §14.2.
- **Q10 Više članarina:** preklapanje je zabranjeno, `MEMBERSHIP_OVERLAPPING_COVERAGE`.
  1. Preklapanje = razdoblja važenja se preklapaju I dijele barem jednu uslugu u barem jednoj poslovnici.
  2. Dopušteno: nova članarina s početkom nakon zakazanog kraja postojeće.
  3. Dopušteno: istovremene članarine s različitim uslugama (npr. grupni + personalni).
  4. Postojeća bez zakazanog kraja → svaka nova s preklapajućim uslugama se odbija; greška navodi članarinu s kojom se preklapa.
  5. Promjena plana iste članarine (od sljedećeg ciklusa) nije "druga članarina" i ne prolazi ovu provjeru.

### Odluke Q5/Q12, Q14, Q15, Q19 (2026-10-07)
- **Q5/Q12 Pauza:** pravila na **planu** (dopuštena, max dana, max puta, produljuje li period).
  1. Produljenje po danima samo za planove "od datuma kupnje". Kod planova "kalendarski mjesec" pauza je dopuštena samo u
     **cijelim periodima** (preskok: period se ne otvara, nema zaduženja), da obnova ostane 1. u mjesecu.
  2. Bez retroaktivne pauze u P2: početak je danas ili kasnije.
  3. Pauza nije dopuštena dok članstvo ima dug nakon isteka grace perioda.
  4. "Max puta godišnje" = u 12 mjeseci od početka članstva (rolling), ne kalendarska godina.
  5. Postojeće rezervacije u pauzi postaju nepokrivene (storno claima), recepcija dobiva popis pogođenih termina, termini se
     NE otkazuju automatski.
  6. Prijevremeni završetak pauze: iskorišteni dani se broje u limit, kraj perioda se skraćuje na stvarno trajanje pauze.
     Termini od povratka do planiranog kraja pauze ponovno dobivaju pokriće (Q27 mehanizam).
- **Q14 Izmjena plana:** bira se pri svakoj izmjeni: **"samo nove prodaje" (default)** ili "i postojeća članstva".
  1. "I postojeća": nove uvjete članstvo dobiva tek pri prvoj obnovi koja je najmanje X dana nakon izmjene (X = postavka
     organizacije, npr. 30 dana najave). Tekući period uvijek ostaje po starim uvjetima.
  2. Uz izmjenu "i postojeća" sustav vraća popis pogođenih članstava (obavijest klijentima nije u P2).
  3. Članstvo uvijek čuva snapshot uvjeta koji za njega vrijede; izmjena plana nikad ne mijenja snapshot retroaktivno.
  4. Strogo povoljne izmjene: Q48.
- **Q15 Dug:** default organizacije **grace 7 dana, "ne pokrivaj"**.
  1. Kad grace istekne, claimovi za **buduće** termine se oslobađaju (storno), ti termini postaju nepokriveni (normalna naplata),
     uz popis pogođenih termina za recepciju. Prošli termini ostaju kako jesu.
  2. Kad se dug plati, pokriće se ponovno primjenjuje na buduće nepokrivene termine mehanizmom iz Q27 (redom po vremenu, do
     limita). Termini koji su u međuvremenu naplaćeni ostaju naplaćeni.
  3. Stanje pokrića se izvodi iz **najstarijeg** neplaćenog zaduženja nakon grace: dok postoji, članarina ne pokriva ni u novim
     periodima.
  4. Isto za opciju "blokiraj rezervaciju": blokira nove, a buduće postojeće postaju nepokrivene (ne otkazuju se automatski).
- **Q19 Aktivacija:** za prodaju na recepciji **odmah** od datuma početka, i kad prvo zaduženje (i početna naknada) još nije
  plaćeno; neplaćeno prvo zaduženje ide kroz grace i pravila duga (Q15).
  1. Ako se članarina otkaže ili istekne, a prvo zaduženje nikad nije plaćeno, dug ostaje; studio ga naplaćuje ili otpisuje.
  2. Budući kanal online kupnje: aktivna tek nakon uspješnog prvog plaćanja. Ne implementira se sada, ali članstvo pamti kanal
     prodaje (`sold_via`) i pravilo aktivacije se razrješava po kanalu.

### Odluke Q47, Q48, Q17, Q13 (2026-10-07)
- **Q47 Prijevremeni povratak iz pauze kod kalendarskog plana:** preskočeni period se otvara od dana povratka do kraja mjeseca,
  **puni iznos i puni krediti** (kao Q3); pauza se broji kao jedno korištenje. **Nije automatski:** recepcija potvrđuje uz jasno
  upozorenje (npr. "Otvara se period 15.11.–30.11., zaduženje 50 €"). Klijent i dalje može platiti termine pojedinačno do 1. u
  mjesecu, bez prekida pauze.
- **Q48 Strogo povoljne izmjene plana:** sustav ih prepoznaje automatski i one vrijede od sljedećeg perioda **bez roka najave**.
  1. Uspoređuju se SVI uvjeti koje klijent osjeti: cijena, usluge, poslovnice, limiti, interval i način obnove, pravila pauze
     (max dana, max puta), otkazni rok, minimalno trajanje obveze, cjenovna pogodnost za članove.
  2. Izmjena koja se ne može jednoznačno svrstati u povoljnu je **mješovita** (rok najave X dana).
  3. Pri spremanju izmjene odgovor vraća klasifikaciju (povoljna / mješovita) i od kad vrijedi za postojeća članstva.
- **Q17 Prozori limita:** **kalendarski** (dan, tjedan pon–ned, kalendarski mjesec, kvartal) u zoni poslovnice termina. Termin
  pripada prozoru po vremenu **termina**, ne rezervacije. Validacija kombinacije prozora i kredita perioda: P2_PLAN §15.4
  (korisnikova preporuka (a), u razradi). → **Odlučeno** Q49.
- **Q13 Zajednički limit:** limit na razini plana (bez usluge) vrijedi za sve pokrivene usluge zajedno.
  1. Limit plana i limiti po usluzi kombiniraju se kao AND: termin je pokriven samo ako ima mjesta u SVIM primjenjivim limitima.
  2. Jedan claim troši sa svih primjenjivih brojača odjednom; storno vraća na iste brojače.
  3. Limiti plana mogu biti i prozori (npr. max 1 dolazak dnevno ukupno), uz istu validaciju kao limiti po usluzi.
  4. Warning kod iskorištenog limita (Q4) kaže KOJI je limit iskorišten (ukupni ili za uslugu).

### Odluke Q49, Q16, Q20/Q24, Q43 (2026-10-07)
- **Q49 Validacija prozora:** pravilo po duljini prozora u odnosu na period (P2_PLAN §15.4): kraći prozor uz limit manji od
  kredita, iste duljine zabranjen, dulji samo uz limit veći od kredita.
  1. Plan **bez** kredita perioda: svi prozori dopušteni, bez ove provjere.
  2. Usporedba s kreditima istog opsega: prozor usluge s kreditima te usluge ako postoje, inače s kreditima plana; prozor plana s
     kreditima plana.
  3. Warning "bez učinka" i kad je dulji prozor ≥ kredit perioda × broj perioda u prozoru (npr. 30 kvartalno uz 8 mjesečno).
- **Q16 Status zaduženja:** **izveden iz plaćanja**; spremljeno samo `Open | WrittenOff | Voided`.
  1. `PartiallyPaid` se za pravila duga (Q15) tretira kao neplaćeno: grace i gubitak pokrića vrijede dok nije `Paid` ili
     `WrittenOff`.
  2. "Konačno" zaduženje (Q42 i ostala pravila) = `Paid` ili `WrittenOff`. Storno alokacije vraća izvedeni status (npr.
     Paid → Open) i poništava provizije koje su o tome ovisile.
  3. Za brze upite dopuštena je denormalizirana projekcija statusa, ažurirana u istoj transakciji kao alokacija; izvor istine
     ostaju alokacije (prijedlog: P2_PLAN §16.1).
- **Q20/Q24 Prodaja i plaćanje:** **naredba prodaje + plaćanje kroz checkout** (stavka `MembershipCharge`), uključujući
  djelomično plaćanje.
  1. UI nakon naredbe prodaje odmah otvara checkout s dodanim zaduženjima (prvi period + početna naknada): jedan tok na recepciji.
  2. Prodavač / korisnik provizije na prodaju zapisuje se na članstvo u naredbi prodaje, ne u checkoutu. (Precizirano prije 2F,
     2026-10-08: članstvo ostaje jedini izvor, ali ga stavke `MembershipCharge` mogu promijeniti dok je checkout otvoren.)
  3. Isti tip stavke plaća i zaduženja obnove; stavke se mogu kombinirati s ostalima u istom checkoutu.
  4. **Poništavanje prodaje:** dopušteno samo ako nema nijedne alokacije plaćanja i nijednog claima → zaduženja `Voided`, članstvo
     `Voided` (zaseban status od otkazanog). Inače samo regularni otkaz + otpis. Tko smije: P2_PLAN §16.2.
     → **Zamijenjeno** uvjetom Q51.1 (nema AKTIVNIH alokacija) i odlukom 2D (2026-10-08, izbor 12): blokira samo stvarno korištenje
     (aktivna uplata, claim na sesiji koja je počela ili je odrađena, propali kredit); claimovi budućih termina se vraćaju uz
     upozorenje `MEMBERSHIP_VOIDED_SESSIONS_UNCOVERED`.
- **Q43 Korisnik provizije na prodaju:** bira se **pri prodaji**, izmjenjiv dok provizija ne nastane. (Točka 1 zamijenjena
  općim pravilom provizije na prodaju, a izvor i promjena preciziranjem prije 2F, 2026-10-08.) → **Precizirano** (pregled 2F):
  izmjenjiv do jednokratne evaluacije prve prodaje (ne samo do nastanka provizije); nakon evaluacije bez korisnika samo naknadna
  dodjela uz `commissions.manage` + razlog. Default prijedloga = AKTIVAN zaposlenik koji izvršava prodaju (točka 1 time vrijedi
  za članarinu); neaktivan zaposlenik se ne može odabrati.
  1. Default = zaposlenik koji izvršava naredbu prodaje; može se odabrati drugi.
  2. Svaka promjena se bilježi (tko, kada, s koga na koga).
  3. Nakon nastanka provizije promjena samo kao eksplicitna korekcija (storno + nova provizija), uz ograničen grant (P2_PLAN §16.3).

### Odluke: minimalna obveza i otkazni rok, Q22, Q45, Q46 (2026-10-07)
- **Minimalna obveza i otkazni rok** (bivši nepotvrđeni prijedlog): oba u P2 kao opcionalna polja plana, bez penala. Otkaz
  djeluje na `max(kraj tekućeg perioda, kraj minimalne obveze, prvi kraj perioda nakon isteka otkaznog roka)`.
  1. Minimalna obveza broji se u periodima koji NISU bili u pauzi (pauza produljuje obvezu).
  2. Ručno nadjačavanje datuma završetka (raniji izlazak bez penala): dopušteno uz ovlast, obavezan razlog, bilježi se tko i kada
     (grant: P2_PLAN §17.1).
  3. Pri otkazu odgovor vraća izračunati datum od kad otkaz djeluje i razlog (otkazni rok / minimalna obveza / kraj perioda).
  4. Dulja minimalna obveza ili dulji otkazni rok pri izmjeni plana = nepovoljna izmjena (Q48).
- **Q22 Zona za scheduler:** **zona organizacije** za obnovu, istek grace perioda i kraj pauze.
  1. Pripadnost termina periodu (Q6): granice perioda u zoni organizacije; vrijeme termina se pretvara u tu zonu prije usporedbe.
     Prozori limita (Q17) ostaju u zoni poslovnice termina.
  2. Scheduler računa s lokalnim DATUMIMA (ne UTC trenutkom), ispravno preko DST prijelaza, idempotentan (ponovno pokretanje
     istog dana ne otvara period ni zaduženje dvaput).
- **Q45 Datum početka:** ne u prošlosti, najviše mjesec dana unaprijed; do početka članstvo je `Scheduled`.
  1. Zaduženje prvog perioda nastaje pri prodaji (može se platiti odmah), ali grace i pravila duga (Q15) računaju se od DATUMA
     POČETKA.
  2. `Scheduled` članstvo: termini od datuma početka mogu dobiti pokriće već prije početka, istom logikom kao Q27 (prvi + sljedeći
     period).
  3. Odustajanje prije početka bez plaćanja i bez korištenja = poništavanje (Q24.4), ne otkaz; minimalna obveza i otkazni rok se
     ne primjenjuju. Ako je plaćeno: P2_PLAN §17.2 (Q51).
- **Q46 Promjena plana koja se preklapa s drugom članarinom:** odbija se pri zakazivanju (`MEMBERSHIP_OVERLAPPING_COVERAGE`).
  1. Provjera je vremenska kao Q10: od datuma stupanja promjene na snagu; ako druga članarina završava prije, promjena je dopuštena.
  2. Provjera preklapanja pri PRODAJI (Q10) uzima u obzir i zakazane promjene plana postojećih članarina (od datuma stupanja na
     snagu).
  3. Sigurnosna provjera pri obnovi: ako se preklapanje ipak pojavi, promjena se ne primjenjuje, ostaje stari plan, recepcija dobiva
     obavijest.

### Odluke Q18, Q50, Q51 i opće pravilo provizije na prodaju (ispravak Q43/Q44) (2026-10-07)
- **Q18 Generiranje grupe uz postavku duga "blokiraj rezervaciju":** član u dugu se **preskače** u generiranim terminima dok je
  dug aktivan (poštuje se odluka organizacije).
  1. Član NE gubi mjesto u grupi (članstvo u grupi ostaje); preskače se samo sudjelovanje u pojedinim terminima.
  2. Kad se dug plati, generiranje se nastavlja za buduće termine; naknadno generiranje za već generirane preskočene termine:
     prijedlog P2_PLAN §18.2 (Q53).
  3. Recepcija može ručno dodati člana u pojedini termin unatoč blokadi (grant: Q54); takvo sudjelovanje je bez pokrića.
  4. Recepcija dobiva obavijest/popis preskočenih članova po terminu.
  5. Uz "ne pokrivaj" i "nastavi pokrivati" generiranje radi normalno (bez pokrića odnosno s pokrićem).
  - Namjerna promjena ponašanja: F-23 karakterizacija (generiranje reproducira sve članove) mijenja se SAMO za ovaj slučaj → ADR.
- **Q50 Korekcija korisnika provizije nakon nastanka:** grant `commissions.manage`, obavezan razlog, bilježi se (Q43). Ako je
  izvorna provizija u zatvorenom ili isplaćenom obračunu, storno i nova provizija ulaze u SLJEDEĆI obračun, a zatvoreni se ne
  mijenja. Danas taj koncept ne postoji (P2_PLAN §18.3).
- **Q51 Plaćeno pa odustajanje prije početka:** recepcija bira (a) storno uplate + poništavanje ili (b) regularni otkaz.
  1. Uvjet poništavanja prodaje (Q24.4) postaje: nema **aktivnih** alokacija plaćanja i nema claimova.
  2. Kod (a) provizija na prodaju (Q42) se poništava; ako je u zatvorenom obračunu, korekcija ide u sljedeći (Q50).
  3. Kod (b) članarina traje točno prvi plaćeni period i ne obnavlja se; minimalna obveza i otkazni rok se ne primjenjuju;
     provizija na prodaju ostaje.
  4. Djelomični povrat nije u P2.
- **Opće pravilo provizije na prodaju (ispravak Q43 i Q44):**
  1. Za SVAKU stavku koja se naplaćuje (članarina, paket, usluga, proizvod, bilo što) na naplati se može odabrati kome ide
     provizija na prodaju. **Default = zaposlenik koji naplaćuje.** Vrijedi jednako za sve vrste stavki. → **Precizirano** (pregled 2F,
     izbor 6, potvrđeno): default je AKTIVAN zaposlenik korisnika koji DODAJE stavku; korisnik bez zaposlenika → prazno; neaktivan
     zaposlenik se ne može odabrati.
  2. Provizija na prodaju i provizija za odrađeno su DVIJE ODVOJENE provizije i mogu postojati na istoj stvari: na prodaju ide
     odabranom zaposleniku na naplati; za odrađeno ide izvođaču sesije po pravilu trener × usluga (Q28).
  3. Članarina: naredba prodaje predlaže korisnika provizije (default prodavač), a konačni odabir je na stavci `MembershipCharge`
     u checkoutu. Kod djelomičnog plaćanja kroz više checkouta vrijedi odabir važeći u trenutku nastanka provizije (Q42: kad su
     zaduženja konačna). Promjene se bilježe; nakon nastanka provizije samo korekcija uz `commissions.manage` (Q50).
     (Zamjenjuje Q43 "default = izvršitelj naredbe, odabir pri prodaji".) → **Precizirano prije 2F (2026-10-08):** nema
     zasebnog odabira po stavci; jedini izvor je članstvo, a promjena na bilo kojoj stavci `MembershipCharge` mijenja vrijednost na
     članstvu. Pri nastanku provizije uzima se vrijednost s članstva.
  4. Q44 više nije "izvan P2". Opseg po vrstama stavki i provizija na prodaju usluge: prijedlog P2_PLAN §18.1 (Q52).

### Završne odluke dizajna: Q52 – Q54 i potvrde §18 (2026-10-07)
- **Brojanje provizija po događajima** (2F): zarada se broji u razdoblju nastanka (`EarnedAt`), storno kao negativan iznos u
  razdoblju storna (`ReversedAt`). Pregled za prošlo razdoblje se nikad ne mijenja naknadno.
- **Provizija na prodaju:** polje "kome ide provizija na prodaju" na svakoj stavci checkouta (default naplatitelj → **precizirano**:
  zaposlenik koji dodaje stavku, vidi gore), oznaka pravila
  `Performance | Sale`, migracija bez promjene iznosa.
- **Storno uplate poništava proviziju na prodaju**, i za proizvode i pakete. **Namjerna promjena ponašanja** za postojeće
  korisnike: dosad se provizija na prodaju nikad nije poništavala.
- **Q18:** ADR i izmjena karakterizacijskog testa F-23 samo za slučaj blokade duga.
- **Q52:** proizvodi i paketi — mijenja se samo tko dobiva; prva prodaja članarine — nova provizija; obnove i usluge — odabir se
  sprema, provizija kasnije. Za usluge mogu postojati obje provizije, bez posebne postavke (nema pravila = nema provizije).
  Provizija na prodaju usluga i obnova = zasebna odluka nakon 2F (osnovica i trenutak).
- **Q53:** nakon plaćanja duga automatsko dodavanje u preskočene buduće termine redom po datumu termina, dok ima mjesta; puni
  termini idu na popis recepciji.
- **Q54:** zasebni grant `appointments.membership-block.override`. Default dodjela po ADR-0023: migracija ga dodaje samo
  sistemskim Admin grupama (`system_key = 'admin'`), ostale grupe ga ne dobivaju (studio ga može dodijeliti recepciji ručno).
- **Q37** ostaje izvan P2.
- **Dizajn P2 je zatvoren (2026-10-07).** Implementacija kreće fazom 2A.

### Provjera provizija prije 2F (2026-10-08)
Korisnikov sažetak konačnih odluka o provizijama provjeren je prema P2_PLAN §13/§18 i ovom zapisu: sadržajno se slaže, a zastarjeli
tekst je označen kao zamijenjen (plan §13.1, §13.4, §13.5, §14.1, §16.3, §18.1; ovdje Q28.1, Q39, Q20/Q24.2, Q43). Odluke:
- **Osnovica uz sve prekidače isključene** = cijena sesije bez oduzimanja popusta i pokrića. Cijena sesije = ručni iznos ako je
  upisan (i kad je viši od cjenika), inače cjenik. Cjenikovna cijena (`BaseAmount`) se pamti uz sesiju za izvještaje.
- **Q38 uz postavku "samo kad je naknada plaćena":** primjenjuje se pravilo trener × usluga važeće na datum sesije s
  nadjačavanjem **Direct** (i kad je izvorna sesija bila pokrivena članarinom ili paketom, jer se naknada plaća novcem).
  → **Zamijenjeno** (Vagaro, 2026-10-08): nadjačavanja nema; vrijedi isto pravilo kao za sesiju (pravilo usluge, inače opće pravilo
  zaposlenika; "Bez provizije" = ništa). Ostatak (postotak od naknade, Fixed do iznosa naknade, puna uplata, storno/oprost) vrijedi.
  `Percentage` = postotak od naknade; `Fixed` = fiksni iznos ograničen na iznos naknade (provizija nikad veća od naplaćenog).
  Nastaje tek kad je naknada plaćena **u cijelosti**. Storno uplate ili oprost naknade poništava proviziju (negativan iznos u
  razdoblju storna).
- **Provizija na prvu prodaju članarine (Q42) — trenutak i korisnik:**
  1. Korisnik provizije ima **jedan izvor: članstvo** (prijedlog iz naredbe prodaje, Q43). Stavke `MembershipCharge` ga prikazuju
     i mogu ga promijeniti dok je checkout otvoren; promjena na bilo kojoj stavci mijenja ga na članstvu. Pri nastanku provizije
     uzima se vrijednost s članstva.
  2. Provjera "oba zaduženja konačna" radi se pri svakom **Checkout Complete** i pri **otpisu**. Provizija nastaje kad se uvjet
     prvi put ispuni, jednom (i kad su zaduženja plaćena u različitim checkoutima).
  3. Oba zaduženja otpisana bez ikakve uplate → osnovica 0 → provizija **ne nastaje** (nema zapisa s iznosom 0).
  4. Nakon nastanka promjena korisnika samo uz `commissions.manage` + razlog (Q50).
- **Datum važenja vrijedi i za prodajna pravila** (proizvod, paket, plan članarine): pravilo se bira po lokalnom datumu
  **nastanka provizije**. Posljedica: kod članarine (i djelomično plaćenih stavki) ne vrijedi datum prodaje; ako se pravilo
  promijeni između prodaje i konačne naplate, primjenjuje se novo.
- **Napomena (bez odluke):** provizije na prodaju (proizvod, paket, prva prodaja članarine) nastaju na Checkout Complete, a
  `VoidPayment` postoji samo u otvorenom checkoutu, pa "storno uplate poništava proviziju na prodaju" u 2F nema putanju u kodu.
  Poništavanje se gradi kao zajednički put (poziva ga budući povrat iz P3, §22, i poništavanje prodaje Q51a), a u 2F se izvršava
  kroz korekciju korisnika (Q50) i Q38 (oprost/storno naknade).
- Unique pravila: `(employee, kind, subject_type, subject_id, effective_from)` (usklađuje §13.1 i §18.1).

### Nepotvrđeni prijedlozi
- (nema)

## Implementacija

### Faza 2A — katalog planova (2026-10-07)
- **ADR-0025**; migracije `20261027000000` (`P2MembershipPlans`) i `20261027000001` (`P2AdminGrants`), primijenjene lokalno.
- Tablice `membership_plans`, `membership_plan_versions`, `membership_plan_version_services`,
  `membership_plan_version_companies`, `membership_plan_usage_limits` (sve s `organization_id`; CHECK-ovi za interval/način
  obnove, pauzu po načinu obnove, iznose i limite).
- `MembershipPlanService` + `MembershipPlanHandler`, pravila u `Utils/MembershipPlanRules` (Q3, Q5/Q12, Q13, Q17, Q29, Q49,
  2A pravila). API `/api/membership-plans`: GET, GET `{id}`, POST, PATCH `{id}/details`, PUT `{id}/capacity`,
  POST `{id}/versions`, POST `{id}/activate`, POST `{id}/deactivate`.
- Grantovi `catalog.memberships.view/.manage` (+ capability `catalog.memberships.manage`, osjetljivost Sensitive).
- Kodovi: `MEMBERSHIP_PLAN_COMPANIES_REQUIRED` (400), `MEMBERSHIP_USAGE_LIMIT_INVALID` (400); upozorenja
  `MEMBERSHIP_LIMIT_WITHOUT_EFFECT`, `MEMBERSHIP_PLAN_NO_ACTIVE_COMPANY` (računaju se pri čitanju plana).
- Verzije planova blokiraju trajno brisanje usluga i poslovnica (`ServiceHandler`/`CompanyHandler.IsReferenced`).
- Pregled 2A (2026-10-07): dopuštena pauza ima obavezno najveće trajanje (max dana / max periodi), migracija
  `20261027000002` (`ck_membership_plan_versions_pause_limit_required`); "max puta u 12 mjeseci" smije ostati prazno.
- Implementacijski izbori (potvrđeni u pregledu 2A): prazan "max puta u 12 mjeseci" = bez ograničenja broja pauza; minimalna obveza i
  otkazni rok moraju biti ≥ 1 ili prazni (0 nije dopušten da nema dva zapisa za "nema"); `MaxActiveMemberships` je na profilu
  plana (kapacitet prodaje, nije uvjet koji klijent osjeti) i mijenja se zasebnom naredbom; nova verzija u 2A vrijedi samo za
  nove prodaje, a `apply_to` i klasifikacija izmjene (Q14/Q48) dolaze u 2B zajedno s članstvima.
- Testovi: `UnitTests/Memberships/` (pravila, servis nad bazom, HTTP ugovor).

### Faza 2B — članstva i lifecycle (2026-10-07)
- **ADR-0026**; migracije `20261027000003` (`P2ClientMemberships`: članstva, pauze, povijest, `organization_settings.
  membership_change_notice_days`) i `20261027000004` (`P2MembershipGrants`), primijenjene lokalno.
- `ClientMembershipService` + `ClientMembershipHandler`; čista pravila `MembershipPeriodCalendar`, `MembershipLifecycleRules`,
  `MembershipCoverageOverlap`, `MembershipPlanChangeClassifier`, zajedničko `MembershipPlanReadModel` i `MembershipTimelines`.
- API: `GET/POST /api/clients/{clientId}/memberships`, `GET /api/memberships/{id}`, `GET .../cancellation-preview`,
  `POST/DELETE .../cancellation`, `POST .../pauses`, `POST .../pauses/{pid}/end-early`, `POST .../pauses/{pid}/cancel`,
  `POST/DELETE .../plan-change`, `POST .../end`, `POST .../void-sale`; `POST /api/membership-plans/{id}/versions` sada prima
  `applyTo` i vraća klasifikaciju i pogođena članstva; `PUT /api/organization/settings/membership-change-notice`.
- Grantovi: `clients.memberships.view/.sell/.cancel/.pause/.plan-change/.end-override/.void-sale`, `catalog.memberships.deactivate`
  (aktivacija/deaktivacija plana više nije pod `.manage`). Capabilityji: `clients.memberships.manage` (pregled + otkaz, pauza,
  promjena plana), `clients.memberships.sell`, `.void-sale`, `.end-override`, `catalog.memberships.deactivate`.
- Kodovi: `MEMBERSHIP_PLAN_INACTIVE`, `_PLAN_FULL`, `_PLAN_NO_ACTIVE_COMPANY`, `_OVERLAPPING_COVERAGE`,
  `_START_DATE_OUT_OF_RANGE`, `_NOT_ACTIVE`, `_END_ALREADY_SCHEDULED`, `_NO_SCHEDULED_CANCELLATION`, `_PAUSE_NOT_ALLOWED`,
  `_PAUSE_LIMIT_EXCEEDED`, `_PAUSE_NOT_PENDING`, `_END_DATE_OUT_OF_RANGE`, `_PLAN_CHANGE_NOT_ALLOWED`,
  `_NO_SCHEDULED_PLAN_CHANGE`; upozorenja `MEMBERSHIP_PLAN_NOT_VALID_AT_SALE_COMPANY`, `MEMBERSHIP_SCHEDULED_PAUSE_CANCELLED`.
- Ograničenja 2B (dolaze u 2C/2D): nema zaduženja ni redaka perioda (prodaja ne stvara zaduženje; poništavanje ne provjerava
  alokacije; pauza ne provjerava dug); nema claimova (otkaz, pauza i raniji izlazak još ne oslobađaju pokriće); zakazane
  promjene uvjeta primjenjuje obnova u 2C.
- **Implementacijski izbori (bez nove poslovne odluke; navedeni za potvrdu):**
  1. ✅ POTVRĐENO uz dopunu (pregled 2B): izmjena plana uz "i postojeća članstva" se NE primjenjuje na članstvo kojem bi
     stvorila preklapanje (Q10); članstvo dobiva TRAJNU oznaku (verzija + razlog), vidljivu na članstvu (popis i profil klijenta)
     i u popisu za admina (`GET /api/memberships/plan-update-not-applied`), dok je kasnija uspješna izmjena ne riješi. Bez
     automatske ponovne primjene.
  2. ✅ POTVRĐENO: zakazani datum promjene uvjeta je donja granica: promjena stupa na prvoj obnovi na ili nakon njega (pauza može pomaknuti
     obnovu).
  3. ❌ ODBIJENO i promijenjeno (pregled 2B): povlačenje klijentove promjene plana vraća stanje kao da promjene nije bilo —
     istisnuta izmjena plana vraća se s IZVORNIM datumom (rok najave od objave verzije); ako je ta obnova prošla, stupa pri
     prvoj sljedećoj obnovi. Izmjena objavljena dok klijentova promjena čeka pamti se na isti način. Migracija `20261027000005`.
  4. ✅ POTVRĐENO: datum otkaza (i gornja granica ranijeg izlaska) ne računa pauze koje još nisu počele, jer ih otkaz poništava.
  5. ✅ POTVRĐENO: raniji povratak na prvi dan pauze = povlačenje pauze (ne troši limit).
  6. ✅ POTVRĐENO (pregled 2B): pauza nije dopuštena uz bilo koji zakazani završetak (otkaz ili raniji izlazak).
  7. ✅ POTVRĐENO uz dopunu: rok najave izmjene plana 0–365 dana, default 30; rok kraći od 14 dana vraća upozorenje `MEMBERSHIP_CHANGE_NOTICE_SHORT` (nepovoljne izmjene bez ili s kratkom najavom).
  8. ❌ ODBIJENO i promijenjeno: promjena plana provjerava kapacitet ciljnog plana (`MEMBERSHIP_PLAN_FULL`) — broje se članstva na planu na datum stupanja promjene (bez odlazaka do tog datuma) i SVA zakazana dolaženja klijentovom promjenom (konzervativno); isto brojanje vrijedi i za prodaju (na datum početka). Ciljni plan se zaključava kao pri prodaji.
  9. ✅ POTVRĐENO: ranije zakazana pauza nakon ranijeg izlaska se poništava; pauza u tijeku završava s članstvom.
- Testovi: `UnitTests/Memberships/` (matematika perioda, pravila lifecycla, preklapanje, klasifikator s testom refleksijom,
  servis nad bazom, HTTP ugovor po grantu).

### Faza 2C — periodi, zaduženja, obnova (2026-10-07)
- **ADR-0027**; migracije `20261027000006` (`P2MembershipCharges`: periodi, zaduženja, stavka checkouta `MembershipCharge`,
  pravila duga organizacije, `terms_anchor_on`, razlog `NonPayment`) i `20261027000007` (`P2ChargeWriteOffGrant`), primijenjene
  lokalno.
- `MembershipRenewalService` (jedina putanja koja otvara periode i zaduženja) + `MembershipRenewalBackgroundService`
  (`MembershipRenewalSettings`: uključeno, interval 30 min; u testnom API hostu isključeno), `Utils/MembershipChargeSettlement`
  (plaćenost iz alokacija, stanje duga, neplaćeni periodi), projekcija plaćenosti (`RefreshChargeSettlement`) iz
  `CheckoutService.RecordPayment/VoidPayment`.
- API: `GET /api/memberships/{id}/periods`, `GET /api/memberships/{id}/charges`, `POST /api/membership-charges/{id}/write-off`
  (`memberships.charges.write-off`), `POST /api/checkouts/{id}/items/membership-charge` (`checkout.manage`),
  `GET /api/membership-plans/{id}/memberships-ending`, `PUT /api/organization/settings/membership-debt`; članstvo nosi
  `Standing` i `OutstandingAmount`; deaktivacija plana vraća upozorenje `MEMBERSHIP_PLAN_MEMBERSHIPS_ENDING` s brojem članstava.
- Kodovi: `MEMBERSHIP_CHARGE_NOT_OPEN`, `MEMBERSHIP_CHARGE_IN_OPEN_CHECKOUT`, `MEMBERSHIP_SALE_HAS_PAYMENTS`, `MEMBERSHIP_DELINQUENT`.
- **Implementacijski izbori (bez nove poslovne odluke; za potvrdu):**
  1. ✅ POTVRĐENO: plan s cijenom 0 ne stvara zaduženje perioda (zaduženje uvijek ima iznos > 0); početna naknada > 0 i tada stvara zaduženje (test).
  2. ✅ POTVRĐENO (sučelje neplaćeno novo članstvo prikazuje kao "čeka plaćanje"): dospijeće zaduženja perioda = početak perioda; početne naknade = datum početka članstva. Novo prodano članstvo je zato odmah
     `InGrace` dok se prvo zaduženje ne plati.
  3. ✅ POTVRĐENO: redoslijed na granici obnove: automatski završetak zbog neplaćanja → primjena zakazanih uvjeta → provjera aktivnosti plana
     (klijentov prelazak na aktivan plan "spašava" članstvo deaktiviranog plana).
  4. ✅ POTVRĐENO: "neplaćen period" za automatski završetak broji se na dan obnove (zaduženja perioda koja nisu konačna nakon grace perioda).
  5. ✅ POTVRĐENO: otpis je moguć samo dok zaduženje nije stavka otvorenog checkouta; otpisuje se preostali dug, plaćeni dio ostaje.
  6. ❌ ODBIJENO i promijenjeno: poništavanje prodaje prebacuje SVA zaduženja (i otpisana) u Voided; zapis otpisa ostaje u povijesti zaduženja; izvještaj otpisa broji samo lifecycle WrittenOff. Migracija `20261027000008`.
  7. ✅ POTVRĐENO za P2: stavka `MembershipCharge` traži istog klijenta kao checkout; poslovnica nije ograničena. Platitelj ≠ član = kasnija zasebna odluka.
  8. ❌ ODBIJENO (dio o obvezi) i promijenjeno: kraj minimalne obveze nakon promjene uvjeta = kasniji od (dosadašnji kraj obveze, datum promjene + obveza novog plana); `commitment_from_on` + `commitment_floor_on`, migracija `20261027000008`, test. Sidro granica (`terms_anchor_on`) ostaje: nakon promjene uvjeta s drugačijim načinom obnove ili intervalom granice se računaju od
     datuma promjene; minimalna obveza novih uvjeta broji se od tog datuma.
- Ograničenja: storno uplate postoji samo u otvorenom checkoutu (`VoidPayment`); outbox događaji se ne pišu do P4; pokriće (2D)
  još ne postoji.
- Testovi: `UnitTests/Memberships/MembershipBillingTests` (11) + HTTP ugovor za zaduženja i otpis.

### Faza 2D — pokriće članarinom (2026-10-08)
- **ADR-0028**; migracije `20261027000009` (`P2MembershipCoverage`: ledger `membership_usages`, projekcija
  `participation_membership_coverages`, `MembershipAction` u verziji politike i snapshot na posljedici,
  `organization_settings.membership_limit_exceeded_behavior`) i `20261027000010` (`P2MembershipDebtBlock`: grant
  `appointments.membership-block.override` samo Admin grupama, `group_occurrence_membership_skips`), primijenjene lokalno.
- `IMembershipCoverageService` (jedina putanja: claim/storno/projekcija, usklađivanje strane članstva, oslobođeno mjesto),
  `Utils/MembershipCoverageRules` (čista evaluacija), `Utils/MembershipCoverages` (čitanje projekcije), `IGroupMembershipSkipService`
  (Q18 popis, Q53 naknadno dodavanje). Kuke u svim ulaznim točkama sudjelovanja i naredbama članstva (ADR-0028).
- API: `PUT /api/organization/settings/membership-coverage` (`organization.settings.manage`), `GET /api/groups/{id}/membership-skips`
  (`groups.view`); `BookingParticipationDto.MembershipCoverage`; posljedica politike nosi `MembershipAction`, `ClientMembershipId`,
  `MembershipCreditForfeited`; pravilo događaja politike prima opcionalni `MembershipAction`; rezultat generiranja grupe nosi
  `MembershipSkips`.
- Kodovi: `MEMBERSHIP_LIMIT_EXCEEDED`, `MEMBERSHIP_COVERAGE_PENDING`, `PARTICIPATION_COVERED_BY_MEMBERSHIP`,
  `MEMBERSHIP_SALE_HAS_USAGE`, `MEMBERSHIP_BOOKING_BLOCKED`.
- **Naglasak 1 (regresija P1):** prije kuka dodan test `MembershipCoverageNoMembershipTests` (klijent bez članarine u organizaciji
  u kojoj drugi klijent ima članarinu: kasni otkaz, izostanak, naplata, grupa — isti iznosi, nijedan zapis pokrića ni korištenja).
  Sva postojeća karakterizacija prolazi bez izmjene (jedina izmjena testa: grant bez endpointa dodan u popis iznimki kataloga).
- **Naglasak 2 (istovremenost):** lock odgovornog članstva prije brojanja; test utrke dvije istovremene rezervacije za zadnji
  kredit → točno jedna pokrivena.
- **Naglasak 3 (objašnjivost):** svaka odluka piše projekciju (stanje, razlog, događaj, limit); nijedna odluka nije tiha.
- **Implementacijski izbori (bez nove poslovne odluke; za potvrdu):**
  *Završni pregled (2026-10-08): svi preostali izbori 2D (2, 3, 4, 6, 7, 9, 14) su POTVRĐENI — 2 tehnički skupno, ostali kao ponašanje vidljivo korisniku (tablica u odgovoru završnog pregleda).*
  1. ✅ POTVRĐENO uz test konzistentnosti (brisanje termina, sudjelovanja, serije i člana grupe ne ostavlja projekciju ni aktivan
     claim bez sudjelovanja) i integritetnu zaštitu u `ParticipationHistory` (svaka putanja brisanja bez prethodnog vraćanja claima
     puca). Projekcija pokrića je zasebna tablica, a ledger i projekcija nemaju FK na sudjelovanje (redoslijed lockova, ADR-0028);
     brisanje netaknutog sudjelovanja prvo vraća claim (`ParticipationRemoved`) i briše projekciju.
  2. "Oznaka čekanja" (§11.2) je stanje projekcije `PendingEvaluation` (razlog `BeyondHorizon`, očekivani period), ne redak
     ledgera; ledger ima samo Claim/Release.
  3. Jedan mehanizam usklađivanja za prodaju, pauzu, otkaz, raniji izlazak, promjenu plana, obnovu/dnevni prolaz, dug, storno
     uplate i oslobođeno mjesto: prvo storno neprihvatljivih claimova budućih termina, zatim evaluacija nepokrivenih redom po
     vremenu; postojeći claimovi se nikad ne preraspodjeljuju.
  4. Odgovorno članstvo za termin: ono koje na datum termina pokriva uslugu u poslovnici; inače prvo koje vrijedi (razlog
     Service/CompanyNotCovered); inače nedavno završeno čiji je kraj prije termina (AfterMembershipEnd); inače nijedno.
  5. ✅ POTVRĐENO za P2; kad dođe online booking, "odbij" vrijedi i za rezervacije klijenta (Q4 t.3, razlikovanje kanala).
     Postavka "odbij" vrijedi samo za novu rezervaciju osoblja (kreiranje termina, cijela ponavljajuća serija, dodavanje
     klijenta/segmenta, gost grupe i check-in novog gosta); nikad za korekcije, check-in postojećeg sudjelovanja, promociju liste
     čekanja, generiranje i pridruživanje grupi.
  6. Na Completed pokrivene sesije eksplicitni paket → `PARTICIPATION_COVERED_BY_MEMBERSHIP`; `PaymentMethod/IsPaid` se ne
     naplaćuje (dug je 0); grupni check-in ne bira paket automatski.
  7. Gost evidentiran izravno kao Completed/NoShow dobiva claim pri nastanku kao svaka rezervacija, pa prijelaz odlučuje
     (izostanak uz ForfeitCredit troši kredit).
  8. ❌ ODBIJENO i promijenjeno: ForfeitCredit uz događaj BEZ naknade troši kredit samo kad ga je organizacija izričito odabrala u
     verziji politike; bez izbora događaj bez naknade (FeeType None ili 0) dobiva `ReturnCreditChargeFee` (kredit se vraća, razlog
     `CreditReturned`), a događaj s naknadom ForfeitCredit. Vrijednost se uvijek postavlja eksplicitno pri nastanku verzije
     (`CancellationPolicyRules.MembershipActionFor`); default stupca uklonjen, postojeće verzije bez naknade prebačene (migracija
     `20261027000011`). Uz zadržan kredit perioda kazna paketom se i dalje ne bira (jedinica ILI naknada ILI kredit). Test.
  9. Propali kredit je "stvarni učinak" posljedice: korekcija koja ga vraća traži `appointments.policy.override` i razlog (kao
     potrošena jedinica paketa).
  10. ✅ POTVRĐENO. Povlačenje otkaza ponovno pokriva termine nakon dotadašnjeg datuma kraja (usklađivanje), umjesto prijedloga §10.1
      "ne pokriva retroaktivno" (koji nije bio odlučen).
  11. ◐ DJELOMIČNO PRIHVAĆENO i promijenjeno: "blokiraj rezervaciju" blokira samo sesije koje bi pokrila članarina u dugu. Novi član
      dodan u grupu s već generiranim terminima tretira se kao generiranje (Q18: preskače se i bilježi, Q53 ga dodaje nakon
      plaćanja). Lista čekanja: blokada pri UPISU (nova rezervacija, uz override grant); promocija postojećeg upisa prolazi bez
      pokrića, a obavijest o promociji (`waitlist.promoted.v1` → Notification) nosi stanje i razlog pokrića za recepciju. Testovi.
  12. ✅ ODLUČENO (dnevnik 2026-10-08): poništavanje prodaje blokira samo stvarno korištenje (claim na sesiji koja je počela ili
      je odrađena, propali kredit) — `MEMBERSHIP_SALE_HAS_USAGE`; claimovi budućih termina se vraćaju (`MembershipVoided`), termini
      postaju NotCovered/MembershipVoided (normalna naplata), a odgovor nosi upozorenje `MEMBERSHIP_VOIDED_SESSIONS_UNCOVERED` s
      popisom termina. Test.
  13. ✅ POTVRĐENO: odgovor rezervacije (termin, serija, gost, prijelaz) vraća sudjelovanje s `MembershipCoverage` (stanje,
      razlog, limit), pa frontend prikazuje upozorenje bez dodatnog poziva (test na odgovoru serije). Objašnjivost je na sudjelovanju (`MembershipCoverage`) umjesto zasebnih upozorenja u odgovoru; upozorenja
      `MEMBERSHIP_LIMIT_FALLBACK_PACKAGE/_PAID` (§11.5) nisu dodana (razlog `LimitReached` + limit su na sudjelovanju; paket se
      kod Individual termina i dalje bira pri Completed, Q30).
  14. "Budući termin" = početak segmenta nakon trenutka obrade; prošli termini se ne diraju. Dnevni prolaz obnove radi
      usklađivanje s istim "danas" kao obnova.
- Ograničenja: obavijesti (outbox) tek s P4 — recepcija vidi razlog i događaj na sudjelovanju te popis preskočenih članova;
  ručna naredba "primijeni pokriće" (§10.1) nije dodana (usklađivanje je automatsko); provizije se ne mijenjaju (2F).
- Testovi: `MembershipCoverageNoMembershipTests` (1), `MembershipCoverageTests` (25).

### Faza 2E — cjenovna pogodnost i prilagodbe cijene (2026-10-08)
- **ADR-0029**; migracija `20261027000012` (`P2MembershipPriceBenefits`), primijenjena lokalno.
- Pravila pogodnosti na verziji plana (`PriceBenefits` u zahtjevu i read modelu plana), `Utils/PriceAdjustmentResolver` i
  `MembershipPriceBenefitRules` (čisto), korak cijene u `MembershipCoverageService` (Sync, usklađivanje strane članstva,
  poništavanje, check-in `PriceOnCompletion`). Sudjelovanje nosi `PriceAdjustment` (primijenjena prilagodba + kandidati),
  projekcija pokrića zadnju automatsku promjenu cijene, razlog zaštite i `PriceStale`.
- **Implementacijski izbori (bez nove poslovne odluke; za potvrdu):**
  *Završni pregled (2026-10-08): svi preostali izbori 2E (2, 5, 7, 8, 9) su POTVRĐENI — 7 i 9 tehnički skupno, 2, 5 i 8 kao ponašanje vidljivo korisniku.*
  1. ✅ POTVRĐENO uz upozorenje: fiksna cijena za člana viša od cjenika se ne primjenjuje (ishod `NoReduction`) — pogodnost
     nikad ne podiže cijenu. Čitanje plana vraća `MEMBERSHIP_BENEFIT_WITHOUT_EFFECT` (usluga, poslovnica, cjenik, cijena za
     člana) kad je fiksna cijena >= cjeniku u nekoj poslovnici plana; računa se pri čitanju jer se cjenik mijenja. Test.
  2. Kandidati su sva neponištena članstva klijenta (i plan samo s pogodnošću); kod iste cijene odlučuje redoslijed tipa, pa
     članstva (početak, Id). Pravilo Q10 (preklapanje) i dalje gleda samo pokrivene usluge — dvije članarine s pogodnošću na istu
     uslugu su dopuštene, pobjeđuje najbolja cijena.
  3. ✅ POTVRĐENO. Automatska cijena vrijedi za potvrđene (Confirmed) sesije; odrađivanje ne mijenja cijenu osim grupnog check-ina (koji ionako
     re-cijeni iz cjenika, pa ponovno primjenjuje pogodnost) i potrošnje paketa pri odrađivanju (cijena se vraća na cjenik, Q2).
  4. ✅ POTVRĐENO uz jamstvo usklađivanja: sudjelovanje zaključano drugom naredbom tijekom usklađivanja strane članstva se
     preskače i označava `PriceStale`. (a) Pozadinski prolaz uz obnovu (`RefreshStalePrices`, vlastita transakcija i lock po
     sudjelovanju) usklađuje sve zastarjele cijene; (b) dodavanje u checkout, plaćanje checkouta i svaki prijelaz u zauzimajuće
     stanje (odrađivanje, check-in) prvo usklađuju zastarjelu cijenu (`EnsurePriceCurrent` / Sync), nikad ne naplaćuju zastarjelu;
     (c) `PriceStale` je na sesiji (`MembershipCoverage.PriceStale`) dok traje. Testovi (checkout, dnevni prolaz).
  5. Re-cijenjenje koje mijenja predloženu ili osnovnu cijenu (promjena usluge/vremena segmenta, check-in) briše staru prilagodbu;
     pogodnost ponovno postavlja samo servis pokrića. Ručni iznos ne mijenja predloženu cijenu, pa zapis pogodnosti ostaje, a
     vraćanje ručnog iznosa (null) vraća cijenu na predloženu (s pogodnošću).
  6. ✅ POTVRĐENO. Plan samo s pogodnošću ne smije imati limite korištenja (nema pokrivenih usluga); njegove sesije imaju projekciju
     `NotCovered/ServiceNotCovered` i cijenu s pogodnošću.
  7. Evaluacija kandidata se snapshotira kad se cijena (ponovno) računa; zaštićena sesija zadržava prethodnu evaluaciju, a
     projekcija nosi razlog zaštite.
  8. Zaokruživanje na 0,01 "od nule" (33,335 → 33,34).
  9. Shema-test tablice sudjelovanja ažuriran za nove stupce prilagodbe (namjerna promjena sheme po §10.4); karakterizacija
     ponašanja nepromijenjena.
- Testovi: `MembershipPriceBenefitTests` (11), regresijski test bez članarine proširen (pogodnost aktivna u organizaciji).

### Faza 2F — provizije po Vagaro modelu (2026-10-08)
- Implementirano prema Q28, Q38 – Q43, Q50 – Q52, "Provjera provizija prije 2F", odluci o §16.3, pregledu 2F i odluci "kopiramo
  Vagaro model" (ADR-0030, migracije `20261027000013` i `20261027000014`). Migracija 13 je već bila primijenjena lokalno, pa ukinute
  dijelove uklanja migracija 14 (pravilo: primijenjena migracija se ne mijenja).
- Bez novog granta:
  - `commissions.manage`: pravila, postavke, korekcija Q50, naknadna dodjela;
  - `checkout.manage`: odabir na stavci;
  - `clients.memberships.sell`: odabir na članstvu.
- API:
  - `GET/PUT /api/commissions/settings`;
  - pravila: `kind`, `effectiveFrom`, `deactivatedFrom`, `membershipPlanId`, `AllServices` s `tiers`, `None`;
  - `PATCH /api/checkouts/{id}/items/{itemId}/sale-commission-employee`;
  - `PATCH /api/memberships/{id}/sale-commission-employee`;
  - `POST /api/commissions/entries/{id}/reassign`;
  - `POST /api/commissions/sale-assignments`.
  
  Zapis provizije dobiva snapshot, `appliedRuleScope`, `ruleEvaluation`, `reversedAt`/`reversalReason` i `periodAmount`.
- **Ukinuto (odluka "kopiramo Vagaro model"):**
  - nadjačavanje po načinu plaćanja;
  - prekidači "oduzmi pokriće članarinom/paketom" (zamijenjeni jednim "oduzmi popuste članstva");
  - vrijednost jedinice paketa (pregled 2F izbor 5 i pitanje o SharedPool otpadaju).
- **Namjerne promjene ponašanja:**
  1. Product/PackageSale: korisnik je zaposlenik odabran na stavci (default onaj koji je dodao stavku), a ne onaj koji zatvara
     checkout.
  2. Sažetak i pregled broje po događajima: `EarnedAmount` uključuje i kasnije stornirane provizije zarađene u razdoblju; storno se
     broji u razdoblju storna.
  3. Sesija s popustom za članove (2E): osnovica je cijena sesije, osim uz "oduzmi popuste članstva".
- **Implementacijski izbori** (svi potvrđeni 2026-10-08: tehnički 1, 5, 14 skupno; vidljivi 4, 7, 10, 11 uz dopune niže):
  1. Vrsta pravila se izvodi iz predmeta (Service/AllServices = Performance; ostalo = Sale). Sale za uslugu se odbija (Q52b).
  2. Datum važenja: postojeća pravila imaju `0001-01-01` ("bez početnog datuma"). Nova verzija je novo pravilo s novim datumom, a
     izmjena (`PUT`) ispravlja tu verziju. Deaktivacija (pregled 2F, potvrđeno) od današnjeg datuma u kalendaru organizacije znači
     "nema pravila", bez povratka na stariju verziju. Starija verzija vrijedi za datume prije svoje kasnije verzije. Reaktivacija
     briše datum deaktivacije. Verzija bez provizija se smije obrisati.
  3. **(potvrđeno 2026-10-08, uz upozorenje `COMMISSION_SERVICE_RULE_GENERAL_APPLIES` u odgovoru deaktivacije kad postoji opće
     pravilo)** Deaktivirano pravilo za USLUGU znači "nema pravila za uslugu", pa se primjenjuje opće pravilo zaposlenika
     (ako postoji). Za izričito isključenje usluge koristi se "Bez provizije". Deaktivirano opće pravilo znači da za usluge bez
     vlastitog pravila nema provizije.
  4. "Oduzmi popuste" uzima cijenu nakon prilagodbe koja NIJE članarinska (oznaka, grupa, promocija; danas ih nema). "Oduzmi
     popuste članstva" uzima cijenu nakon popusta za članove; pokrivena sesija → 0. Ručni iznos nije popust. Osnovica nikad nije
     ispod 0.
  5. Opće pravilo: `CalculationType`/`Value` su prazni, iznos je u jedinoj razini (`FromRevenue = 0`, Percentage | Fixed). Opće
     pravilo se ne primjenjuje na grupne usluge. "Bez provizije" je dopušten samo na pravilu usluge (i grupne).
  6. Default korisnik provizije na stavci je aktivan zaposlenik korisnika koji DODAJE stavku; korisnik bez zaposlenika → prazno
     (potvrđeno).
  7. Zaduženja prve prodaje: početna naknada i zaduženje perioda s dospijećem na datum početka članstva; besplatan plan samo s
     početnom naknadom također se broji. Pravilo plana je trenutni plan članstva u trenutku nastanka; datum je u kalendaru
     organizacije; poslovnica provizije je poslovnica prodaje. **Preciziranje (potvrda 2026-10-08):** iznos plaćenih zaduženja prve
     prodaje je OSNOVICA provizije, a ne provizija: besplatan plan s početnom naknadom 20 € ima osnovicu 20 €, pa je provizija pravilo
     primijenjeno na 20 € (npr. 10 % → 2 €, Fixed 5 € → 5 €).
  8. Prva prodaja se evaluira jednom, a ishod (`Earned | NoRecipient | NoRule | ZeroBase`) i osnovica se pamte. Nakon evaluacije
     promjena korisnika na članstvu ili stavci vraća `COMMISSION_SALE_ALREADY_EARNED` ili `COMMISSION_SALE_ALREADY_EVALUATED`.
     `NoRecipient` dopušta naknadnu dodjelu (pregled 2F, potvrđeno uz iznimku). Datum pravila: za članarinu datum evaluacije, za
     proizvod/paket datum zatvaranja checkouta. Provizija se broji u razdoblju dodjele (po događajima).
  9. Stavka zaduženja obnove sprema vlastiti odabir (potvrđeno).
  10. Q38: "plaćeno u cijelosti" = aktivna posljedica s naknadom > 0 i plaćeno ≥ dug naknade. Kazna jedinicom paketa ili kreditom
      članarine ne daje proviziju; grupni termini su izuzeti. Pravilo je isto kao za sesiju (usluga, pa opće), bez nadjačavanja.
  11. Q50: korekcija za ProductSale, PackageSale i MembershipSale. Novi korisnik mora biti aktivan i dobiva proviziju po svom
      pravilu važećem na datum izvorne provizije. Razlog ima najviše 500 znakova. **Dopuna (potvrda 2026-10-08):** ako novi korisnik
      nema pravilo važeće na taj datum, naredba PRIJE ikakve promjene vraća `COMMISSION_REASSIGN_WITHOUT_RULE` (409, detalji:
      zaposlenik, datum pravila, predmet) i ništa ne mijenja; s `ConfirmWithoutCommission = true` stornira postojeću proviziju bez
      nove (svjesna potvrda). Najjednostavniji oblik: parametar potvrde u istoj naredbi, bez zasebnog pregleda (test).
  12. Postavke provizija su pod `commissions.manage` (pregled 2F). **Prijedlog:** bez zasebnog granta, jer `commissions.manage`
      već upravlja pravilima, koja jednako izravno mijenjaju zaradu. Zaseban grant (npr. za strukture u fazi Payroll) je pitanje
      7.2 u `docs/payroll/PAYROLL_QUESTIONS.md`.
  13. Neaktivan korisnik (odluka 2026-10-08):
      - odabir neaktivnog zaposlenika odbija se svugdje (`INACTIVE_EMPLOYEE`): na stavci, na članstvu, pri prodaji, kod korekcije i
        naknadne dodjele;
      - default se postavlja samo na aktivnog;
      - zaposlenik odabran dok je bio aktivan dobiva proviziju i ako je kasnije postao neaktivan.
      
      **(potvrđeno 2026-10-08)** Slučaj 3 ("ipak odabran neaktivan, npr. stari podatak") se kroz API ne može dogoditi. Razlikovanje bi
      tražilo zapis aktivnosti u trenutku odabira, kojeg nema, a razvojna baza nema starih podataka. Zato se takav odabir pri
      nastanku tretira kao valjan.
  14. Povijest članstva: `SaleCommissionEmployeeChanged`, `SaleCommissionCorrected` (Q50) i `SaleCommissionAssigned` (naknadna
      dodjela). Isti nazivi su u checkout auditu.
- Testovi: `CommissionVagaroTests` (14, uklj. upozorenje pri deaktivaciji), prepisani za Vagaro model:
  - osnovica uz isključene prekidače, popusti članstva, pokriće članarinom i paketom;
  - opće pravilo, prednost pravila usluge, "Bez provizije" i objašnjenje;
  - povijest pravila s deaktivacijom bez povratka i brisanjem;
  - Q38;
  - "Sold By" s neaktivnim zaposlenikom, korekcija i izvještaj po događajima;
  - naknadna dodjela za paket i članarinu;
  - prva prodaja članarine i otpis.
  
  Testovi ukinutih dijelova su uklonjeni. Ukupno 1230/1230.

## Dnevnik odluka
| Datum | Pitanje | Odgovor | Posljedica |
|---|---|---|---|
| 2026-10-07 | Što Membership daje u P2, kako se naplaćuje, gdje vrijedi, treba li pauza? | Korisnik je umjesto izbora dao puni spec (sažet gore): pokriće + cjenovna pogodnost; obnova po periodima s Charge-om, recepcija zatvara ručno; pauza po pravilima organizacije. Scope (org vs poslovnica) nije odgovoren. | Napravljen P2_PLAN.md s nalazima iz koda i pitanjima Q1–Q24; scope ostaje otvoren (Q23). |
| 2026-10-07 | Q6 — kada se troši kredit? | Na rezervaciji. Neotvoreni period → claim na očekivani period, inače normalan settlement; otkaz unutar prozora i NoShow ne vraćaju kredit; prozor je postavka organizacije; promjena vremena = storno + novi claim s provjerom limita; paketi ostaju OnCompletion. | P2_PLAN §10.1–10.3; novo pitanje o izvoru prozora (P1 politika vs nova postavka) i ponašanju kad novi period nema mjesta. **⇒ Prozor: zamijenjeno Q25 (P1 klasifikacija).** |
| 2026-10-07 | Q9 — FinalPrice pokrivene sesije? | Retail cijena, MonetaryDue 0 (ADR-0012). Dodatno: analizirati proviziju na članarinske sesije (retail / raspodijeljena vrijednost / bez) i treba li ADR. | P2_PLAN §10.5 (analiza + prijedlog); provizija se ne implementira do odluke. **⇒ Provizija odlučena: ADR-0030 (Vagaro).** |
| 2026-10-07 | Q1 — prioritet cjenovnih pogodnosti? | Najbolja cijena, jedna prilagodba. Zapisati primijenjenu + izgubljene kandidate; deterministički tie-breaker po tipu; omogućiti kasnije objašnjenje klijentu; fiksni prioritet kasnije, ne sada. | P2_PLAN §10.4 (zapis evaluacije, redoslijed tipova). |
| 2026-10-07 | Q23 — gdje članstvo vrijedi? | Plan bira poslovnice. | Tablica `membership_plan_companies`; P2_PLAN §10.6. |
| 2026-10-07 | (napomena korisnika) Sustav pogodnosti | Promo kodovi, tag pogodnosti itd. još nisu ni izgrađeni ni isplanirani, ali sigurno dolaze i morat će se implementirati. | P2 gradi opći mehanizam prilagodbi (izvori + resolver + zapis evaluacije), članarina je prvi izvor; sam sustav pogodnosti je zasebna buduća faza (P2_PLAN §10.4). |
| 2026-10-07 | Q25 — izvor otkaznog prozora za kredit? | P1 politika; jedan prozor. Dodatno: provjeriti kako P1 tretira naknadu kod paketa, predložiti fiksno pravilo vs postavka i konzistentnost s paketima. | P2_PLAN §11.1; novo pitanje Q31. |
| 2026-10-07 | Q26 — kasni otkaz/NoShow pokrivene sesije? | Samo zadržan kredit, bez naknade (P1 D6). Neograničen = bez kredita perioda → P1 naknada na retail, i dalje troši mjesto u prozorima. Nepokriveno zbog duga = normalna P1 pravila. | P2_PLAN §11.1. |
| 2026-10-07 | Q27 — horizont pokrića? | Tekući + sljedeći. Naknadna primjena redom po vremenu termina (tie: vrijeme rezervacije) + obavijest; rezervacija koja čeka ne stvara dug; nakon zakazanog kraja nema pending claima; opisati serije. | P2_PLAN §11.2–11.4. |
| 2026-10-07 | Q4 — limit iskorišten? | Prolazi + warning, org postavka "odbij". Fallback lanac članarina → paket → normalno, s warningom koji kaže ishod; isto u scheduleru. Jedna org postavka u P2, model spreman za plan/tip limita/kanal. | P2_PLAN §11.5; novo pitanje Q30 (paket kao fallback kod Individual termina). |
| 2026-10-07 | Q28 — provizija na članarinske sesije? | Jedno polje: PercentOfRetail / PercentOfCollected / FixedPerSession / None; vrijedi za sve izvore (direktno, paket, članarina); default po usluzi + nadjačavanje po izvoru; postojeće → PercentOfRetail bez promjene iznosa. Odgovor se poziva na "prošlu poruku" i "raniji odgovor" koji u ovoj sesiji nisu dostupni. | P2_PLAN §12.1; novi ADR; otvorena pitanja Q32–Q36. **⇒ ZAMIJENJENO: Q28 konačno (Vagaro), zatim ADR-0030.** |
| 2026-10-07 | Q29 — prazan skup poslovnica? | Eksplicitno AllCompanies / SelectedCompanies; prazna Selected = greška; sve odabrane deaktivirane → warning, grandfathering, nema nove prodaje. Provjeriti obrazac u drugim domenama. | P2_PLAN §12.2 (inventura obrazaca; promjene drugih domena čekaju potvrdu). |
| 2026-10-07 | Q30 — paket kao fallback kod Individual? | Eksplicitno na Completed; paketi se ne mijenjaju; warning pri rezervaciji; predodabir u UI-u; auto-primjena paketa = tema za online booking. | P2_PLAN §11.5 vrijedi uz ovo preciziranje. |
| 2026-10-07 | Q31 — kredit kod kasnog otkaza fiksno ili konfigurabilno? | Dimenzija P1 politike MembershipAction (ForfeitCredit default / ReturnCreditChargeFee); waiver = storno cijelog claima; pravilo nazivnika za PercentOfCollected. | Migracija na verzijama politike + snapshot na posljedici (P2_PLAN §11.1, opcija b). **⇒ Pravilo nazivnika ukinuto (Q28); default precizirao pregled 2D izbor 8.** |
| 2026-10-07 | Q28 — konačni model provizije | Vagaro model (korisnikovo istraživanje Vagaro/Fresha): ukida se Allocated i sve vezano uz nju (Q32–Q36 otpadaju); provizija i dalje na Completed; pravilo trener × usluga Percentage/Fixed/None (None eksplicitan, migracija dodjela bez pravila); osnovica po org postavkama "oduzmi popuste" i "oduzmi pokriće članarinom" (default oba isključena); paket po jedinici, neograničen = 0; nadjačavanje po izvoru plaćanja; povijest pravila s datumom važenja; izvor pokrića zapisan na sesiji; grupe nepromijenjene. Faza 2F, ADR proširuje ADR-0010. | P2_PLAN §13; nova pitanja: provizija kod kasnog otkaza, provizija na prodaju članarina/paketa, razina postavki osnovice, prekidač za paket, opseg migracije na None. **⇒ Djelomično ZAMIJENJENO: None i migracija (preciziranja), prekidači pokrića, paket i nadjačavanje (Vagaro 2026-10-08, ADR-0030).** |
| 2026-10-07 | Preciziranja provizija (ručna cijena, obaveznost pravila, prekidač paketa) + Q38–Q41 | Ručna cijena = konačna direktna cijena, uz zapis cjenika; nema pravila = nema provizije, bez migracije (Q41 otpada), BezProvizije samo kao nadjačavanje; zaseban prekidač za paket; Q38 org postavka default nikad; Q39 prva prodaja u 2F, storno poništava; Q40 samo organizacija. | P2_PLAN §13 vrijedi uz ova preciziranja (§14.1). **⇒ BezProvizije samo kao nadjačavanje i prekidač paketa ZAMIJENJENI (Vagaro 2026-10-08).** |
| 2026-10-07 | Q39 dodatno — tko dobiva proviziju na prodaju? | Proizvoljno pri naplati: trener, recepcionar koji naplaćuje ili drugi zaposlenik. | Eksplicitan odabir korisnika provizije na naplati (P2_PLAN §14.1); današnji PackageSale (korisnik = tko zatvara checkout) je zasebna tema. **⇒ ZAMIJENJENO ispravkom Q43/Q44 i preciziranjem prije 2F.** |
| 2026-10-07 | Q42 — osnovica provizije na prvu prodaju? | Prvi period + početna naknada, stvarno plaćeno; nastaje kad su oba zaduženja konačna (plaćeno ili otpisano = 0). | P2_PLAN §14.1. |
| 2026-10-07 | Q2 — popust članarine na pokrivene sesije? | Samo nepokrivene; ne na sesije pokrivene članarinom ni paketom. | Izvor članarine vraća NotApplicable za pokrivene sesije (§10.4). |
| 2026-10-07 | Q3 — prvi period? | Od datuma kupnje: pun period, kraj mjeseca → zadnji dan pa povratak na izvorni dan. Kalendarski: puna cijena i puni krediti za kraći prvi period. Datum početka biran pri prodaji. | P2_PLAN §14.2 (sidro obnove, ograničenje datuma početka). |
| 2026-10-07 | Q10 — više članarina? | Zabrana preklapanja po vremenu + uslugama + poslovnicama; nastavak nakon zakazanog kraja i različite usluge dopušteni; promjena plana nije druga članarina. | MEMBERSHIP_OVERLAPPING_COVERAGE s detaljem članarine; provjera pri prodaji pod lockom klijenta. |
| 2026-10-07 | Q5/Q12 — pauza? | Pravila na planu, produljuje; kalendarski planovi samo cijeli periodi; bez retroaktivne; ne uz dug nakon grace; max puta rolling 12 mj.; rezervacije u pauzi nepokrivene + popis, bez auto-otkaza; prijevremeni kraj skraćuje produljenje. | P2_PLAN §15.1; pitanje Q47 (prijevremeni kraj kod kalendarskog plana). |
| 2026-10-07 | Q14 — izmjena plana i postojeća članstva? | Bira se pri izmjeni (default samo nove prodaje); "i postojeća" od prve obnove ≥ X dana nakon izmjene (org postavka); popis pogođenih; snapshot nikad retroaktivno. Predložiti tretman povoljnih izmjena. | P2_PLAN §15.2; pitanje Q48. |
| 2026-10-07 | Q15 — dug? | Default grace 7 dana, ne pokrivaj; istek grace oslobađa buduće claimove (+ popis); plaćanje ponovno primjenjuje pokriće (Q27 mehanizam); standing iz najstarijeg neplaćenog zaduženja; isto za blokiranje. | Prijašnji prijedlog "postojeći claimovi ostaju" (§3.4) zamijenjen. |
| 2026-10-07 | Q19 — aktivacija? | Recepcija: odmah, i neplaćeno; dug ne nestaje otkazom; online kanal kasnije aktivacija po plaćanju, model razlikuje kanal. | `sold_via` na članstvu (P2_PLAN §15.3). |
| 2026-10-07 | Q47 — povratak iz pauze kalendarskog plana? | Otvara se period od povratka, puni iznos i krediti, jedno korištenje pauze; nije automatski, recepcija potvrđuje uz upozorenje s datumima i iznosom. | Naredba `pauses/{id}/end-early` s potvrdom (P2_PLAN §15.1). |
| 2026-10-07 | Q48 — povoljne izmjene plana? | Automatska klasifikacija strogo povoljnih nad svim uvjetima; nejasno = mješovito; odgovor prikazuje klasifikaciju i datum važenja. | `MembershipPlanChangeClassifier` (čista funkcija), P2_PLAN §15.2. |
| 2026-10-07 | Q17 — prozori limita? | Kalendarski u zoni poslovnice termina; po vremenu termina. Validacija kombinacije s kreditima perioda: korisnik preporučuje (a) zabranu. | P2_PLAN §15.4: prijedlog generalizacije (a) za godišnje planove, čeka potvrdu (Q49). **⇒ Validacija odlučena Q49.** |
| 2026-10-07 | Q13 — zajednički limit? | Da, limit plana; AND s limitima usluge; claim troši sve brojače; prozori i na razini plana; warning kaže koji limit. | Ledger claim vrijedi za sve primjenjive brojače (P2_PLAN §15.3). |
| 2026-10-07 | Q49 — validacija prozora? | Generalizacija prihvaćena; neograničen plan bez provjere; usporedba s kreditima istog opsega; warning i za dulji prozor ≥ kredit × broj perioda. | P2_PLAN §15.4. |
| 2026-10-07 | Q16 — status zaduženja? | Izveden iz alokacija; PartiallyPaid = neplaćeno za dug; konačno = Paid/WrittenOff; storno vraća status i poništava ovisne provizije; dopuštena projekcija za upite. | P2_PLAN §16.1 (projekcija). |
| 2026-10-07 | Q20/Q24 — prodaja i plaćanje? | Naredba + checkout stavka MembershipCharge, djelomično dopušteno; UI otvara checkout nakon prodaje; prodavač na članstvu; ista stavka za obnove; poništavanje prodaje samo bez alokacija i claimova. | P2_PLAN §16.2 (grant za poništavanje). **⇒ Uvjet poništavanja zamijenjen (Q51.1, 2D izbor 12).** |
| 2026-10-07 | Q43 — kada se bira korisnik provizije na prodaju? | Pri prodaji, default izvršitelj naredbe, izmjenjiv uz zapis; nakon nastanka samo korekcija uz ograničenje. | P2_PLAN §16.3; "ograničenje na ulogu" provedeno grantom (ADR-0004/0019). **⇒ Precizirano ispravkom Q43/Q44, prije 2F i pregledom 2F (do evaluacije prve prodaje).** |
| 2026-10-07 | Minimalna obveza i otkazni rok u P2? | Da, formula max(...); obveza u periodima bez pauze; ručni raniji izlazak uz ovlast i razlog; odgovor vraća datum i razlog; dulje = nepovoljna izmjena. | P2_PLAN §17.1 (grant za raniji izlazak). |
| 2026-10-07 | Q22 — zona za scheduler? | Zona organizacije; granice perioda u org zoni, prozori u zoni poslovnice; lokalni datumi, DST, idempotentno. | Utils za granice perioda nad `OrganizationCalendar` organizacije. |
| 2026-10-07 | Q45 — datum početka? | Max mjesec unaprijed; prvo zaduženje pri prodaji, grace od početka; Scheduled može dobiti pokriće unaprijed; odustajanje bez plaćanja = poništavanje; predložiti za plaćeno. | P2_PLAN §17.2; pitanje Q51. |
| 2026-10-07 | Q46 — promjena plana i preklapanje? | Odbij pri zakazivanju (vremenski); prodaja uzima u obzir zakazane promjene; sigurnosna provjera pri obnovi. | Provjera preklapanja nad efektivnim vremenskim linijama planova. |
| 2026-10-07 | Q18 — generiranje grupe uz blokadu duga? | Preskoči člana (ne preporučena opcija): mjesto u grupi ostaje, preskaču se termini; nakon plaćanja nastavak; ručno dodavanje uz ovlast bez pokrića; popis recepciji; ostale postavke generiraju normalno. | Namjerna promjena F-23 → ADR; P2_PLAN §18.2 (Q53 naknadno generiranje, Q54 grant). |
| 2026-10-07 | Q50 — grant za korekciju korisnika provizije? | commissions.manage + razlog + zapis; korekcija zatvorenog obračuna ide u sljedeći. | Zatvoreni obračun danas ne postoji; P2_PLAN §18.3 (pravilo izvještavanja po događajima). |
| 2026-10-07 | Q51 — plaćeno pa odustajanje prije početka? | (a) storno + poništavanje ili (b) regularni otkaz; uvjet poništavanja = nema aktivnih alokacija ni claimova; (a) poništava proviziju na prodaju, (b) je zadržava; bez djelomičnih povrata. | §16.2 i §17.2 usklađeni. |
| 2026-10-07 | Ispravak Q43/Q44 — provizija na prodaju općenito | Odabir korisnika provizije na naplati za svaku stavku, default naplatitelj; prodaja i odrađeno su odvojene provizije; članarina: prijedlog iz naredbe, konačno na stavci MembershipCharge; Q44 ulazi u opseg. | P2_PLAN §18.1; pitanje Q52 (opseg i provizija na prodaju usluge). **⇒ Default precizirano: zaposlenik koji dodaje stavku (pregled 2F); članarina: jedini izvor je članstvo (prije 2F).** |
| 2026-10-07 | Potvrde §18 + Q52–Q54 | Brojanje po događajima, polje korisnika provizije na stavci + Performance/Sale, storno uplate poništava prodajnu proviziju (promjena ponašanja), Q18 ADR + F-23; Q52 prijedlog prihvaćen; Q53 prihvaćen; Q54 zaseban grant, predložiti default dodjelu; Q37 izvan P2; dizajn zatvoren, kreni s 2A. | Q54 default = samo Admin grupe (ADR-0023). Implementacija 2A započeta. |
| 2026-10-07 | (2A) Kalendarski način obnove za godišnji interval? | Ne u P2: kalendarski samo za mjesečni interval, godišnji uvijek "od datuma kupnje" (validacijska greška). Za kasnije: godišnja članarina po kalendarskoj godini (npr. "članarina za 2027.") traži proporcionalni prvi period i prodaju više od mjesec dana unaprijed (Q45) — zasebna odluka. | Validacija u `MembershipPlanRules`; dug zabilježen u P2_PLAN §19. |
| 2026-10-07 | (2A) Plan bez pokrivenih usluga? | U 2A barem jedna pokrivena usluga. U 2E pravilo postaje "barem jedna pokrivena usluga ILI cjenovna pogodnost" (članarina samo za popust). | Validacija u `MembershipPlanRules`; promjena pravila zabilježena za 2E. |
| 2026-10-07 | Pregled 2A — pauza bez ograničenja trajanja? | Kad je pauza dopuštena, "max dana" je OBAVEZAN (≥ 1) za planove "od datuma kupnje"; "max puta godišnje" smije ostati prazno. Razlog: neograničena pauza + minimalna obveza koja se broji samo izvan pauze = obveza se može izbjegavati neograničeno. Nova migracija/ograničenje + MembershipPlanRules + test. | Migracija `20261027000002` (CHECK), pravilo i test. |
| 2026-10-07 | Isto pravilo za kalendarske planove? | Da: kalendarski plan s dopuštenom pauzom mora imati "max periodi" ≥ 1; "max puta godišnje" smije ostati prazno. | Isti CHECK i pravilo. |
| 2026-10-07 | Pregled 2A — ostali implementacijski izbori | Potvrđeno: min. obveza i otkazni rok ≥ 1 ili prazno; nova verzija u 2A samo za nove prodaje. | Bez promjene. |
| 2026-10-07 | Kako recepcija dobiva grantove članarina (registracija stvara samo Admin grupu, ADR-0023)? | Ručno kroz capability editor: nema nove sistemske grupe ni predloška, ADR-0023 bez promjene; novi grantovi migracijom samo Admin grupama; grantovi članarina moraju biti grupirani u smislene capabilityje ("Prodaja članarina", "Upravljanje članstvima"); dokumentirati preporučenu raspodjelu po tipičnim ulogama (recepcija, voditelj, admin). Predlošci grupa = otvorena tema za kasnije, nakon testiranja osnovnog rada aplikacije. | Raspodjela grantova i capabilityja za 2B: P2_PLAN §20. Otvorena tema zabilježena u ARCHITECTURE.md §7.3. |
| 2026-10-07 | (2B) Koja poslovnica smije prodati članarinu? | Bilo koja aktivna. Ako plan ne vrijedi u poslovnici prodaje, odgovor/sučelje upozorava ("ne vrijedi ovdje, vrijedi u: B, C"). Izvještaji moraju razlikovati poslovnicu prodaje (naplata, kasnije fiskalizacija) od poslovnica važenja (korištenje usluga) — zahtjev za izvještaje. | Upozorenje `MEMBERSHIP_PLAN_NOT_VALID_AT_SALE_COMPANY` pri prodaji; `sold_company_id` na članstvu. |
| 2026-10-07 | (2B) Što znači "max dana" pauze? | UKUPNO u 12 mjeseci od početka članstva (rolling), ne po jednoj pauzi (ne preporučena opcija). Isto "max periodi" kod kalendarskih = ukupno preskočenih perioda u 12 mjeseci. Razlog: "max puta" smije biti prazno, pa bi uzastopne pauze zaobišle ograničenje po pauzi. "Max puta" ostaje opcionalno dodatno ograničenje. Pri zadavanju pauze odgovor/sučelje prikazuje preostale dane/periode u tekućih 12 mjeseci. | Provjera zbroja u `MembershipPauseRules`; read model vraća preostalo. |
| 2026-10-07 | (2B) Pauza uz zakazan otkaz? | Ne. Zakazani otkaz se može povući dok ne stupi na snagu, nakon čega je pauza opet dopuštena (grant za povlačenje: predložiti). Zahtjev za otkaz poništava zakazanu pauzu koja još nije počela (upozorenje recepciji + zapis u povijesti); aktivna pauza teče normalno, otkaz djeluje na kraju tekućeg (produljenog) perioda. | `cancellation` naredba poništava buduće pauze. |
| 2026-10-07 | (2B) Otkaz buduće pauze i ručni raniji izlazak? | Buduća pauza se smije otkazati. Raniji izlazak: datum od danas do izračunatog datuma otkaza (samo ranije); ne u prošlosti; zaduženje perioda u kojem pada ostaje (bez djelomičnog povrata), neiskorišteni krediti propadaju; claimovi nakon izlaska se oslobađaju uz popis termina (bez auto-otkaza); zakazana pauza nakon izlaska se poništava. | Naredba `end` (2B); oslobađanje claimova se uključuje u 2D. |
| 2026-10-07 | Otvorena tema: grantovi, grupe, capabilityji (razmišljanje za kasnije) | Princip od 2B: grantovi granularni po RADNJI (ne po polju), posebno za osjetljive radnje; grupe slaže studio sam; nema sistemskih grupa osim Admin ni predložaka (ADR-0023); novi grantovi migracijom samo Admin grupama. Za kasnije (ovisi o frontendu/UX-u): capability kao sloj grupiranja, ovisnosti grantova (automatsko uključivanje grantova bez kojih frontend radnje ne radi), predlošci grupa, preporučena raspodjela po ulogama. Ništa od "za kasnije" se ne implementira sada. | Zapisano u ARCHITECTURE.md §7.3 s trenutnim stanjem; grantovi 2B granularni (P2_PLAN §20). |
| 2026-10-07 | (2B) Granularnost grantova | Prihvaćeno: `clients.memberships.view`, `.sell`, `.cancel` (uključuje povlačenje otkaza), `.pause` (uključuje otkaz buduće pauze), `.plan-change`, `.end-override`, `.void-sale`; 2A: novi `catalog.memberships.deactivate` (aktivacija i deaktivacija). Svi migracijom samo Admin grupama. Provjeriti pokrivenost ranijih grantova i fazu. | Pokrivenost: `memberships.charges.write-off` → 2C; `appointments.membership-block.override` → 2D; `commissions.manage` postoji, koristi se u 2F (Q50); promjena korisnika provizije na stavci checkouta → postojeći `checkout.manage` (§18.1, prihvaćeno uz Q52). P2_PLAN §20.7. |
| 2026-10-07 | (2B) Implementacija | Faza 2B implementirana (ADR-0026). Implementacijski izbori 1–9 zapisani u "Faza 2B" i čekaju potvrdu. Build bez grešaka; svi testovi prolaze (1159/1159). | Sljedeća faza: 2C. |
| 2026-10-07 | Pregled 2B — izbor 1 (preskakanje člana kod preklapanja) | Potvrđeno uz dopunu: trajna oznaka na članstvu (popis članstava, profil klijenta) da izmjena nije primijenjena i zašto, dok se ne riješi; bez automatske ponovne primjene; popis za admina. | Stupci `plan_update_skipped_*` (migracija `20261027000005`), `GET /api/memberships/plan-update-not-applied`; oznaku briše kasnija uspješna izmjena. |
| 2026-10-07 | Pregled 2B — izbor 3 (povlačenje promjene plana) | Ne prihvaćeno: nakon povlačenja stanje kao da promjene nije bilo; istisnuta izmjena se vraća s izvornim datumom (rok najave od objave); ako je izvorni datum prošao, pri prvoj sljedećoj obnovi. Test za scenarij. | Stupci `displaced_*`; izmjena objavljena dok klijentova promjena čeka također se pamti (`HeldByClientPlanChange`); testovi povlačenja (izvorni datum, prošli datum). |
| 2026-10-07 | Pregled 2B — izbor 6 (pauza uz raniji izlazak) | Potvrđeno. | Bez promjene. |
| 2026-10-07 | Pregled 2B — izbori 2, 4, 5, 9 | Potvrđeno. | Bez promjene. |
| 2026-10-07 | Pregled 2B — izbor 7 (rok najave) | Potvrđeno (0–365, default 30) uz dopunu: rok kraći od 14 dana → upozorenje u odgovoru API-ja (nepovoljne izmjene bez ili s kratkom najavom). | Warning `MEMBERSHIP_CHANGE_NOTICE_SHORT` na `PUT /api/organization/settings/membership-change-notice`; test. |
| 2026-10-07 | Pregled 2B — izbor 8 (kapacitet pri promjeni plana) | Ne prihvaćeno: promjena plana provjerava kapacitet ciljnog plana; broje se članstva na planu na datum stupanja promjene, uključujući druge zakazane promjene prema njemu, umanjeno za zakazane odlaske ako je jednostavno (inače konzervativno); pun plan → ista greška kao prodaja. Test. | `MembershipPlanCapacity`: odlasci do datuma se oduzimaju (jednostavno uz postojeću matematiku perioda); dolasci se broje konzervativno bez obzira na datum. Isto brojanje i pri prodaji (inače bi prodaja zaobišla zakazane dolaske). Testovi: dolazak zauzima mjesto (promjena i prodaja odbijene), povlačenje oslobađa, odlazak istog dana oslobađa. |
| 2026-10-07 | (2C) Kapacitet pri prodaji uz zakazane dolaske/odlaske | Potvrđeno. | Bez promjene (`MembershipPlanCapacity` u prodaji i promjeni plana). |
| 2026-10-07 | (2C) Obnova kad je član u dugu nakon grace perioda | (a) periodi se otvaraju i zadužuju normalno, pokriće prestaje po pravilu duga (Q15). DODATNO postavka organizacije "automatski završi članstvo nakon N neplaćenih perioda" (default isključeno): članstvo završava na kraju zadnjeg otvorenog perioda, razlog "neplaćanje", zapis u povijesti; dug ostaje (ne otpisuje se); "neplaćen period" = zaduženje koje nije Paid ni WrittenOff nakon grace perioda. Opcija (c) se ne radi. | `organization_settings.membership_auto_end_after_unpaid_periods` (NULL = isključeno), `end_reason = NonPayment`; provjera pri obnovi. |
| 2026-10-07 | (2C) Kada deaktivacija plana završava članstva | (a) na dan obnove ako je plan još neaktivan; ponovna aktivacija prije toga nastavlja članstva. Recepcija/admin dobiva popis članstava koja će završiti zbog deaktivacije. | Završetak u obnovi (`PlanDeactivated`, kraj tekućeg perioda); popis `GET /api/membership-plans/{id}/memberships-ending` i upozorenje u odgovoru deaktivacije. |
| 2026-10-07 | (2C) Klijent zakazao prelazak na plan deaktiviran prije stupanja na snagu | (a) ne primjenjuje se, članstvo ostaje na starom planu, uz trajnu oznaku (isti obrazac kao preskočena izmjena plana iz 2B). | Obnova poništava zakazani prelazak i postavlja oznaku `plan_update_skipped_*` s razlogom `MEMBERSHIP_PLAN_INACTIVE` + zapis u povijesti. |
| 2026-10-07 | (2C) Outbox događaji članarina | Slaže se: ne pišu se do P4. U plan P4 zapisati: handleri moraju moći raditi iz stanja u bazi, jer povijesni događaji iz P2 neće postojati. | P2_PLAN §21.1 i ARCHITECTURE §7.3 (P4). |
| 2026-10-07 | (2C) Implementacija | Faza 2C implementirana (ADR-0027). Implementacijski izbori 1–8 zapisani u "Faza 2C" i čekaju potvrdu. Build bez grešaka; svi testovi prolaze (1176/1176). | Sljedeća faza: 2D (pokriće, ledger, limiti, dug). |
| 2026-10-07 | Pregled 2C — izbori 1–5 | Potvrđeno. Uz #1: početna naknada > 0 stvara zaduženje i kad je cijena plana 0. Uz #2: sučelje stanje novog neplaćenog članstva prikazuje kao "čeka plaćanje", ne kao dug. | #1 provjereno u kodu i testom. #2 je prikaz: backend vraća `InGrace` (dug unutar grace perioda = još nije dug), frontend ga za neplaćeno članstvo labelira "čeka plaćanje". |
| 2026-10-07 | Pregled 2C — izbor 6 (poništavanje i otpisana zaduženja) | Ne prihvaćeno: poništavanje prodaje prebacuje SVA zaduženja, uključujući otpisana, u Voided. Otpis ostaje zabilježen u povijesti zaduženja, ali izvještaj o otpisima ne smije pokazivati gubitak za prodaju poništenu kao greška. | Migracija `20261027000008`: CHECK dopušta zapis otpisa uz lifecycle Voided; izvještaj otpisa = samo lifecycle WrittenOff. Test. |
| 2026-10-07 | Pregled 2C — izbor 7 (stavka zaduženja i klijent checkouta) | Potvrđeno za P2. Za kasnije: plaćanje zaduženja drugog klijenta (roditelj djetetu, partner partneru), platitelj ≠ član — zasebna odluka. | Zapisano u P2_PLAN §22 i ARCHITECTURE §7.3. |
| 2026-10-07 | Pregled 2C — izbor 8 (minimalna obveza nakon promjene uvjeta) | Ne prihvaćeno: kraj obveze = KASNIJI od (dosadašnji kraj obveze, datum promjene + minimalna obveza novog plana); inače se promjenom plana izbjegava obveza. Pauza i dalje produljuje obvezu. Test. | `commitment_from_on` (od kad se broje periodi obveze trenutnih uvjeta) i `commitment_floor_on` (dosadašnji kraj obveze) na članstvu; datum otkaza ≥ prvi kraj perioda na ili nakon donje granice. |
| 2026-10-07 | Pregled 2C — storno i Q51(a) | Storno uplate samo u otvorenom checkoutu znači da Q51(a) (povrat + poništavanje prije početka) u praksi nije izvediv. Provjeriti postoji li put za povrat iz zatvorenog checkouta i zapisati kad Q51(a) postaje moguć; do tada recepcija ima samo (b). | Provjereno: povrat (refund) nije implementiran (`Payment` napomena: odgođeno), Completed → Voided checkouta je samo interna korekcija check-ina. Q51(a) postaje moguć s P3 (Client Credit Ledger: povrat/kredit iz zatvorene naplate). P2_PLAN §22. |
| 2026-10-07 | Pregled 2C — implementacija izmjena | #6 i #8 promijenjeni, #1 potvrđen testom, napomene o Q51(a), platitelju ≠ članu i izvještaju otpisa zapisane (P2_PLAN §22, ARCHITECTURE §7.3). | Migracija `20261027000008`; testovi za #1, #6 i #8. |
| 2026-10-08 | (2D) Naglasci za 2D | (1) Klijent BEZ članarine mora imati potpuno isto ponašanje (rezervacija, otkaz, P1, settlement, provizije); provjeriti karakterizacijsku pokrivenost i dodati testove prije izmjena gdje fale. (2) Istovremenost: dvije rezervacije ne smiju obje potrošiti zadnji kredit/mjesto u limitu; claim zaključava brojač; test utrke. (3) Svaka odluka o pokriću vraća razlog za recepciju; nijedno pokriće nije tiho. Promjena P1 ponašanja za klijente bez članarine = stani i pitaj. | Pokrivenost provjerena (~1100 testova: otkaz/izostanak 26, P1 22, ispravci 20, settlement 27, provizije 16, paketi 34, generiranje grupe 28, lista čekanja 18, ponavljajući 10, promjena vremena 38); dodaje se izričit test "bez članarine nema ničeg članarinskog" prije izmjena. Sve 2D kuke su no-op bez neponištene članarine. 2D se radi u koracima 2D.1–2D.5 (P2_PLAN §23). |
| 2026-10-08 | (2D) Postojeće buduće rezervacije pri kupnji/početku članarine | Da, automatski, redom po vremenu termina do limita. Već (djelomično) plaćene (unaprijed, depozitom ili kroz zatvoren checkout) NE dobivaju automatski pokriće — popis s razlogom "već plaćeno" za recepciju (povrat s P3). Prioritet članarina → paket ostaje: rezervacija koju bi pokrio paket prelazi na članarinu ako ima mjesta. Prošli termini se ne diraju. | Evaluacija pri prodaji i početku članstva nad budućim potvrđenim sudjelovanjima. |
| 2026-10-08 | (2D) Već plaćeno sudjelovanje | Ostaje plaćeno, razlog `AlreadyPaid`, kredit se ne troši. Razlog vidljiv recepciji (na terminu i u popisu nepokrivenih). Ako se novčana alokacija kasnije stornira (dok je checkout otvoren), sudjelovanje ponovno ulazi u evaluaciju. Za P3: razmotriti "prebaci na članarinu i vrati uplatu u kredit". | Ponovna evaluacija iz `VoidPayment`; P3 napomena u P2_PLAN §22. |
| 2026-10-08 | (2D) Oslobođeno mjesto u limitu | Automatski se pokriva najraniji nepokriveni budući termin istog članstva. Mjesto oslobađa samo storno claima (otkaz na vrijeme, waiver, pomicanje termina, ponovna evaluacija); kasni otkaz / no-show uz ForfeitCredit ne oslobađa. "Najraniji nepokriveni" = najraniji budući termin istog članstva koji nakon oslobađanja prolazi SVE primjenjive limite, nije `AlreadyPaid` i nije nepokriven zbog duga. Jedan korak (ne lanac) osim ako je oslobođeno više mjesta odjednom. Recepcija dobiva obavijest; razlog promjene pokrića se zapisuje na sudjelovanju. | Obavijest = trajni razlog na sudjelovanju + povijest; dostava obavijesti s P4 (iz stanja u bazi). |
| 2026-10-08 | (2D) Implementacija | Faza 2D implementirana (ADR-0028): pokriće s ledgerom i projekcijom, limiti uz lock članstva, P1 `MembershipAction`, horizont, pauza/otkaz/kraj/dug, blokada rezervacije s grantom, preskakanje i naknadno dodavanje člana grupe. Implementacijski izbori 1–14 zapisani u "Faza 2D" i čekaju potvrdu. Build bez grešaka; svi testovi prolaze (1198/1198). | Sljedeća faza: 2E (cjenovna pogodnost / mehanizam prilagodbi). |
| 2026-10-08 | (2D) Poništavanje prodaje uz automatske claimove budućih rezervacija (sukob Q24.4 i pokrića pri prodaji) | Vrati buduće claimove. (1) Poništavanje blokira samo stvarno korištenje: aktivna uplata, claim na sesiji koja je počela, ili propali kredit (kasni otkaz / izostanak uz ForfeitCredit). (2) Claimovi budućih termina se vraćaju (storno), termini postaju nepokriveni s razlogom `MembershipVoided` i ponovno se evaluiraju kao da članarine nikad nije bilo (normalna naplata; paket i dalje na Completed). (3) Odgovor vraća popis pogođenih termina za recepciju. | Precizira Q24.4 ("nijednog claima" = nijednog korištenja). Storno razlog `MembershipVoided`, upozorenje s popisom termina; implementacijski izbor 12 zamijenjen. |
| 2026-10-08 | Pregled 2D — izbor 1 (projekcija bez FK) | Prihvaćeno uz test konzistentnosti: brisanje termina, sudjelovanja ili serije ne ostavlja zapise o pokriću ni aktivne stavke evidencije kredita bez sudjelovanja; pokriti i druge putanje brisanja. | Sve putanje (brisanje termina istog dana, uklanjanje sudjelovanja/segmenta, uklanjanje člana grupe) vraćaju claim prije brisanja; integritetna zaštita u `ParticipationHistory`; test konzistentnosti i test zaštite. |
| 2026-10-08 | Pregled 2D — izbor 5 ("odbij" samo za osoblje) | Prihvaćeno za P2. Kad dođe online booking, "odbij kad je limit pun" vrijedi i za rezervacije koje radi klijent (Q4 t.3, razlikovanje kanala). | Zapisano za fazu online bookinga. |
| 2026-10-08 | Pregled 2D — izbor 8 (ForfeitCredit uz događaj bez naknade) | Ne prihvaćeno: ForfeitCredit uz događaj BEZ naknade troši kredit samo ako ga je organizacija izričito odabrala za taj događaj; default za događaj bez naknade = bez posljedice (kredit se vraća). Studio koji ne kažnjava kasni otkaz ne smije nesvjesno kažnjavati samo članove. Provjeriti kako se default danas postavlja i predložiti izmjenu. | Nalaz: default je bio na tri mjesta (default stupca, CLR default entiteta, `?? ForfeitCredit` u servisu), pa je i neutralna zadana politika svake organizacije trošila kredit. Izmjena: jedno pravilo `CancellationPolicyRules.MembershipActionFor` (izričit izbor > bez naknade → ReturnCreditChargeFee > ForfeitCredit), default stupca uklonjen, postojeće verzije prebačene (migracija `20261027000011`), razlog `CreditReturned`. |
| 2026-10-08 | Pregled 2D — izbor 10 (povlačenje otkaza ponovno pokriva) | Prihvaćeno. | Bez promjene. |
| 2026-10-08 | Pregled 2D — izbor 11 (blokada duga, lista čekanja, novi član grupe) | Djelomično: novi član dodan u grupu s već generiranim terminima tretira se kao generiranje (Q18: preskače se, Q53 vraća nakon plaćanja); lista čekanja — blokada pri UPISU (uz override grant), promocija postojećeg upisa prolazi bez pokrića uz obavijest recepciji. | Preskakanje u `JoinFutureOccurrences` (zapis se ponovno otvara ako postoji), blokada u `WaitlistService.Join`, obavijest o promociji nosi stanje i razlog pokrića (aditivna polja, null se ne serijalizira). Testovi. |
| 2026-10-08 | Pregled 2D — izbor 13 (objašnjivost na sudjelovanju) | Prihvaćeno, ako odgovor rezervacije vraća sudjelovanje s MembershipCoverage. | Potvrđeno: odgovori kreiranja termina, serije, dodavanja klijenta, gosta i prijelaza vraćaju sudjelovanja s `MembershipCoverage` (isti read model); test na odgovoru serije. Generiranje grupe vraća `MembershipSkips`. |
| 2026-10-08 | (2E) Oblik cjenovne pogodnosti na planu | Pravila po usluzi: (1) opseg izričit — "Sve usluge" ILI "Odabrana usluga", prazna usluga NIKAD ne znači "sve" (princip Q29); "Sve usluge" uključuje i kasnije dodane usluge. (2) Pravilo za konkretnu uslugu ima prednost pred "Sve usluge"; najviše jedno pravilo po usluzi i jedno "Sve usluge" po verziji plana. (3) Vrijedi samo u poslovnicama plana. (4) Rezultat zaokružen na 0,01, nikad ispod 0. (5) Samo nepokrivene sesije (Q2), ulazi u izbor najbolje cijene (Q1) kao jedna prilagodba. (6) Proizvodi nisu u P2 — zabilježiti za kasnije (popust za članove na proizvode). (7) Promjena pogodnosti je promjena uvjeta (nova verzija) i ulazi u klasifikaciju povoljno/nepovoljno (Q48). | Tipovi pravila: postotak popusta, fiksni iznos popusta, fiksna cijena za člana. Implementacija 2E. |
| 2026-10-08 | (2E) Kada članstvo daje pogodnost | Kao pokriće: članstvo vrijedi na datum sesije, poslovnica u opsegu plana, nije u pauzi, nije u dugu uz "ne pokrivaj"/"blokiraj"; limiti ne vrijede. (1) Uz "nastavi pokrivati" pogodnost se nastavlja. (2) Ponovna evaluacija na istim događajima kao pokriće (istek grace, plaćanje duga, pauza, otkaz, raniji izlazak, promjena plana, poništavanje prodaje) za buduće neplaćene sesije; sesija s aktivnom novčanom alokacijom zadržava cijenu po kojoj je plaćena. (3) Razlog primjene/neprimjene se zapisuje na sesiji (objašnjivost). | Implementacija 2E. |
| 2026-10-08 | (2E) Mijenja li se cijena kad se odluka o pokriću promijeni | Da, osim zaštićenih: (1) pokrivena sesija = cjenik (retail), dug 0 (Q9); nepokrivena = cjenik ili cijena s pogodnošću po Q1. (2) Zaštićene od automatske promjene: ručno postavljen iznos, sesija s aktivnom novčanom alokacijom. (3) Svaka automatska promjena cijene zapisuje staru i novu cijenu i razlog (isti događaj kao promjena pokrića), vidljivo recepciji na sesiji. | Implementacija 2E. |
| 2026-10-08 | (2E) Implementacija | Faza 2E implementirana (ADR-0029): pravila pogodnosti na verziji plana, opći mehanizam prilagodbi (najbolja cijena, evaluacija kandidata), automatska cijena uz promjenu pokrića sa zaštitama i tragom promjene. Implementacijski izbori 1–9 zapisani u "Faza 2E" i čekaju potvrdu. Popust za članove na proizvode zabilježen za kasnije. Build bez grešaka; svi testovi prolaze (1211/1211). | Sljedeća faza: 2F (provizije, Vagaro model). |
| 2026-10-08 | Pregled 2E — izbori 3 i 6 | Potvrđeno. | Bez promjene. |
| 2026-10-08 | Pregled 2E — izbor 1 (fiksna cijena viša od cjenika) | Potvrđeno, uz upozorenje pri spremanju verzije plana: ako je fiksna cijena za člana viša ili jednaka cjeniku usluge (u bilo kojoj poslovnici plana), odgovor nosi warning da se pogodnost neće primjenjivati; računa se pri čitanju plana (kao ostala upozorenja), jer se cjenik može promijeniti kasnije. | `MEMBERSHIP_BENEFIT_WITHOUT_EFFECT` (detalji: usluga, poslovnica, cjenik, cijena za člana) na čitanju plana (i u odgovoru kreiranja/objave, koji čita plan); samo pravila FixedPrice; "Sve usluge" provjerava aktivne usluge bez vlastitog pravila. Test. |
| 2026-10-08 | Pregled 2E — izbor 4 (PriceStale) | Treba jamstvo usklađivanja: (a) pozadinski posao koji redovito usklađuje sve sesije s PriceStale; (b) svaki put koji koristi cijenu za naplatu (checkout, odrađivanje, check-in) prvo usklađuje zastarjelu cijenu, nikad ne naplaćuje zastarjelu; (c) PriceStale vidljiv recepciji dok traje; test da checkout ne uzima zastarjelu cijenu. | (a) `RefreshStalePrices` u prolazu obnove; (b) `EnsurePriceCurrent` pod lockom u `AddBookingItem` i `RecordPayment`, Sync usklađuje zastarjelu cijenu i pri odrađivanju/check-inu; (c) `MembershipCoverage.PriceStale` na sesiji. Događaj promjene `PriceRefresh`. Testovi checkouta i dnevnog prolaza. |
| 2026-10-08 | Pregled 2E — #1 (upozorenje fiksne cijene) i #4 (jamstvo PriceStale) | Potvrđeno. | Bez promjene. |
| 2026-10-08 | Ograničenje: stavka sesije u otvorenom checkoutu nakon promjene cijene | Prihvaćeno za sada, uz malu zaštitu ako nije velik zahvat: pri plaćanju/zatvaranju checkouta, ako se iznos stavke sesije razlikuje od trenutnog duga sesije (automatska ILI ručna promjena cijene), odgovor nosi upozorenje s oba iznosa, a recepcija može osvježiti stavku prije zatvaranja; bez automatske promjene stavke. Ako je zahvat veći, samo zabilježiti kao otvorenu temu za checkout (vrijedi i za P1 ručnu cijenu). | Mala izmjena: upozorenje `CHECKOUT_ITEM_PRICE_CHANGED` (stavka, sudjelovanje, iznos stavke, trenutni dug) računa se pri čitanju otvorenog checkouta, pa ga nose i odgovori plaćanja i zatvaranja; aditivno polje `CheckoutDto.Warnings`. Osvježavanje = uklanjanje i ponovno dodavanje stavke (nije moguće uz aktivnu uplatu) — naredba za osvježavanje zabilježena kao otvorena tema (ARCHITECTURE §7.3). Testovi (ručna cijena bez članarine, sesija postala pokrivena). |
| 2026-10-08 | Prije 2F — provjera plana (§13, §18) i zapisa prema sažetku konačnih odluka o provizijama | Prijavi razlike prije implementacije. | Sadržajno usklađeno; zastarjeli tekst označen kao zamijenjen (plan §13.1, §13.4, §13.5, §14.1, §16.3, §18.1; zapis Q28.1, Q39, Q20/Q24.2, Q43); unique pravila usklađen na (employee, kind, subject, effective_from). Napomena: storno uplate za prodajne provizije u 2F nema putanju do P3. |
| 2026-10-08 | Osnovica uz sve prekidače isključene kod ručne cijene? | Ručni iznos. Osnovica = cijena sesije bez oduzimanja popusta i pokrića; cijena sesije = ručni iznos ako je upisan (i kad je viši od cjenika), inače cjenik; cjenik se pamti uz sesiju. Ispraviti "svi isključeni = cjenik". | Formulacija ispravljena u planu §13.2 i zapisu Q28.2. |
| 2026-10-08 | Q38 — primjena pravila na plaćenu naknadu? | Pravilo važeće na datum sesije + nadjačavanje Direct (i kad je sesija bila pokrivena članarinom/paketom); Fixed ograničen na iznos naknade; nastaje tek uz punu uplatu; storno ili oprost poništava (negativno u razdoblju storna). | "Provjera provizija prije 2F". **⇒ Nadjačavanje Direct ZAMIJENJENO (Vagaro): isto pravilo kao za sesiju.** |
| 2026-10-08 | Q42 — kada nastaje provizija na prvu prodaju članarine? | Na Checkout Complete / otpisu, prvi put kad su oba zaduženja konačna, jednom. Korisnik ima jedan izvor (članstvo); stavke MembershipCharge ga mijenjaju dok je checkout otvoren. Oba otpisana bez uplate → nema provizije. Nakon nastanka samo commissions.manage + razlog. | Plan §18.1 ("zadnja stavka") zamijenjen; "Provjera provizija prije 2F". |
| 2026-10-08 | Datum važenja i za prodajna pravila? | Da, po datumu nastanka provizije. Posljedica: kod članarine vrijedi pravilo na datum konačne naplate, ne prodaje. | "Provjera provizija prije 2F". |
| 2026-10-08 | §16.3 — promjena korisnika provizije na prodaju prije nastanka? | (1) Da, naredba izravno na članstvu izvan checkouta dok provizija nije nastala, grant `clients.memberships.sell`; nakon nastanka samo korekcija uz `commissions.manage` + razlog (Q50). (2) Nema zasebne tablice: promjena (i preko stavke checkouta) je događaj u postojećoj povijesti članstva (2B), tko/kada/s koga na koga. Kreni s 2F. | P2_PLAN §16.3 ažuriran; implementacija 2F započeta. **⇒ Precizirano: do evaluacije prve prodaje (pregled 2F).** |
| 2026-10-08 | (2F) Implementacija | Faza 2F implementirana (ADR-0030): pravila s vrstom, datumom važenja i nadjačavanjem po načinu plaćanja; osnovica po postavkama organizacije; Q38 provizija na plaćenu naknadu; provizija na prodaju za odabranog zaposlenika; prva prodaja članarine; korekcija Q50; izvještaj po događajima. Namjerne promjene ponašanja i implementacijski izbori 1–14 zapisani u "Faza 2F" i čekaju potvrdu. Build bez grešaka; svi testovi prolaze (1227/1227). | Migracija `20261027000013` primijenjena lokalno. **⇒ Nadjačavanje i prekidači pokrića ZAMIJENJENI usklađivanjem s Vagaro modelom.** |
| 2026-10-08 | Pregled 2F — izbor 2 (deaktivacija verzije) | NE fallback: deaktivacija = od datuma deaktivacije nema pravila (bez vraćanja starije verzije). Za grešku se verzija smije obrisati samo ako po njoj nije nastala nijedna provizija; tada za njezin raspon vrijedi prethodna verzija. | Deaktivacija dobiva datum; brisanje već traži da nema provizija. |
| 2026-10-08 | Pregled 2F — izbori 6 i 9 (zadani korisnik = tko dodaje stavku; obnova sprema vlastiti odabir) | Potvrđeno. | Bez promjene. |
| 2026-10-08 | Pregled 2F — izbor 8 (jednokratna evaluacija prve prodaje) | Prihvaćeno uz iznimku: ako provizija nije nastala jer NIJE BILO KORISNIKA, uz commissions.manage + razlog smije se naknadno dodijeliti korisnik i provizija tada nastaje (po pravilu važećem na izvorni datum nastanka). Isto za proizvode i pakete s praznim korisnikom. "Nema pravila" i "osnovica 0" ostaju konačni. | Nova naredba naknadne dodjele; ishod evaluacije se pamti. |
| 2026-10-08 | Pregled 2F — izbor 5 (jedinica paketa) | NE "bilo koja neograničena = 0". Neograničene usluge se ne broje u raspodjelu (dodatak paketu); vrijednost jedinice usluge = plaćena cijena × (cjenik usluge / Σ(cjenik × broj jedinica) ograničenih usluga), cjenik u trenutku prodaje paketa (zapamtiti uz paket); samo neograničene usluge → 0. Primjer: 300 € za 5 × A (40) + 5 × B (60) → A = 24 €, B = 36 €. | Snapshot cjenika po usluzi pri prodaji paketa. **⇒ OTPALO: izračun jedinice paketa ukinut (Vagaro).** |
| 2026-10-08 | Pregled 2F — izbor 12 (grant za postavke provizija) | NE organization.settings.manage: commissions.manage ili novi zaseban grant (predložiti), jer izravno mijenjaju zaradu zaposlenika. | Vidi odgovor na prijedlog. **⇒ Odlučeno: commissions.manage, bez zasebnog granta.** |
| 2026-10-08 | Provizije: kopiramo Vagaro model (zamjenjuje sve prethodne odluke o osnovici za pokrivene sesije) | Zadržava se: pravilo zaposlenik × usluga (%/fiksno, datum važenja, nema pravila = nema provizije); grupe fiksno po terminu; provizija na prodaju od naplaćenog uz "Sold By" na stavci; odrađeno po datumu Completed; prekidač "oduzmi popuste" (osnovica nakon popusta); prekidač "oduzmi popuste članstva" (osnovica nakon popusta za članove, pokrivena sesija → 0; isključen = cijena sesije); prekidači isključeni = cijena sesije (ručni iznos ili cjenik); sesija pokrivena paketom = cijena sesije; odluke pregleda 2F (deaktivacija bez fallbacka, naknadna dodjela korisnika provizije na prodaju uz commissions.manage + razlog, postavke pod commissions.manage ili zaseban grant — predložiti); Q38 ostaje. Ukida se: nadjačavanje po načinu plaćanja; prekidači "oduzmi pokriće članarinom/paketom"; izračun vrijednosti jedinice paketa (pitanje o SharedPool otpada). Poboljšanja: "bez provizije" je izričit izbor (nula nikad ne znači povratak na drugi model); jednoznačna i vidljiva pravila prednosti (API uz svaku proviziju vraća primijenjeno pravilo, razlog i neprimijenjena pravila). Ukloniti kod, migraciju i testove ukinutih dijelova; ažurirati ADR-0030, plan i record. Zasebna faza nakon P2 "Payroll po Vagaro modelu" (obračunsko razdoblje, tiered, klase, trošak usluge, napojnice, satnica ili provizija, ovlasti) — ne implementirati, zapisati u plan i pripremiti dokument s pitanjima. | Pitanja o općem pravilu i neaktivnom korisniku postavljena prije implementacije. |
| 2026-10-08 | Uvodimo li opće pravilo zaposlenika (za sve usluge) sada? | Da. (1) Vrijedi za sve INDIVIDUALNE usluge, postotak ili fiksno, s datumom važenja; grupe ostaju fiksno po terminu. (2) Pravilo za uslugu ima prednost i može biti "Bez provizije" (izričito isključuje uslugu). (3) Objašnjenje uz proviziju navodi primijenjeno i zaobiđeno opće pravilo. (4) Opće pravilo je tiered model s JEDNOM razinom (bez praga), da ga faza Payroll proširi razinama po prometu bez zamjene modela ili migracije podataka — zapisati u plan Payroll. (5) Opće pravilo za proizvode ne sada (Payroll). | Implementacija u 2F (Vagaro usklađivanje). |
| 2026-10-08 | Neaktivan korisnik provizije na prodaju u trenutku nastanka? | (1) Aktivan pri odabiru (prodaja / zadnja promjena), neaktivan pri nastanku → provizija NASTAJE njemu (isplata kroz završni obračun). (2) Neaktivan zaposlenik se ne smije moći odabrati (validacija na stavci i na članstvu). (3) Ako je ipak odabran neaktivan (stari podatak, greška) → kao "nema korisnika": provizija ne nastaje, naknadna dodjela uz commissions.manage + razlog. | Implementacija u 2F. |
| 2026-10-08 | (2F) Usklađivanje s Vagaro modelom — implementacija | Ukinuto nadjačavanje po načinu plaćanja, prekidači pokrića i jedinica paketa; uvedeno opće pravilo (tiered, jedna razina), "Bez provizije", objašnjenje izbora pravila, deaktivacija s datumom bez povratka, "oduzmi popuste članstva", naknadna dodjela korisnika, validacija aktivnog korisnika, postavke pod commissions.manage. Plan faze Payroll (§26) i dokument s pitanjima (`docs/payroll/PAYROLL_QUESTIONS.md`). Build bez grešaka; 1229/1229. | Migracija `20261027000014` primijenjena lokalno; izbori 3 i 13 (slučaj 3) čekaju potvrdu. |
| 2026-10-08 | Potvrde usklađivanja 2F (izbori 3, 12, 13; migracija 13) | (1) Deaktivirano pravilo za uslugu → vrijedi opće pravilo, UZ upozorenje pri deaktivaciji kad postoji opće pravilo: "Od [datum] za ovu uslugu vrijedi opće pravilo ([vrijednost]). Za isključenje usluge odaberi Bez provizije." — odgovor API-ja nosi warning za frontend. (2) Odabran neaktivan zaposlenik bez posebne obrade — potvrđeno. (3) Postavke pod commissions.manage bez zasebnog granta — potvrđeno. (4) Migracija 13 se ne mijenja; sažimanje migracija prije produkcije ostaje u planu. Zatim završni pregled P2 (samo izvještaj). | Warning `COMMISSION_SERVICE_RULE_GENERAL_APPLIES` pri deaktivaciji; izbori 3 i 13 potvrđeni. |
| 2026-10-08 | Završni pregled P2 (a–d, samo izvještaj) | Popis otvorenog/odgođenog s fazom, provjera proturječnih odluka, redoslijed ručnog testiranja, potrebe frontenda. | `P2_ZAVRSNI_PREGLED.md`. Zamijenjene odluke u recordu označene (spec, Q6, Q9, Q17, Q24.4, Q25, Q28, Q31, Q38, Q39, Q43, opće pravilo prodaje, §18) + oznake ⇒ u 16 redaka dnevnika; "sažimanje migracija prije produkcije" nije bilo zapisano — dodano u plan §27 i ARCHITECTURE §7.4. |
| 2026-10-08 | Alat za testiranje vremena prije ručnog testiranja (samo Development) | Naredba za prolaz obnove za zadani datum i/ili pomicanje sata organizacije; predložiti izvedivije; endpoint fizički nedostupan izvan Developmenta (ne samo grant); tokovi (c) koriste alat. Uz to: 19 nepotvrđenih izbora podijeliti na tehničke (skupna potvrda) i vidljive korisniku (tablica s primjerom). | Prijedlog: prolaz obnove za datum (servis već prima datum); pomicanje sata = zaseban tehnički zadatak (237 poziva `UtcNow`). Implementirano: `POST /api/dev/time/membership-renewal-run` (kontroler se ne registrira izvan Developmenta). |
| 2026-10-08 | Potvrde 19 izbora (a tehnički, b vidljivi) i alata za vrijeme | (a) 6 tehničkih potvrđeno skupno. (b) 13 potvrđeno uz dopune: 2F-11 — kad korekcija prebacuje proviziju na zaposlenika bez pravila, odgovor PRIJE izvršenja vraća upozorenje da nova provizija neće nastati, korisnik potvrđuje svjesno (predložiti najjednostavniji oblik); 2F-7 — preciziraj da je 20 € OSNOVICA (provizija = pravilo primijenjeno na 20 €). Alat za vrijeme prihvaćen; TimeProvider kao tehnički dug prihvaćen — potreban i za ručno testiranje P1 otkaznih prozora. Nakon 2F-11 i 2F-7 P2 je zaključen; nova faza samo na nalog. | 2F-11: parametar potvrde `ConfirmWithoutCommission` na korekciji; bez njega `COMMISSION_REASSIGN_WITHOUT_RULE` (409) s detaljima i bez ikakve promjene. |
