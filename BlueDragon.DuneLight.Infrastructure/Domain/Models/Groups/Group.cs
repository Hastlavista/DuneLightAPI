using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

/// <summary>
/// Definicija grupe (npr. "Yoga pon-sri 19h"). Traje neograničeno (nema datuma kraja). Trener NIJE
/// dio identiteta grupe — DefaultTrainerId je samo prijedlog koji se snapshotira na generirani
/// termin i može se mijenjati po pojedinom terminu (zamjene) bez diranja grupe. Bez pravog brisanja
/// — samo deaktivacija (IsActive); deaktivacija ne dira već generirane termine, zaustavlja samo
/// buduće generiranje.
///
/// Phase M1F: usluga, trajanje, prostorija i kapacitet NISU svojstva grupe nego njezinih predložaka segmenata
/// (<see cref="SegmentTemplates"/>). Grupa zadržava kalendar (slotovi = sidro occurrencea), poslovnicu, trenera (pravilo
/// osoblja: svaki generirani segment nasljeđuje istog trenera, najviše jedan zaposlenik po segmentu) i članove.
/// </summary>
[Table("groups")]
public class Group
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("note")]
    public string Note { get; set; }

    /// <summary>Phase M1F.1 — revizija aktivnog članstva i odabira predložaka. Mijenja je SAMO atomični SQL inkrement u
    /// izmjeni članstva (EF je nikad ne upisuje — vidi DatabaseContext); generiranje occurrencea je čita i provjerava.</summary>
    [Column("membership_version")]
    public long MembershipVersion { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Company Company { get; set; }
    public List<GroupSlot> Slots { get; set; } = new();

    /// <summary>Phase M1F — izvršna definicija grupe: svaki predložak generira točno jedan segment occurrencea (usluga,
    /// pomak od sidra, trajanje, prostorija, resursi, MEKI kapacitet). Barem jedan.</summary>
    public List<GroupSegmentTemplate> SegmentTemplates { get; set; } = new();
    public List<GroupMember> Members { get; set; } = new();
}
