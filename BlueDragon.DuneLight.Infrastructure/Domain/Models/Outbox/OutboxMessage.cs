using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;

/// <summary>
/// Transakcijski outbox — generička infrastrukturna evidencija "ovo se poslovno dogodilo, asinkrona reakcija
/// treba jednom uspješno proteći" (vidi spec sekcije 1-24). Poslovne transakcije INSERT-aju redak u ISTOJ
/// DB transakciji/UnitOfWork kao domensku mutaciju (vidi IOutboxWriter) — rollback poslovne transakcije briše i
/// ovaj redak, commit ga trajno perzistira. Pozadinski OutboxProcessorService kasnije obrađuje Pending redke
/// preko dva kratka koraka (claim pa process/mark — vidi spec section 16/45): lease (LockedUntil/LockedBy)
/// sprječava dva workera da obrađuju isti redak istovremeno; istekli lease čini "zaglavljen" Processing redak
/// ponovno preuzimljivim (crash-recovery, vidi spec section 17).
///
/// Nikad se fizički ne briše kao dio normalne obrade (Processed/Failed ostaju povijest, vidi spec section 5) —
/// retencija je izvan opsega ovog MVP-a. Type je stabilan versioniran string (OutboxEventTypes), NE CLR
/// assembly-qualified ime (vidi spec section 6) — payload je čist JSON (Payload), bez EF entiteta.
/// IdempotencyKey je opcionalan i, kad je postavljen, jedinstven po (OrganizationId, Type) preko djelomičnog
/// unique indeksa u migraciji — štiti od duplicirane logičke pojave kad se izvorna poslovna operacija ponovi
/// (vidi spec section 11-12).
/// </summary>
[Table("outbox_messages")]
public class OutboxMessage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    /// <summary>Nullable radi budućih system-level (ne-tenant-scoped) događaja — svi eventi u ovom MVP-u su
    /// tenant-scoped pa je ovo uvijek popunjeno (vidi spec section 4).</summary>
    [Column("organization_id")]
    public Guid? OrganizationId { get; set; }

    [Column("type")]
    public string Type { get; set; }

    /// <summary>Sirov JSON serijaliziran preko OutboxJsonOptions — perzistiran kao jsonb (vidi migraciju).</summary>
    [Column("payload")]
    public string Payload { get; set; }

    [Column("idempotency_key")]
    public string IdempotencyKey { get; set; }

    [Column("status")]
    public OutboxMessageStatus Status { get; set; }

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Kad redak postaje ponovno prihvatljiv za obradu — jednako CreatedAt kod prvog pokušaja, pomiče
    /// se unaprijed (backoff) nakon svakog neuspjelog pokušaja (vidi spec section 20, OutboxSettings).</summary>
    [Column("available_at")]
    public DateTimeOffset AvailableAt { get; set; }

    [Column("attempt_count")]
    public int AttemptCount { get; set; }

    [Column("last_attempt_at")]
    public DateTimeOffset? LastAttemptAt { get; set; }

    [Column("processed_at")]
    public DateTimeOffset? ProcessedAt { get; set; }

    [Column("locked_at")]
    public DateTimeOffset? LockedAt { get; set; }

    /// <summary>Lease istek — claim upit tretira Processing redak čiji je LockedUntil u prošlosti kao ponovno
    /// prihvatljiv (worker je vjerojatno pao prije dovršetka, vidi spec section 17).</summary>
    [Column("locked_until")]
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Fencing/ownership token — SVJEŽ Guid dodijeljen PO CLAIMU (ne proces-trajni worker identitet,
    /// vidi IOutboxHandler.ClaimBatch/spec section 19-20). Terminalne mutacije (MarkProcessed/MarkForRetry/
    /// MarkFailed) su uvjetovane na (Status='Processing' AND LockedBy=vlastiti token) — ako je lease istekao i
    /// redak je u međuvremenu preuzeo NOVIJI claim (čak i od ISTOG procesa), stari pokušaj pogađa 0 redaka i
    /// mora odustati bez mutacije (vidi spec section 27-31).</summary>
    [Column("locked_by")]
    public Guid? LockedBy { get; set; }

    [Column("last_error")]
    public string LastError { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
