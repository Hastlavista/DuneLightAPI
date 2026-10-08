# ADR-0028: P2 — Pokriće sudjelovanja članarinom

- **Status:** Prihvaćeno, implementirano 2026-10-08 (faza 2D, migracije `20261027000009` – `20261027000011`)
- **Datum:** 2026-10-08 (P2 Decision Record: pravila pokrića 1–7, Q4, Q6, Q9, Q13, Q15, Q17, Q18, Q24.4, Q25–Q27, Q30, Q31,
  Q53, Q54 i odluke 2D)

> Izvor: [P2 Decision Record](../p2/P2_DECISION_RECORD.md). Za P2 detalje record ima prednost.

## Kontekst
Članarina pokriva sudjelovanja (Participation) u uslugama plana, uz limite po periodu i kalendarskim prozorima. Kredit se troši
na rezervaciji (Q6), a odluka mora biti deterministička, sigurna za istovremene rezervacije i objašnjiva recepciji. Klijent bez
članarine mora imati potpuno isto ponašanje kao prije (P1, settlement, provizije).

## Razmotrene opcije
1. **Status pokrića kao stupci na sudjelovanju** — jednostavno čitanje, ali strana članstva (pauza, obnova, dug) bi pisala u
   redak sudjelovanja koji prijelaz drži zaključanim, a prijelaz čeka lock članstva → deadlock.
2. **Ledger + zasebna projekcija bez FK na sudjelovanje** — ledger `membership_usages` (Claim −1 / Release +1) je izvor istine
   za potrošnju, projekcija `participation_membership_coverages` nosi odluku i razlog; obje bez FK na sudjelovanje (FK bi pri
   pisanju uzeo KEY SHARE lock na sudjelovanje).

## Odluka
- **Opcija 2.** Jedina putanja je `IMembershipCoverageService` (u pozivateljevoj transakciji):
  - **strana sudjelovanja** — `SyncParticipation` nakon svakog nastanka i prijelaza (kreiranje termina, ponavljajuća serija,
    dodavanje klijenta/segmenta, gost grupe, check-in, promocija liste čekanja, generiranje i pridruživanje grupi, otkaz,
    izostanak, korekcija, otpis), `ReevaluateParticipation` pri promjeni vremena/usluge segmenta, `ReleaseForRemoval` prije
    brisanja netaknutog sudjelovanja;
  - **strana članstva** — `ReconcileMembership` (prodaja, pauza, otkaz i povlačenje, raniji izlazak, promjena plana i objava
    izmjene za postojeće, obnova/dnevni prolaz, plaćanje/otpis/storno zaduženja, storno uplate sudjelovanja): pod lockom
    članstva stornira claimove budućih termina koji više nisu prihvatljivi, pa redom po početku termina (vrijeme rezervacije,
    Id) evaluira nepokrivene buduće potvrđene termine. Postojeći claimovi se nikad ne preraspodjeljuju; prošli termini se ne diraju.
- **No-op bez članarine:** bez claima, projekcije i neponištenog nezavršenog članstva klijenta ništa se ne čita dalje ni piše.
- **Istovremenost:** lock odgovornog članstva (FOR UPDATE) prije brojanja; redoslijed subjekti rasporeda → termin → sudjelovanje →
  članstvo. Aktivan claim najviše jedan po sudjelovanju (djelomični unique indeks).
- **Pravila evaluacije** (`Utils/MembershipCoverageRules`, čisto): odgovorno članstvo; redom kraj/početak članstva, usluga,
  poslovnica (uvjeti koji vrijede na datum termina), pauza i preskočeni period, dug (Q15), već plaćeno (2D), horizont tekući +
  sljedeći period (Q27, `PendingEvaluation` bez duga i naplate), limiti AND (Q13; Period u kalendaru organizacije, prozori u
  kalendaru poslovnice, Q17/Q22). Iskorišten limit: sljedeći izvor (Q4) ili `MEMBERSHIP_LIMIT_EXCEEDED` uz postavku "odbij"
  (samo nova rezervacija osoblja).
- **Settlement:** pokrivena usluga i pokriće koje čeka evaluaciju imaju dug 0 (`Amount` ostaje retail, Q9); isključivost s
  novcem i paketom (članarina ima prednost); `PackageCovered` ostaje samo paket.
- **P1 (Q26/Q31):** `MembershipAction` po događaju u verziji politike, snapshot na posljedici. Izričit izbor organizacije ima
  prednost; bez izbora događaj bez naknade vraća kredit (`ReturnCreditChargeFee` uz naknadu 0), a događaj s naknadom dobiva
  `ForfeitCredit` (pregled 2D #8, `CancellationPolicyRules.MembershipActionFor`, migracija `20261027000011`).
  ForfeitCredit zadržava claim; na planu s kreditima perioda kredit propada umjesto naknade (dug 0), bez kredita perioda
  naplaćuje se naknada, a mjesto u prozorima ostaje potrošeno. ReturnCreditChargeFee i otpis vraćaju claim.
- **Poništavanje prodaje (Q24.4, precizirano 2026-10-08):** blokira samo stvarno korištenje (claim na sesiji koja je počela ili
  je odrađena, propali kredit); claimovi budućih termina se vraćaju, termini postaju nepokriveni uz popis za recepciju.
- **Oslobođeno mjesto:** svaki storno claima pokreće jedan prolaz evaluacije istog članstva (najraniji nepokriveni termin koji
  prolazi sve limite, nije već plaćen i nije u dugu).
- **Dug "blokiraj rezervaciju" (Q15.4/Q18/Q53/Q54):** nova rezervacija sesije koju bi pokrila članarina u dugu traži grant
  `appointments.membership-block.override` (tada bez pokrića); generiranje grupe preskače člana i bilježi preskakanje
  (`group_occurrence_membership_skips`); nakon plaćanja/otpisa i u dnevnom prolazu član se dodaje u preskočene buduće termine
  redom po datumu dok ima mjesta (pun termin / sudar ostaje na popisu recepciji).

## Posljedice
- Objašnjivost: `BookingParticipationDto.MembershipCoverage` (stanje, razlog, događaj zadnje promjene, iscrpljeni limit,
  očekivani period) i `ParticipationPolicyConsequenceDto.MembershipAction/MembershipCreditForfeited`; obavijesti (outbox) tek s P4
  iz stanja u bazi.
- Namjerna promjena ponašanja samo za članove: F-23 (generiranje reproducira sve članove) ne vrijedi za člana u dugu uz
  "blokiraj rezervaciju" (Q18).
- Provizije se ne mijenjaju u 2D (2F).
