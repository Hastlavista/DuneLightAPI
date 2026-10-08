using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Interaktivna naredba osoblja (postavka "odbij" kod iskorištenog limita vrijedi) ili automatski proces (generiranje
/// grupe, promocija liste čekanja, ponovna evaluacija — nikad ne odbija, Q4/§11.4).</summary>
public enum MembershipCoverageMode
{
    Interactive,
    Automatic
}

/// <summary>Aktivan claim sudjelovanja (pod lockom članstva) za odluku P1 politike (Q26/Q31).</summary>
public sealed record MembershipActiveClaim(MembershipUsage Usage, ClientMembership Membership, bool HasPeriodCredits);

/// <summary>
/// P2 (faza 2D) — JEDINA putanja za pokriće sudjelovanja članarinom: claim/storno u ledgeru korištenja (MembershipUsage) i
/// projekcija odluke (ParticipationMembershipCoverage), uvijek u pozivateljevoj transakciji (IUnitOfWork).
/// - No-op bez članarine: sudjelovanje klijenta bez neponištenog, nezavršenog članstva, bez claima i bez projekcije se ne
///   dira (ponašanje kao prije P2).
/// - Istovremenost: svaka odluka zaključava redak odgovornog članstva (FOR UPDATE) PRIJE čitanja brojača — dvije istovremene
///   rezervacije ne mogu obje potrošiti zadnji kredit / zadnje mjesto u limitu. Redoslijed lockova: subjekti rasporeda →
///   Appointment → sudjelovanje → članstvo (više članstava po Id-u). Ledger i projekcija nemaju FK na sudjelovanje, pa strana
///   članstva (pauza, obnova, dug) ne čeka lock sudjelovanja.
/// - Objašnjivost: svaka odluka piše projekciju sa stanjem, razlogom, događajem i iscrpljenim limitom (nijedno pokriće nije tiho).
/// - Oslobođeno mjesto (storno claima) automatski pokriva najraniji nepokriveni budući termin istog članstva koji prolazi sve
///   limite (2D); kasni otkaz / izostanak uz ForfeitCredit ne oslobađa mjesto.
/// </summary>
public interface IMembershipCoverageService
{
    /// <summary>Usklađuje pokriće JEDNOG sudjelovanja nakon nastanka ili prijelaza (pozivatelj drži lock termina i sudjelovanja):
    /// zauzimajuće sudjelovanje bez claima se evaluira (claim ili razlog), postojeći claim se zadržava dok je prihvatljiv;
    /// otkazano/izostalo sudjelovanje vraća claim osim kad ga zadržava aktivna posljedica politike (ForfeitCredit). Vraća
    /// odluku ili null (klijent bez relevantne članarine).</summary>
    Task<MembershipCoverageDecision> SyncParticipation(
        IUnitOfWork uow, Guid organizationId, Guid? userId, Appointment appointment, Booking booking, BookingSegmentParticipation participation,
        MembershipCoverageEvent @event, MembershipCoverageMode mode);

    /// <summary>Promjena vremena (Q6.3): vlastiti claim se stornira (Rescheduled) i sudjelovanje se evaluira na novom datumu.</summary>
    Task<MembershipCoverageDecision> ReevaluateParticipation(
        IUnitOfWork uow, Guid organizationId, Guid? userId, Appointment appointment, Booking booking, BookingSegmentParticipation participation);

    /// <summary>2E — cijena pri odrađivanju (check-in): nakon re-cijenjenja iz cjenika primjenjuje pogodnost članarine na
    /// nepokrivenu sesiju (pokrivenu članarinom ili paketom ostavlja na cjeniku, Q2). Bez članarine no-op.</summary>
    Task PriceOnCompletion(
        IUnitOfWork uow, Guid organizationId, Guid? userId, Appointment appointment, Booking booking, BookingSegmentParticipation participation,
        bool packageCovered);

    /// <summary>Pregled 2E #4 — prije svake upotrebe cijene za naplatu (dodavanje u checkout, plaćanje, odrađivanje): ako je cijena
    /// sudjelovanja zastarjela (PriceStale), uskladi je sada. Pozivatelj drži lock sudjelovanja. Vraća je li bilo usklađivanja.</summary>
    Task<bool> EnsurePriceCurrent(IUnitOfWork uow, Guid organizationId, Guid? userId, Guid participationId);

    /// <summary>Pregled 2E #4 — pozadinski prolaz (uz dnevnu obnovu): usklađuje sve zastarjele cijene organizacije.</summary>
    Task<int> RefreshStalePrices(Guid organizationId);

    /// <summary>Aktivan claim sudjelovanja pod lockom članstva (null = sesija nije pokrivena).</summary>
    Task<MembershipActiveClaim> GetActiveClaim(
        IUnitOfWork uow, Guid organizationId, BookingSegmentParticipation participation, ParticipationExecutionContextView execution);

    /// <summary>Netaknuto sudjelovanje se fizički briše: claim se stornira (ParticipationRemoved), projekcija se briše, a
    /// oslobođeno mjesto se prerasporedi. Poziva se PRIJE brisanja.</summary>
    Task ReleaseForRemoval(IUnitOfWork uow, Guid organizationId, Guid? userId, IReadOnlyCollection<BookingSegmentParticipation> participations);

    /// <summary>Strana članstva (prodaja, pauza, otkaz, raniji izlazak, promjena plana, obnova/horizont, dug): pod lockom
    /// članstva stornira claimove budućih termina koji više nisu prihvatljivi, pa redom po vremenu termina evaluira nepokrivene
    /// buduće potvrđene termine klijenta za koje je to članstvo odgovorno. Prošli termini se ne diraju.</summary>
    /// <remarks><paramref name="today"/> = simulirani "danas" obnove (testovi, sustizanje); null = danas u zoni organizacije.</remarks>
    Task ReconcileMembership(IUnitOfWork uow, Guid organizationId, Guid membershipId, MembershipCoverageEvent @event, Guid? userId, DateOnly? today = null);

    /// <summary>Isto za sva neponištena članstva klijenta (npr. nakon storna uplate sudjelovanja — ponovna evaluacija).</summary>
    Task ReconcileClient(IUnitOfWork uow, Guid organizationId, Guid clientId, MembershipCoverageEvent @event, Guid? userId);

    /// <summary>Poništena prodaja (Q24.4 precizirano u 2D): claimovi budućih termina se vraćaju (MembershipVoided), termini koje
    /// je članarina pokrivala ili su čekali evaluaciju postaju NotCovered/MembershipVoided (normalna naplata), pa se klijent
    /// ponovno evaluira kao da članarine nije bilo (druga članstva). Pozivatelj je članstvo već označio poništenim. Vraća
    /// pogođene termine za recepciju.</summary>
    Task<List<Core.Shared.WarningMembershipSession>> ReleaseForVoid(IUnitOfWork uow, Guid organizationId, Guid membershipId, Guid? userId);

    /// <summary>Q15.4/Q18 — članstvo koje bi pokrilo sesiju (usluga, poslovnica, početak), a u dugu je nakon grace perioda uz
    /// postavku "blokiraj rezervaciju"; null = nema blokade (i klijent bez članarine). Bez locka (odluka generiranja grupe).</summary>
    Task<Guid?> BlockingMembership(IUnitOfWork uow, Guid organizationId, Guid clientId, Guid serviceId, Guid companyId, DateTimeOffset startsAt);

    /// <summary>Stvarno korištenje članstva (uvjet poništavanja prodaje, Q24.4/2D): aktivni claimovi na sesiji koja je počela,
    /// odrađenoj sesiji ili uz propali kredit (kasni otkaz / izostanak uz ForfeitCredit).</summary>
    Task<int> CountUsage(IUnitOfWork uow, Guid membershipId);
}

/// <summary>Minimalni kontekst izvršenja sudjelovanja za pokriće (usluga, poslovnica, početak, klijent).</summary>
public readonly record struct ParticipationExecutionContextView(
    Guid ParticipationId, Guid ClientId, Guid ServiceId, Guid CompanyId, DateTimeOffset StartsAt, Guid AppointmentId = default);
