using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;

namespace BlueDragon.DuneLight.Core.Interfaces.Appointments;

/// <summary>
/// Upravlja Booking retcima (Klijent↔Appointment) neovisno o cijelom terminu — omogućuje npr. otkazivanje
/// ili no-show JEDNOG klijenta na terminu s više Bookinga (duo/grupa) bez diranja ostalih. Appointment-
/// razina operacija (cijeli termin Scheduled/Completed/Cancelled, uklj. kaskadno zatvaranje svih aktivnih
/// Bookinga) ostaje na IAppointmentService — vidi Booking.cs za punu domensku napomenu.
/// </summary>
public interface IBookingService
{
    Task<List<BookingDto>> GetForAppointment(Guid organizationId, Guid appointmentId);

    /// <summary>Ad-hoc dodavanje Bookinga (Status=Confirmed) na postojeći, još ne-terminalni termin — npr. gost
    /// na grupnom terminu izvan popisa članova. Klijent mora biti aktivan, ne-anoniman, isti tenant.</summary>
    Task<BookingDto> AddBooking(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, BookingCreateRequest request);

    /// <summary>Prijelaz statusa jednog Bookinga. Vlasništvo: trener smije samo na terminima gdje je on
    /// Appointment.EmployeeId (isto pravilo kao IAppointmentService), osim uz hasFullScope. Za Form=Individual
    /// dopušteni ciljni statusi su Cancelled/NoShow (Completed ide isključivo kroz
    /// IAppointmentService.CompleteNew/CompleteExisting, koji naplatu razrješavaju po klijentu preko
    /// AppointmentCompleteRequest.Settlements — mješovito plaćanje na istom terminu je podržano) i Confirmed KAO
    /// USKA administrativna korekcija ISKLJUČIVO iz Completed (poništenje pogrešnog check-ina — Cancelled/NoShow
    /// nemaju povratnu putanju za Individual, vidi BookingService.ApplyIndividualCompletionCorrection) — za
    /// Form=Group dopušteni su svi prijelazi uklj. povratak na Confirmed s bilo kojeg terminalnog statusa
    /// (poništenje check-ina/otkazivanja).</summary>
    Task<BookingDto> SetStatus(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId, BookingSetStatusRequest request);
}
