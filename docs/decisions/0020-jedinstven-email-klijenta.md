# ADR-0020: Email klijenta jedinstven unutar organizacije

- **Status:** Prihvaćeno, nije implementirano
- **Datum:** 2026-10-06

## Kontekst
Stari sustav je dopuštao isti email kod više klijenata (Business Rules vodič §15.2). Target Architecture v1 §6 i sažetak
arhitekta kažu da duplikat emaila NIJE dopušten, ali u kodu nema ni provjere u `ClientService` ni unique indeksa na
`clients.email`. Korisnički računi (`users`) već imaju jedinstven email u organizaciji (`EMAIL_ALREADY_IN_USE`).

## Razmotrene opcije
1. **Dopustiti duplikate** (staro ponašanje) - jednostavno, ali otežava identifikaciju klijenta i buduće notifikacije.
2. **Jedinstven email po organizaciji** - isto pravilo kao za korisničke račune.

## Odluka
Email klijenta je jedinstven unutar organizacije (tenant granica), neovisno o velikim/malim slovima, isto kao email
korisničkog računa. Klijent bez emaila ostaje dopušten.

## Posljedice
- Provjera u servisu (kreiranje i izmjena) + DB unique indeks po (`organization_id`, `lower(email)`) za ne-NULL vrijednosti.
- Usporedba je neovisna o velikim/malim slovima (odlučeno 2026-10-06). Otvoreno za implementaciju: trimanje razmaka;
  kod greške (novi specifičan kod ili reuse `EMAIL_ALREADY_IN_USE`). Anonimizirani klijenti imaju `NULL` email, pa ne ulaze u provjeru.
- Postojeći razvojni podaci s duplikatima: baza se smije ponovno izgraditi (ADR-0003), seed ne smije imati duplikate.
