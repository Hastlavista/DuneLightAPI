using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Dashboard;

namespace BlueDragon.DuneLight.Core.Interfaces.Dashboard;

/// <summary>
/// Glavni operativni read-model za reception/staff/manager — vidi OperationalDashboardDto. Čisto čitanje,
/// ne mutira ništa (bez promocije liste čekanja, čekiranja, plaćanja, zalihe — sve to ide kroz postojeće
/// module). Sve financijske/kapacitetne/dostupnostne izračune preuzima od već postojećih izvora istine
/// (ParticipationSettlement/CheckoutFinancialsCalculator/WorkingHoursCalculator) — ne duplicira ih.
/// </summary>
public interface IOperationalDashboardService
{
    /// <summary>
    /// `date` = odabrani kalendarski dan (T1-7: DateOnly, "yyyy-MM-dd"); izostavljeno =
    /// "danas" u efektivnoj zoni poslovnice (Company.TimeZone ?? Organization.TimeZone). Granice dana su lokalne
    /// ponoći te zone pretvorene u UTC instante (dan može imati 23/25 sati na DST prijelazu). Company mora pripadati organizationId (inače NotFoundAppException) —
    /// deaktivirana Company i dalje vraća podatke (Company.IsActive se samo prenosi u DTO, vidi spec section 4).
    /// </summary>
    Task<OperationalDashboardDto> GetDashboard(Guid organizationId, Guid companyId, DateOnly? date);
}
