using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Termin — operativni kontejner (organizacija, poslovnica, životni ciklus, metapodaci, Bookinzi, segmenti). Od Phase D3A
/// izvršni podaci (usluga, planirano vrijeme, zaposlenici, prostorija, resursi) žive ISKLJUČIVO na njegovim
/// AppointmentSegmentima — nekadašnji stupci termina su uklonjeni; od Phase M1B raspon termina se IZVODI
/// (Domain.Models.Appointments.AppointmentRange: MIN početka .. MAX kraja segmenata). Naplata (Amount/SuggestedAmount, i
/// monetarni Payment ledger) živi isključivo na/preko Booking, ne ovdje — omogućuje mješovito plaćanje po
/// klijentu na istom terminu (npr. duo: jedan klijent iz paketa, drugi karticom). Vidi Booking.cs za punu
/// domensku napomenu. Form=Group: veza na Group/GroupSlot koji ga je generirao.
/// </summary>
[Table("appointments")]
public class Appointment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("form")]
    public AppointmentForm Form { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("status")]
    public AppointmentStatus Status { get; set; }

    [Column("note")]
    public string Note { get; set; }

    /// <summary>Popunjeno samo kad je Status Cancelled ili NoShow (trener/recepcija upisuje razlog kod ChangeToTerminalStatus).</summary>
    [Column("cancellation_reason")]
    public string CancellationReason { get; set; }

    /// <summary>Phase M1A.1 — TRENUTNA eksplicitna otkazanost TERMINA (AppointmentService.Cancel/MarkNoShow na razini
    /// termina): kada/tko. Razlikuje "sesija je otkazana" od "svi klijenti su pojedinačno otkazali" (to drugo ostavlja
    /// termin Scheduled). Ulaz je u AppointmentLifecycle.Derive; nikad se ne izvodi iz statusa sudjelovanja. Korekcija
    /// koja vrati sudjelovanje na Confirmed briše TRENUTNI učinak (CancelledAt/By/CancellationReason → null), a povijest
    /// ostaje u audit logu ("AppointmentCancelled"/"AppointmentCancellationCleared").</summary>
    [Column("cancelled_at")]
    public DateTimeOffset? CancelledAt { get; set; }

    [Column("cancelled_by")]
    public Guid? CancelledBy { get; set; }

    /// <summary>Phase M1A.1 — poslovna činjenica "grupna sesija je zatvorena (close-out)" (CompleteGroupAppointment):
    /// kada/tko. NIJE životni ciklus termina (Closed se izvodi iz sudjelovanja) — identitet close-outa za idempotenciju
    /// (istek liste čekanja i provizija po sesiji samo jednom), neovisno o postojanju CommissionEntry. Samo Form=Group.</summary>
    [Column("closed_out_at")]
    public DateTimeOffset? ClosedOutAt { get; set; }

    [Column("closed_out_by")]
    public Guid? ClosedOutBy { get; set; }

    /// <summary>Je li termin TRENUTNO eksplicitno otkazan (vidi <see cref="CancelledAt"/>).</summary>
    [NotMapped]
    public bool IsExplicitlyCancelled => CancelledAt.HasValue;

    [Column("group_id")]
    public Guid? GroupId { get; set; }

    /// <summary>Koji slot grupe (dan+vrijeme) je generirao ovaj termin — koristi se za idempotentnu
    /// provjeru kod ponovnog generiranja. Izmjena/uklanjanje slota ne dira već generirane termine.</summary>
    [Column("group_slot_id")]
    public Guid? GroupSlotId { get; set; }

    /// <summary>Zajednička oznaka svih termina generiranih iz istog zahtjeva za ponavljajući termin.</summary>
    [Column("recurrence_group_id")]
    public Guid? RecurrenceGroupId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Company Company { get; set; }
    public Group Group { get; set; }
    public GroupSlot GroupSlot { get; set; }
    public List<Booking> Bookings { get; set; } = new();

    /// <summary>Izvršni segmenti termina (barem jedan) — JEDINI izvor izvršnih podataka. Produkcijsko kreiranje je do daljnjeg
    /// ograničeno na jedan segment (Phase M1B: MULTI_SEGMENT_NOT_ENABLED); jezgra, read-model, vlasništvo i kontekst su
    /// višesegmentni.</summary>
    public List<AppointmentSegment> Segments { get; set; } = new();
}
