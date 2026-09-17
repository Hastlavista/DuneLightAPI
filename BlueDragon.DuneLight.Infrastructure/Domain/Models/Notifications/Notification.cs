using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;

/// <summary>
/// Kanal-neovisna komunikacijska NAMJERA — "za ovaj poslovni događaj bi se trebala dogoditi komunikacija prema
/// klijentu", NE "email/SMS je poslan" (vidi spec section 25-26). Ne postoji provider u ovom MVP-u, pa Status
/// ostaje Pending/Cancelled (nikad "Sent" — vidjeti NotificationStatus). Data nosi minimalan strukturiran
/// kontekst za buduće renderiranje (jsonb), bez PII (vidi handlere ispod).
///
/// Idempotencija je DB-garantirana preko unique indeksa na (OrganizationId, Type, SourceType, SourceId,
/// SourceVersion) — svaka KONKRETNA pojava poslovnog prijelaza (npr. JEDAN konkretan Booking NoShow, ne "ovaj
/// Booking zauvijek") proizvodi NAJVIŠE jedan Notification redak, čak i ako Outbox handler izvrši at-least-once
/// obradu dvaput (vidi spec section 24/28, BookingCancelledNotificationHandler i srodni). SourceVersion je
/// SourceType-specifičan — za Booking je to Booking.StatusVersion u trenutku te konkretne pojave (dopušta
/// legitimno ciklirajući Booking status da proizvede više odvojenih Notification redaka kroz vrijeme, vidi spec
/// section 5-18); za izvore bez ciklirajućeg životnog vijeka (WaitlistEntry — promocija je terminalna) ostaje
/// konstantno 0 (vidi WaitlistPromotedNotificationHandler).
/// </summary>
[Table("notifications")]
public class Notification
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    /// <summary>Jedini trenutno potreban oblik primatelja — Employee/User namjerno nisu dodani dok ne postoji
    /// stvaran use-case (vidi spec section 25).</summary>
    [Column("client_id")]
    public Guid? ClientId { get; set; }

    [Column("type")]
    public NotificationType Type { get; set; }

    [Column("source_type")]
    public NotificationSourceType SourceType { get; set; }

    [Column("source_id")]
    public Guid SourceId { get; set; }

    /// <summary>Vidi domensku napomenu na klasi — dio idempotency uniqueness-a uz (OrganizationId, Type,
    /// SourceType, SourceId).</summary>
    [Column("source_version")]
    public int SourceVersion { get; set; }

    [Column("status")]
    public NotificationStatus Status { get; set; }

    /// <summary>Minimalan strukturiran kontekst (npr. AppointmentId/BookingId/CompanyId) za buduće
    /// rendering/provider slojeve — namjerno bez renderiranog HTML/teksta i bez Client PII (vidi spec section 27).</summary>
    [Column("data")]
    public string Data { get; set; }

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public Client Client { get; set; }
}
