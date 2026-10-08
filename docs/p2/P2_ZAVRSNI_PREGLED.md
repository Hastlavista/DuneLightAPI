# P2 — Memberships — završni pregled (2026-10-08)

> **P2 ZAKLJUČEN 2026-10-08.** Samo izvještaj, bez implementacije. Stanje: faze 2A–2F su implementirane (ADR-0025 – ADR-0030, migracije `20261027000000` –
> `20261027000014`). Build prolazi bez grešaka, svi testovi prolaze (1234/1234).
> Izvori: [decision record](P2_DECISION_RECORD.md) (ima prednost), [plan](P2_PLAN.md), [ARCHITECTURE §7](../ARCHITECTURE.md).

---

## a) Namjerno otvoreno ili odgođeno

### P3 — Client Credit Ledger (povrati)
| Tema | Izvor | Napomena |
|---|---|---|
| Povrat novca / kredit klijenta iz zatvorenog checkouta | P2_PLAN §22, ARCH §7.3 | Danas `VoidPayment` radi samo dok je checkout otvoren. |
| Q51(a): plaćeno pa odustajanje prije početka → storno + poništavanje prodaje | Q51, §22 | Do P3 recepcija ima samo Q51(b), regularni otkaz. |
| Okidač storna provizije na prodaju iz povrata | ADR-0030, Q39, Q51.2 | Zajednički put storna (`Reverse`) postoji; okidač stiže s povratom. |
| Osvježavanje projekcije plaćenosti zaduženja nakon povrata | §22 | `RefreshChargeSettlement` |
| Djelomični povrat | Q51.4 | Nije u P2. |
| Preplata (surplus) kao kredit klijenta | P1 D7 | Danas samo informativno. |

### P4 — Obavijesti
| Tema | Izvor |
|---|---|
| Outbox događaji članarina; handleri moraju raditi iz stanja u bazi | §21.1, ARCH §7.3 |
| Obavijest klijentu i recepciji kad se pokriće primijeni naknadno ili ga nema za sve termine (limit) | Q27 |
| Obavijest klijentima o izmjeni plana "i postojeća članstva" (popis pogođenih postoji) | Q14.2 |
| Popisi za recepciju bez pushanja: termini u pauzi, dug nakon grace, preskočeni članovi grupe | Q5/Q12.5, Q15.1, Q18.4 |
| Vanjska dostava (provider, kanali, podsjetnici) | ARCH §7.3 |

### P5 — Grupe
| Tema | Izvor |
|---|---|
| Propagacija izmjena predloška na generirane termine ("samo ovaj / ovaj i budući") | ARCH §7.3, dug E |
| Predlošci grupa korisnika (npr. "Recepcija") i ovisnosti grantova u editoru | ARCH §7.3 (ovlasti) |

### Online booking (zasebna faza)
| Tema | Izvor |
|---|---|
| "Odbij kad je limit pun" i za rezervacije koje radi klijent; nadjačavanje postavke po kanalu, planu i tipu limita | Q4 t.3, pregled 2D izbor 5 |
| Aktivacija online kupnje tek nakon uspješnog prvog plaćanja (`sold_via` postoji) | Q19.2 |
| Automatska primjena paketa za individualne termine | Q30 |
| Samostalno otkazivanje klijenta; ranije otvaranje rezervacija za članove | spec "Izvan P2" |

### Stripe, računi i fiskalizacija
| Tema | Izvor |
|---|---|
| Kartice, SCA, retry; stanje zaduženja "neuspjelo" | spec "Izvan P2", Q16 |
| Račun i fiskalni zapis vezan na zaduženje (HR Fiskalizacija 2.0) | spec "Osnovno" 4 |

### Payroll po Vagaro modelu (nakon P2)
Plan §26, [PAYROLL_QUESTIONS.md](../payroll/PAYROLL_QUESTIONS.md). Uključuje:
- obračunsko razdoblje i zatvaranje (uz pravilo Q50 "korekcija ulazi u sljedeći obračun");
- tiered po prometu, kao nadogradnju općeg pravila bez migracije;
- opće pravilo za proizvode;
- klase: tiered i po polazniku;
- trošak usluge, napojnice, "satnica ili provizija";
- ovlasti: `commissions.view.own`;
- postotnu grupnu proviziju (dug D) i pitanje zarađuje li prazna ili sve-NoShow grupna sesija proviziju (dug B);
- zaokruživanje postotne provizije (ARCH §7.4).

