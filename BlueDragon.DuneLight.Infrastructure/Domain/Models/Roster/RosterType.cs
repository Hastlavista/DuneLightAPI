using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Šifrarnik vrsta zapisa rostera (Smjena, Bowen, Rec/dvok, Godišnji, Bolovanje, Praznik...) — admin ga
/// uređuje bez izmjene koda. Zajednički za cijelu firmu, svima dostupan za čitanje.
/// </summary>
[Table("roster_types")]
public class RosterType
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("color_hex")]
    public string ColorHex { get; set; }

    /// <summary>Ulazi li u zbroj radnih sati (rad da; godišnji/bolovanje/praznik ne).</summary>
    [Column("counts_as_work")]
    public bool CountsAsWork { get; set; }

    /// <summary>Određuje oblik zapisa: true = raspon datuma bez vremena; false = jedan dan s vremenom.</summary>
    [Column("is_absence")]
    public bool IsAbsence { get; set; }

    /// <summary>Informativno svojstvo šifrarnika — stvarni oblik zapisa uvijek određuje IsAbsence.</summary>
    [Column("requires_time")]
    public bool RequiresTime { get; set; }

    /// <summary>Troši li se fond godišnjeg odmora kod upisa ovog tipa — mora biti IsAbsence=true (validirano u RosterTypeService). Vidi LeaveFundAllocator.</summary>
    [Column("deducts_from_leave_fund")]
    public bool DeductsFromLeaveFund { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }
}
