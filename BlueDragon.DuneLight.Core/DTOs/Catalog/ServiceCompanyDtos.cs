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

/// <summary>K1-6 (P-7) — zadani resurs usluge: resurs (pripada jednoj poslovnici) i količina po segmentu.</summary>
public class ServiceDefaultResourceDto
{
    public Guid ResourceId { get; set; }
    public string ResourceName { get; set; }
    public Guid CompanyId { get; set; }
    public int QuantityRequired { get; set; }
    public bool ResourceIsActive { get; set; }
}

/// <summary>K1-6 — konačno stanje zadanih resursa usluge (zamjenjuje cijeli popis; prazno = usluga nema zadanih resursa).</summary>
public class ReplaceServiceDefaultResourcesRequest
{
    public List<ServiceDefaultResourceRequest> Resources { get; set; } = new();
}

public class ServiceDefaultResourceRequest
{
    public Guid ResourceId { get; set; }
    public int QuantityRequired { get; set; }
}