### Zasebne odluke nakon P2 (bez dodijeljene faze)
| Tema | Izvor |
|---|---|
| Q37 — eksplicitni opseg usluga zaposlenika | Q37 |
| Q52(b) — provizija na prodaju usluge i na obnove članarine (odabir se već sprema) | Q52 |
| Plaćanje zaduženja za drugog klijenta (platitelj ≠ član) | §22, pregled 2C izbor 7 |
| Godišnja članarina po kalendarskoj godini (proporcionalni prvi period, prodaja više od mjesec dana unaprijed) | §19, dnevnik 2A |
| Naredba za osvježavanje stavke checkouta, i stavke s uplatom (`CHECKOUT_ITEM_PRICE_CHANGED` već upozorava) | ARCH §7.3, dnevnik 2026-10-08 |
| Popust za članove na proizvode | §24 |
| Sustav pogodnosti (oznaka, grupa klijenata, promo kod) kao novi kandidati u `PriceAdjustmentResolver`; fiksni prioritet po organizaciji; prikaz klijentu koja je pogodnost primijenjena | Q1, §24 |
| Rollover kredita, proporcija pri promjeni plana, penali za raniji izlazak | spec "Izvan P2" |
| Nadjačavanje postavki osnovice provizije po treneru | Q40 |
| Izvještaj otpisa (broji samo `WrittenOff`) | §22 |
| Ručna naredba "primijeni pokriće" (danas je usklađivanje automatsko) | 2D ograničenja |
| Tko smije uređivati `ActualStart/ActualEnd` | ARCH §7.3 |

### Prije produkcije
- Sažimanje migracija P2 u početnu shemu (plan §27, ARCH §7.4) i novi ADR s produkcijskim pravilima (ADR-0024).

### Implementacijski izbori koji nisu bili izričito potvrđeni (19) — SVI POTVRĐENI 2026-10-08
**a) Čisto tehnički — ne mijenjaju što vide recepcija, klijent ili zaposlenik (potvrđeno skupno):**

| Faza | Izbor | Sadržaj |
|---|---|---|
| 2D | 2 | "Čeka evaluaciju" je stanje projekcije pokrića, a ne redak ledgera kredita. |
| 2E | 7 | Evaluacija kandidata popusta se snapshotira kad se cijena računa; zaštićena sesija čuva prethodnu. |
| 2E | 9 | Shema-test tablice sudjelovanja ažuriran za nove stupce. |
| 2F | 1 | Vrsta pravila (za odrađeno ili za prodaju) se izvodi iz predmeta. |
| 2F | 5 | Iznos općeg pravila je spremljen u razini (tier); `CalculationType`/`Value` su prazni. |
| 2F | 14 | Nazivi događaja u povijesti članstva i checkouta. |

**b) Mijenjaju ponašanje vidljivo korisniku (13):** potvrđeno, uz dopune 2F-7 (20 € je OSNOVICA provizije) i 2F-11 (upozorenje
prije korekcije na zaposlenika bez pravila, `ConfirmWithoutCommission`). Vidi decision record "Faza 2F" i dnevnik.

---

## b) Provjera proturječnih odluka u decision recordu

Record sam pročitao cijeli: spec, odjeljke odluka, implementaciju 2A–2F i dnevnik. Pravilo čitanja, sada napisano u zaglavlju
recorda: zamijenjena odluka nosi oznaku **Zamijenjeno**, **Precizirano** ili **Odlučeno** uz pokazivač, a kasniji zapis u
dnevniku ima prednost pred ranijim.

**Označeno u ovom pregledu (prije bez oznake ili s nepotpunom oznakom):**

