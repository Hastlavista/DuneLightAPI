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
/// je DB-garantirana (ne "if not exists"): unique indeks na (BookingId, SourceVersion) (nullable, nenul samo za
/// IndividualService — vidi Migration_2026_09_25), unique indeks na CheckoutItemId (nullable, nenul samo za
/// Product/PackageSale), i djelomični unique indeks na AppointmentId WHERE source_type='GroupService' (vidi
/// migraciju) — svaka poslovna pojava (COMPLETION-OCCURRENCE za IndividualService, ne samo booking) može
/// proizvesti najviše jedan CommissionEntry, čak i pod konkurentnim/ponovljenim zahtjevima.
///
/// SourceVersion (samo za SourceType=IndividualService, inače uvijek 0) je Booking.StatusVersion snapshotan u
/// TRENUTKU zarade — daje stabilan identitet JEDNOJ konkretnoj completion-pojavi istog Bookinga (isti obrazac kao
/// Booking.StatusVersion/Notification.SourceVersion), jer se Individual Booking legitimno može vratiti na
/// Confirmed nakon Completed (BookingService.ApplyIndividualCompletionCorrection — poništenje pogrešnog
/// check-ina) i kasnije ponovno odraditi, što mora zaraditi NOVI Earned zapis bez sudara sa starim
/// (sad Reversed) zapisom iste geneze.
///
/// Status=Earned je jedina vrijednost koju je ovaj MVP prvotno proizvodio (vidi CommissionEntryStatus) —
/// individualni Booking-completion sada IMA reverzijsku putanju (ApplyIndividualCompletionCorrection, vidi gore),
/// grupni Appointment-completion i dalje NEMA (vidi CommissionService domensku napomenu za punu analizu zašto).
/// ReversedAt/ReversedBy (vidi ispod) bilježe tko/kada je zapis reverziran — isti obrazac kao
/// checkouts.cancelled_at/cancelled_by. Retke se NIKAD ne briše niti cascade-briše kad se
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

    /// <summary>Booking.StatusVersion u trenutku zarade (samo SourceType=IndividualService, inače uvijek 0) —
    /// vidi klasnu napomenu za puno obrazloženje. Dio unique indeksa uz BookingId (Migration_2026_09_25).</summary>
    [Column("source_version")]
    public int SourceVersion { get; set; }

    /// <summary>Poslovni trenutak zarade (kad je izvor stvarno odrađen/prodan) — koristi se za date-range upite,
    /// ne nužno identično CreatedAt (iako u ovom MVP-u uvijek jest, jer se zapis stvara unutar iste transakcije
    /// kao prijelaz koji ga zarađuje).</summary>
    [Column("earned_at")]
    public DateTimeOffset EarnedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Popunjeno SAMO kad je Status=Reversed — vidi ApplyIndividualCompletionCorrection.</summary>
    [Column("reversed_at")]
    public DateTimeOffset? ReversedAt { get; set; }

    [Column("reversed_by")]
    public Guid? ReversedBy { get; set; }

    public Employee Employee { get; set; }
    public Company Company { get; set; }
    public CommissionRule CommissionRule { get; set; }
    public Appointment Appointment { get; set; }
    public Booking Booking { get; set; }
    public CheckoutItem CheckoutItem { get; set; }
}
