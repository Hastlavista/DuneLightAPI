using System;

namespace BlueDragon.DuneLight.Core.Events;

/// <summary>Payload za OutboxEventTypes.WaitlistPromotedV1 — emitira se u ISTOJ transakciji kao promocija liste
/// čekanja i stvaranje pratećeg Bookinga (WaitlistService.PromoteEligibleWaiters), vidi spec section 35.</summary>
public class WaitlistPromotedEvent
{
    public Guid OrganizationId { get; set; }
    public Guid WaitlistEntryId { get; set; }
    public Guid BookingId { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid ClientId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>P2 (pregled 2D #11) — promovirana sesija koju članarina klijenta NE pokriva (npr. dug uz "blokiraj rezervaciju"):
    /// stanje i razlog pokrića za recepciju. Null = bez članarine ili pokriveno (aditivno polje, stari payloadi ostaju valjani).</summary>
    public Enums.MembershipCoverageStatus? MembershipCoverageStatus { get; set; }
    public Enums.MembershipCoverageReason? MembershipCoverageReason { get; set; }
}
