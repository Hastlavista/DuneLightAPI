using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// P1 (ADR-0015, D1/D11) — JEDINI resolver politike otkazivanja: ResolveCancellationPolicy(organizacija, poslovnica, usluga)
/// s prednošću Company+Service → Service → Company → zadana politika organizacije, pa NAJNOVIJA verzija tog profila.
/// Poziva se točno jednom po događaju, UNUTAR lifecycle transakcije (pozivateljev IUnitOfWork); ulazi su
/// Appointment.CompanyId i Segment.ServiceId sudjelovanja. Membership, Client Tags i paket nisu dimenzije. Organizacija
/// bez zadane politike (ili profil bez verzije) je integritetna greška.
/// </summary>
public interface ICancellationPolicyResolver
{
    Task<ResolvedCancellationPolicy> ResolveCancellationPolicy(IUnitOfWork uow, Guid organizationId, Guid companyId, Guid serviceId);

    /// <summary>Inicijalizacija organizacije (registracija, ista transakcija): neutralna zadana politika — prozor 1440 min,
    /// LateCancellation i NoShow: FeeType None + PackageAction None.</summary>
    Task CreateNeutralOrganizationDefault(IUnitOfWork uow, Guid organizationId, Guid? userId);
}

/// <summary>Snapshot razriješene verzije politike za jedan događaj.</summary>
public sealed record ResolvedCancellationPolicy(
    Guid PolicyId,
    int Version,
    int CancellationWindowMinutes,
    PolicyEventRule LateCancellation,
    PolicyEventRule NoShow,
    string ResolvedFrom);

public sealed record PolicyEventRule(
    CancellationFeeType FeeType, decimal? FeeValue, CancellationPackageAction PackageAction,
    CancellationMembershipAction MembershipAction = CancellationMembershipAction.ForfeitCredit);

public static class CancellationPolicyResolvedFrom
{
    public const string CompanyAndService = "CompanyAndService";
    public const string Service = "Service";
    public const string Company = "Company";
    public const string OrganizationDefault = "OrganizationDefault";
}