| Područje | Odluka | Oznaka |
|---|---|---|
| Provizije | Q28 točke 2, 3, 4, 6 (prekidači pokrića, jedinica paketa, nadjačavanje) | Zamijenjeno Vagaro odlukom; točke 5 i 7 vrijede. |
| Provizije | Q28.1 | Ponovno zamijenjeno: "Bez provizije" je na pravilu usluge, uz opće pravilo. |
| Provizije | Preciziranja Q28 ("BezProvizije samo kao nadjačavanje", prekidač za paket) | Zamijenjeno. |
| Provizije | Q31.4 | Odlučeno Q38. |
| Provizije | Q31 default | Precizirano pregledom 2D, izbor 8. |
| Provizije | Q9 | Provizija na članarinske sesije odlučena (ADR-0030). |
| Provizije | Q38 uz nadjačavanje Direct (provjera prije 2F) | Zamijenjeno: isto pravilo kao za sesiju. |
| Provizije | Q39 "storno prvog zaduženja poništava proviziju" | Vrijedi, uz napomenu da u P2 nema okidača. |
| Q43/Q44 | Q43 (izmjenjiv "dok provizija ne nastane") | Precizirano: do evaluacije prve prodaje, uz naknadnu dodjelu. |
| Q43/Q44 | Opće pravilo provizije na prodaju i potvrde §18 ("default naplatitelj") | Precizirano: zaposlenik koji DODAJE stavku, aktivan. |
| Q43/Q44 | Članarina | Jedini izvor je članstvo (već označeno prije 2F). |
| Pauza | Spec "pravila pauze definira organizacija" | Zamijenjeno Q5/Q12: pravila su na planu. |
| Pauza | Q12 i Q47 | Nisu proturječni: Q47 je izričito proširenje za kalendarske planove. |
| Pauza | Pregled 2B | Pauza uz zakazan završetak nije dopuštena; usklađeno s dnevnikom. |
| Dug | Spec "Default b) ili a)" | Odlučeno Q15 (grace 7 dana, "ne pokrivaj"). |
| Dug | Q15.4 i pregled 2D izbor 11 | Nisu proturječni: blokada vrijedi samo za sesije koje bi pokrila članarina u dugu. |
| Dug | Q18 | Usklađeno (novi član grupe se tretira kao generiranje). |
| Poništavanje prodaje | Q24.4 ("nijedne alokacije") | Zamijenjeno uvjetom Q51.1 i odlukom 2D (izbor 12). |
| Ostalo | Q6 (prozor kao postavka organizacije) | Zamijenjeno Q25. |
| Ostalo | Q25 "otvoreno (Q31)" | Odlučeno Q31. |
| Ostalo | Q17 "u razradi" | Odlučeno Q49. |
| Ostalo | Spec: status članstva, statusi zaduženja, ledger "+1 kod otkaza", deaktivacija plana, kalendarska obnova | Precizirano odlukama 2B, 2C, Q16, Q26/Q31 i 2A. |
| Dnevnik | 16 redaka čija je odluka kasnije zamijenjena ili precizirana | Dobili su oznaku **⇒ ...** u stupcu Posljedica: Q6, Q9, Q28 (oba), Q31, preciziranja provizija, Q39 dodatno, Q17, Q20/Q24, Q43, ispravak Q43/Q44, Q38 primjena, §16.3, 2F implementacija, pregled 2F izbori 5 i 12. |

**Nakon označavanja nema preostalih proturječja.** Dvije napomene koje nisu proturječja:
- Q16.2 "storno alokacije poništava ovisne provizije": dok checkout ne zatvori ni jedna provizija ne nastane, provizija na prodaju
  nema okidač prije P3.
- Plan §13 i §14 ostaju povijesni prijedlozi. Na vrhu §13 je oznaka da vrijede ADR-0030 i §25.

---

## c) Ručno testiranje — redoslijed tokova (Swagger / frontend)

**Priprema (jednom):**
- postavke organizacije: vremenska zona, `membership-debt`, `membership-coverage`, `GET/PUT /api/commissions/settings`;
- dva zaposlenika s korisničkim računima (recepcija, trener) i grantovi:
  - `clients.memberships.*`;
  - `catalog.memberships.*`;
  - `memberships.charges.write-off`;
  - `checkout.manage`;
  - `commissions.manage` / `.view`;
  - `appointments.policy.override`;
  - `appointments.membership-block.override`;
