# Prvi klijent — trenutni alati i mapiranje na DuneLight

> 2026-10-09. Opis STRUKTURE tablica koje prvi klijent danas koristi i njihovo mapiranje na DuneLight. Datoteke sadrže stvarne
> osobne podatke i **ne idu u repozitorij** (lokalno samo u `/klijent-podaci/`, ignorirano u `.gitignore`). Ovaj dokument ne
> sadrži nijedan osobni podatak: samo zaglavlja, vrste podataka, kodove i brojeve redaka. Pitanja za klijenta: P-22 i dalje u
> [POVRATNE_INFORMACIJE_v1.md](POVRATNE_INFORMACIJE_v1.md).

## Kontekst
- Prvi klijent traži osnovni tier; online booking, Stripe i fiskalizacija se ne rade dok prvi klijent ne koristi platformu.
- Širina sustava se **ne smanjuje**: drugi klijent (veća organizacija, danas koristi Vagaro) treba širi opseg. Isti backend i
  frontend služe obojici; razlika je samo u tome što tko vidi (frontend FE-ADR-0006, FE-ADR-0007).
- **Cilj F1:** prvi klijent može prestati koristiti svoje Excel tablice. Ono što backend ima, a klijent ne koristi, mora raditi,
  ali se ne dotjeruje.
- Studio ima **dvije lokacije** (u tablicama "Ambient" i "Sunset"; u blagajni oznake `amb` / `sun`, polog S / polog A).

## 1. Opis alata (struktura)

| # | Alat | Struktura |
|---|---|---|
| 1 | **Baza klijenata** (1 datoteka, 2 lista) | List "Ambient": jedan red = klijent (~2.790 zapisa od 2004.). Stupci: broj člana (cijeli broj, uglavnom jedinstven i rastući, najveći reda veličine ~2,8 tisuća; ~46 rupa, 3 duplikata, nekoliko nebrojčanih vrijednosti), ime, prezime, datum rođenja (`d.m.yyyy.`, nekoliko tekstualnih i neispravnih), telefon (rijetko), mobitel (više formata), e-mail (dio kao `HYPERLINK` formula), zanimanje (slobodan tekst), kvačice **radionice** (interes), **foto/video** (pristanak), **rođendani** (pristanak na čestitke; ispunjeno samo za manji dio), zabilješka. List "Rođendani": formule nad prvim listom (broj, rođendan kao `d.m.`, ime, prezime, mobitel) + ručna kvačica "Suglasnost" — pristanak na čestitke vodi se na dva mjesta, neusklađeno. |
| 2 | **Tjedni raspored** (1 list = 1 tjedan, ~30 listova) | 6 blokova (pon–sub), u svakom red datuma, red s 4 zaposlenika (stupci) i 15 redova sati 07–21 (puni sat). Ćelija = ime klijenta + kod vrste; početak koji nije na puni sat upisan je tekstom u ćeliju sata (npr. :10, :15, :30, :40, :45). Kodovi: `G`/`g` (grupa), `b`, `r`/`R`, `Y`/`y` (vjerojatno yoga), `tecar`, `bow`, `konz`, `kozm`, `lifting`, `mas`, `friz`, `ol`, `terapija`, `D i R`, `xxxx` (blokirano), `PRAZNIK`, `ZG`, `god`, `?` (nepotvrđeno), dvije osobe u istom terminu. Trajanje, prostorija i resurs se ne bilježe. |
| 3 | **Evidencija po grupama** (1 list = 1 grupa, 11 grupa) | Naziv lista = instruktor (ili vrsta, npr. yoga) + dan(i) + vrijeme. Red 1 = tjedni termini grupe (najčešće 2× tjedno); stupac A = redni broj mjesta (kapacitet ~8–10); ćelije = **stalni polaznici** po terminu. Napomene "+ č 19h" (dolazi i u drugi termin), "3x". **U datoteci nema dnevne prisutnosti** (pitanje P-28). |
| 4 | **Plaćanja po danu** (1 list = 1 mjesec) | Blok po danu; red = klijent + iznos u stupcu kategorije **Grupe / Individ. / Masaža** (red može imati dvije kategorije); lokacija samo kao oznaka u imenu (`amb`/`sun`); nema načina plaćanja ni zbrojeva. Tipični iznosi: grupe 50 (i 30/40/70/90), individualno 240 (i 160/320/640/960 — vjerojatno paketi), masaža 10–50. |
| 5 | **Mjesečni pregled blagajne** (snimka zaslona; 1 list = 1 mjesec) | Red = dan; stupci **Grupe, Individ., Masaže, Polog S, Polog A, Gotovina, vrsta, sunset, kuća, ukupno**; zbroj po stupcu na dnu mjeseca. |
| 6 | **Evidencija rada osoblja** (1 list = 1 mjesec) | Red = radni dan (pon–pet); po zaposleniku (3) dva stupca: **"smjena"** + **"bowen"** ili **"rec/dvok"**; ćelija = broj (0–9) ili kod `bo` (bolovanje) / `go` (godišnji); zbroj po stupcu i po zaposleniku za mjesec. Osnova za plaću. |
| 7 | Troškovnik (1 list = 1 godina) | Opis, mjesečni i godišnji iznos, po lokaciji. **Izvan opsega DuneLighta.** |

