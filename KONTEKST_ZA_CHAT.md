# Kontekst: DuneLight — nastavak rada

> Sažetak za početak novog chata. Zalijepi ga (ili uputi Claudea na ovu datoteku) na početku sesije.
> Ažurira se na kraju svake faze, backend i frontend (obavezno, vidi oba `CLAUDE.md`).
> Zadnje ažuriranje: 2026-10-09 (T1 implementiran; F1 spreman za početak).

## Što je DuneLight
> **Klijenti (2026-10-09):** prvi klijent (osnovni tier, dvije lokacije, danas Excel) i drugi (veća organizacija, danas Vagaro).
> Isti backend i frontend za oba; razlika samo u grantovima. Online booking, Stripe i fiskalizacija čekaju da prvi klijent koristi
> platformu. Širina se ne smanjuje.

Multi-tenant sustav za vođenje manjeg fitness/wellness obrta s više poslovnica — zamjena za Excel. Organization je tenant,
Company su poslovnice. Pokriva raspored (individualni termini, grupe, lista čekanja), klijente, katalog i cjenik, pakete,
članarine, naplatu, provizije, roster zaposlenika i ovlasti. Postojeća aplikacija se **inkrementalno modernizira** (bez
prepisivanja). Sustav nije samo za fitness: **agnostičan je prema ulogama**.

| Dio | Repozitorij | Stack |
|---|---|---|
| Backend | `C:\Users\Silvio\RiderProjects\BlueDragon.DuneLight` (grana `development-claude`) | .NET 10 Web API, PostgreSQL + EF Core, FluentMigrator, xUnit nad stvarnim Postgresom |
| Frontend | `C:\Users\Silvio\WebstormProjects\BlueDragon.DuneLight` (grana `master`) | Angular 21 (standalone, signali, zoneless), PrimeNG 21, ngx-translate (hr), Vitest |

## Kako radimo
- Znanje je u dokumentima, ne u povijesti chata. Novi chat po funkcionalnosti; Claude prvo čita `CLAUDE.md` i `ARCHITECTURE.md`.
- Ciklus: plan → pregled plana → implementacija (Claude pokreće build i testove) → ažuriranje dokumenata → commit (korisnik).
- Svaka faza ima mapu (`docs/<faza>/` u backendu, `docs/f<N>/` u frontendu): plan, decision record (zaključane odluke +
  dnevnik pitanja i odgovora, upisuje se odmah), na kraju završni pregled. Decision record faze ima prednost pred planom.
- Neodlučeno poslovno pravilo → Claude STAJE i pita. Odstupanje od arhitekture → prijedlog ADR-a, ne tiho odstupanje.
- Git (commit, push, PR) radi korisnik iz Ridera/WebStorma; Claude ne radi git operacije.
- Claude smije lokalno pokretati migracije; brisanje/rebuild lokalne baze samo uz potvrdu.

## Ključni principi (backend `docs/ARCHITECTURE.md` §6)
- Participation (sudjelovanje klijenta u segmentu) je izvor istine za status, cijenu, paket i settlement; Booking i Appointment
  nemaju vlastiti status ni cijenu (ADR-0005, ADR-0006).
- Povijest se ne briše: korekcije su kompenzacijski zapisi (ADR-0007).
- Autorizacija isključivo granularnim grantovima po poslovnoj radnji; bez uloga, predložaka i raspodjele po ulogama
  (ADR-0004, ADR-0019, ADR-0023, ADR-0032).
- Uske poslovne naredbe, nikad generički `PUT` agregata (ADR-0014).
- UTC instanti, `DateOnly` poslovni datumi, efektivna zona poslovnice (ADR-0013).
- Nikad automatski birati jednog od više zaposlenika (cijena) ni implicitno izvoditi segment (ADR-0009, ADR-0011).
- Čista ciljna shema bez compatibility slojeva; migracije ne seedaju ništa (ADR-0003, ADR-0022, ADR-0024).
- **Poslovna pravila žive samo u backend dokumentima**; frontend ih referencira (FE-ADR-0001).

## Gdje su odluke
| Što | Gdje |
|---|---|
| Backend pravila rada | backend `CLAUDE.md`, `docs/WORKFLOW.md` |
| Backend arhitektura, otvorene teme (§7), dug | backend `docs/ARCHITECTURE.md` |
| Backend ADR-ovi (ADR-0001 – ADR-0035) | backend `docs/decisions/` |
| Zapisi backend faza | `docs/p1/`, `docs/p2/` (plan, record, završni pregled), `docs/k1/`, `docs/k2/`, `docs/baseline-reset/`, `docs/foundation-cleanup/`, `docs/payroll/` (pitanja) |
| Povratne informacije klijenta i plan faza | backend `docs/klijent/POVRATNE_INFORMACIJE_v1.md` |
| Što frontend treba od P2 (endpointi, upozorenja, polja, UX) | backend `docs/p2/P2_ZAVRSNI_PREGLED.md` d) |
| Frontend pravila rada | frontend `CLAUDE.md` |
| Frontend arhitektura, otvorene teme, dug | frontend `ARCHITECTURE.md` |
| Frontend ADR-ovi (FE-ADR-0001 – FE-ADR-0005) | frontend `docs/adr/` |
| Backend faza T1 (sat, testni alati, 403, Swagger) | backend `docs/t1/`, ADR-0033 – ADR-0035; što frontend treba: `T1_ZAVRSNI_PREGLED.md` d) |
| Zapisi frontend faza | frontend `docs/f1/` |

