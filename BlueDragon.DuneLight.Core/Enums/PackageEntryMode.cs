namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// SharedPool: ulasci se troše iz zajedničkog fonda na razini paketa.
/// PerService: svaka usluga u paketu ima vlastiti brojač ulazaka.
/// </summary>
public enum PackageEntryMode
{
    SharedPool,
    PerService
}
