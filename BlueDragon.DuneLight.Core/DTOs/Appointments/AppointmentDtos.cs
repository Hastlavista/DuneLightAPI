using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.DTOs.Appointments;

/// <summary>Jedan Klijent na jednom Appointmentu — vidi Booking.cs za domensku napomenu. Zamjenjuje
/// nekadašnje AppointmentClientDto (individualni) i ClientAttendanceDto (grupni). Nosi klijent-specifičnu
/// komercijalnu evidenciju (Amount/SuggestedAmount) — od 2026-09-15 više NIJE zajednička za cijeli termin.
/// PaidAmount/OutstandingAmount/IsPaid su IZVEDENI iz Payment ledgera (vidi BookingFinancialsCalculator) —
/// od 2026-09-16 Booking više ne nosi persistirani PaymentMethod/IsPaid (vidi Payment.cs).</summary>
public class BookingDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; }
    public BookingStatus Status { get; set; }
    public decimal Amount { get; set; }
    public decimal SuggestedAmount { get; set; }
    public bool IsAmountManuallyOverridden { get; set; }

    /// <summary>Zbroj aktivnih (ne-voidanih) Paymenta ovog Bookinga — vidi BookingFinancialsCalculator.</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>0 ako je Amount=0 ili je booking paket-pokriven (ClientPackageId), inače Amount-PaidAmount
    /// (nikad negativno) — vidi BookingFinancialsCalculator.</summary>
    public decimal OutstandingAmount { get; set; }

    /// <summary>Izvedeno: OutstandingAmount &lt;= 0 (uklj. paket-pokriće i besplatan termin).</summary>
    public bool IsPaid { get; set; }

    public Guid? ClientPackageId { get; set; }
    public AttendanceCoverageType? CoverageType { get; set; }
    public bool PackageCoverageApplied { get; set; }
    public bool PackageCoverageReturned { get; set; }

    /// <summary>Puna povijest Paymenta ovog Bookinga (uklj. voidane), najnoviji prvi — vidi PaymentDto.</summary>
    public List<PaymentDto> Payments { get; set; } = new();

    public string Note { get; set; }
    public string CancellationReason { get; set; }

    /// <summary>Klasifikacija trenutka otkazivanja naspram OrganizationSettings.CancellationCutoffMinutes — vidi
    /// Booking.cs domensku napomenu za točan opseg (null osim za klijentsko/booking-razina otkazivanje).</summary>
    public bool? IsLateCancellation { get; set; }
}