- usluge (individualne i jedna grupna), cjenik;
- politika otkazivanja s naknadom i `MembershipAction`;
- paket u katalogu.

> **Simulacija vremena (samo Development).** Za sve što ovisi o protoku dana (obnova, istek grace perioda, automatski završetak,
> horizont pokrića, naknadno dodavanje preskočenih članova grupe, PriceStale) koristi se razvojni alat umjesto čekanja:
> - `POST /api/dev/time/membership-renewal-run` s tijelom `{ "date": "YYYY-MM-DD" }`;
> - pokreće isti prolaz kao pozadinski servis, za trenutnu organizaciju i zadani lokalni datum (grant
>   `organization.settings.manage`);
> - idempotentno, pa se može pozivati redom za više dana ("dan po dan");
> - izvan Developmenta ruta fizički ne postoji (404, test `DevelopmentOnlyToolsTests`).
>
> Prije testiranja vremena isključiti pozadinski servis, jer bi prolaz s pravim datumom vratio stanje duga:
> `MembershipRenewalSettings__Enabled=false` (varijabla okoline) ili `"MembershipRenewalSettings": { "Enabled": false }`.
>
> Ograničenje: alat pomiče samo "danas" prolaza obnove. Čitanja (npr. `Standing` članstva), P1 otkazni prozor i "budući termin" i
> dalje koriste stvarni sat. Zato se dug provjerava na sudjelovanjima (`MembershipCoverage.Reason = DebtNotCovered`) i u
> zaduženjima, a ne samo na `Standing`. Pomicanje sata za cijelu organizaciju je zaseban tehnički zadatak (apstrakcija vremena
> umjesto 237 poziva `UtcNow`).

1. **Plan:** `POST /api/membership-plans`.
   - Pokrivene usluge, limiti (dan/tjedan/mjesec), krediti perioda, pauza, minimalna obveza, otkazni rok, početna naknada,
     pogodnost za članove.
   - Provjeriti upozorenja `MEMBERSHIP_LIMIT_WITHOUT_EFFECT`, `MEMBERSHIP_BENEFIT_WITHOUT_EFFECT` i
     `MEMBERSHIP_PLAN_NO_ACTIVE_COMPANY`.
   - Nova verzija s `applyTo` (samo nove / i postojeća) i klasifikacija povoljno/mješovito.
2. **Prodaja:** `POST /api/clients/{id}/memberships`.
   - Datum početka danas ili do mjesec dana unaprijed (`Scheduled`).
   - Poslovnica prodaje izvan plana → `MEMBERSHIP_PLAN_NOT_VALID_AT_SALE_COMPANY`.
   - Preklapanje → `MEMBERSHIP_OVERLAPPING_COVERAGE`; pun plan → `MEMBERSHIP_PLAN_FULL`.
   - Prijedlog korisnika provizije (aktivan zaposlenik).
3. **Naplata:**
   - `GET .../charges` (prvi period i početna naknada, stanje "čeka plaćanje");
   - checkout + `POST /api/checkouts/{id}/items/membership-charge`, djelomična uplata, pa ostatak, pa Complete;
   - `Standing` i `OutstandingAmount` na članstvu;
   - storno uplate u otvorenom checkoutu;
   - otpis `POST /api/membership-charges/{id}/write-off`;
   - poništavanje prodaje `void-sale` (uvjeti, `MEMBERSHIP_VOIDED_SESSIONS_UNCOVERED`).
4. **Rezervacija s pokrićem:**
   - individualni termin, ponavljajuća serija i gost grupe → `MembershipCoverage` u odgovoru (stanje, razlog, limit,
     očekivani period);
   - termin iza horizonta → `PendingEvaluation`;
   - već plaćena sesija → `AlreadyPaid`;
   - generiranje grupe;
   - pokrivena sesija ima dug 0, a checkout odbija naplatu (`PARTICIPATION_COVERED_BY_MEMBERSHIP`).
5. **Limit:**
   - iscrpiti dnevni ili tjedni limit → `LimitReached` + limit na sudjelovanju; uz postavku "odbij" → `MEMBERSHIP_LIMIT_EXCEEDED`;
   - paket kao sljedeći izvor biran pri Completed (Q30);
   - test utrke (dvije istovremene rezervacije za zadnji kredit).
