using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Jedan Klijent na jednom Appointmentu — zamjenjuje nekadašnji AppointmentClient (individualni termini)
/// i AppointmentAttendance (grupni termini), koji su bili dvije paralelne, djelomično redundantne
/// strukture za istu stvar (Klijent↔Appointment veza). Appointment nosi samo okvir/resurs (usluga,
/// vrijeme, trener, prostorija) — SVA komercijalna evidencija (cijena, potrošnja paketa, namirenje) je
/// klijent-specifična i živi na Bookingu odnosno njegovom sudjelovanju, što omogućuje
/// mješovito plaćanje na istom terminu (npr. duo: jedan klijent iz paketa, drugi karticom — vidi
/// BookingService/AppointmentService).
///
/// Phase D3B3B: novčano namirenje NIJE na Bookingu — CheckoutItem usluge referencira SUDJELOVANJE
/// (BookingSegmentParticipation.CheckoutItems), a "koliko je plaćeno / duguje se" izvodi Utils.ParticipationSettlement
/// (aktivne PaymentAllocation preko svih stavki sudjelovanja naspram cijene sudjelovanja, ili aktivna PackageConsumption).
/// Booking nikad ne nosi PaymentMethod/IsPaid/PaidAmount.
///
/// Za Form=Group, Booking se generira odmah za sve aktivne GroupMembere kad se termin generira (vidi
/// GroupService.GenerateAppointments) — GroupMember je "tko normalno dolazi" (trajno članstvo), Booking
/// je "stvarna rezervacija/sudjelovanje na OVOM terminu" (po-terminska evidencija). Gost izvan popisa
/// članova dobiva ad-hoc Booking (BookingService.AddBooking) tek kad se pojavi/čekira. SuggestedAmount se
/// snapshotta odmah kod generiranja/dodavanja (isti IPricingService poziv kao za Individual), no naplata
/// ostaje neriješena do stvarnog check-ina (BookingService.ResolveCoverage).
///
/// Phase D3B1/D3B2: životni ciklus (status, StatusVersion, otkazivanje) i CIJENA (Amount, SuggestedAmount,
/// IsAmountManuallyOverridden) više NISU na Bookingu — autoritativno ih nosi njegovo jedino sudjelovanje
/// (BookingSegmentParticipation; pisanje ParticipationPrice/ParticipationLifecycle). Phase M0: Booking nema status,
/// cijenu, namirenje ni verziju — Booking read-model polja su IZVEDENI sažeci (Utils.BookingSummary/
/// BookingCommercialSummary); naredbe adresiraju sudjelovanje (BookingId samo kao privremena kompatibilnost).
/// "Amount" u tekstu iznad znači tu cijenu sudjelovanja. Phase D3B3A: ni potrošnja paketa nije na Bookingu — nosi
/// je povijest PackageConsumption sudjelovanja (Utils.PackageConsumptions). Phase D3B3B: ni namirenje. Booking je
/// samo identitet (termin + klijent) i spremnik sudjelovanja tog klijenta.
/// </summary>
[Table("bookings")]
public class Booking
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

    [Column("note")]
    public string Note { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Appointment Appointment { get; set; }
    public Client Client { get; set; }

    /// <summary>Sudjelovanja ovog Bookinga u segmentima termina — u jednostrukom modelu točno jedno (BookingFactory).
    /// Autoritativno za životni ciklus (Phase D3B1) i cijenu (Phase D3B2); čitaj kroz Utils.BookingParticipations.</summary>
    public List<BookingSegmentParticipation> Participations { get; set; } = new();
}
