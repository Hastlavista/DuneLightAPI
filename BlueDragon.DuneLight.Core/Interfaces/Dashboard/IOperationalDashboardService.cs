using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Dashboard;

namespace BlueDragon.DuneLight.Core.Interfaces.Dashboard;

/// <summary>
/// Glavni operativni read-model za reception/staff/manager — vidi OperationalDashboardDto. Čisto čitanje,
/// ne mutira ništa (bez promocije liste čekanja, čekiranja, plaćanja, zalihe — sve to ide kroz postojeće
/// module). Sve financijske/kapacitetne/dostupnostne izračune preuzima od već postojećih izvora istine
/// (BookingFinancialsCalculator/CheckoutFinancialsCalculator/WorkingHoursCalculator) — ne duplicira ih.
/// </summary>
public interface IOperationalDashboardService
{
    /// <summary>
    /// `date` = odabrani kalendarski dan (UTC, isti konvencija kao ostatak sustava — vidi RosterEntryService/
    /// AppointmentService, nema per-organizaciju timezone). Izostavljeno = tekući UTC kalendarski dan
    /// (DateTimeOffset.UtcNow.Date). Company mora pripadati organizationId (inače NotFoundAppException) —
    /// deaktivirana Company i dalje vraća podatke (Company.IsActive se samo prenosi u DTO, vidi spec section 4).
    /// </summary>
    Task<OperationalDashboardDto> GetDashboard(Guid organizationId, Guid companyId, DateTimeOffset? date);
}
