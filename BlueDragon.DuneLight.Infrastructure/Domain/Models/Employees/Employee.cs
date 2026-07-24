using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

[Table("employees")]
public class Employee
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("first_name")]
    public string FirstName { get; set; }

    [Column("last_name")]
    public string LastName { get; set; }

    [Column("phone")]
    public string Phone { get; set; }

    [Column("email")]
    public string Email { get; set; }

    [Column("date_of_birth")]
    public DateTimeOffset? DateOfBirth { get; set; }

    [Column("address")]
    public string Address { get; set; }

    [Column("oib")]
    public string Oib { get; set; }

    [Column("note")]
    public string Note { get; set; }

    /// <summary>Slobodna, čisto informativna bilješka o dogovoru oko naknade — nigdje se ne parsira ni koristi.</summary>
    [Column("compensation_note")]
    public string CompensationNote { get; set; }

    [Column("color_hex")]
    public string ColorHex { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("employment_start_date")]
    public DateTimeOffset EmploymentStartDate { get; set; }

    [Column("employment_end_date")]
    public DateTimeOffset? EmploymentEndDate { get; set; }

    [Column("engagement_type_id")]
    public Guid EngagementTypeId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public EngagementType EngagementType { get; set; }
    public User User { get; set; }
    public List<EmployeeLocation> Locations { get; set; } = new();
    public List<EmployeeServiceAssignment> Services { get; set; } = new();
}
