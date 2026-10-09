using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// P2 (Q5/Q12/Q47) — pauza članstva. Days (planovi "od datuma kupnje"): StartsOn..PlannedEndsOn, uz produljenje perioda za
/// stvarne dane. SkipPeriods (kalendarski planovi): cijeli periodi od prvog dana perioda. Raniji povratak postavlja
/// ActualEndsOn (dan prije povratka); otkazana pauza koja nije počela ostaje u povijesti (CancelledAt) i ne broji se u limite.
/// Pauza se nikad ne briše.
/// </summary>
[Table("membership_pauses")]
public class MembershipPause
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_membership_id")]
    public Guid ClientMembershipId { get; set; }

    [Column("kind")]
    public MembershipPauseKind Kind { get; set; }

    [Column("starts_on")]
    public DateOnly StartsOn { get; set; }

    /// <summary>K1-8 — tko je zadao pauzu (Client / CompanyClosure).</summary>
    [Column("source")]
    public MembershipPauseSource Source { get; set; } = MembershipPauseSource.Client;

    /// <summary>Null samo za otvorenu sustavnu pauzu (CompanyClosure dok su poslovnice zatvorene; DB CHECK).</summary>
    [Column("planned_ends_on")]
    public DateOnly? PlannedEndsOn { get; set; }

    [Column("actual_ends_on")]
    public DateOnly? ActualEndsOn { get; set; }

    [Column("reason")]
    public string Reason { get; set; }

    [Column("cancelled_at")]
    public DateTimeOffset? CancelledAt { get; set; }

    [Column("cancelled_by")]
    public Guid? CancelledBy { get; set; }

    [Column("cancellation_reason")]
    public MembershipPauseCancellationReason? CancellationReason { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public ClientMembership Membership { get; set; }

    /// <summary>Zadnji dan pauze koji stvarno vrijedi (raniji povratak skraćuje planirani). Otvorena sustavna pauza vrijedi
    /// do <see cref="Utils.MembershipPauseSpan.OpenEnd"/>.</summary>
    [NotMapped]
    public DateOnly EffectiveEndsOn => ActualEndsOn ?? PlannedEndsOn ?? Utils.MembershipPauseSpan.OpenEnd;

    /// <summary>K1-8 — otvorena sustavna pauza: članstvo trenutno stoji (kraj još nije poznat).</summary>
    [NotMapped]
    public bool IsOpenCompanyClosure => Source == MembershipPauseSource.CompanyClosure && CancelledAt == null && ActualEndsOn == null;
}
