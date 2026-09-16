using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Dashboard;

/// <summary>
/// Operativna nadzorna ploča — jedan agregirani read-model za odabranu Company/Date (vidi
/// IOperationalDashboardService). NIJE BI/reporting: ne perzistira ništa, ne mutira ništa, samo sažima
/// postojeće stanje (Appointment/Booking/Group/Roster/Waitlist/Checkout/Payment/ProductStock) preko već
/// postojećih izračuna (BookingFinancialsCalculator/CheckoutFinancialsCalculator/WorkingHoursCalculator).
/// </summary>
public class OperationalDashboardDto
{
    public DashboardCompanyDto Company { get; set; }

    /// <summary>Kalendarski dan (UTC) na koji se sve niže vezano odnosi — vidi domensku napomenu na
    /// IOperationalDashboardService za točnu granicu (StartsAt/CreatedAt &gt;= Date I &lt; Date+1 dan).</summary>
    public DateTimeOffset Date { get; set; }

    public List<DashboardScheduleOccurrenceDto> Schedule { get; set; } = new();
    public List<DashboardStaffMemberDto> Staff { get; set; } = new();
    public DashboardFinancialDto Financial { get; set; }
    public DashboardAlertsDto Alerts { get; set; }
}

public class DashboardCompanyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    /// <summary>Company je možda naknadno deaktivirana — povijesni pregled i dalje radi (vidi spec section 4),
    /// ovo je samo informativna oznaka za UI, ne blokira ništa.</summary>
    public bool IsActive { get; set; }
}

/// <summary>Jedno occurrence na rasporedu (Individual ili Group Appointment) — vidi spec section 6/7/8.
/// Individual popunjava Bookings; Group popunjava GroupSummary umjesto punog popisa klijenata.</summary>
public class DashboardScheduleOccurrenceDto
{
    public Guid AppointmentId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public AppointmentStatus Status { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid? RoomId { get; set; }
    public string RoomName { get; set; }

    public bool IsGroup { get; set; }
    public Guid? GroupId { get; set; }
    public string GroupName { get; set; }

    /// <summary>Popunjeno samo za Form=Individual — vidi spec section 7. Praznо za Form=Group.</summary>
    public List<DashboardBookingSummaryDto> Bookings { get; set; } = new();

    /// <summary>Popunjeno samo za Form=Group — vidi spec section 8. Null za Form=Individual.</summary>
    public DashboardGroupSummaryDto GroupSummary { get; set; }
}

/// <summary>Booking-razina sažetak jednog klijenta na Individual terminu — vidi spec section 7. Naplata je
/// IZVEDENA preko BookingFinancialsCalculator, ne duplicirana aritmetika.</summary>
public class DashboardBookingSummaryDto
{
    public Guid BookingId { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; }
    public BookingStatus BookingStatus { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool IsPaid { get; set; }

    /// <summary>True kad je booking stvarno paket-namiren (Booking.PackageCoverageApplied &amp;&amp; ne
    /// PackageCoverageReturned) — isto pravilo kao BookingFinancialsCalculator.IsPackageSettled.</summary>
    public bool PackageCovered { get; set; }
}

/// <summary>Agregatni kapacitet/prisutnost jednog Group occurrence-a — vidi spec section 8/43. Capacity
/// semantika ostaje ona iz GroupService/BookingService (Confirmed = buduća rezervirana mjesta), ne redefinira
/// se ovdje.</summary>
public class DashboardGroupSummaryDto
{
    public int Capacity { get; set; }
    public int ConfirmedCount { get; set; }
    public int CompletedCount { get; set; }
    public int NoShowCount { get; set; }
    public int CancelledCount { get; set; }
    public int WaitingCount { get; set; }

    /// <summary>max(0, Capacity - ConfirmedCount) — vidi spec section 8.</summary>
    public int AvailableReservationSeats { get; set; }

    /// <summary>True kad je termin već počeo/odrađen (StartsAt &lt;= sada) i i dalje postoji BAREM jedan
    /// Confirmed Booking (nije čekiran/riješen) — operativni signal za osoblje, vidi spec section 9. Ne mijenja
    /// nikakav status, samo ga prikazuje.</summary>
    public bool HasUnresolvedAttendance { get; set; }
}

/// <summary>Jedan radni interval unutar dana (HH:mm-HH:mm) — vidi WorkingHoursCalculator.Interval.</summary>
public class DashboardWorkIntervalDto
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}

/// <summary>Jedna pauza zaposlenika tog dana — sažeto (bez punog ScheduleBreakDto), vidi spec section 34
/// (dashboard izlaže samo ono što je operativno potrebno).</summary>
public class DashboardBreakDto
{
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
}

/// <summary>Tko je danas (odabrani datum) očekivano na poslu za odabranu Company — vidi spec section 11-13.
/// Reusa WorkingHoursCalculator.GetEffectiveEmployeeIntervals (isti izračun kao GetAvailableSlots), presjeknuto
/// s efektivnim intervalima poslovnice (predložak/praznik) da odgovara istim pravilima koja vrijede za
/// zakazivanje termina.</summary>
public class DashboardStaffMemberDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public bool IsActive { get; set; }

