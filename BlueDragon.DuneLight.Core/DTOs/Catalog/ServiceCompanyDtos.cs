using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

/// <summary>
/// CompanyIds je konačno, željeno stanje dostupnosti usluge — zamjenjuje cijeli popis atomično, ne
/// dodaje na postojeći. Prazan popis znači "usluga dostupna nigdje" (namjerno, vidi ServiceCompany).
/// </summary>
public class ReplaceServiceCompaniesRequest
{
    public List<Guid> CompanyIds { get; set; } = new();
}
