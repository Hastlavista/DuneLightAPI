using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Infrastructure.Integrations;

/// <summary>
/// nagerholidays.com/api/v3/publicholidays/{year}/{countryCode} — besplatan, javan, bez API ključa (vidi
/// IPublicHolidayApiClient). HttpClient se injecta preko IHttpClientFactory (BaseAddress + Timeout postavljeni
/// u Startup.ConfigureServices) — nikad ne baca, svaki neuspjeh (mreža, timeout, ne-200, neočekivan JSON)
/// hvata se ovdje i vraća null.
/// </summary>
public class NagerPublicHolidayApiClient : IPublicHolidayApiClient
{
    private readonly HttpClient _httpClient;

    public NagerPublicHolidayApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<PublicHolidayResult>> GetPublicHolidays(int year, string countryCode)
    {
        try
        {
            List<NagerHolidayResponseItem> response = await _httpClient.GetFromJsonAsync<List<NagerHolidayResponseItem>>(
                $"api/v3/publicholidays/{year}/{countryCode}");

            if (response == null)
                return null;

            return response
                .Where(h => !string.IsNullOrWhiteSpace(h.Date))
                .Select(h => new PublicHolidayResult
                {
                    Date = new DateTimeOffset(DateTime.ParseExact(h.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.Zero),
                    Name = string.IsNullOrWhiteSpace(h.LocalName) ? h.Name : h.LocalName
                })
                .ToList();
        }
        catch (Exception)
        {
            // Best-effort po dizajnu (vidi IPublicHolidayApiClient) — bilo koji uzrok neuspjeha vraća null,
            // pozivatelj (CompanyHolidayService.Generate) pada natrag na ručnu DefaultCompanyHolidays listu.
            return null;
        }
    }

    private class NagerHolidayResponseItem
    {
        [JsonPropertyName("date")]
        public string Date { get; set; }

        [JsonPropertyName("localName")]
        public string LocalName { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}
