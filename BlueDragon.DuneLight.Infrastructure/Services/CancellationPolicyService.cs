using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// P1 (ADR-0015) — upravljanje politikama otkazivanja i JEDINI resolver (vidi ICancellationPolicyResolver).
/// Zadana politika organizacije je profil s IsOrganizationDefault (točno jedan po organizaciji, nastaje pri registraciji).
/// Lockovi: svaka promjena profila zaključava njegov redak (FOR UPDATE), pa objava verzije (Version + 1), deaktivacija,
/// dodjela i postavljanje zadane politike nad istim profilom serijaliziraju — deaktivacija ne može "proći pored" dodjele.
/// </summary>
public class CancellationPolicyService : ICancellationPolicyService, ICancellationPolicyResolver
{
    public const int NeutralCancellationWindowMinutes = 1440;
    public const string NeutralDefaultName = "Zadana politika";

    private readonly ICancellationPolicyHandler _handler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public CancellationPolicyService(
        ICancellationPolicyHandler handler,
        ICompanyHandler companyHandler,
        IServiceHandler serviceHandler,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _handler = handler;
        _companyHandler = companyHandler;
        _serviceHandler = serviceHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    #region Resolver

    public async Task<ResolvedCancellationPolicy> ResolveCancellationPolicy(IUnitOfWork uow, Guid organizationId, Guid companyId, Guid serviceId)
    {
        List<CancellationPolicyAssignment> candidates = await _handler.GetResolutionCandidates(uow, organizationId, companyId, serviceId);

        (Guid PolicyId, string From)? picked =
            Pick(candidates, a => a.CompanyId == companyId && a.ServiceId == serviceId, CancellationPolicyResolvedFrom.CompanyAndService)
            ?? Pick(candidates, a => a.CompanyId == null && a.ServiceId == serviceId, CancellationPolicyResolvedFrom.Service)
            ?? Pick(candidates, a => a.CompanyId == companyId && a.ServiceId == null, CancellationPolicyResolvedFrom.Company);

        if (picked == null)
        {
            CancellationPolicy organizationDefault = await _handler.GetOrganizationDefault(uow, organizationId)
                ?? throw new InvalidOperationException(
                    $"Organizacija {organizationId} nema zadanu politiku otkazivanja (integritetna greška, ADR-0015).");
            picked = (organizationDefault.Id, CancellationPolicyResolvedFrom.OrganizationDefault);
        }

        CancellationPolicyVersion version = await _handler.GetLatestVersion(uow, picked.Value.PolicyId)
            ?? throw new InvalidOperationException($"Politika otkazivanja {picked.Value.PolicyId} nema nijednu verziju (integritetna greška).");

        return new ResolvedCancellationPolicy(
            version.CancellationPolicyId,
            version.Version,
            version.CancellationWindowMinutes,
            new PolicyEventRule(version.LateCancellationFeeType, version.LateCancellationFeeValue, version.LateCancellationPackageAction,
                version.LateCancellationMembershipAction),
            new PolicyEventRule(version.NoShowFeeType, version.NoShowFeeValue, version.NoShowPackageAction, version.NoShowMembershipAction),
            picked.Value.From);
    }

    private static (Guid PolicyId, string From)? Pick(
        List<CancellationPolicyAssignment> candidates, Func<CancellationPolicyAssignment, bool> scope, string from)
    {
        CancellationPolicyAssignment match = candidates.SingleOrDefault(scope);
        return match == null ? null : (match.CancellationPolicyId, from);
    }

    public async Task CreateNeutralOrganizationDefault(IUnitOfWork uow, Guid organizationId, Guid? userId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CancellationPolicy policy = new CancellationPolicy
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = NeutralDefaultName,
            IsActive = true,
            IsOrganizationDefault = true,
            CreatedAt = now,
            CreatedBy = userId
        };
        uow.Context.CancellationPolicies.Add(policy);
        uow.Context.CancellationPolicyVersions.Add(NewVersion(policy, 1, new CancellationPolicyRulesRequest
        {
            CancellationWindowMinutes = NeutralCancellationWindowMinutes,
            LateCancellation = NeutralRule(),
            NoShow = NeutralRule()
        }, userId, now));
        await uow.Context.SaveChangesAsync();
    }

    private static CancellationPolicyEventRuleDto NeutralRule() => new()
    {
        FeeType = CancellationFeeType.None,
        FeeValue = null,
        PackageAction = CancellationPackageAction.None
    };

    #endregion

    #region Profili i verzije

