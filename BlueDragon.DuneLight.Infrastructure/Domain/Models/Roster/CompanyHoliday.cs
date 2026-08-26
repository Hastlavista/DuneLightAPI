using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Neradni dan jedne poslovnice — WorkingHoursCalculator.GetEffectiveCompanyIntervals ga tretira kao prazan
/// popis intervala (AvailabilitySource.Holiday), bez obzira ima li predložak za taj dan inače radne intervale.
/// "Sve poslovnice odjednom" nije podržano (Faza 2+) — CompanyId je namjerno NE nullable. CRUD je create/delete,
/// bez parcijalnog update-a.
/// </summary>
[Table("company_holidays")]
public class CompanyHoliday
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    /// <summary>Samo Date dio se koristi (isto kao RosterEntry.DateFrom) — vrijeme dana se ignorira.</summary>
    [Column("date")]
    public DateTimeOffset Date { get; set; }

    [Column("name")]
    public string Name { get; set; }

    /// <summary>true = auto-generiran iz DefaultCompanyHolidays kataloga (fiksni datum); false = ručno dodan
    /// (uključivo pomični praznici poput Uskrsa).</summary>
    [Column("is_recurring_fixed")]
    public bool IsRecurringFixed { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public Company Company { get; set; }
}