public class AppointmentDto
{
    public Guid Id { get; set; }
    public AppointmentForm Form { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public string ServiceCategoryColorHex { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public Guid? RoomId { get; set; }
    public string RoomName { get; set; }
    public AppointmentStatus Status { get; set; }
    public string Note { get; set; }

    /// <summary>Popunjeno samo kad je Status Cancelled (uklj. bulk no-show — vidi AppointmentStatus.cs).</summary>
    public string CancellationReason { get; set; }
    public Guid? GroupId { get; set; }

    /// <summary>Popunjeno samo za Form=Group, kad je grupa učitana (npr. GetByClient).</summary>
    public string GroupName { get; set; }

    public Guid? RecurrenceGroupId { get; set; }

    /// <summary>Amount/PaidAmount/OutstandingAmount/IsPaid žive po Bookingu (vidi BookingDto), ne ovdje —
    /// omogućuje mješovito plaćanje po klijentu na istom terminu. Frontend zbraja/derivira agregate ako treba
    /// (npr. "sve plaćeno") — ovaj DTO namjerno ne nosi duplicirane Appointment-razina agregate.</summary>
    public List<BookingDto> Bookings { get; set; } = new();

    /// <summary>Popunjeno samo kao odgovor na create/update (preklapanje trenera/klijenata) — inače prazno.</summary>
    public List<WarningDto> Warnings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Jedan red povijesti termina za KONKRETNOG klijenta (GetByClient) — namjerno ne nosi
/// AppointmentDto.Bookings (druge klijente na istom terminu). Klijent-povijest pristup != roster pristup:
/// vidi domensku napomenu na AppointmentService.ToClientHistoryDto. Amount/PaidAmount/OutstandingAmount/
/// IsPaid dolaze s OVOG klijenta vlastitog Bookinga, ne dijele se s ostalim klijentima na istom terminu.</summary>
public class ClientAppointmentHistoryDto
{
    public Guid Id { get; set; }
    public AppointmentForm Form { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public string ServiceCategoryColorHex { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }

    /// <summary>Status termina (occurrence) — Scheduled/Completed/Cancelled itd., vidi AppointmentStatus.</summary>
    public AppointmentStatus Status { get; set; }

    /// <summary>Popunjeno samo za Form=Group.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Popunjeno samo za Form=Group — naziv grupe, ne otkriva identitet drugih članova.</summary>
    public string GroupName { get; set; }

    /// <summary>Booking-razina (vlastiti booking ovog klijenta) — vidi domensku napomenu na klasi.</summary>
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool IsPaid { get; set; }

    /// <summary>Booking ID zahtjevanog klijenta — NE Appointment.Bookings (to bi otkrilo druge klijente).</summary>
    public Guid BookingId { get; set; }
    public BookingStatus BookingStatus { get; set; }
    public Guid? ClientPackageId { get; set; }
    public AttendanceCoverageType? CoverageType { get; set; }
    public bool PackageCoverageApplied { get; set; }
    public bool PackageCoverageReturned { get; set; }
    public string BookingNote { get; set; }

    /// <summary>Popunjeno samo kad je BookingStatus Cancelled/NoShow.</summary>
    public string BookingCancellationReason { get; set; }
}

/// <summary>Lagani DTO za ćelije rasporeda — puni detalj dolazi preko GetById na klik.</summary>
public class AppointmentScheduleCellDto
{
    public Guid Id { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public string ServiceCategoryColorHex { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public Guid? RoomId { get; set; }
    public string RoomName { get; set; }
    public List<string> ClientNames { get; set; } = new();

    /// <summary>ID-jevi klijenata, indeksno poravnati s ClientNames (isti redoslijed, isti broj elemenata). Prazan za grupne termine.</summary>
    public List<Guid> ClientIds { get; set; } = new();
    public AppointmentStatus Status { get; set; }
    public bool IsCancelled { get; set; }

    public AppointmentForm Form { get; set; }

    /// <summary>Popunjeno samo za Form=Group.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Naziv grupe — za prikaz u ćeliji umjesto imena klijenta (ClientNames je prazan za grupne termine).</summary>
    public string GroupName { get; set; }

    /// <summary>Broj članova s Attended=true. Null za individualne termine.</summary>
    public int? AttendanceCount { get; set; }

    /// <summary>Broj aktivnih članova grupe (roster kapacitet za prikaz, npr. "6/8"). Null za individualne termine.</summary>
    public int? ExpectedCount { get; set; }

    /// <summary>Popunjeno samo kao odgovor na GroupService.GenerateAppointments (izvan radnog vremena/odsutnost
    /// trenera na ovoj konkretnoj instanci) — inače prazno. Isto polje/semantika kao AppointmentDto.Warnings.</summary>
    public List<WarningDto> Warnings { get; set; } = new();
}

public class AppointmentScheduleQuery
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }

    public Guid? CompanyId { get; set; }
    public Guid? RoomId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? ServiceId { get; set; }
    public ServiceExecutionMode? ExecutionMode { get; set; }
    public AppointmentStatus? Status { get; set; }
}

/// <summary>Naplata JEDNOG klijenta na terminu kod complete/complete-existing — zamjenjuje staru
/// AppointmentClientPackageSelection (koja je pokrivala samo paket-granu, uz jedan zajednički
/// AppointmentCompleteRequest.PaymentMethod za sve). Sada svaki klijent na terminu ima vlastiti
/// PaymentMethod/Amount/paket, čime je moguće mješovito plaćanje (npr. duo: jedan paket, drugi kartica).
///
/// Paket-pokriće (ClientPackageId popunjen) i novčano plaćanje (PaymentMethod popunjen) se međusobno
/// isključuju — kad je ClientPackageId popunjen, PaymentMethod se IGNORIRA (paket namiruje obvezu bez
/// stvaranja Payment retka, vidi Payment.cs/spec section 3/40). Kad ni jedno ni drugo nije popunjeno (i
/// Amount &gt; 0), booking ostaje evidentiran ali financijski neplaćen (naplata naknadno).</summary>
public class AppointmentClientSettlement
{
    [Required]
    public Guid ClientId { get; set; }

    /// <summary>Novčani način plaćanja — IGNORIRA se ako je ClientPackageId popunjen (vidi klasnu napomenu).
    /// Null = booking se ne naplaćuje sada (naknadna naplata preko Payment API-ja).</summary>
    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>Ručni override predložene cijene za OVOG klijenta. Null = koristi se predložena cijena iz cjenika.</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }

    /// <summary>Paket koji pokriva ovaj booking — kad je popunjen, booking je entitlement-namiren i PaymentMethod
    /// se ignorira (vidi klasnu napomenu).</summary>
    public Guid? ClientPackageId { get; set; }

    /// <summary>Zadano true — relevantno samo kad je PaymentMethod popunjen i ClientPackageId nije: true stvara
    /// stvaran Payment za puni Amount odmah (vidi spec section 13), false znači "evidentirano, plaćanje
    /// naknadno" (booking ostaje financijski outstanding). Bez učinka kad je PaymentMethod null ili je booking
    /// paket-pokriven (paket uvijek namiruje obvezu, vidi spec section 32).</summary>
    public bool IsPaid { get; set; } = true;
}

/// <summary>"Zakaži" — kreira termin u statusu Scheduled, bez naplate.</summary>
public class AppointmentCreateRequest
{
    [Required]
    public DateTimeOffset StartsAt { get; set; }

    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    /// <summary>Opcionalno — mora pripadati istoj CompanyId.</summary>
    public Guid? RoomId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Termin mora imati barem jednog klijenta.")]
    public List<Guid> ClientIds { get; set; } = new();

    /// <summary>Ručni override predložene cijene. Null = koristi se predložena cijena iz cjenika.</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }

    public string Note { get; set; }

    /// <summary>Zaobilazi MEKE radne-snage blokade (izvan radnog vremena, odsutnost, praznik poslovnice,
    /// pauza trenera) — NIKAD strukturne (neaktivan/nevaljan Company/Service/Employee/Room, sudar). Ignorira
    /// se (tretira kao false) ako pozivatelj nema appointments.write.all — vidi AppointmentEligibilityHelper.</summary>
    public bool OverrideAvailability { get; set; }
}

/// <summary>"Upiši odrađeno" — kreira/prevodi termin u Completed, naplata odmah. Mora sadržavati točno jedan
/// AppointmentClientSettlement po svakom ClientIds — omogućuje mješovito plaćanje (npr. duo: jedan paket,
/// drugi kartica). Naslijeđeni Amount (iz AppointmentCreateRequest) se ovdje IGNORIRA — svaki klijent ima
/// vlastiti Settlements[].Amount.</summary>
public class AppointmentCompleteRequest : AppointmentCreateRequest
{
    [Required]
    [MinLength(1)]
    public List<AppointmentClientSettlement> Settlements { get; set; } = new();
}

/// <summary>Izmjena vremena/usluge/trenera/tvrtke/klijenata/napomene/iznosa. Ne dira plaćanje/paket — za to postoje complete/cancel/no-show.</summary>
public class AppointmentUpdateRequest : AppointmentCreateRequest
{
}

/// <summary>Brzo pomicanje termina (drag-and-drop) — mijenja samo StartsAt i po potrebi trenera/tvrtku.
/// Ne dira uslugu/klijente/iznos/napomenu/plaćanje/paket.</summary>
public class AppointmentMoveRequest
{
    [Required]
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Null = trener se ne mijenja.</summary>
    public Guid? EmployeeId { get; set; }

    /// <summary>Null = tvrtka se ne mijenja.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Null = prostorija se ne mijenja. Za uklanjanje dodijeljene prostorije koristiti Update.</summary>
    public Guid? RoomId { get; set; }

    /// <summary>Vidi AppointmentCreateRequest.OverrideAvailability.</summary>
    public bool OverrideAvailability { get; set; }
}

public class AppointmentCancelRequest
{
    /// <summary>Klijenti kojima se eksplicitno vraća skinuti ulazak iz paketa. Prazna lista = ništa se ne vraća.</summary>
    public List<Guid> ReturnEntryForClientIds { get; set; } = new();

    /// <summary>Opcionalan razlog otkazivanja/no-showa — vidi Appointment.CancellationReason.</summary>
    [MaxLength(500)]
    public string CancellationReason { get; set; }
}

public class RecurringAppointmentCreateRequest
{
    /// <summary>Daily = svaki kalendarski dan uključivo vikend; Weekly = +7 dana (postojeće ponašanje).</summary>
    [Required]
    public RecurrenceType RecurrenceType { get; set; }

    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    /// <summary>Opcionalno — mora pripadati istoj CompanyId.</summary>
    public Guid? RoomId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Termin mora imati barem jednog klijenta.")]
    public List<Guid> ClientIds { get; set; } = new();

    /// <summary>Datum/vrijeme prvog termina — dan u tjednu i vrijeme se ponavljaju iz ovoga.</summary>
    [Required]
    public DateTimeOffset FirstOccurrenceStartsAt { get; set; }

    [Required]
    public DateTimeOffset EndDate { get; set; }

    public string Note { get; set; }

    /// <summary>Vidi AppointmentCreateRequest.OverrideAvailability — primjenjuje se po occurrenceu.</summary>
    public bool OverrideAvailability { get; set; }
}

/// <summary>Jedan sudarajući datum u nizu — dio { conflicts: [...] } priloga uz 409 RECURRING_CONFLICT.</summary>
public class RecurringConflictDetail
{
    public DateTimeOffset Date { get; set; }

    /// <summary>ErrorCodes.RecurringConflictReasonAppointment ili ErrorCodes.RecurringConflictReasonRosterAbsence.</summary>
    public string Reason { get; set; }
}

public class AvailableSlotsQuery
{
    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    public DateTimeOffset Date { get; set; }

    /// <summary>Ako je postavljeno, vraća se samo za tog zaposlenika (npr. Member zaključan na sebe u formi).</summary>
    public Guid? EmployeeId { get; set; }
}

public class AvailableSlotDto
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}

/// <summary>Slobodni slotovi jednog zaposlenika za traženi dan — dio odgovora GET .../available-slots.
/// Uključen i s praznim Slots ako zaposlenik radi/smije uslugu ali nema ništa slobodno tog dana.</summary>
public class EmployeeAvailableSlotsDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public string ColorHex { get; set; }
    public List<AvailableSlotDto> Slots { get; set; } = new();
}

/// <summary>Agregirane brojke dolazaka jednog klijenta (individualni + grupni termini zajedno) — interni
/// rezultat AppointmentHandler.GetStatsForClient, koristi ga IClientHistoryService za Povijest klijenta.</summary>
public class ClientAppointmentStatsDto
{
    public int CompletedVisitsCount { get; set; }
    public int NoShowCount { get; set; }
    public int CancelledCount { get; set; }
    public DateTimeOffset? LastVisitAt { get; set; }
    public DateTimeOffset? NextVisitAt { get; set; }
}

/// <summary>Ad-hoc dodavanje Bookinga na postojeći termin bez pune izmjene (npr. gost/zamjena na grupnom
/// terminu izvan popisa članova) — vidi IBookingService.AddBooking.</summary>
public class BookingCreateRequest
{
    [Required]
    public Guid ClientId { get; set; }
}

/// <summary>Otkazivanje/no-show JEDNOG Bookinga (npr. jedan od dvoje na duo terminu) — vidi
/// AppointmentsController.CancelBooking/MarkBookingNoShow. Isto oblik kao AppointmentCancelRequest, samo
/// bez ReturnEntryForClientIds liste (uvijek točno jedan klijent, poznat iz rute).</summary>
public class BookingCancelRequest
{
    /// <summary>Vraća li se već skinuti ulazak iz paketa — isto značenje kao stari
    /// AppointmentCancelRequest.ReturnEntryForClientIds, sad boolean po jednom Bookingu.</summary>
    public bool ReturnPackageEntry { get; set; }

