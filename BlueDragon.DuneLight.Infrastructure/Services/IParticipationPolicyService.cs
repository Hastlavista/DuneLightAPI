using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// P1 (ADR-0016/0017/0018) — JEDINA putanja koja evaluira politiku otkazivanja i piše ledger posljedica
/// (ParticipationPolicyConsequence) i kaznu u paketu. Poziva se UNUTAR lifecycle transakcije pozivatelja (koji drži lockove
/// termina i sudjelovanja, već je postavio novi status i StatusVersion te očistio stare metapodatke). Autorizaciju
/// (initiator Business, korekcija zatvorenog termina) provjerava pozivatelj; grant otpisa u trenutku događaja provjerava ovaj
/// servis nad grantovima koje daje pozivatelj (PolicyEventOptions.WaiverGrants), jer učinak posljedice (naknada ili
/// jedinica/kredit) postaje poznat tek pri izračunu (K2). Nikad ne pomiče novac i nikad ne stvara proviziju (D7, D8).
/// </summary>
public interface IParticipationPolicyService
{
    /// <summary>Klijentsko otkazivanje (D3): razrješava politiku jednom, klasificira kasno/na vrijeme po PlannedStart segmenta i
    /// <paramref name="eventAt"/>, upisuje klasifikaciju i snapshot na sudjelovanje; kasno → posljedica LateCancellation.</summary>
    Task ApplyClientCancellation(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, DateTimeOffset eventAt, PolicyEventOptions options);

    /// <summary>Izostanak (D4): svaki valjani NoShow → posljedica NoShow prema razriješenoj politici.</summary>
    Task ApplyNoShow(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, DateTimeOffset eventAt, PolicyEventOptions options);

    /// <summary>D12 — poništava AKTIVNU posljedicu (Active → Reversed) i vraća jedinicu paketa potrošenu kao kaznu, u istoj
    /// transakciji. Waived/Reversed zapisi se nikad ne mijenjaju. False ako aktivne posljedice nema.</summary>
    Task<bool> ReverseActive(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation, string reason, DateTimeOffset at);

    /// <summary>D10 — otpisuje AKTIVNU posljedicu (Active → Waived, razlog obavezan, nepovratno), vraća jedinicu paketa i piše
    /// audit PolicyConsequenceWaived. Baca NO_ACTIVE_POLICY_CONSEQUENCE ako aktivne posljedice nema.</summary>
    Task WaiveActive(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, string waiverReason, DateTimeOffset at);
}

/// <summary>Opcije događaja politike iz naredbe: eksplicitni paket za kaznu (D6) i otpis u trenutku događaja (D10). K2 (12.2):
/// uz otpis pozivatelj daje grantove pozivatelja (<paramref name="WaiverGrants"/>) — grant otpisa ovisi o učinku koji bi
/// posljedica imala (naknada ili jedinica/kredit), a on je poznat tek pri izračunu posljedice.</summary>
public sealed record PolicyEventOptions(Guid? ClientPackageId, bool WaivePolicyConsequence, string WaiverReason, GrantContext WaiverGrants = null)
{
    public static readonly PolicyEventOptions None = new(null, false, null);
}
