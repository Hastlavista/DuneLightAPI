using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class ClientService : IClientService
{
    private readonly IClientHandler _clientHandler;
    private readonly IClientTagHandler _clientTagHandler;
    private readonly ILocationHandler _locationHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IClientFutureActivityProvider _futureActivityProvider;

    public ClientService(
        IClientHandler clientHandler,
        IClientTagHandler clientTagHandler,
        ILocationHandler locationHandler,
        IEmployeeHandler employeeHandler,
        IClientFutureActivityProvider futureActivityProvider)
    {
        _clientHandler = clientHandler;
        _clientTagHandler = clientTagHandler;
        _locationHandler = locationHandler;
        _employeeHandler = employeeHandler;
        _futureActivityProvider = futureActivityProvider;
    }

    public async Task<PagedResult<ClientDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? tagId, Guid? homeTrainerId, Guid? homeLocationId,
        bool mineFirst, Guid currentUserId)
    {
        Guid? mineFirstEmployeeId = null;
        if (mineFirst)
        {
            Employee currentEmployee = await _employeeHandler.GetByUserId(organizationId, currentUserId);
            mineFirstEmployeeId = currentEmployee?.Id;
        }

        (List<Client> items, int totalCount) = await _clientHandler.GetPaged(
            organizationId, request, tagId, homeTrainerId, homeLocationId, mineFirstEmployeeId);

        return PagedResult<ClientDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ClientDto> GetById(Guid organizationId, Guid id)
    {
        Client client = await _clientHandler.GetById(organizationId, id);
        if (client == null)
            throw new NotFoundAppException("Client", id);

        return ToDto(client);
    }

    public async Task<ClientDto> Create(Guid organizationId, Guid userId, ClientCreateRequest request)
    {
        ValidateDateOfBirth(request.DateOfBirth);
        ValidateGdprConsent(request.GdprConsentGiven, request.GdprConsentDate);
        await EnsureMemberNumberIsFree(organizationId, request.MemberNumber, excludeId: null);
        await EnsureHomeLocationExists(organizationId, request.HomeLocationId);
        await EnsureHomeTrainerExists(organizationId, request.HomeTrainerId);
        await EnsureTagsExist(organizationId, request.TagIds);

        Client client = new Client
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            MemberNumber = request.MemberNumber,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DateOfBirth = request.DateOfBirth,
            Occupation = request.Occupation,
            Phone = request.Phone,
            Email = request.Email,
            Note = request.Note,
            HealthNote = request.HealthNote,
            GdprConsentGiven = request.GdprConsentGiven,
            GdprConsentDate = request.GdprConsentDate,
            HomeLocationId = request.HomeLocationId,
            HomeTrainerId = request.HomeTrainerId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        client.Tags = BuildTags(request.TagIds);

        await _clientHandler.Add(client);
        return await GetById(organizationId, client.Id.GetValueOrDefault());
    }

    public async Task<ClientDto> Update(Guid organizationId, Guid userId, Guid id, ClientUpdateRequest request)
    {
        Client existing = await _clientHandler.GetByIdLight(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Client", id);

        EnsureNotAnonymized(existing);
        ValidateDateOfBirth(request.DateOfBirth);
        ValidateGdprConsent(request.GdprConsentGiven, request.GdprConsentDate);
        await EnsureMemberNumberIsFree(organizationId, request.MemberNumber, excludeId: id);
        await EnsureHomeLocationExists(organizationId, request.HomeLocationId);
        await EnsureHomeTrainerExists(organizationId, request.HomeTrainerId);
        await EnsureTagsExist(organizationId, request.TagIds);

        existing.MemberNumber = request.MemberNumber;
        existing.FirstName = request.FirstName;
        existing.LastName = request.LastName;
        existing.DateOfBirth = request.DateOfBirth;
        existing.Occupation = request.Occupation;
        existing.Phone = request.Phone;
        existing.Email = request.Email;
        existing.Note = request.Note;
        existing.HealthNote = request.HealthNote;
        existing.GdprConsentGiven = request.GdprConsentGiven;
        existing.GdprConsentDate = request.GdprConsentDate;
        existing.HomeLocationId = request.HomeLocationId;
        existing.HomeTrainerId = request.HomeTrainerId;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedBy = userId;

        List<ClientTagAssignment> newTags = BuildTags(request.TagIds);

        await _clientHandler.Update(existing, newTags);
        return await GetById(organizationId, id);
    }

    public async Task<ClientDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Client client = await _clientHandler.GetByIdLight(organizationId, id);
        if (client == null)
            throw new NotFoundAppException("Client", id);

        if (isActive)
            EnsureNotAnonymized(client);

        if (isActive != client.IsActive)
            await _clientHandler.SetActiveAndStamp(organizationId, id, isActive, DateTimeOffset.UtcNow, userId);

        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Client client = await _clientHandler.GetByIdLight(organizationId, id);
        if (client == null)
            throw new NotFoundAppException("Client", id);

        bool hasFutureActivity = await _futureActivityProvider.HasFutureActivity(organizationId, id);
        if (hasFutureActivity)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Klijent je referenciran (termini/paketi) i ne može se trajno obrisati — deaktivirajte ga umjesto toga.");

        await _clientHandler.Delete(client);
    }

    public async Task<ClientDto> Anonymize(Guid organizationId, Guid userId, Guid id)
    {
        Client client = await _clientHandler.GetByIdLight(organizationId, id);
        if (client == null)
            throw new NotFoundAppException("Client", id);

        if (!client.IsAnonymized)
            await _clientHandler.Anonymize(organizationId, id, DateTimeOffset.UtcNow, userId);

        return await GetById(organizationId, id);
    }

    public async Task<List<ClientBirthdayDto>> GetBirthdays(Guid organizationId, DateTimeOffset from, DateTimeOffset to)
    {
        List<Client> candidates = await _clientHandler.GetBirthdayCandidates(organizationId);

        List<ClientBirthdayDto> result = new List<ClientBirthdayDto>();
        foreach (Client client in candidates)
        {
            (bool isInRange, DateTimeOffset occurrence) = FindOccurrenceInRange(client.DateOfBirth.Value, from, to);
            if (!isInRange)
                continue;

            result.Add(new ClientBirthdayDto
            {
                Id = client.Id.GetValueOrDefault(),
                FirstName = client.FirstName,
                LastName = client.LastName,
                Phone = client.Phone,
                DateOfBirth = client.DateOfBirth.Value,
                NextOccurrence = occurrence
            });
        }

        return result.OrderBy(r => r.NextOccurrence).ToList();
    }

    public async Task<int> GetNextMemberNumberSuggestion(Guid organizationId)
    {
        return await _clientHandler.GetNextMemberNumber(organizationId);
    }

    private static void ValidateDateOfBirth(DateTimeOffset? dateOfBirth)
    {
        if (dateOfBirth.HasValue && dateOfBirth.Value.Date > DateTimeOffset.UtcNow.Date)
            throw new ValidationAppException("Datum rođenja ne smije biti u budućnosti.");
    }

    private static void ValidateGdprConsent(bool consentGiven, DateTimeOffset? consentDate)
    {
        if (consentGiven && !consentDate.HasValue)
            throw new ValidationAppException("Datum GDPR suglasnosti je obavezan kad je suglasnost dana.");
    }

    private static void EnsureNotAnonymized(Client client)
    {
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran i više se ne može uređivati.");
    }

    private async Task EnsureMemberNumberIsFree(Guid organizationId, int memberNumber, Guid? excludeId)
    {
        bool taken = await _clientHandler.IsMemberNumberTaken(organizationId, memberNumber, excludeId);
        if (taken)
            throw new BusinessRuleException(ErrorCodes.DuplicateMemberNumber, $"Broj člana {memberNumber} je već zauzet.");
    }

    private async Task EnsureHomeLocationExists(Guid organizationId, Guid? homeLocationId)
    {
        if (!homeLocationId.HasValue)
            return;

        Location location = await _locationHandler.GetById(organizationId, homeLocationId.Value);
        if (location == null)
            throw new NotFoundAppException("Location", homeLocationId.Value);
    }

    private async Task EnsureHomeTrainerExists(Guid organizationId, Guid? homeTrainerId)
    {
        if (!homeTrainerId.HasValue)
            return;

        Employee employee = await _employeeHandler.GetByIdLight(organizationId, homeTrainerId.Value);
        if (employee == null)
            throw new NotFoundAppException("Employee", homeTrainerId.Value);
    }

    private async Task EnsureTagsExist(Guid organizationId, List<Guid> tagIds)
    {
        if (tagIds == null)
            return;

        List<Guid> distinctIds = tagIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return;

        List<ClientTag> tags = await _clientTagHandler.GetByIds(organizationId, distinctIds);
        if (tags.Count != distinctIds.Count)
        {
            HashSet<Guid> foundIds = tags.Select(t => t.Id.GetValueOrDefault()).ToHashSet();
            Guid missingId = distinctIds.First(id => !foundIds.Contains(id));
            throw new NotFoundAppException("ClientTag", missingId);
        }
    }

    private static List<ClientTagAssignment> BuildTags(List<Guid> tagIds)
    {
        if (tagIds == null)
            return new List<ClientTagAssignment>();

        return tagIds.Distinct().Select(tagId => new ClientTagAssignment
        {
            Id = Guid.NewGuid(),
            TagId = tagId
        }).ToList();
    }

    /// <summary>Nalazi prvu pojavu datuma rođenja (bez obzira na stvarnu godinu) unutar [from, to], provjeravajući godinu 'from' i sljedeću (pokriva prijelaz preko Nove godine).</summary>
    private static (bool IsInRange, DateTimeOffset Occurrence) FindOccurrenceInRange(DateTimeOffset dateOfBirth, DateTimeOffset from, DateTimeOffset to)
    {
        foreach (int year in new[] { from.Year, from.Year + 1 })
        {
            int day = dateOfBirth.Day;
            int month = dateOfBirth.Month;
            if (month == 2 && day == 29 && !DateTime.IsLeapYear(year))
                day = 28;

            DateTimeOffset occurrence = new DateTimeOffset(year, month, day, 0, 0, 0, from.Offset);
            if (occurrence.Date >= from.Date && occurrence.Date <= to.Date)
                return (true, occurrence);
        }

        return (false, default);
    }

    private static ClientDto ToDto(Client client)
    {
        return new ClientDto
        {
            Id = client.Id.GetValueOrDefault(),
            MemberNumber = client.MemberNumber,
            FirstName = client.FirstName,
            LastName = client.LastName,
            DateOfBirth = client.DateOfBirth,
            Occupation = client.Occupation,
            Phone = client.Phone,
            Email = client.Email,
            Note = client.Note,
            HealthNote = client.HealthNote,
            GdprConsentGiven = client.GdprConsentGiven,
            GdprConsentDate = client.GdprConsentDate,
            HomeLocationId = client.HomeLocationId,
            HomeLocationName = client.HomeLocation?.Name,
            HomeTrainerId = client.HomeTrainerId,
            HomeTrainerName = client.HomeTrainer != null ? $"{client.HomeTrainer.FirstName} {client.HomeTrainer.LastName}" : null,
            IsActive = client.IsActive,
            IsAnonymized = client.IsAnonymized,
            AnonymizedAt = client.AnonymizedAt,
            Tags = client.Tags.Select(t => new ClientTagRefDto
            {
                TagId = t.TagId,
                Name = t.Tag?.Name,
                ColorHex = t.Tag?.ColorHex
            }).ToList(),
            CreatedAt = client.CreatedAt,
            CreatedBy = client.CreatedBy,
            UpdatedAt = client.UpdatedAt,
            UpdatedBy = client.UpdatedBy
        };
    }
}
