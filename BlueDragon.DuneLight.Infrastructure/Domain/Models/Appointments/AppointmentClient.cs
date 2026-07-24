using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Veza termin↔klijent za individualne termine (booking, do 2+ klijenata za par/duo). Nosi i
/// plaćanje iz paketa PO KLIJENTU — kod duo termina svaki klijent skida ulazak iz svog vlastitog
/// paketa, neovisno o ostalima na istom terminu.
/// </summary>
[Table("appointment_clients")]
public class AppointmentClient
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("appointment_id")]
    public Guid AppointmentId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    /// <summary>Koji paket OVOG klijenta pokriva ovaj termin, ako je plaćeno iz paketa.</summary>
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

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public Appointment Appointment { get; set; }
    public Client Client { get; set; }
    public ClientPackage ClientPackage { get; set; }
}