## Status faza
| Faza | Status |
|---|---|
| S1–S3, Timezone foundation, C, D1–D3B3, M0–M1H | završene (sažete u ADR-0001 – ADR-0014) |
| Foundation cleanup, baseline reset | završeni 2026-10-06 (ADR-0019 – ADR-0024) |
| P1 Cancellation / NoShow policy engine | implementiran 2026-10-06 (ADR-0015 – ADR-0018) |
| P2 Memberships (2A–2F) | ZAKLJUČEN 2026-10-08 (ADR-0025 – ADR-0030); ručno testiranje slijedi |
| K1 Dorade iz povratnih informacija | ZAKLJUČEN 2026-10-08 (ADR-0031) |
| K2 Ovlasti (granularni grantovi, zatvoren termin) | implementiran 2026-10-09 (ADR-0032), 1290/1290 testova |
| T1 Sat sustava i testni alati (backend, preduvjet F1) | implementiran 2026-10-09 (ADR-0033 – ADR-0035, `docs/t1/`, završni pregled `T1_ZAVRSNI_PREGLED.md`), 1375/1375 testova; spreman za commit |
| F1 Frontend: puno usklađivanje s backendom | cilj revidiran 2026-10-09: prvi klijent prestaje koristiti Excel (`docs/klijent/TRENUTNI_ALATI_PRVI_KLIJENT.md`); princip "jednostavno za prvog, isto za sve" (FE-ADR-0006 – 0008); temelji dizajna u F1-0, redizajn u F2 nakon prve provjere s klijentom; plan revidiran (frontend `docs/f1/F1_PLAN.md`) |

## Sljedeći koraci
1. Commit T1 (korisnik); ručno testiranje po fazama iz `docs/t1/T1_ZAVRSNI_PREGLED.md` c) ("Osnova" → … → "Puni demo");
   zatim B1 — Blagajna (backend, prije F1-6) i F1-0.
2. F1 po revidiranom redoslijedu (F1-0 temelji uklj. dizajn → klijenti → raspored i upis → naplata → ★ provjera s klijentom →
   grupe → blagajna → izvještaj odrađenog → ★ → ostalo), na grani `master` frontend repozitorija. Backend dopune za prvog
   klijenta (uvoz, blagajna i polog, pregled blagajne, izvještaj odrađenog, pristanci) — male faze na potvrdu.
3. Ključna pravila F1: frontend nikad ne koristi sat preglednika za poslovnu logiku; prikaz u zoni poslovnice; tipovi iz
   Swaggera; 403 s popisom grantova; jedna navigacija po grantovima.
4. Backend redoslijed (odluka 2026-10-09): **K3** Nositelj + podračuni → **P3** Client Credit Ledger → **P4** Notifications →
   **P5** Group occurrence propagacija → **P6** Workforce/catalog integritet → **Paketi v2 / P1+** → **Payroll**.
   K3 prije P3: povrat i kredit moraju znati platitelja.

## Otvorene teme (detalji: backend ARCH §7.3/§7.4, frontend ARCHITECTURE.md §12)
- Ovlasti: ovisnosti grantova i capability kao sloj grupiranja u editoru (čeka frontend).
- Granica za roster unatrag i za kasno označavanje sudjelovanja (postavka organizacije?).
- Checkout: osvježavanje stavke sesije nakon promjene cijene.
- Povrat novca / kredit klijenta (P3); platitelj ≠ član (K3).
- P4: obavijesti o članarinama moraju raditi iz stanja u bazi.
- Tko smije zatvoriti Group occurrence u own opsegu; provizija prazne / sve-NoShow grupe (P-21, prije Payrolla).
- Propagacija izmjena grupa "ovaj i budući" (P5); politika deaktivacije kataloga (P6).
- Na odluku (T1): fond godišnjeg na dan isteka; provizija veća od naplaćenog (T1-8 nalaz); grupe ovlasti prvog klijenta u seedu.
- Nove otvorene teme (ARCH §7.3): cijena od određenog sata, zona dana izvještaja provizija, ručni iznos (grant/razlog), GDPR
  povijest pristanka, prebacivanje prošlih termina na paket upisan unatrag, uvoz klijenata.
- Tajne u `appsettings.json`; sažimanje P2 migracija prije produkcije.
- Prije go-livea ukloniti testne alate (simulirano vrijeme, seed) — T1.
- Frontend: alat za generiranje tipova (F1-0).