    [MaxLength(500)]
    public string CancellationReason { get; set; }
}

/// <summary>Prijelaz statusa jednog Bookinga (Confirmed→Completed/Cancelled/NoShow, ili poništenje
/// check-ina natrag na Confirmed) — vidi IBookingService.SetStatus.</summary>
public class BookingSetStatusRequest
{
    [Required]
    public BookingStatus Status { get; set; }

    /// <summary>Ručni odabir paketa kad klijent ima više prihvatljivih paketa (Form=Group check-in). Ako je
    /// izostavljen i postoji točno jedan prihvatljiv paket, koristi se automatski.</summary>
    public Guid? ClientPackageId { get; set; }

    /// <summary>Relevantno samo za Form=Group prijelaz u Completed BEZ paketa (CoverageType=SinglePaid) —
    /// ako je popunjen i IsPaid=true, stvara stvaran Payment za puni Amount ovog check-ina (vidi
    /// IPaymentLedgerService.RecordPayment). Ako je izostavljen, booking ostaje evidentiran (Attended) ali
    /// financijski neplaćen — isto ponašanje kao prije uvođenja naplate na grupne bookinge. IGNORIRA se kad
    /// je pokriće paketom (ClientPackageId popunjen) — paket namiruje obvezu bez stvaranja Paymenta.</summary>
    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>Ručni override predložene cijene za ovaj check-in — vidi PaymentMethod. Null = koristi
    /// predloženu cijenu iz cjenika.</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }

    /// <summary>Zadano true — vidi AppointmentClientSettlement.IsPaid za istu semantiku.</summary>
    public bool IsPaid { get; set; } = true;

    public string Note { get; set; }

    /// <summary>Relevantno samo za Form=Individual prijelaz u Cancelled/NoShow — eksplicitna odluka vraća li
    /// se već skinuti ulazak iz paketa (isto ponašanje kao staro AppointmentCancelRequest.ReturnEntryForClientIds,
    /// sada po jednom Bookingu). Za Form=Group vraćanje je uvijek automatsko kod poništenja check-ina — vidi
    /// domensku napomenu na BookingService.</summary>
    public bool ReturnPackageEntry { get; set; }

    /// <summary>Opcionalan razlog — popunjava se samo kod prijelaza u Cancelled/NoShow.</summary>
    [MaxLength(500)]
    public string CancellationReason { get; set; }
}
