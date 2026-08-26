using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Infrastructure.Integrations;

public class PublicHolidayResult
{
    public DateTimeOffset Date { get; set; }
    public string Name { get; set; }
}

/// <summary>Vanjski, besplatni javni servis za državne praznike (trenutno nagerholidays.com REST API v3) —
/// koristi ga CompanyHolidayService.Generate kao PRVI pokušaj, prije ručne DefaultCompanyHolidays liste.
/// Best-effort po dizajnu: implementacija nikad ne baca, vraća null na bilo kakav neuspjeh (mreža, timeout,
/// neočekivan odgovor), pa pozivatelj tiho pada natrag na ručnu listu.</summary>
public interface IPublicHolidayApiClient
{
    Task<List<PublicHolidayResult>> GetPublicHolidays(int year, string countryCode);
}
