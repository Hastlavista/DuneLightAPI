# ADR-0030: P2 — Provizije po Vagaro modelu (proširuje ADR-0010)

- **Status:** Prihvaćeno, implementirano 2026-10-08 (faza 2F, migracije `20261027000013` i `20261027000014`)
- **Datum:** 2026-10-08 (P2 Decision Record: Q28, Q38 – Q43, Q50 – Q52, pregled 2F, "kopiramo Vagaro model")

> Izvor: [P2 Decision Record](../p2/P2_DECISION_RECORD.md). Za P2 detalje record ima prednost.

## Kontekst
ADR-0010 računa proviziju za odrađeno od konačne cijene sudjelovanja, bez povijesti pravila, postavki osnovice i znanja o
članarinama. Provizija na prodaju ide onome tko zatvara checkout. Studio treba sam odlučiti što je osnovica. Treba i proviziju na
prvu prodaju članarine i odabir zaposlenika koji dobiva proviziju na prodaju.

Odluka 2026-10-08: DuneLight provizije rade kao Vagaro (payroll/commission). Prvi prijedlog 2F zamijenjen je jednostavnijim
Vagaro modelom. Ukinuto je nadjačavanje po načinu plaćanja, prekidači "oduzmi pokriće članarinom/paketom" i vrijednost jedinice
paketa.

## Razmotrene opcije
1. **Postotak od naplaćenog (Allocated)** s periodičnim obračunom. Odbijeno u Q28.
2. **Vlastiti model:** nadjačavanje po načinu plaćanja i jedinična vrijednost paketa (prvi prijedlog 2F). Zamijenjeno: Vagaro to
   nema, a model je teže objasniti.
3. **Vagaro model** uz dva namjerna poboljšanja: izričito "Bez provizije" i vidljiva pravila prednosti.

## Odluka
**Opcija 3.** Trenutak nastanka i identitet iz ADR-0010 ostaju nepromijenjeni.

**Pravila (Commission by Service)**
- Pravilo zaposlenik × usluga je postotak ili fiksni iznos s datumom važenja. Nema pravila = nema provizije.
- **Opće pravilo zaposlenika** (`AllServices`) vrijedi za sve individualne usluge.
  - Iznos je u razinama (`commission_rule_tiers`). U P2 opće pravilo ima točno jednu razinu bez praga.
  - Faza Payroll dodaje razine po prometu ("Tiered by Revenue") bez promjene modela ili migracije podataka.
- Pravilo za uslugu uvijek ima prednost pred općim pravilom.
- **"Bez provizije"** (`None`) je izričit izbor na pravilu za uslugu. Zaposlenik za tu uslugu ne dobiva ništa, a opće pravilo se
  ne primjenjuje. Nula nikad ne znači "vrati se na drugo pravilo".
- **Objašnjenje uz svaku proviziju** (`applied_rule_scope`, `rule_evaluation`): primijenjeno pravilo i zašto, te neprimijenjena
  pravila s razlogom (npr. "pravilo za uslugu ima prednost pred općim pravilom zaposlenika").
- **Povijest:**
  - Promjena od nekog datuma je nova verzija (`effective_from`). Bira se verzija s najvećim datumom važenja ≤ lokalni datum
    (sesija: kalendar poslovnice termina; prodaja: datum nastanka).
  - Deaktivacija ima datum (`deactivated_from` = danas): od tada pravila nema i nikad se ne vraća starija verzija. Za
    deaktivirano pravilo USLUGE od tog datuma vrijedi opće pravilo zaposlenika; odgovor deaktivacije tada nosi upozorenje
    `COMMISSION_SERVICE_RULE_GENERAL_APPLIES` ("za isključenje usluge odaberi Bez provizije").
  - Verzija po kojoj nije nastala nijedna provizija smije se obrisati. Tada za njezin raspon vrijedi prethodna verzija.
  - Jedinstvenost (zaposlenik, vrsta, predmet, datum važenja) ne ovisi o aktivnosti.
- **Grupni treninzi** ostaju fiksno po održanom terminu (= Vagaro Commission by Class). Vrijedi samo pravilo usluge; "Bez
  provizije" znači da provizije nema.
- **Vrsta pravila:** `Performance` (usluga, opće pravilo) ili `Sale` (proizvod, paket, plan članarine). Provizija na prodaju usluge
  (Q52b) i opće pravilo za proizvode nisu u P2.

**Osnovica za odrađeno (individualno)**
- Ako su prekidači isključeni (default), osnovica je cijena sesije: ručni iznos ako je upisan (ručni iznos nije popust), inače
  cjenik.
