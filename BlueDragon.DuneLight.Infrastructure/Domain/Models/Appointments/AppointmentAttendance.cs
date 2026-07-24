using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Evidencija prisutnosti na grupnom terminu — jedan redak po polazniku (član grupe ili gost izvan
/// popisa članova). Attended=null znači da još nije evidentiran. Poništavanje prisutnosti (Attended
/// true→false) kod SessionPackage pokrića automatski vraća skinuti ulazak — za razliku od
/// individualnih termina gdje je vraćanje eksplicitna admin/trener akcija.
/// </summary>
[Table("appointment_attendances")]
public class AppointmentAttendance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("appointment_id")]
    public Guid AppointmentId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("attended")]
    public bool? Attended { get; set; }

    /// <summary>Kako je dolazak pokriven — postavlja se kad se Attended prvi put zabilježi.</summary>
    [Column("coverage_type")]
    public AttendanceCoverageType? CoverageType { get; set; }

    [Column("client_package_id")]
    public Guid? ClientPackageId { get; set; }

    [Column("package_entry_deducted")]
    public bool PackageEntryDeducted { get; set; }

    [Column("package_entry_returned")]
    public bool PackageEntryReturned { get; set; }

    [Column("package_entry_returned_at")]
    public DateTimeOffset? PackageEntryReturnedAt { get; set; }

    [Column("package_entry_returned_by")]
    public Guid? PackageEntryReturnedBy { get; set; }

    [Column("note")]
    public string Note { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public Appointment Appointment { get; set; }
    public Client Client { get; set; }
    public ClientPackage ClientPackage { get; set; }
}