    public async Task<List<CancellationPolicyDto>> GetAll(Guid organizationId)
    {
        List<CancellationPolicy> policies = await _handler.GetAll(organizationId);
        return policies.Select(p => ToDto(p, includeVersions: false)).ToList();
    }

    public async Task<CancellationPolicyDto> GetById(Guid organizationId, Guid id)
    {
        CancellationPolicy policy = await _handler.GetById(organizationId, id)
            ?? throw new NotFoundAppException("CancellationPolicy", id);
        return ToDto(policy, includeVersions: true);
    }

    public async Task<CancellationPolicyDto> Create(Guid organizationId, Guid userId, CancellationPolicyCreateRequest request)
    {
        string name = NormalizeName(request?.Name);
        CancellationPolicyRules.Validate(request);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        CancellationPolicy policy = new CancellationPolicy
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            IsActive = true,
            IsOrganizationDefault = false,
            CreatedAt = now,
            CreatedBy = userId
        };

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await EnsureActiveNameFree(uow, organizationId, name, exceptId: null);
            uow.Context.CancellationPolicies.Add(policy);
            uow.Context.CancellationPolicyVersions.Add(NewVersion(policy, 1, request, userId, now));
            await SaveTranslatingDuplicates(uow, name);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, policy.Id);
    }

    public async Task<CancellationPolicyDto> Rename(Guid organizationId, Guid userId, Guid id, CancellationPolicyRenameRequest request)
    {
        string name = NormalizeName(request?.Name);
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            CancellationPolicy policy = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("CancellationPolicy", id);
            if (policy.IsActive)
                await EnsureActiveNameFree(uow, organizationId, name, exceptId: id);
            policy.Name = name;
            Touch(policy, userId);
            await SaveTranslatingDuplicates(uow, name);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    public async Task<CancellationPolicyDto> PublishVersion(Guid organizationId, Guid userId, Guid id, CancellationPolicyRulesRequest request)
    {
        CancellationPolicyRules.Validate(request);
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            CancellationPolicy policy = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("CancellationPolicy", id);

            // D11: kreiranje verzije = objava; starije verzije se nikad ne mijenjaju. Version + 1 pod lockom profila.
            int next = await _handler.GetLatestVersionNumber(uow, id) + 1;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            uow.Context.CancellationPolicyVersions.Add(NewVersion(policy, next, request, userId, now));
            Touch(policy, userId);
            await uow.Context.SaveChangesAsync();
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    public async Task<CancellationPolicyDto> Activate(Guid organizationId, Guid userId, Guid id)
    {
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            CancellationPolicy policy = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("CancellationPolicy", id);
            if (!policy.IsActive)
            {
                await EnsureActiveNameFree(uow, organizationId, policy.Name, exceptId: id);
                policy.IsActive = true;
                Touch(policy, userId);
                await SaveTranslatingDuplicates(uow, policy.Name);
            }

            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    public async Task<CancellationPolicyDto> Deactivate(Guid organizationId, Guid userId, Guid id)
    {
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            CancellationPolicy policy = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("CancellationPolicy", id);

            // D11: profil koji je zadani ili ima dodjelu se ne deaktivira — nema tihog propadanja na drugi scope.
            if (policy.IsOrganizationDefault || await _handler.HasAssignments(uow, organizationId, id))
                throw new BusinessRuleException(ErrorCodes.CancellationPolicyInUse,
                    "Politika je zadana politika organizacije ili je dodijeljena poslovnici/usluzi — ne može se deaktivirati.");

            if (policy.IsActive)
            {
                policy.IsActive = false;
                Touch(policy, userId);
                await uow.Context.SaveChangesAsync();
            }

            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    public async Task<CancellationPolicyDto> SetOrganizationDefault(Guid organizationId, Guid userId, CancellationPolicyDefaultRequest request)
    {
        Guid id = request?.CancellationPolicyId ?? throw new ValidationAppException("CancellationPolicyId je obavezan.");
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Redoslijed lockova: trenutna zadana pa nova (stabilno, jer je trenutna jedinstvena po organizaciji).
            CancellationPolicy current = await _handler.GetOrganizationDefaultForUpdate(uow, organizationId);
            CancellationPolicy target = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("CancellationPolicy", id);
            if (!target.IsActive)
                throw new BusinessRuleException(ErrorCodes.CancellationPolicyInactive, "Neaktivna politika ne može biti zadana politika organizacije.");

            if (current == null || current.Id != target.Id)
            {
                if (current != null)
                {
                    current.IsOrganizationDefault = false;
                    Touch(current, userId);
                    // Djelomični unique (organization_id) WHERE is_organization_default — stara zadana se gasi prije nove.
                    await uow.Context.SaveChangesAsync();
                }

                target.IsOrganizationDefault = true;
                Touch(target, userId);
                await uow.Context.SaveChangesAsync();
            }

            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    #endregion

    #region Dodjele

    public async Task<List<CancellationPolicyAssignmentDto>> GetAssignments(Guid organizationId)
    {
        List<CancellationPolicyAssignment> assignments = await _handler.GetAssignments(organizationId);
        return assignments.Select(ToDto).ToList();
    }

    public async Task<CancellationPolicyAssignmentDto> Assign(Guid organizationId, Guid userId, CancellationPolicyAssignmentRequest request)
    {
        if (request == null || (!request.CompanyId.HasValue && !request.ServiceId.HasValue))
            throw new ValidationAppException("Dodjela mora imati poslovnicu, uslugu ili oboje.");
        Guid policyId = request.CancellationPolicyId ?? throw new ValidationAppException("CancellationPolicyId je obavezan.");

        if (request.CompanyId.HasValue && await _companyHandler.GetById(organizationId, request.CompanyId.Value) == null)
            throw new NotFoundAppException("Company", request.CompanyId.Value);
        if (request.ServiceId.HasValue && await _serviceHandler.GetById(organizationId, request.ServiceId.Value) == null)
            throw new NotFoundAppException("Service", request.ServiceId.Value);

        Guid assignmentId;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            CancellationPolicy policy = await _handler.GetForUpdate(uow, organizationId, policyId)
                ?? throw new NotFoundAppException("CancellationPolicy", policyId);
            if (!policy.IsActive)
                throw new BusinessRuleException(ErrorCodes.CancellationPolicyInactive, "Neaktivna politika se ne može dodijeliti.");

            DateTimeOffset now = DateTimeOffset.UtcNow;
            CancellationPolicyAssignment existing = await _handler.GetAssignmentForScope(uow, organizationId, request.CompanyId, request.ServiceId);
            if (existing == null)
            {
                existing = new CancellationPolicyAssignment
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    CompanyId = request.CompanyId,
                    ServiceId = request.ServiceId,
                    CancellationPolicyId = policyId,
                    CreatedAt = now,
                    CreatedBy = userId
                };
                uow.Context.CancellationPolicyAssignments.Add(existing);
            }
            else if (existing.CancellationPolicyId != policyId)
            {
                existing.CancellationPolicyId = policyId;
                existing.UpdatedAt = now;
                existing.UpdatedBy = userId;
            }

            try
            {
                await uow.Context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                // Rijedak race: dvije istovremene dodjele istog scopea — gubitnik pada na unique indeksu scopea.
                throw new BusinessRuleException(
                    ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
            }

            assignmentId = existing.Id;
            await uow.CommitAsync();
        }

        List<CancellationPolicyAssignment> all = await _handler.GetAssignments(organizationId);
        return ToDto(all.Single(a => a.Id == assignmentId));
    }

    public async Task RemoveAssignment(Guid organizationId, Guid assignmentId)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        CancellationPolicyAssignment assignment = await _handler.GetAssignment(uow, organizationId, assignmentId)
            ?? throw new NotFoundAppException("CancellationPolicyAssignment", assignmentId);
        uow.Context.CancellationPolicyAssignments.Remove(assignment);
        await uow.Context.SaveChangesAsync();
        await uow.CommitAsync();
    }

    public async Task<CancellationPolicyResolutionDto> Resolve(Guid organizationId, Guid companyId, Guid serviceId)
    {
        if (await _companyHandler.GetById(organizationId, companyId) == null)
            throw new NotFoundAppException("Company", companyId);
        if (await _serviceHandler.GetById(organizationId, serviceId) == null)
            throw new NotFoundAppException("Service", serviceId);

        ResolvedCancellationPolicy resolved;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
            resolved = await ResolveCancellationPolicy(uow, organizationId, companyId, serviceId);

        CancellationPolicy policy = await _handler.GetById(organizationId, resolved.PolicyId);
        CancellationPolicyVersion version = policy.Versions.Single(v => v.Version == resolved.Version);
        return new CancellationPolicyResolutionDto
        {
            CancellationPolicyId = policy.Id,
            CancellationPolicyName = policy.Name,
            ResolvedFrom = resolved.ResolvedFrom,
            Version = ToDto(version)
        };
    }

    #endregion

    #region Pomoćno

    private static CancellationPolicyVersion NewVersion(
        CancellationPolicy policy, int version, CancellationPolicyRulesRequest rules, Guid? userId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = policy.OrganizationId,
        CancellationPolicyId = policy.Id,
        Version = version,
        CancellationWindowMinutes = rules.CancellationWindowMinutes,
        LateCancellationFeeType = rules.LateCancellation.FeeType.GetValueOrDefault(),
        LateCancellationFeeValue = rules.LateCancellation.FeeValue,
        LateCancellationPackageAction = rules.LateCancellation.PackageAction.GetValueOrDefault(),
        LateCancellationMembershipAction = CancellationPolicyRules.MembershipActionFor(
            rules.LateCancellation.MembershipAction, rules.LateCancellation.FeeType.GetValueOrDefault(), rules.LateCancellation.FeeValue),
        NoShowFeeType = rules.NoShow.FeeType.GetValueOrDefault(),
        NoShowFeeValue = rules.NoShow.FeeValue,
        NoShowPackageAction = rules.NoShow.PackageAction.GetValueOrDefault(),
        NoShowMembershipAction = CancellationPolicyRules.MembershipActionFor(
            rules.NoShow.MembershipAction, rules.NoShow.FeeType.GetValueOrDefault(), rules.NoShow.FeeValue),
        CreatedAt = now,
        CreatedBy = userId
    };

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationAppException("Naziv politike je obavezan.");
        return name.Trim();
    }

    private async Task EnsureActiveNameFree(IUnitOfWork uow, Guid organizationId, string name, Guid? exceptId)
    {
        if (await _handler.ActiveNameExists(uow, organizationId, name, exceptId))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna politika otkazivanja s nazivom '{name}' već postoji.");
    }

    private static async Task SaveTranslatingDuplicates(IUnitOfWork uow, string name)
    {
        try
        {
            await uow.Context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna politika otkazivanja s nazivom '{name}' već postoji.");
        }
    }

    private static void Touch(CancellationPolicy policy, Guid userId)
    {
        policy.UpdatedAt = DateTimeOffset.UtcNow;
        policy.UpdatedBy = userId;
    }

    private static CancellationPolicyDto ToDto(CancellationPolicy policy, bool includeVersions)
    {
        List<CancellationPolicyVersion> ordered = policy.Versions.OrderByDescending(v => v.Version).ToList();
        return new CancellationPolicyDto
        {
            Id = policy.Id,
            Name = policy.Name,
            IsActive = policy.IsActive,
            IsOrganizationDefault = policy.IsOrganizationDefault,
            LatestVersion = ordered.Count > 0 ? ToDto(ordered[0]) : null,
            Versions = includeVersions ? ordered.Select(ToDto).ToList() : new List<CancellationPolicyVersionDto>(),
            CreatedAt = policy.CreatedAt,
            CreatedBy = policy.CreatedBy,
            UpdatedAt = policy.UpdatedAt,
            UpdatedBy = policy.UpdatedBy
        };
    }

    private static CancellationPolicyVersionDto ToDto(CancellationPolicyVersion version) => new()
    {
        Id = version.Id,
        Version = version.Version,
        CancellationWindowMinutes = version.CancellationWindowMinutes,
        LateCancellation = new CancellationPolicyEventRuleDto
        {
            FeeType = version.LateCancellationFeeType,
            FeeValue = version.LateCancellationFeeValue,
            PackageAction = version.LateCancellationPackageAction,
            MembershipAction = version.LateCancellationMembershipAction
        },
        NoShow = new CancellationPolicyEventRuleDto
        {
            FeeType = version.NoShowFeeType,
            FeeValue = version.NoShowFeeValue,
            PackageAction = version.NoShowPackageAction,
            MembershipAction = version.NoShowMembershipAction
        },
        CreatedAt = version.CreatedAt,
        CreatedBy = version.CreatedBy
    };

    private static CancellationPolicyAssignmentDto ToDto(CancellationPolicyAssignment a) => new()
    {
        Id = a.Id,
        CompanyId = a.CompanyId,
        CompanyName = a.Company?.Name,
        ServiceId = a.ServiceId,
        ServiceName = a.Service?.Name,
        CancellationPolicyId = a.CancellationPolicyId,
        CancellationPolicyName = a.Policy?.Name,
        CreatedAt = a.CreatedAt,
        CreatedBy = a.CreatedBy,
        UpdatedAt = a.UpdatedAt,
        UpdatedBy = a.UpdatedBy
    };

    #endregion
}