6. **Otkaz:**
   - termina: na vrijeme (kredit se vraća i pokriva najraniji nepokriveni budući termin), kasno ili izostanak (`ForfeitCredit`
     ili `ReturnCreditChargeFee` + naknada), waiver;
   - članstva: `cancellation-preview`, `cancellation` (datum i razlog: period / obveza / otkazni rok), povlačenje otkaza, raniji
     izlazak `end` (grant `end-override`).
7. **Dug:**
   - prodati članstvo bez plaćanja, rezervirati termine unutar 2 perioda; `membership-renewal-run` za dan nakon dospijeća + grace
     (default 7 dana) → buduće sesije postaju `DebtNotCovered`; platiti zaduženje → pokriće se vraća odmah (bez alata);
   - "blokiraj rezervaciju" → `MEMBERSHIP_BOOKING_BLOCKED` i override grant;
   - grupa: preskočeni član (`GET /api/groups/{id}/membership-skips`), plaćanje duga → naknadno dodavanje (Q53);
   - pauza uz dug → `MEMBERSHIP_DELINQUENT`;
   - automatski završetak nakon N neplaćenih perioda: `membership-renewal-run` redom za datume obnove (N+1 perioda) bez plaćanja.
8. **Pauza:**
   - plan "od datuma kupnje" (produljenje, max dana, rolling 12 mjeseci);
   - kalendarski plan (cijeli periodi, `end-early` s potvrdom Q47);
   - otkaz buduće pauze;
   - pauza uz zakazan otkaz → odbijeno;
   - termini u pauzi su nepokriveni i nakon povratka ponovno pokriveni;
   - kraj pauze i produljenje perioda: `membership-renewal-run` za dan nakon pauze → novi period počinje pomaknuto.
9. **Obnova:**
   - `membership-renewal-run` za datum obnove → novi period i zaduženje; ponoviti isti datum → ništa novo (idempotentno);
     preskočiti nekoliko mjeseci → otvaraju se svi propušteni periodi (catch-up);
   - zakazana promjena plana i izmjena "i postojeća" primijenjena na obnovi;
   - preskočena izmjena (`plan-update-not-applied`);
   - deaktivacija plana (`memberships-ending`), `membership-renewal-run` za datum obnove → članstvo završava; reaktivacija prije
     tog datuma → nastavlja se;
   - horizont: termin dva perioda unaprijed je `PendingEvaluation`; `membership-renewal-run` za početak sljedećeg perioda → evaluira se.
10. **Cjenovna pogodnost:**
    - plan samo s pogodnošću;
    - cijena nepokrivene sesije s `PriceAdjustment` (kandidati i ishodi);
    - zaštita ručnog iznosa i već plaćene sesije;
    - `PriceStale` i `CHECKOUT_ITEM_PRICE_CHANGED`; `membership-renewal-run` (bilo koji datum ≥ danas) usklađuje zastarjele cijene.
11. **Provizije:**
    - pravila: pravilo usluge (postotak, fiksno, "Bez provizije"), opće pravilo (jedna razina), verzija s datumom, deaktivacija s
      upozorenjem `COMMISSION_SERVICE_RULE_GENERAL_APPLIES`;
    - postavke "oduzmi popuste" i "oduzmi popuste članstva";
    - odraditi direktnu, pokrivenu i paketnu sesiju → `GET /api/commissions/entries` (osnovica, snapshot, `ruleEvaluation`);
    - Q38 (`WhenFeePaid`): plaćena naknada → provizija, storno ili oprost → poništenje;
    - prodaja paketa ili proizvoda s promjenom "Sold By" → Complete → provizija;
    - prva prodaja članarine (dva checkouta, promjena kroz stavku, zabrana promjene nakon evaluacije);
    - korekcija `reassign`, naknadna dodjela `sale-assignments`;
    - sažetak po događajima kroz dva razdoblja.

---

## d) Što frontend treba od P2

