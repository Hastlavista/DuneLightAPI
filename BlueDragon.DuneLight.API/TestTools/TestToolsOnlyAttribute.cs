using System;

namespace BlueDragon.DuneLight.API.TestTools;

/// <summary>
/// T1 — PRIVREMENI testni alat (simulirani pomak sata, seed): kontroler postoji samo kad je izričito uključena postavka
/// <c>TestTools:Enabled</c>; inače ga <see cref="TestToolsOnlyControllers"/> uklanja iz otkrivanja kontrolera, pa ruta fizički
/// ne postoji (404), neovisno o autentikaciji. Zamjenjuje raniji [DevelopmentOnly] (ovisan samo o imenu okruženja).
/// Uklanja se prije go-livea.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TestToolsOnlyAttribute : Attribute
{
}
