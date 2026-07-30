using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// Klijent studija. HomeLocationId/HomeTrainerId su čisto informativni — ne ograničavaju
/// pristup (svi treneri vide sve klijente), služe samo za redoslijed prikaza.
///
/// Prodani paketi (ClientPackage) i termini/dolasci (AppointmentClient, AppointmentAttendance)
/// referenciraju ovaj entitet preko ClientId, ali namjerno nema stub navigacijskih kolekcija
/// ovdje — pristupa im se preko odgovarajućih handlera/servisa.
/// </summary>
[Table("clients")]
public class Client
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("member_number")]
    public int MemberNumber { get; set; }

    [Column("first_name")]
    public string FirstName { get; set; }

    [Column("last_name")]
    public string LastName { get; set; }

    [Column("date_of_birth")]
    public DateTimeOffset? DateOfBirth { get; set; }

    [Column("occupation")]
    public string Occupation { get; set; }

    [Column("phone")]
    public string Phone { get; set; }

    [Column("email")]
    public string Email { get; set; }

    [Column("note")]
    public string Note { get; set; }

    /// <summary>Vidljivo svim trenerima i adminu — bitno za sigurno vođenje treninga.</summary>
    [Column("health_note")]
    public string HealthNote { get; set; }

    [Column("gdpr_consent_given")]
    public bool GdprConsentGiven { get; set; }

    [Column("gdpr_consent_date")]
    public DateTimeOffset? GdprConsentDate { get; set; }

    [Column("home_location_id")]
    public Guid? HomeLocationId { get; set; }

    [Column("home_trainer_id")]
    public Guid? HomeTrainerId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>GDPR pravo na zaborav — kad je true, osobni podaci su nepovratno uklonjeni.</summary>
    [Column("is_anonymized")]
    public bool IsAnonymized { get; set; }

    [Column("anonymized_at")]
    public DateTimeOffset? AnonymizedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Location HomeLocation { get; set; }
    public Employee HomeTrainer { get; set; }
    public List<ClientTagAssignment> Tags { get; set; } = new();
}