- **"Oduzmi popuste"** (Vagaro Subtract Discounts): cijena nakon prilagodbi koje nisu članarinske (oznaka, promocija).
- **"Oduzmi popuste članstva"** (Vagaro Subtract Membership Discounts):
  - cijena nakon popusta za članove (ADR-0029);
  - sesija koju članarina pokriva u cijelosti ima proviziju za odrađeno 0.
- Sesija pokrivena **paketom**: osnovica je cijena sesije (paket je način plaćanja).
- Provizija nastaje po datumu odrađivanja (Completed), ne po datumu plaćanja.
- Snapshot na zapisu: način plaćanja (informativno), izvor pokrića, cijena sesije, cjenik, ručna cijena da/ne i primijenjeni
  prekidači.

**Q38: provizija na plaćenu P1 naknadu**
- Postavka organizacije: `Never` (default) ili `WhenFeePaid`.
- Provizija nastaje kad je naknada plaćena u cijelosti. Vrijedi isto pravilo kao za sesiju.
- `Percentage` se računa od naknade, a `Fixed` je ograničen na iznos naknade.
- Storno uplate, oprost ili reverzija posljedice poništavaju proviziju. Samo individualni termini.

**Provizija na prodaju ("Sold By")**
- Provizija se računa od naplaćenog iznosa stavke. Korisnik se bira na stavci: default je aktivan zaposlenik koji dodaje stavku,
  a promjena je moguća dok je checkout otvoren (`checkout.manage`).
- Neaktivan zaposlenik se ne može odabrati, ni na stavci ni na članstvu. Odabran dok je bio aktivan, provizija nastaje njemu i
  ako je kasnije postao neaktivan.
- **Prva prodaja članarine (Q42):**
  - Korisnik je samo na članstvu i mijenja se do evaluacije: naredbom na članstvu (`clients.memberships.sell`) ili kroz stavku
    prve prodaje.
  - Provizija se evaluira jednom, kad su zaduženje prvog perioda i početna naknada konačni (Checkout Complete ili otpis).
  - Ishod se pamti (`Earned | NoRecipient | NoRule | ZeroBase`), zajedno s osnovicom.
- **Naknadna dodjela** (`POST /api/commissions/sale-assignments`, `commissions.manage` + razlog):
  - Dopuštena kad provizija nije nastala jer korisnika nije bilo (proizvod ili paket zatvorenog checkouta, ili članarina s
    ishodom `NoRecipient`).
  - Provizija tada nastaje po pravilu važećem na izvorni datum nastanka. "Nema pravila" i "osnovica 0" su konačni.
- **Korekcija nakon nastanka (Q50):** `commissions.manage` + razlog. Ako novi korisnik nema pravilo, naredba prvo vraća
  `COMMISSION_REASSIGN_WITHOUT_RULE` i ništa ne mijenja; tek uz `ConfirmWithoutCommission` stornira bez nove provizije. Inače se
  postojeća provizija stornira, a nastaje nova po pravilu
  novog korisnika na izvorni datum.
- Storno uplate poništava proviziju na prodaju. Put storna je jedan; okidači iz povrata (P3) i poništavanja prodaje (Q51a)
  dolaze kasnije.

**Postavke i izvještavanje**
- Postavke provizija (`GET/PUT /api/commissions/settings`) traže `commissions.manage`, jer izravno mijenjaju zaradu zaposlenika.
  Samo razina organizacije (Q40).
- Brojanje po događajima: zarada se broji u razdoblju nastanka, storno kao negativan iznos u razdoblju storna. Prošlo razdoblje
  se ne mijenja.

## Posljedice
- Klijent bez članarine i bez popusta: iznosi provizije za odrađeno su isti kao prije.
- `ProductSale`/`PackageSale`: korisnik je odabrani zaposlenik na stavci, a ne onaj tko zatvara checkout.
- Ukinuto i uklonjeno migracijom `20261027000014`:
  - tablica nadjačavanja po načinu plaćanja;
  - prekidač za paket;
  - zastavica nadjačavanja na zapisu.
- Prekidač "oduzmi pokriće članarinom" je preimenovan u "oduzmi popuste članstva".
- Sljedeća zasebna faza nakon P2 je **Payroll po Vagaro modelu**: obračunsko razdoblje i zatvaranje, tiered po prometu, klase,
  trošak usluge, napojnice, satnica ili provizija, ovlasti. Pitanja su u [docs/payroll](../payroll/PAYROLL_QUESTIONS.md).
- Otvoreno: provizija na prodaju usluge i obnova (Q52b), postotna grupna provizija (dug D), zaokruživanje postotka.

