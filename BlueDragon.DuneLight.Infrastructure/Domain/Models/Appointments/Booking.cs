using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Jedan Klijent na jednom Appointmentu — zamjenjuje nekadašnji AppointmentClient (individualni termini)
/// i AppointmentAttendance (grupni termini), koji su bili dvije paralelne, djelomično redundantne
/// strukture za istu stvar (Klijent↔Appointment veza). Appointment nosi samo okvir/resurs (usluga,
/// vrijeme, trener, prostorija) — SVA komercijalna evidencija (Amount/SuggestedAmount, uz postojeće
/// ClientPackageId/CoverageType/PackageCoverage*) je klijent-specifična i živi ovdje, što omogućuje
/// mješovito plaćanje na istom terminu (npr. duo: jedan klijent iz paketa, drugi karticom — vidi
/// BookingService/AppointmentService).
///
/// Booking = komercijalna OBVEZA (koliko klijent duguje za ovu uslugu). Stvarno primljen novac više NE živi
/// izravno na Bookingu (staro Payment.BookingId je uklonjeno, vidi Migration_2026_09_17_CheckoutFoundation) —
/// Booking se novčano namiruje preko CheckoutItem stavke(a) koje ga referenciraju (CheckoutItems ispod), čiji
/// PaymentAllocation redci pokazuju stvaran plaćen iznos (vidi Checkout.cs/CheckoutItem.cs/PaymentAllocation.cs).
/// Booking i dalje NE nosi PaymentMethod/IsPaid kao persistirana polja. "Je li plaćeno" se izvodi iz zbroja
/// aktivnih (Payment.Status=Completed) alokacija preko svih CheckoutItems ovog Bookinga naspram Amount, ili
/// iz paket-namirenja (ClientPackageId popunjen I PackageCoverageApplied I ne PackageCoverageReturned —
/// vidi domensku napomenu na PackageCoverageApplied ispod, sam ClientPackageId NIJE dovoljan) — vidi
/// BookingFinancialsCalculator, jedini izvor istine za PaidAmount/OutstandingAmount/IsPaid.
///
/// Za Form=Group, Booking se generira odmah za sve aktivne GroupMembere kad se termin generira (vidi
/// GroupService.GenerateAppointments) — GroupMember je "tko normalno dolazi" (trajno članstvo), Booking
/// je "stvarna rezervacija/sudjelovanje na OVOM terminu" (po-terminska evidencija). Gost izvan popisa
/// članova dobiva ad-hoc Booking (BookingService.AddBooking) tek kad se pojavi/čekira. SuggestedAmount se
/// snapshotta odmah kod generiranja/dodavanja (isti IPricingService poziv kao za Individual), no naplata
/// ostaje neriješena do stvarnog check-ina (BookingService.ResolveCoverage).
/// </summary>
[Table("bookings")]
public class Booking
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("appointment_id")]
    public Guid AppointmentId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("status")]
    public BookingStatus Status { get; set; }

    /// <summary>Monotono raste SAMO kad se Status stvarno promijeni (nikad za idempotentan poziv sa istim
    /// statusom) — vidi BookingStatusVersioning.TrySetStatus, jedina dozvoljena mutacijska putanja za Status.
    /// Daje stabilan identitet JEDNOJ konkretnoj pojavi prijelaza (npr. Confirmed-&gt;NoShow #5 naspram sljedećeg
    /// Confirmed-&gt;NoShow #7 nakon međuvremenog #6 povratka na Confirmed), potrebno jer grupni Booking status
    /// legitimno ciklira (Confirmed/NoShow/Cancelled naprijed-natrag) — bez ovoga Outbox idempotencija po samom
    /// BookingId bi trajno "zaključala" prvu pojavu i tiho progutala svaku narednu (vidi BookingCancelledEvent/
    /// BookingNoShowEvent.StatusVersion, Notification.SourceVersion). Počinje od 0 za sve retke (i postojeće i
    /// nove) — povijesne pojave prije uvođenja ovog polja se ne rekonstruiraju.</summary>
    [Column("status_version")]
    public int StatusVersion { get; set; }

    /// <summary>Cijena OVOG klijenta za ovaj booking (uvijek popunjeno od trenutka kreiranja, prije bilo kakve
    /// naplate) — vrijednost usluge bez obzira na način podmirenja: kod paket-pokrića (ClientPackageId) ovo i
    /// dalje nosi redovnu/predloženu cijenu (ne 0), OutstandingAmount=0 samo znači da je obveza podmirena
    /// entitlementom, ne da je iznos naplaćen u novcu. Amount &gt;= 0 (nula je valjana — promocija/gratis termin).</summary>
    [Column("amount")]
    public decimal Amount { get; set; }

    /// <summary>Snapshot predložene cijene iz IPricingService (Service/Company/Appointment.StartsAt) u trenutku
    /// kreiranja/naplate ovog Bookinga — ne mijenja se retroaktivno kasnijim promjenama cjenika (vidi domensku
    /// napomenu na klasi).</summary>
    [Column("suggested_amount")]
    public decimal SuggestedAmount { get; set; }

    [Column("is_amount_manually_overridden")]
    public bool IsAmountManuallyOverridden { get; set; }

    /// <summary>Koji paket OVOG klijenta pokriva ovaj booking, ako je plaćeno/pokriveno iz paketa.</summary>
    [Column("client_package_id")]
    public Guid? ClientPackageId { get; set; }

    /// <summary>Kako je booking pokriven — relevantno prvenstveno za Form=Group (vidi AttendanceCoverageType).
    /// Za Form=Individual pokriće se izvodi iz OVOG Bookinga ClientPackageId, ovo polje ostaje null.</summary>
    [Column("coverage_type")]
    public AttendanceCoverageType? CoverageType { get; set; }

    /// <summary>True znači: valjan ClientPackage entitlement je STVARNO PRIMIJENJEN na ovaj Booking — postavlja se
    /// isključivo u trenutku completiona/check-ina (BookingService.ResolveCoverage, AppointmentService.CompleteNew/
    /// CompleteExisting), NIKAD samo zato što je ClientPackageId odabran na budućem/Confirmed Bookingu. NE znači
    /// nužno da je numerički brojač ulazaka smanjen — kod neograničenog (MonthlyPackage/SharedPool-neograničen)
    /// paketa nema brojača za smanjiti, entitlement je svejedno "primijenjen" (booking je odrađen pokriven njime).
    /// Kod ograničenog paketa (SessionPackage/SharedPool s brojem) prati stvarni ClientPackageEntryMutator.Deduct
    /// poziv. Ovo polje je JEDINI izvor istine za "je li booking paket-namiren" (vidi BookingFinancialsCalculator)
    /// — bilo koja putanja koja primjenjuje paket-pokriće MORA ga postaviti na true, neovisno o tome je li brojač
    /// stvarno smanjen. Preimenovano iz PackageEntryDeducted (Migration_2026_09_16_RenamePackageCoverageFlags) jer
    /// je staro ime sugeriralo da uvijek znači numeričko smanjenje, što nije bilo dosljedno između Individual i
    /// Group putanje (Individual je uvijek postavljao true, Group ga je ostavljao false za neograničene pakete).</summary>
    [Column("package_coverage_applied")]
    public bool PackageCoverageApplied { get; set; }

    /// <summary>True znači: prethodno primijenjeno pokriće više NE namiruje ovaj Booking (poništenje check-ina ili
    /// eksplicitno vraćanje kod otkazivanja) — vidi BookingFinancialsCalculator (IsPackageSettled zahtijeva
    /// PackageCoverageApplied &amp;&amp; !PackageCoverageReturned). Preimenovano iz PackageEntryReturned.</summary>
    [Column("package_coverage_returned")]
    public bool PackageCoverageReturned { get; set; }

    [Column("package_coverage_returned_at")]
    public DateTimeOffset? PackageCoverageReturnedAt { get; set; }

    [Column("package_coverage_returned_by")]
    public Guid? PackageCoverageReturnedBy { get; set; }

    [Column("note")]
    public string Note { get; set; }

    /// <summary>Popunjeno samo kad je Status Cancelled ili NoShow — booking-razina ekvivalent Appointment.CancellationReason.</summary>
    [Column("cancellation_reason")]
    public string CancellationReason { get; set; }

    /// <summary>Klasifikacija trenutka otkazivanja naspram OrganizationSettings.CancellationCutoffMinutes — vidi
    /// BookingCancellationPolicy. Popunjeno SAMO za otkazivanje ovog konkretnog Bookinga od strane
    /// klijenta/osoblja (BookingService.SetStatus) — namjerno null za NoShow (klasifikacija je isključivo o
    /// "kasnom otkazivanju", ne o izostanku) i za posloVno/appointment-wide otkazivanje cijelog termina
    /// (AppointmentService.Cancel) jer se kasno-otkazivanje pravilo odnosi na inicijativu klijenta, ne na
    /// odluku poslovnice da otkaže termin (vidi spec section 38 — nema kazne kad poslovnica otkazuje).
    /// Nikad se ne koristi za stvarnu naplatu naknade u ovoj fazi — samo priprema za buduću Commerce logiku.</summary>
    [Column("is_late_cancellation")]
    public bool? IsLateCancellation { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Appointment Appointment { get; set; }
    public Client Client { get; set; }
    public ClientPackage ClientPackage { get; set; }

    /// <summary>Povijesne CheckoutItem stavke koje referenciraju ovaj Booking (obično točno jedna, ali može biti
    /// više kroz vrijeme — npr. stavka je uklonjena iz otkazanog Checkouta pa je Booking kasnije dodan u drugi,
    /// vidi spec section 29). Zbroj aktivnih (Payment.Status=Completed) PaymentAllocation redaka preko svih ovih
    /// stavki je izvor istine za "koliko je plaćeno", ne persistirani boolean (vidi BookingFinancialsCalculator).</summary>
    public List<CheckoutItem> CheckoutItems { get; set; } = new();
}
