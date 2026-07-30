namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Individual: jedan ili više klijenata na istom terminu, naplata po terminu.
/// Group: termin generiran iz Group/GroupSlot (modul Grupe), Amount=0/IsPaid=false.
/// </summary>
public enum AppointmentForm
{
    Individual,
    Group
}
