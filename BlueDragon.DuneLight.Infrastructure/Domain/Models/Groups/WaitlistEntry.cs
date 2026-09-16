using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

/// <summary>
/// Jedan redak liste čekanja za KONKRETAN grupni Appointment occurrence — "ovaj Klijent želi mjesto na OVOM
/// terminu, ali trenutno nema kapaciteta". Occurrence-specifično, NE veže se na Group definiciju (Klijent koji
/// čeka za ponedjeljak 18h ne čeka automatski i za srijedu 18h) — vidi GroupMember.cs za suprotan koncept
/// (trajno članstvo). Waiting je jedino ne-terminalno stanje; Promoted/Cancelled/Expired ostaju kao povijest
/// (bez fizičkog brisanja). Ponovni upis nakon Cancelled/Expired/Promoted stvara NOVI redak (partial unique
/// indeks dopušta samo jedan aktivan (AppointmentId, ClientId) par u Waiting stanju u isto vrijeme).
/// </summary>
[Table("waitlist_entries")]
public class WaitlistEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("appointment_id")]
    public Guid AppointmentId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("status")]
    public WaitlistEntryStatus Status { get; set; }

    [Column("joined_at")]
    public DateTimeOffset JoinedAt { get; set; }

    [Column("promoted_at")]
    public DateTimeOffset? PromotedAt { get; set; }

    /// <summary>Booking nastao promocijom ovog retka — popunjeno samo kad je Status Promoted. Poveznica je
    /// jednosmjerna (Booking ne zna za WaitlistEntry koji ga je stvorio) jer nakon promocije Booking postaje
    /// potpuno običan Booking, vidi IWaitlistService.PromoteEligibleWaiters.</summary>
    [Column("promoted_booking_id")]
    public Guid? PromotedBookingId { get; set; }

    [Column("cancelled_at")]
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Popunjeno samo kad Status pređe u Expired (sustav), nikad za Cancelled (eksplicitna odluka) —
    /// vidi WaitlistExpiredReasons.</summary>
    [Column("expired_reason")]
    public string ExpiredReason { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    public Appointment Appointment { get; set; }
    public Client Client { get; set; }
    public Booking PromotedBooking { get; set; }
}
