# ADR-0010: Model provizija (Individual po Participationu, Group po Segmentu)

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (zatvara Decision Log v1 #38; implementirano u M1G)

## Kontekst
Izvor provizije (Participation vs. Segment vs. Sale/CheckoutItem) bio je otvoren. Uz više Employeeja na Segmentu trebalo
je odlučiti dijeli li se provizija i tko je korisnik. Korekcije moraju reverzirati provizije bez dvostrukog obračuna.

## Razmotrene opcije
1. **Jedan "primarni" Employee ili dijeljenje provizije** - uvodi koncept koji klijent nije tražio.
2. **Provizija po (izvor, Employee) s verzioniranjem izvora** - svaki Employee zasebno po svom pravilu.

## Odluka
- Cijena i provizija su odvojene.
- **Individual:** izvor je Participation. Za SVAKOG Employeeja Segmenta zasebno se traži njegovo pravilo. Nema dijeljenja,
  nema primarnog. Postotak se računa od `Participation.FinalPrice` (nakon overridea, ne od plaćenog iznosa; pokriće
  paketom ne svodi FinalPrice na 0). Identitet: Participation + Employee + SourceVersion. Completed → Confirmed reverzira
  sve aktivne; ponovni Completed stvara nove s novom verzijom.
- **Group:** izvor je AppointmentSegment. Jedna fiksna provizija po (Segment, Employee) kod close-outa, neovisno o broju
  klijenata. Postotna grupna provizija NIJE modelirana (upozorenje `GROUP_COMMISSION_RULE_NOT_SUPPORTED`). Korekcije
  sudjelovanja ne reverziraju grupnu proviziju.

## Posljedice
- Unique indeksi na `commission_entries` po (participation, source_version) i za grupni izvor.
- `PricingEmployeeId` se nikad ne koristi kao korisnik provizije.
- Otvoreno: postotna grupna provizija (dug D); zarađuje li prazna / sve-NoShow sesija proviziju; postotna provizija
  nema zaokruživanje.
- Target Architecture v1 traži i tier/vremensku logiku te provizije na prodaju paketa/membershipa — nije u ovom modelu.
