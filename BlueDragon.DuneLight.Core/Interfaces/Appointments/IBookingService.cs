using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;

namespace BlueDragon.DuneLight.Core.Interfaces.Appointments;

/// <summary>
/// Upravlja Booking retcima (Klijent↔Appointment) neovisno o cijelom terminu — omogućuje npr. otkazivanje
/// ili no-show JEDNOG klijenta na terminu s više Bookinga (duo/grupa) bez diranja ostalih. Appointment-
/// razina operacija (cijeli termin; status Scheduled/Cancelled/Closed se izvodi iz sudjelovanja, uklj. kaskadno zatvaranje svih aktivnih
/// Bookinga) ostaje na IAppointmentService — vidi Booking.cs za punu domensku napomenu.
/// </summary>
public interface IBookingService
{
    Task<List<BookingDto>> GetForAppointment(Guid organizationId, Guid appointmentId);

    /// <summary>Gost na GRUPNOM occurrenceu (izvan popisa članova): novo Confirmed sudjelovanje na EKSPLICITNOM segmentu
    /// (postojeći Booking klijenta se ponovno koristi). Klijent mora biti aktivan, ne-anoniman, isti tenant. Individualni termin
    /// dodaje klijente kroz IAppointmentService.AddClient.</summary>
    Task<BookingDto> AddGroupGuest(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, BookingCreateRequest request);

    /// <summary>Phase M1H — GRUPNI occurrence, adresa (termin, klijent, EKSPLICITNI segment): prijelaz postojećeg sudjelovanja
    /// na tom segmentu, ili check-in gosta bez sudjelovanja (novo sudjelovanje). Put prisutnosti grupe
    /// (GroupAttendanceService). Individualni termin adresira sudjelovanje (<see cref="SetParticipationStatus"/>).</summary>
    Task<BookingDto> SetStatusOnSegment(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId, BookingSetStatusRequest request);

    /// <summary>Phase M0 — participation-native prijelaz (check-in/Completed, Cancelled, NoShow, korekcija na Confirmed,
    /// paket i check-in plaćanje kroz BookingSetStatusRequest) JEDNOG sudjelovanja; ostala sudjelovanja istog Bookinga
    /// se ne diraju. Vlasništvo slijedi segment sudjelovanja.</summary>
    Task<BookingDto> SetParticipationStatus(Guid organizationId, Guid userId, bool hasFullScope, Guid participationId, BookingSetStatusRequest request);

    /// <summary>Phase M0 — Booking-wide otkazivanje: svako AKTIVNO (Confirmed) sudjelovanje Bookinga prelazi u Cancelled
    /// (zasebno: StatusVersion, audit, Outbox pojava po sudjelovanju), terminalna ostaju netaknuta. Booking nema status.</summary>
    Task<BookingDto> CancelBooking(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId, BookingCancelRequest request);

    /// <summary>Phase M1H — ručni konačni iznos JEDNOG sudjelovanja (samo Confirmed; null = predložena cijena). Snapshot
    /// razrješavanja cjenika se ne mijenja; namirenje se izvodi iz novog iznosa (iznos ispod već naplaćenog se odbija).</summary>
    Task<BookingDto> SetParticipationPrice(Guid organizationId, Guid userId, bool hasFullScope, Guid participationId, ParticipationPriceChangeRequest request);
}
