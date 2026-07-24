using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

[Table("client_tag_assignments")]
public class ClientTagAssignment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("tag_id")]
    public Guid TagId { get; set; }

    public Client Client { get; set; }
    public ClientTag Tag { get; set; }
}
