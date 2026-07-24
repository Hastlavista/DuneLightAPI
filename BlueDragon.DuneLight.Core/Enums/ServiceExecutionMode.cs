namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Jedino svojstvo kategorije usluge koje sustav mora znati programski — o njemu ovisi
/// logika termina (grupni termin veže grupu i više klijenata, individualni jednog klijenta).
/// Naziv kategorije (Bowen, Pilates, Sauna, EMS, ...) je slobodan podatak koji admin uređuje,
/// ne kod.
/// </summary>
public enum ServiceExecutionMode
{
    Individual,
    Group
}