### Novi endpointi
| Područje | Endpointi | Grant |
|---|---|---|
| Planovi | `GET/POST /api/membership-plans`, `GET /{id}`, `PATCH /{id}/details`, `PUT /{id}/capacity`, `POST /{id}/versions`, `POST /{id}/activate`, `POST /{id}/deactivate`, `GET /api/membership-plans/{planId}/memberships-ending` | `catalog.memberships.view/.manage/.deactivate`, `clients.memberships.view` |
| Članstva | `GET/POST /api/clients/{clientId}/memberships`, `GET /api/memberships/{id}`, `GET /api/memberships/plan-update-not-applied`, `GET .../cancellation-preview`, `POST/DELETE .../cancellation`, `POST .../pauses`, `POST .../pauses/{pid}/end-early`, `POST .../pauses/{pid}/cancel`, `POST/DELETE .../plan-change`, `POST .../end`, `POST .../void-sale`, `GET .../periods`, `GET .../charges`, `PATCH .../sale-commission-employee` | `clients.memberships.*` |
| Zaduženja | `POST /api/membership-charges/{id}/write-off`; `POST /api/checkouts/{id}/items/membership-charge` | `memberships.charges.write-off`, `checkout.manage` |
| Checkout | `PATCH /api/checkouts/{id}/items/{itemId}/sale-commission-employee` | `checkout.manage` |
| Grupe | `GET /api/groups/{id}/membership-skips` | `groups.view` |
| Postavke | `PUT /api/organization/settings/membership-debt`, `.../membership-coverage`, `.../membership-change-notice`; `GET/PUT /api/commissions/settings` | `organization.settings.manage`; `commissions.manage` |
| Provizije | `/api/commissions/rules` (nova polja), `POST /api/commissions/entries/{id}/reassign`, `POST /api/commissions/sale-assignments` | `commissions.manage` |

### Upozorenja (neblokirajuća, `warnings` u odgovoru) — prikazati korisniku
| Kod | Kada |
|---|---|
| `MEMBERSHIP_LIMIT_WITHOUT_EFFECT` | Plan: limit bez učinka. |
| `MEMBERSHIP_BENEFIT_WITHOUT_EFFECT` | Plan: fiksna cijena za člana nije niža od cjenika. |
| `MEMBERSHIP_PLAN_NO_ACTIVE_COMPANY` | Plan: nijedna poslovnica plana nije aktivna. |
| `MEMBERSHIP_PLAN_MEMBERSHIPS_ENDING` | Deaktivacija plana (broj članstava koja će završiti). |
| `MEMBERSHIP_PLAN_NOT_VALID_AT_SALE_COMPANY` | Prodaja u poslovnici u kojoj plan ne vrijedi. |
| `MEMBERSHIP_SCHEDULED_PAUSE_CANCELLED` | Otkaz ili raniji izlazak poništio je zakazanu pauzu. |
| `MEMBERSHIP_CHANGE_NOTICE_SHORT` | Rok najave izmjene plana kraći od 14 dana. |
| `MEMBERSHIP_VOIDED_SESSIONS_UNCOVERED` | Poništena prodaja; popis termina koji postaju nepokriveni. |
| `CHECKOUT_ITEM_PRICE_CHANGED` | Iznos stavke sesije razlikuje se od trenutnog duga (ponuditi uklanjanje i ponovno dodavanje). |
| `COMMISSION_SERVICE_RULE_GENERAL_APPLIES` | Deaktivacija pravila usluge, a postoji opće pravilo ("za isključenje odaberi Bez provizije"). |
| `GROUP_COMMISSION_RULE_NOT_SUPPORTED` | Postojeće upozorenje kod zatvaranja grupe. |

### Kodovi grešaka (za prijevode i UX)
- **Članarine:** `MEMBERSHIP_*` (plan, prodaja, preklapanje, datum, pauza, otkaz, promjena plana, zaduženja, dug, limit, blokada,
  korištenje, pokriće na čekanju), `PARTICIPATION_COVERED_BY_MEMBERSHIP`.
