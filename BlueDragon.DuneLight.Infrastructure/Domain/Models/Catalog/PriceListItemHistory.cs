using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>Bilježi prethodnu cijenu pri svakoj promjeni stavke cjenika (posebno traženi audit).</summary>
[Table("price_list_item_history")]
public class PriceListItemHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("price_list_item_id")]
    public Guid PriceListItemId { get; set; }

    [Column("old_price")]
    public decimal OldPrice { get; set; }

    [Column("new_price")]
    public decimal NewPrice { get; set; }

    [Column("changed_at")]
    public DateTimeOffset ChangedAt { get; set; }

    [Column("changed_by")]
    public Guid? ChangedBy { get; set; }
}
