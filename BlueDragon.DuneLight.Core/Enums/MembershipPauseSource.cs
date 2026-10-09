namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// K1-8 — tko je zadao pauzu. Client: klijentova pauza (pravila plana, limiti). CompanyClosure: sustavna pauza "članstvo stoji"
/// dok su sve poslovnice opsega plana neaktivne — otvara je i zatvara obnova, ne troši klijentove limite pauza, a dok traje
/// klijent ne može zadati novu pauzu.
/// </summary>
public enum MembershipPauseSource
{
    Client,
    CompanyClosure
}