- **Provizije:** `COMMISSION_SALE_ALREADY_EARNED`, `COMMISSION_SALE_ALREADY_EVALUATED`, `COMMISSION_SALE_NOT_ASSIGNABLE`,
  `COMMISSION_ENTRY_NOT_REASSIGNABLE`, `COMMISSION_RULE_ALREADY_EXISTS`, `INACTIVE_EMPLOYEE`, `COMMISSION_REASSIGN_WITHOUT_RULE`
  (prikazati kao upozorenje s potvrdom i ponoviti korekciju s `ConfirmWithoutCommission = true`).
- Popis je u `Core/Shared/ErrorCodes.cs`.

### Polja za prikaz
**Sudjelovanje (`BookingParticipationDto`)**
- `MembershipCoverage`:
  - `Status`, `Reason` (Claimed, CreditForfeited, LimitReached, AlreadyPaid, DebtNotCovered, BeyondHorizon, Paused,
    ServiceNotCovered, CompanyNotCovered, AfterMembershipEnd, CancelledOnTime...);
  - `ChangedByEvent`, `LimitWindow`/`LimitServiceId`/`LimitMaxUses`/`LimitUsed`, `ExpectedPeriodStartsOn`;
  - zadnja automatska promjena cijene (stara, nova, događaj, vrijeme), `PriceProtectedReason`, `PriceStale`.
  - Prijedlog prikaza: oznaka "pokriveno / nije pokriveno" s rečenicom razloga.
- `PriceAdjustment`: primijenjena prilagodba i kandidati s ishodom i razlogom. Na zahtjev ("zašto ova cijena?").
- Posljedica politike: `MembershipAction`, `ClientMembershipId`, `MembershipCreditForfeited`.

**Članstvo (`ClientMembershipDto`)**
- `State`, `CurrentPeriod`, `Standing` ("čeka plaćanje" za novo neplaćeno, ne "dug"), `OutstandingAmount`;
- `PendingChange`, `DisplacedPlanUpdate`, `PlanUpdateNotApplied` (trajna oznaka), `Pauses`, `PauseAllowance`;
- `EndsOn`/`EndReason`, `SaleCommissionEmployeeId`, `FirstSaleSettledAt`.

**Zaduženje:** `Status` (izveden), `SettledAmount`, `OutstandingAmount`, `InOpenCheckout`, podaci o otpisu.

**Stavka checkouta:** `SaleCommissionEmployeeId`, `SaleCommissionFromMembership` (stavka prve prodaje pokazuje i mijenja korisnika
na članstvu). Checkout ima `Warnings`.

**Pravilo provizije:** `Kind`, `SubjectType` (uklj. `AllServices`), `CalculationType` (`None` = "Bez provizije"), `Tiers`,
`EffectiveFrom`, `DeactivatedFrom`, `Warnings`.

**Zapis provizije:**
- `AppliedRuleScope` i `RuleEvaluation` (primijenjeno pravilo i razlog, neprimijenjena pravila s razlogom) — tooltip "zašto ova
  provizija";
- snapshot: `PaymentSource`, `CoverageSourceId`, `SessionPriceAmount`, `ListPriceAmount`, `IsManualPrice`, primijenjeni prekidači,
  `WasCapped`;
- `ReversedAt`/`ReversalReason`, `CorrectionOfEntryId`, `PeriodAmount` (iznos u traženom razdoblju; storno je negativan).

**Rezultat generiranja grupe:** `MembershipSkips`.

### UX tokovi koje traži backend
- Nakon naredbe prodaje odmah otvoriti checkout sa zaduženjima prvog perioda i početne naknade (Q20.1).
- `end-early` kod kalendarskog plana: potvrda s datumima i iznosom novog perioda (Q47).
- Pri odrađivanju individualne sesije bez pokrića zbog limita predodabrati jedini prihvatljiv paket (Q30; backend ostaje
  eksplicitan).
- Odabir zaposlenika za proviziju: nuditi samo aktivne zaposlenike; kod prve prodaje članarine nakon evaluacije bez korisnika
  nuditi "naknadna dodjela" (`commissions.manage` + razlog).
- Pravila provizije: opcija "Bez provizije" po usluzi; kod deaktivacije pravila usluge prikazati upozorenje o općem pravilu.
- Za recepciju: popisi preskočenih članova grupe, termina koji su postali nepokriveni (pauza, dug, poništena prodaja) i
  članstava s neprimijenjenom izmjenom plana.
