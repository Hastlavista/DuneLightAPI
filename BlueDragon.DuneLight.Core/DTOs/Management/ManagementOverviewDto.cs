namespace BlueDragon.DuneLight.Core.DTOs.Management;

/// <summary>Samo metrike koje domena danas pouzdano podržava — NAMJERNO bez "active organizations"/lifecycle
/// koncepata koji još ne postoje na Organization entitetu (vidi zahtjev).</summary>
public class ManagementOverviewDto
{
    public int TotalOrganizations { get; set; }
    public int TotalUsers { get; set; }
    public int TotalCompanies { get; set; }
}