## Dopuna T1-10 (2026-10-09): provizija i primljeni iznos
Izvor: [T1 record](../t1/T1_DECISION_RECORD.md) (dnevnik "Tri odluke T1 (2)", sekcija T1-10). Ima prednost pred gornjim tekstom
gdje se razlikuju.

**Princip:** *Provizija nikad nije veća od iznosa primljenog za tu uslugu — izravno naplaćenog ili unaprijed plaćenog kroz paket.
Jedina iznimka je termin pokriven članarinom kad je prekidač "oduzmi popuste članstva" isključen, kao izričit izbor studija.*

- **Izravno naplaćena sesija** (`PaymentSource = Direct`): provizija za odrađeno = min(izračunata, iznos sudjelovanja). Npr. fiksno
  10 € uz ručni iznos 5 € → 5 €; uz 0 € → 0 €. Postotno pravilo (0–100 %) od uobičajene osnovice to ograničenje ne dosegne.
  Ograničenje se bilježi: `WasCapped = true`, a u objašnjenju (`rule_evaluation`) `CappedAt` i `CapReason` ("Ograničeno na
  naplaćeni iznos 5,00 €.").
- **Sesija pokrivena paketom:** osnovica je **stvarno plaćena cijena paketa po jedinici** (`ClientPackage.PaidPrice` / broj jedinica:
  zajednički fond `TotalEntryCount` ili zbroj jedinica po uslugama), zaokruženo na cent; prekidači se ne primjenjuju; bez
  ograničenja (fiksno pravilo je fiksni iznos). U objašnjenju `BaseNote` ("Osnovica je plaćena cijena paketa 60,00 € / 5 jedinica =
  12,00 €."). Prije: cijena sesije iz upisa (cjenik). Paket bez konačnog broja jedinica (neograničen) nema cijenu jedinice — tada
  osnovica ostaje cijena sesije (otvoreno, vidi T1-10).
- **Sesija pokrivena članarinom:** uz isključen "oduzmi popuste članstva" osnovica je cijena sesije, bez ograničenja (iznimka). Uz
  uključen prekidač naplaćeno na sesiji je 0 €, pa je provizija 0 i za fiksno pravilo (prije je fiksno pravilo davalo puni iznos,
  suprotno gornjem "pokrivena sesija ima proviziju za odrađeno 0").
- **Spremanje postavki** (`PUT /api/commissions/settings`) s isključenim "oduzmi popuste članstva" vraća neblokirajuće upozorenje
  `COMMISSION_MEMBERSHIP_SESSIONS_AT_LIST_PRICE` (`OrganizationCommissionSettingsDto.Warnings`).
- Izvan opsega dopune (bez promjene): grupna provizija (fiksno po održanom terminu), provizija na prodaju (od iznosa stavke),
  Q38 naknada (već ograničena na naknadu).

## Dopuna T1-11 (2026-10-09): neograničen paket, ograničenje paketne sesije, grant za čitanje
Izvor: [T1 record](../t1/T1_DECISION_RECORD.md) (dnevnik "T1-11 (2)–(4), (6)"). Ima prednost pred gornjim tekstom i dopunom T1-10 gdje se
razlikuju.

- **Sesija pokrivena paketom s cijenom jedinice** (`PaidPrice / jedinice`): provizija najviše ta vrijednost — i za fiksno pravilo (fiksno
  10 € na sesiji od 5 € → 5 €; `WasCapped`, `CapReason` "Ograničeno na vrijednost sesije iz paketa 5,00 €."). Zamjenjuje "bez
  ograničenja" iz dopune T1-10.
- **Neograničen paket** (bez konačnog broja jedinica, pa bez cijene jedinice) obračunava se **kao članarina**: uključen "oduzmi popuste
  članstva" → 0 € (i za fiksno pravilo); isključen → osnovica je cijena sesije (cjenik / ručni iznos), bez ograničenja — ista iznimka
  načela kao za članarinu. `PaymentSource` ostaje `Package`; `BaseNote` objašnjava. Zatvara "otvoreno" iz dopune T1-10.
- Upozorenje `COMMISSION_MEMBERSHIP_SESSIONS_AT_LIST_PRICE` (kod nepromijenjen) odnosi se i na neograničene pakete; `Details` =
  `{ appliesTo: ["Membership", "UnlimitedPackage"] }`.
- Potvrđeno bez promjene: članarina + uključen prekidač → 0 € i za fiksno pravilo; ograničenje izravno naplaćene sesije s popustom za članove.
- **Čitanje pravila i postavki:** `GET /api/commissions/rules`, `/rules/{id}` i `GET /api/commissions/settings` primaju `commissions.rules.view`
  ili `commissions.manage`; pisanje i dalje samo `commissions.manage` (mijenja gornju rečenicu "postavke traže `commissions.manage`" za čitanje).
