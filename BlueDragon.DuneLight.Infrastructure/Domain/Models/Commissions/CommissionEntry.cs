using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;

/// <summary>
/// Nepromjenjiv zapis staff-compensation ledgera — "Employee X je zaradio Y provizije jer Z pod pravilom-
/// snapshotom R" (vidi spec). BaseAmount/CalculationType/RuleValue/CommissionAmount su SNAPSHOT u trenutku
/// zarade — kasnija promjena CommissionRule ne mijenja retroaktivno ove retke (izvještaji čitaju OVE stupce,
/// nikad trenutni CommissionRule, vidi CommissionService).
///
/// Izvor je točno JEDAN od BookingId (IndividualService)/AppointmentId (GroupService, po CIJELOM terminu, ne po
/// sudioniku — vidi CommissionSourceType)/CheckoutItemId (ProductSale/PackageSale), prema SourceType. Idempotencija
/// je DB-garantirana (ne "if not exists"): unique indeks na BookingId (nullable, nenul samo za IndividualService),
/// unique indeks na CheckoutItemId (nullable, nenul samo za Product/PackageSale), i djelomični unique indeks na
/// AppointmentId WHERE source_type='GroupService' (vidi migraciju) — svaka od ove tri poslovne pojave može
/// proizvesti najviše jedan CommissionEntry, čak i pod konkurentnim/ponovljenim zahtjevima.
///
/// Status=Earned je jedina vrijednost koju ovaj MVP trenutno proizvodi (vidi CommissionEntryStatus) — nijedan
/// postojeći poslovni prijelaz nema legitiman put natrag koji bi trebao reverzirati OVE izvore (individualni
/// Booking-completion i grupni Appointment-completion nemaju "undo" u trenutnom lifecycleu, Checkout Voided
/// vrijedi samo za jednostavačne check-in-generirane Checkoute koji nikad ne sadrže Product/Package stavke —
/// vidi CommissionService domensku napomenu za punu analizu). Retke se NIKAD ne briše niti cascade-briše kad se
/// CommissionRule obriše (CommissionRuleId je Restrict FK, referencirano pravilo se ne može trajno obrisati —
/// vidi CommissionRuleHandler.IsReferenced) — jedina cascade veza je na Appointment/Booking (isto ponašanje kao
/// AppointmentAuditLog: "isti dan" hard-delete termina briše i njegov povijesni trag, vidi
/// AppointmentService.Delete/DatabaseContext).
/// </summary>
[Table("commission_entries")]
public class CommissionEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("commission_rule_id")]
    public Guid CommissionRuleId { get; set; }

    [Column("source_type")]
    public CommissionSourceType SourceType { get; set; }

    [Column("appointment_id")]
    public Guid? AppointmentId { get; set; }

    [Column("booking_id")]
    public Guid? BookingId { get; set; }

    [Column("checkout_item_id")]
    public Guid? CheckoutItemId { get; set; }

    /// <summary>Snapshot retail vrijednosti izvora u trenutku zarade (Booking.Amount za IndividualService,
    /// CheckoutItem.Amount za Product/PackageSale). 0 za GroupService (nema nedvosmislene per-occurrence
    /// osnovice u ovom MVP-u, vidi CommissionRule — samo Fixed je podržan za Group, pa BaseAmount nije
    /// relevantan za izračun, čuva se kao 0 radi dosljednosti stupca, ne kao "prava" osnovica).</summary>
    [Column("base_amount")]
    public decimal BaseAmount { get; set; }

    [Column("calculation_type")]
    public CommissionCalculationType CalculationType { get; set; }

    [Column("rule_value")]
    public decimal RuleValue { get; set; }

    [Column("commission_amount")]
    public decimal CommissionAmount { get; set; }

    [Column("status")]
    public CommissionEntryStatus Status { get; set; }

    /// <summary>Poslovni trenutak zarade (kad je izvor stvarno odrađen/prodan) — koristi se za date-range upite,
    /// ne nužno identično CreatedAt (iako u ovom MVP-u uvijek jest, jer se zapis stvara unutar iste transakcije
    /// kao prijelaz koji ga zarađuje).</summary>
    [Column("earned_at")]
    public DateTimeOffset EarnedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public Employee Employee { get; set; }
    public Company Company { get; set; }
    public CommissionRule CommissionRule { get; set; }
    public Appointment Appointment { get; set; }
    public Booking Booking { get; set; }
    public CheckoutItem CheckoutItem { get; set; }
}