## 2. Mapiranje na DuneLight

| # | Alat | Zamjena u DuneLightu (ekran / funkcija) | Backend | Što nedostaje |
|---|---|---|---|---|
| 1 | Baza klijenata | Klijenti (popis, obrazac, broj člana K1, rođendani, oznake) | **Postoji** (klijenti, automatski broj člana od najvećeg postojećeg, `GET /api/clients/birthdays`, oznake) | **Uvoz** (CSV/Excel) s postojećim brojevima; **pristanci** foto/video i čestitke za rođendan (sada samo GDPR); interes "radionice" → oznaka klijenta (postoji) |
| 2 | Tjedni raspored | Raspored po zaposleniku (dan/tjedan, stupac po zaposleniku), upis termina, pauze, praznici, odsutnosti | **Postoji** (termini sa segmentima, bilo koji početak, radno vrijeme, praznici, roster odsutnosti, pauze) | Usluge kataloga za kodove (`b`, `r`, `tecar`, `bow`, `konz`…, P-25); ništa na backendu |
| 3 | Evidencija po grupama | Grupe sa slotovima i stalnim polaznicima, generiranje termina, prisutnost | **Postoji** (grupe, slotovi, članovi, generiranje, prisutnost, lista čekanja, kapacitet) | Ništa na backendu |
| 4 | Plaćanja po danu | Naplata (checkout) po terminu / klijentu, način plaćanja | **Postoji** (checkout, uplate s načinom: gotovina/kartica/transfer/ostalo, alokacije) | **Zajedničko plaćanje** dva klijenta → K3 |
| 5 | Mjesečni pregled blagajne | Pregled blagajne (dan / mjesec, po kategoriji i poslovnici) | **Ne postoji** (dashboard ima samo dnevni zbroj za jednu poslovnicu; uplata nema poslovnicu ni kategoriju, izvode se preko checkouta i stavke; usluga nema kategoriju izvještaja) | Izvještaj blagajne; **kategorija usluge** za izvještaj; **polog gotovine** i podizanje (vlasnik) |
| 6 | Evidencija rada | Izvještaj odrađenog po zaposleniku (broj po vrsti usluge, dan, mjesec) + roster (smjena, bolovanje, godišnji) | **Djelomično** (roster i pregled rostera postoje; termini po zaposleniku bez raspona i zbroja; provizije nisu to) | Izvještaj odrađenog (broj termina po zaposleniku × vrsti × danu/mjesecu) |
| 7 | Troškovnik | — | — | Izvan opsega |

## 3. Provjera backenda (zadatak 2) i prijedlozi

