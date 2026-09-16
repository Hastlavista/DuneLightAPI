using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;

namespace BlueDragon.DuneLight.Core.Interfaces.Appointments;

/// <summary>
/// Kontroler-okrenuta strana liste čekanja za PUN grupni Appointment occurrence — vidi WaitlistEntry.cs
/// (Infrastructure) za punu domensku napomenu. Promocija/masovno isticanje (koje se pokreće IZ TUĐE
/// transakcije — BookingService nakon otkazivanja Bookinga, GroupService nakon uklanjanja člana,
/// AppointmentService nakon otkazivanja/zatvaranja termina) namjerno NIJE ovdje — živi na
/// Infrastructure-only IWaitlistPromotionService (isti uow-parametar obrazac kao Handler sučelja), jer Core
/// ne smije referencirati Infrastructure.UnitOfWork. Jedan WaitlistService implementira oba sučelja.
/// </summary>
public interface IWaitlistService
{
    /// <summary>Puna lista (uklj. povijest) za admin/trener roster prikaz, s izvedenom Position za Waiting retke.</summary>
    Task<List<WaitlistEntryDto>> GetForAppointment(Guid organizationId, Guid appointmentId);

    /// <summary>Upis na listu čekanja — dopušteno SAMO kad je termin pun (vidi ErrorCodes.CapacityAvailable).
    /// Vlasništvo: isto pravilo kao IBookingService (trener samo na svojim terminima, osim uz hasFullScope).</summary>
    Task<WaitlistEntryDto> Join(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, WaitlistJoinRequest request);

    /// <summary>Ručno uklanjanje (Waiting -&gt; Cancelled) — idempotentno, ne diže grešku za već terminalan redak.</summary>
    Task<WaitlistEntryDto> Cancel(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId);
}
