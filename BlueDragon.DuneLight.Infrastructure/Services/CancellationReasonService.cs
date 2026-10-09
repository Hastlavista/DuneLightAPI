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
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// K1-4 (12.3) — šifrarnik razloga otkazivanja i izostanka. Šifra se ne briše (sudjelovanja je referenciraju), samo deaktivira;
/// sudjelovanje pamti naziv iz trenutka događaja. <see cref="ResolveForEvent"/> je JEDINA provjera odabira (aktivnost, događaj,
/// obaveznost po postavci organizacije). Bez utjecaja na politiku naplate (P-10 / P1+).
/// </summary>
public class CancellationReasonService : ICancellationReasonService
{
    private readonly ICancellationReasonHandler _handler;
    private readonly IOrganizationSettingsHandler _settingsHandler;

    public CancellationReasonService(ICancellationReasonHandler handler, IOrganizationSettingsHandler settingsHandler)
    {
        _handler = handler;
        _settingsHandler = settingsHandler;
    }

    public async Task<List<CancellationReasonDto>> GetAll(Guid organizationId, bool? isActive, CancellationReasonEvent? appliesTo) =>
        (await _handler.GetAll(organizationId))
            .Where(r => isActive == null || r.IsActive == isActive)
            .Where(r => appliesTo == null || AppliesTo(r, appliesTo.Value))
            .Select(ToDto)
            .ToList();

    public async Task<CancellationReasonDto> Create(Guid organizationId, Guid userId, CancellationReasonUpsertRequest request)
    {
        string name = Validate(request);
        await EnsureNameFree(organizationId, name, excludeId: null);
        CancellationReason reason = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };
        Apply(reason, request, name);
        await _handler.Add(reason);
        return ToDto(reason);
    }

    public async Task<CancellationReasonDto> Update(Guid organizationId, Guid userId, Guid id, CancellationReasonUpsertRequest request)
    {
        CancellationReason reason = await _handler.GetById(organizationId, id) ?? throw new NotFoundAppException("CancellationReason", id);
        string name = Validate(request);
        if (reason.IsActive)
            await EnsureNameFree(organizationId, name, excludeId: id);
        Apply(reason, request, name);
        reason.UpdatedAt = DateTimeOffset.UtcNow;
        reason.UpdatedBy = userId;
        await _handler.Update(reason);
        return ToDto(reason);
    }

    public async Task<CancellationReasonDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        CancellationReason reason = await _handler.GetById(organizationId, id) ?? throw new NotFoundAppException("CancellationReason", id);
        if (reason.IsActive == isActive)
            return ToDto(reason);
        if (isActive)
            await EnsureNameFree(organizationId, reason.Name, excludeId: id);
        reason.IsActive = isActive;
        reason.UpdatedAt = DateTimeOffset.UtcNow;
        reason.UpdatedBy = userId;
        await _handler.Update(reason);
        return ToDto(reason);
    }

    public async Task<(Guid? Id, string Name)> ResolveForEvent(Guid organizationId, Guid? reasonId, CancellationReasonEvent reasonEvent)
    {
        if (reasonId.HasValue)
        {
            CancellationReason reason = await _handler.GetById(organizationId, reasonId.Value)
                ?? throw new NotFoundAppException("CancellationReason", reasonId.Value);
            if (!reason.IsActive || !AppliesTo(reason, reasonEvent))
                throw new BusinessRuleException(ErrorCodes.CancellationReasonNotApplicable,
                    $"Razlog '{reason.Name}' nije aktivan ili ne vrijedi za ovaj događaj.");
            return (reason.Id, reason.Name);
        }

        OrganizationSettings settings = await _settingsHandler.GetByOrganizationId(organizationId);
        bool required = reasonEvent switch
        {
            CancellationReasonEvent.ClientCancellation => settings?.CancellationReasonRequiredClient == true,
            CancellationReasonEvent.BusinessCancellation => settings?.CancellationReasonRequiredBusiness == true,
            _ => settings?.CancellationReasonRequiredNoShow == true
        };
        // Obavezno samo kad za događaj postoji barem jedna aktivna šifra (bez šifara nema što odabrati).
        if (required && (await _handler.GetAll(organizationId)).Any(r => r.IsActive && AppliesTo(r, reasonEvent)))
            throw new ValidationAppException(ErrorCodes.CancellationReasonRequired, "Odabir razloga je obavezan.");
        return (null, null);
    }

    private static bool AppliesTo(CancellationReason reason, CancellationReasonEvent reasonEvent) => reasonEvent switch
    {
        CancellationReasonEvent.ClientCancellation => reason.AppliesToClientCancellation,
        CancellationReasonEvent.BusinessCancellation => reason.AppliesToBusinessCancellation,
        _ => reason.AppliesToNoShow
    };

    private static string Validate(CancellationReasonUpsertRequest request)
    {
        string name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new ValidationAppException("Naziv razloga je obavezan.");
        if (!request.AppliesToClientCancellation && !request.AppliesToBusinessCancellation && !request.AppliesToNoShow)
            throw new ValidationAppException("Razlog mora vrijediti za barem jedan događaj (otkaz klijenta, otkaz studija ili izostanak).");
        return name;
    }

    private async Task EnsureNameFree(Guid organizationId, string name, Guid? excludeId)
    {
        if (await _handler.NameExistsAmongActive(organizationId, name, excludeId))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivni razlog s nazivom '{name}' već postoji.");
    }

    private static void Apply(CancellationReason reason, CancellationReasonUpsertRequest request, string name)
    {
        reason.Name = name;
        reason.SortOrder = request.SortOrder;
        reason.AppliesToClientCancellation = request.AppliesToClientCancellation;
        reason.AppliesToBusinessCancellation = request.AppliesToBusinessCancellation;
        reason.AppliesToNoShow = request.AppliesToNoShow;
    }

    private static CancellationReasonDto ToDto(CancellationReason r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        IsActive = r.IsActive,
        SortOrder = r.SortOrder,
        AppliesToClientCancellation = r.AppliesToClientCancellation,
        AppliesToBusinessCancellation = r.AppliesToBusinessCancellation,
        AppliesToNoShow = r.AppliesToNoShow
    };
}
