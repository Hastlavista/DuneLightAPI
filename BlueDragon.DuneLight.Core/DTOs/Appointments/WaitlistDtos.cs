using System;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Appointments;

/// <summary>Jedan redak liste čekanja za konkretan grupni Appointment occurrence — vidi WaitlistEntry.cs
/// (Infrastructure) za punu domensku napomenu.</summary>
public class WaitlistEntryDto
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; }
    public WaitlistEntryStatus Status { get; set; }

    /// <summary>1-bazirana pozicija unutar Waiting reda (FIFO po JoinedAt/Id) — samo dok je Status Waiting,
    /// inače null. Izvedeno polje, ne perzistira se (vidi IWaitlistService.GetForAppointment).</summary>
    public int? Position { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? PromotedAt { get; set; }

    /// <summary>Popunjeno samo kad je Status Promoted — Booking koji je promocija stvorila.</summary>
    public Guid? PromotedBookingId { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Popunjeno samo kad je Status Expired — vidi WaitlistExpiredReasons.</summary>
    public string ExpiredReason { get; set; }
}

public class WaitlistJoinRequest
{
    [Required]
    public Guid ClientId { get; set; }
}