| Funkcija | Stanje | Prijedlog | Kada |
|---|---|---|---|
| **Uvoz klijenata** (CSV/Excel) uz očuvanje brojeva članova | **Ne postoji.** Automatski broj (K1) nastavlja od najvećeg postojećeg, uključujući ručno upisane — uvoz se uklapa bez promjene pravila. Ručni broj veći od max+1000 traži potvrdu (pri uvozu svjesno potvrđeno). | Zaseban alat uvoza (backend naredba + ekran), grant `clients.import`, izvještaj o odbijenim redovima (duplikat broja, neispravan datum, e-mail duplikat), mapiranje kvačica na pristanke/oznake. Datoteke klijenta samo lokalno. | **Uz F1** (prije prelaska s Excela); odluka o opsegu (P-27) |
| **Polog gotovine** (po poslovnici i danu) i podizanje gotovine (vlasnik) | **Ne postoji** (nema blagajne ni kretanja gotovine) | Mala domena "blagajna": kretanje gotovine po poslovnici i danu (polog u banku, podizanje, početno stanje), audit; stanje gotovine = gotovinske uplate − polozi − podizanja | **Uz F1**, zasebna mala backend faza prije ekrana blagajne (nova odluka/ADR; značenje stupaca P-22/P-23) |
| **Dnevni i mjesečni pregled blagajne** po kategoriji i poslovnici | **Ne postoji** | Izvještaj nad uplatama (datum u zoni poslovnice, poslovnica preko checkouta, kategorija preko stavke → usluga/paket/članarina/proizvod, način plaćanja) + **kategorija usluge za izvještaj** (npr. Grupe / Individualno / Masaže; zadana iz `ExecutionMode`, prilagodljiva po usluzi); grant za pregled | **Uz F1** (ista mala faza kao polog) |
| **Izvještaj odrađenog po zaposleniku** | **Djelomično** (roster da, broj termina po vrsti ne) | Izvještaj: odrađena sudjelovanja / termini po zaposleniku × usluzi (ili kategoriji) × danu, zbroj za mjesec; uz broj smjena i odsutnosti iz rostera; neovisno o provizijama; grant za pregled (own/all) | **Uz F1** (mali); plaća ostaje Payroll |
| **Pristanci** osim GDPR-a (foto/video, čestitke) i interes (radionice) | **Ne postoji** (samo GDPR; oznake klijenta postoje) | Pristanci kao izričita polja klijenta (zastavica + datum, audit kao GDPR, T1-9); interes "radionice" kao oznaka klijenta (bez promjene) | **Uz F1** (mali) |
| **Popis rođendana** (tjedan / mjesec) | **Postoji** (`GET /api/clients/birthdays?from&to`, prijelaz godine, 29. 2.) | Frontend računa raspon iz `GET /api/organization/clock`; uz popis prikazati pristanak na čestitku | F1 (samo frontend) |
| **Zajedničko plaćanje** dva klijenta | **Ne postoji** (`CHECKOUT_ITEM_CLIENT_MISMATCH`) | Model nositelj + podračuni (P-14, potvrđen) | **K3** |

## 4. Odluke i odgovori klijenta (2026-10-09)

Odgovori na P-21 – P-30 su u `POVRATNE_INFORMACIJE_v1.md`. Za čitanje tablica: **mjesečni pregled blagajne miješa kućni budžet
i poslovanje** ("vrsta" i "kuća" su kućni troškovi vlasnice) — DuneLight pokriva samo poslovni dio, tablice se ne preslikavaju 1:1.

| Tema | Odluka |
|---|---|
| Blagajna | Mala backend faza (prijedlog naziva **B1 — Blagajna**, `docs/b1/`, s ADR-om), gotova prije F1-6: poslovnica i kategorija na uplati, kategorija usluge za izvještaj, isplate iz blagajne, dnevni i mjesečni pregled. Vrste isplate: **Polog u banku** i **Isplata vlasniku** (zadani nazivi); naziv vrste mijenja svaka organizacija (npr. "Dividenda"), sustav razlikuje vrstu po kodu, izvještaji prikazuju naziv organizacije; sustav ne tvrdi ništa o pravnoj ni poreznoj prirodi isplate. Uz poslovnicu, datum, iznos, napomenu i audit. Treća vrsta **Ostalo** za sitne izdatke iz gotovine (npr. potrošni materijal), napomena obavezna (bez nje se odbija); naziv mijenja organizacija. Bilježi se samo izlaz gotovine radi slaganja blagajne — DuneLight nije knjigovodstvo troškova. |
| Uvoz klijenata | Alat s grantom `clients.import`; uvoze se svi klijenti od 2004., brojevi članova se zadržavaju; izvještaj o duplikatima broja člana i neispravnim datumima (bez tihog ispravljanja); koji popis čestitki vrijedi odlučuje se pri uvozu. **Stvarni podaci uvoze se samo u produkciju pri go-liveu, nikad u testno okruženje**; provjere ★1 i ★2 rade na seedu. |
| Pristanci | Foto/video (objava) i čestitke za rođendan kao polja klijenta s auditom (kao GDPR); radionice kao oznaka klijenta. |
| Izvještaj odrađenog | Broj termina **i sati** po zaposleniku i vrsti usluge (dan, mjesec); uključuje sate administracije iz rostera. |
| Administracija ("recepcija" u evidenciji) | Vrsta rada u rosteru (tip koji organizacija dodaje sama, npr. "Administracija"), ne uloga. |
| P-21 | Provizija i za praznu grupu — bez promjene (dug B zatvoren). |
| 12.3 | Razlog otkaza neobavezan; različito postupanje → otpis naknade, kasnije olakšice po klijentu (P1+, P-10). |
| Usluge, cijene, paketi, članarine | Konfigurira vlasnik projekta pri postavljanju organizacije. |

