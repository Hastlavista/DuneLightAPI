# Kako radimo s Claudeom

## Osnovni ciklus za novu funkcionalnost

1. **Planiranje (Claude Code, plan mode)**
   Opiši što želiš. Primjer prompta:
   > Želim dodati [funkcionalnost]. Pročitaj ARCHITECTURE.md i relevantni kod.
   > Predloži plan: koje datoteke se mijenjaju, na što sve utječe, rizike,
   > i treba li nova arhitektonska odluka. Nemoj još ništa implementirati.

2. **Pregled plana**
   Pročitaj plan, postavi pitanja, traži alternative. Tek kad se slažeš, odobri.

3. **Implementacija**
   Claude Code implementira, pokreće build i testove. Pitanja i odgovori tijekom rada bilježe se u zapis faze
   (vidi "Zapis faze" niže).

4. **Ažuriranje dokumenata**
   > Ažuriraj ARCHITECTURE.md prema ovoj promjeni. Ako smo donijeli novu
   > arhitektonsku odluku, napiši ADR prema predlošku u docs/decisions/.

5. **Commit** koda i dokumenata zajedno.

## Zapis faze (`docs/<faza>/`)

Svaka faza implementacije (npr. P1, P2, ili manji samostalni segment) ima svoju mapu, npr. `docs/p1/`:
- `<FAZA>_DECISION_RECORD.md` — zaključane odluke faze (opseg, pravila, schema/API, namjerne promjene ponašanja, dug).
  Za fazu ima prednost pred sažetim ADR-ovima.
- Na dnu recorda **dnevnik odluka tijekom implementacije**: svako pitanje koje Claude postavi tijekom rada i tvoj odgovor
  (datum — pitanje — odgovor — posljedica). Upisuje se odmah, u istoj sesiji u kojoj je odgovor dan.
- Po potrebi audit ili plan faze (npr. `<FAZA>_AUDIT.md`) u istoj mapi.

Faze završene prije uvođenja ovog načina rada (S1–S3, D1–D3, M0–M1H) nemaju zapis; njihove odluke su sažete u ADR-ovima.
Trajne arhitektonske odluke i dalje dobivaju i ADR u `docs/decisions/`.

## Build, testovi i PR
- Build i testove pokreće Claude nakon implementacije i navodi rezultate u završnom izvještaju.
- PR-ovi idu na granu `development-claude`.

## Kad koristiti browser chat (claude.ai)
- Šire razmišljanje prije nego što postoji konkretan zadatak
- Uspoređivanje tehnologija i pristupa
- Učenje i objašnjenja koncepata
- Ako koristiš Project na claude.ai, povremeno ubaci svježi ARCHITECTURE.md
  u Project knowledge da ne radi sa zastarjelim stanjem.

## Pravila
- Novi chat / nova sesija po funkcionalnosti. Znanje je u dokumentima, ne u povijesti chata.
- Ako Claude predloži nešto što se kosi s ARCHITECTURE.md, to je znak za ADR, ne za tiho odstupanje.
- Kad dokument zastari, to je bug. Popravi ga odmah.
