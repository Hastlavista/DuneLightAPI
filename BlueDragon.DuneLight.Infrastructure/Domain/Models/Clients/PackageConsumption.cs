using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// Phase D3B3A — AUTORITATIVNA povijest korištenja paketa: jedan zapis = entitlement jednog ClientPackage-a
/// primijenjen na JEDNO sudjelovanje (BookingSegmentParticipation) za jednu uslugu. Paket NIJE način plaćanja — ovo
/// nije Payment/PaymentAllocation niti CheckoutItem, i ne nosi novčani iznos (paketi su jedinični, ne novčani).
///
/// Ledger (isti obrazac kao CommissionEntry Earned/Reversed): Consumed -&gt; Reversed na ISTOM retku (ReversedAt/By +
/// razlog), redak se nikad ne briše; ponovna potrošnja nakon poništenja je NOVI redak. Invarijanta: najviše JEDAN
/// aktivan (Consumed) zapis po sudjelovanju (djelomični unique indeks u bazi) — ponovljen completion ne može skinuti
/// ulazak dvaput, ponovljeno poništenje ne može vratiti dvaput.
///
/// Units: koliko je jedinica brojača skinuto — 1 za paket s brojačem (PerService unos s brojem / SharedPool s
/// brojem), 0 za neograničen paket (entitlement je primijenjen, ali nema brojača). ServiceStartsAt: trenutak
/// izvođenja usluge (planirani početak segmenta) po kojem je valjanost paketa provjerena (F-08, Phase D3B3A).
/// Pisanje isključivo kroz IPackageConsumptionLedgerService; čitanje kroz Utils.PackageConsumptions.
/// </summary>
[Table("package_consumptions")]
public class PackageConsumption
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_package_id")]
    public Guid ClientPackageId { get; set; }

    [Column("booking_segment_participation_id")]
    public Guid BookingSegmentParticipationId { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    [Column("units")]
    public int Units { get; set; }

    [Column("service_starts_at")]
    public DateTimeOffset ServiceStartsAt { get; set; }

    /// <summary>Phase D3B3A.1 — lokalni datum izvođenja (kalendar poslovnice termina) prema kojem je valjanost paketa
    /// provjerena (ClientPackage.PurchaseDate &lt;= ServiceDate &lt;= ClientPackage.ValidUntilDate; donja granica od T1-9).</summary>
    [Column("service_date")]
    public DateOnly ServiceDate { get; set; }

    [Column("status")]
    public PackageConsumptionStatus Status { get; set; }

    /// <summary>P1 (D6) — izvršenje usluge ili kazna politike. PolicyConsequence ⇔ ParticipationPolicyConsequenceId je
    /// popunjen (DB CHECK); najviše jedna potrošnja po posljedici (djelomični unique indeks).</summary>
    [Column("trigger")]
    public PackageConsumptionTrigger Trigger { get; set; } = PackageConsumptionTrigger.ServiceCompletion;

    [Column("participation_policy_consequence_id")]
    public Guid? ParticipationPolicyConsequenceId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    /// <summary>Popunjeno SAMO kad je Status=Reversed.</summary>
    [Column("reversed_at")]
    public DateTimeOffset? ReversedAt { get; set; }

    [Column("reversed_by")]
    public Guid? ReversedBy { get; set; }

    [Column("reversal_reason")]
    public PackageConsumptionReversalReason? ReversalReason { get; set; }

    public ClientPackage ClientPackage { get; set; }
    public BookingSegmentParticipation Participation { get; set; }
    public ServiceEntity Service { get; set; }
}
