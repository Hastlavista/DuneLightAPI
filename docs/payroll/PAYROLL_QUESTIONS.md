# Payroll po Vagaro modelu — pitanja prije implementacije

> Zasebna faza **nakon P2**. Ne implementira se dok se pitanja ne zatvore. Odluke idu u `PAYROLL_DECISION_RECORD.md`
> (nastaje kad faza krene, vidi `docs/WORKFLOW.md`). Polazište je P2 2F ([ADR-0030](../decisions/0030-p2-provizije-vagaro-model.md)):
> - pravilo zaposlenik × usluga s datumom važenja;
> - opće pravilo zaposlenika kao tiered model s jednom razinom bez praga (`commission_rule_tiers`);
> - "Bez provizije";
> - objašnjenje izbora pravila;
> - Q38;
> - provizija na prodaju ("Sold By");
> - brojanje po događajima.

## Već odlučeno (2026-10-08)
- Payroll radi kao Vagaro. Opseg faze:
  1. obračunsko razdoblje i zatvaranje;
  2. tiered po prometu za usluge i proizvode;
  3. klase;
  4. trošak usluge;
  5. napojnice;
  6. satnica ili provizija;
  7. ovlasti.
- Isplatu i porez radi računovodstvo (kao Vagaro izvan SAD-a); DuneLight samo ručno označava isplaćeno.
- **Opće pravilo zaposlenika već je tiered model s jednom razinom.** Payroll dodaje razine (`from_revenue > 0`) na istom modelu,
  bez zamjene modela i bez migracije podataka. Pravilo po usluzi ili proizvodu ima prednost pred tiered pravilom.
- Opće pravilo za proizvode (provizija na prodaju) dolazi s ovom fazom.

## Pitanja

### 1. Obračunsko razdoblje i zatvaranje
1.1. Razdoblja: tjedno, dvotjedno, dvaput mjesečno, mjesečno. Je li razdoblje jedno za cijelu organizaciju ili po zaposleniku?
     Kojeg dana počinje tjedni i dvotjedni ciklus?
1.2. Što "zatvori obračun" zaključava: samo zapise provizija razdoblja, ili i satnice, napojnice i troškove?
1.3. Smije li se zatvoreni obračun ponovno otvoriti, i uz koji grant? Prijedlog: ne smije; korekcije ulaze u sljedeći obračun,
     u skladu s brojanjem po događajima iz 2F.
1.4. "Označi isplaćeno": po zaposleniku ili za cijeli obračun? Uz datum i napomenu? Je li djelomična isplata moguća?
1.5. Koji je izvoz potreban za računovodstvo (CSV ili PDF po zaposleniku, stupci)?

### 2. Tiered by Revenue (usluge i proizvodi)
2.1. Skokovito (cijeli promet po dosegnutoj razini, kao Vagaro) ili progresivno (svaka razina samo za svoj dio)? Odluka je
     ostavljena za ovu fazu.
2.2. Što ulazi u promet: cijena sesije, naplaćeno ili osnovica nakon prekidača iz 2F? Ulaze li pokrivene sesije (članarina,
     paket)?
2.3. Kad se razina određuje: na kraju razdoblja (provizije unutar razdoblja su privremene) ili kontinuirano? Kako se tada
     uklapa brojanje po događajima iz 2F?
2.4. Razine po zaposleniku (Vagaro) ili i predlošci za više zaposlenika?
2.5. Imaju li usluge i proizvodi zajedničke ili odvojene razine?

### 3. Klase (grupni treninzi)
3.1. Tiered po prometu klasa: što je promet klase (zbroj cijena polaznika, naplaćeno)?
3.2. Po polazniku: razine po broju polaznika, samo iznosi. Okidač je prijava, plaćeno ili oboje: što točno znači "oboje"?
3.3. Tiered po prometu i po polazniku se zbrajaju, a provizija po klasi (današnje fiksno po terminu) nadjačava oboje. Potvrditi
     prednost kad postoji i opće pravilo zaposlenika.
3.4. Prazna klasa ili klasa u kojoj su svi izostali: ima li provizije po klasi (dug B iz ARCHITECTURE)?
     Postavljeno klijentu kao **P-21** (`docs/klijent/POVRATNE_INFORMACIJE_v1.md`, 2026-10-09, K2). Odgovor treba **prije
     početka Payroll faze**. K2 ga ne odlučuje: korekcija polaznika ne dira grupnu proviziju (po sesiji, ADR-0030).
     **ZATVORENO 2026-10-09 (odgovor prvog klijenta na P-21):** trener dobiva proviziju i za grupni trening na koji nitko nije
     došao (prazna sesija ili svi izostali) — današnje ponašanje (provizija po sesiji pri zatvaranju), bez promjene.

### 4. Trošak usluge (business cost)
4.1. Trošak po usluzi ili varijanti: fiksan iznos ili postotak? Ima li povijest s datumom važenja?
4.2. Prekidač "oduzmi trošak": na razini organizacije (kao prekidači iz 2F) ili po zaposleniku? Kojim redom se oduzima u odnosu
     na popuste?

### 5. Napojnice
5.1. Napojnice danas ne postoje u checkoutu. Treba li prvo uvesti stavku ili alokaciju napojnice i kome pripada (izvođač,
     prodavač, raspodjela)?
5.2. Prekidač "uključi napojnice u obračun": znači li to samo prikaz u obračunu ili i ulazak u promet za razine?

### 6. Satnica ili provizija ("veće od dvoje")
6.1. Gdje je satnica (iznos po satu) i otkud sati: roster (planirano) ili stvarno odrađeno?
6.2. Uspoređuje se po obračunskom razdoblju. Vrijedi po zaposleniku ili za sve?
6.3. Ako je satnica veća, kako se prikazuju provizije: ostaju vidljive kao informacija ili se ne isplaćuju?

### 7. Ovlasti
7.1. Zaposlenik vidi svoje provizije: novi grant `commissions.view.own` (own-scope je uvijek User → Employee) uz postojeći
     `commissions.view` za sve?
7.2. Uređivanje struktura (pravila, razine, troškovi, satnice) uz posebnu ovlast. Je li to postojeći `commissions.manage` ili
     zaseban grant za strukture, uz `commissions.manage` za korekcije? Novi grantovi idu migracijom samo Admin grupama (ADR-0023).
7.3. Zatvaranje obračuna i označavanje isplate: zaseban grant (npr. `payroll.close`)?