## 5. Grupe ovlasti za prvog klijenta (potvrđeno, uz pravila; testni podaci u seedu, ne predložak)

Pravilo: **korekcije nakon zatvaranja** (`appointments.corrections.*`), **otpisi** (`appointments.policy.fee.waive`,
`appointments.policy.unit.waive`), **`appointments.availability.override`** i **svi `*.write.past`** grantovi su samo u grupi
Vlasnik; dodaju se drugima ako ★1 pokaže potrebu. Grupa "Recepcija" otpada (P-30). Paketi i članarine su uključeni (P-26). Neki treneri rade termine i naplatu i za druge
trenere → **dvije trenerske grupe** (odluka 2026-10-09). Sve tri grupe su u seedu ("Osnova" i "Puni demo"), s po jednim
korisnikom (`DemoSeedService.FirstClientGroups`); grantovi izvještaja i blagajne dodaju se s fazama B1 / izvještaja.

| Grupa | Grantovi | Opseg |
|---|---|---|
| **Vlasnik** (nije Admin grupa) | `clients.view/manage/status.manage`, `clients.tags.view/manage`, `clients.packages.view/manage/write.past`, `clients.memberships.view/sell/cancel/pause/plan-change`, `appointments.view/write.all/arrival.mark`, `appointments.corrections.completed/no-show/cancelled`, `appointments.policy.fee.waive/unit.waive`, `appointments.availability.override`, `schedule.breaks.view/write.all`, `groups.view/manage`, `groups.attendance.view/all`, `checkout.view/manage`, `catalog.companies/services/price-list/rooms/packages/memberships.view/manage`, `catalog.cancellation-reasons.view/manage`, `employees.directory.view`, `employees.view/manage`, `roster.types.view/manage`, `roster.entries.view/write.all/write.past`, `roster.reviews.team.view`, `roster.reviews.personal.view.all`, `roster.templates.view/manage`, `dashboard.view`, `permissions.view/manage`, `permissions.assignments.manage`, `organization.settings.view`, izvještaji i blagajna (grantovi dolaze s fazama: `reports.cash.view`, `cash.movements.manage`, `reports.work.view.all`) | all |
| **Trener** | `appointments.view`, `appointments.write.own`, `appointments.arrival.mark`, `schedule.breaks.view/write.own`, `groups.view`, `groups.attendance.view/own`, `clients.view`, `clients.packages.view`, `clients.memberships.view`, `employees.directory.view`, `roster.entries.view/write.own`, `roster.reviews.personal.view.own`, `reports.work.view.own` (s fazom izvještaja); bez naplate | own |
| **Trener + recepcija** | `appointments.view/write.all/arrival.mark`, `schedule.breaks.view/write.own`, `groups.view`, `groups.attendance.view/all`, `clients.view/manage`, `clients.tags.view`, `clients.packages.view/manage`, `clients.memberships.view/sell`, `checkout.view/manage`, `catalog.services.view`, `catalog.price-list.view`, `employees.directory.view`, `roster.entries.view/write.own`, `roster.reviews.personal.view.own`, `roster.reviews.team.view`; dnevna blagajna i polog (s fazom B1) | all (termini, prisutnost, naplata) |
