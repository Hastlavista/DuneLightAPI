namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// P1 (ADR-0016, D2) — tko je inicirao otkazivanje sudjelovanja. Odvojeno od korisnika koji radnju bilježi (Client +
/// CancelledBy = recepcionar znači "klijent je tražio, osoblje je upisalo"). Politika otkazivanja se evaluira SAMO za
/// Client. Nijedna naredba nema zadani initiator; System postavlja isključivo interni kod (uklanjanje člana grupe,
/// odznačavanje predloška) i nikad se ne prihvaća iz javnog zahtjeva.
/// </summary>
public enum CancellationInitiator
{
    Client,
    Business,
    System
}
