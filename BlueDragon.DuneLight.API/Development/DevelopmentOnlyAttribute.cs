using System;

namespace BlueDragon.DuneLight.API.Development;

/// <summary>
/// Kontroler postoji SAMO u Development okruženju: izvan njega ga <see cref="DevelopmentOnlyControllers"/> uklanja iz otkrivanja
/// kontrolera, pa ruta fizički ne postoji (404), neovisno o grantovima. Koristi se za razvojne alate (npr. simulacija vremena za
/// ručno testiranje članarina) koji nikad ne smiju biti dostupni u produkciji.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DevelopmentOnlyAttribute : Attribute
{
}
