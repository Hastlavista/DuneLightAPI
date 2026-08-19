using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Koliko dana JEDNOG LeaveFund-a je potrošeno za JEDAN RosterEntry (odsutnost tipa koji troši fond).
/// Jedan RosterEntry može imati dva retka (split preko granice stari/novi fond, stariji fond prvo — vidi
/// LeaveFundAllocator). Omogućuje točan povrat dana pri brisanju/izmjeni zapisa (RosterEntryService).
/// FK na RosterEntry je CASCADE (fizičko brisanje rostera preživljava audit log, ali ne i ovaj redak —
/// povrat dana MORA se dogoditi u kodu PRIJE brisanja, cascade je samo sigurnosna mreža, ne poslovna logika).
/// </summary>
[Table("leave_fund_usages")]
public class LeaveFundUsage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("roster_entry_id")]
    public Guid RosterEntryId { get; set; }

    [Column("leave_fund_id")]
    public Guid LeaveFundId { get; set; }

    [Column("days")]
    public int Days { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public LeaveFund LeaveFund { get; set; }
}