    /// <summary>True samo kad je izvor efektivne dostupnosti Apsencija (RosterType.IsAbsence) — osobna
    /// odsutnost, neovisno o tome radi li poslovnica taj dan.</summary>
    public bool IsAbsent { get; set; }

    /// <summary>True kad nakon oduzimanja Breaks od WorkIntervals (vidi WorkingHoursCalculator.SubtractIntervals)
    /// ostane barem jedna efektivno slobodna minuta i zaposlenik nije odsutan — mora se slagati s
    /// AppointmentService.GenerateSlots (isti pojam preklapanja s pauzom), da dashboard ne kaže "radi" kad
    /// zakazivanje za taj termin ne bi ponudilo nijedan slobodan slot zbog pauze koja pokriva cijeli interval.</summary>
    public bool IsWorking { get; set; }

    /// <summary>Planirani (prije pauza) efektivni radni intervali — presjek predloška/rostera zaposlenika s
    /// poslovnicom, BEZ oduzimanja Breaks (vidi IsWorking za post-pauza izračun). Namjerno zadržan kao "puni
    /// planirani raspon" jer osoblje na rasporedu obično želi vidjeti kompletnu smjenu, ne isjeckanu na
    /// pod-intervale oko svake pauze.</summary>
    public List<DashboardWorkIntervalDto> WorkIntervals { get; set; } = new();

    public List<DashboardBreakDto> Breaks { get; set; } = new();
}

/// <summary>Operativna financijska pažnja (NE računovodstvo/izvještaj o prihodu) — vidi spec section 14-18.</summary>
public class DashboardFinancialDto
{
    /// <summary>Zbroj Payment.Amount za Status=Completed, CreatedAt na odabrani datum, preko Checkouta ove
    /// Company — vidi spec section 15. Cash/payment aktivnost, NE profit/računovodstveni prihod.</summary>
    public decimal TodayRevenue { get; set; }

    /// <summary>Zbroj BookingFinancialsCalculator.CalculateOutstanding preko svih Booking redaka na rasporedu
    /// odabranog dana (isključujući Cancelled — vidi spec section 17), ne novi izračun.</summary>
    public decimal OutstandingAmount { get; set; }

    /// <summary>Broj Booking redaka (raspored odabranog dana, bez Cancelled) čiji je OutstandingAmount &gt; 0.</summary>
    public int UnpaidBookingCount { get; set; }

    /// <summary>Svi TRENUTNO Open Checkouti ove Company (bez obzira na datum kreiranja) — vidi spec section 18.</summary>
    public int OpenCheckoutCount { get; set; }

    public decimal OpenCheckoutOutstandingAmount { get; set; }
}

/// <summary>Agregirana operativna upozorenja — samo postojeći persistirani statusi, bez izvedenih/vremenskih
/// pretpostavki (vidi spec section 19-25).</summary>
public class DashboardAlertsDto
{
    /// <summary>WaitlistEntryStatus.Waiting redci na terminima odabranog dana/Company — vidi spec section 20.</summary>
    public int WaitingCount { get; set; }

    /// <summary>Booking.Status == NoShow na rasporedu odabranog dana — vidi spec section 21.</summary>
    public int NoShowCount { get; set; }

    /// <summary>Booking.Status == Cancelled na rasporedu odabranog dana.</summary>
    public int CancelledBookingCount { get; set; }

    /// <summary>Appointment.Status == Cancelled na rasporedu odabranog dana — odvojeno od CancelledBookingCount
    /// (vidi spec section 22, različiti koncepti).</summary>
    public int CancelledAppointmentCount { get; set; }

    /// <summary>Isti broj kao Financial.UnpaidBookingCount — ponovno izložen ovdje radi alert-liste, bez
    /// ponovnog izračuna (vidi spec section 23).</summary>
    public int UnpaidBookingCount { get; set; }

    /// <summary>Aktivni Product (IsActive) čija je EFEKTIVNA količina za ovu Company 0 — ProductStock.Quantity
    /// kad redak postoji, inače 0 (Product bez ijednog ProductStock retka za Company se još ne može uspješno
    /// prodati, vidi ProductStockHandler.GetByCompany — vraća samo postojeće retke). Vidi spec section 24/25.</summary>
    public int OutOfStockCount { get; set; }

    public List<DashboardOutOfStockProductDto> OutOfStockProducts { get; set; } = new();
}

public class DashboardOutOfStockProductDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; }
}
