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
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class ClientService : IClientService
{
    private readonly IClientHandler _clientHandler;
    private readonly IClientTagHandler _clientTagHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IGroupHandler _groupHandler;
    private readonly IClientPackageHandler _clientPackageHandler;
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly IWaitlistHandler _waitlistHandler;
    private readonly IClientFutureActivityProvider _futureActivityProvider;
    private readonly TimeProvider _timeProvider;

    public ClientService(
        IClientHandler clientHandler,
        IClientTagHandler clientTagHandler,
        ICompanyHandler companyHandler,
        IEmployeeHandler employeeHandler,
        IAppointmentHandler appointmentHandler,
        IGroupHandler groupHandler,
        IClientPackageHandler clientPackageHandler,
        IWaitlistHandler waitlistHandler,
        IClientFutureActivityProvider futureActivityProvider,
        IOrganizationCalendarService organizationCalendarService,
        TimeProvider timeProvider)
    {
        _organizationCalendarService = organizationCalendarService;
        _clientHandler = clientHandler;
        _clientTagHandler = clientTagHandler;
        _companyHandler = companyHandler;
        _employeeHandler = employeeHandler;
        _appointmentHandler = appointmentHandler;
        _groupHandler = groupHandler;
        _clientPackageHandler = clientPackageHandler;
        _waitlistHandler = waitlistHandler;
        _futureActivityProvider = futureActivityProvider;
        _timeProvider = timeProvider;
    }

    public async Task<PagedResult<ClientDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? tagId, Guid? homeTrainerId, Guid? homeCompanyId,
        bool mineFirst, Guid currentUserId)
    {
        Guid? mineFirstEmployeeId = null;
        if (mineFirst)
        {
            Employee currentEmployee = await _employeeHandler.GetByUserId(organizationId, currentUserId);
            mineFirstEmployeeId = currentEmployee?.Id;
        }

        (List<Client> items, int totalCount) = await _clientHandler.GetPaged(
            organizationId, request, tagId, homeTrainerId, homeCompanyId, mineFirstEmployeeId);

        List<Guid> clientIds = items.Select(c => c.Id.GetValueOrDefault()).ToList();
        Dictionary<Guid, int> noShowCountsByClientId = await _appointmentHandler.GetNoShowCountsByClientIds(organizationId, clientIds);

        List<ClientDto> dtos = items
            .Select(c => ToDto(c, noShowCountsByClientId.GetValueOrDefault(c.Id.GetValueOrDefault())))
            .ToList();

        return PagedResult<ClientDto>.Create(dtos, totalCount, request.Page, request.PageSize);
    }

    public async Task<ClientDto> GetById(Guid organizationId, Guid id)
    {
        Client client = await _clientHandler.GetById(organizationId, id);
        if (client == null)
            throw new NotFoundAppException("Client", id);

        Dictionary<Guid, int> noShowCountsByClientId = await _appointmentHandler.GetNoShowCountsByClientIds(organizationId, new List<Guid> { id });
        return ToDto(client, noShowCountsByClientId.GetValueOrDefault(id));
    }

    public async Task<ClientDto> Create(Guid organizationId, Guid userId, ClientCreateRequest request)
    {
        DateOnly today = await Today(organizationId);
        ValidateDateOfBirth(request.DateOfBirth, today);
        ValidateGdprConsent(request.GdprConsentGiven, request.GdprConsentDate, today);
        if (request.MemberNumber.HasValue)
            await EnsureManualMemberNumberAllowed(organizationId, request.MemberNumber.Value, excludeId: null, request.ConfirmMemberNumberJump);
        string email = EmailNormalizer.Normalize(request.Email);
        await EnsureEmailIsFree(organizationId, email, excludeId: null);
        // Create nema prethodnu vrijednost — svaki zadani HomeCompany/HomeTrainer se tretira kao nova dodjela
        // i mora biti aktivan (za razliku od Update, gdje je nepromijenjena dodjela "grandfathered").
        await EnsureHomeCompanyValid(organizationId, request.HomeCompanyId, previousHomeCompanyId: null);
        await EnsureHomeTrainerValid(organizationId, request.HomeTrainerId, previousHomeTrainerId: null);
        await EnsureTagsExist(organizationId, request.TagIds);

        Client client = new Client
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            MemberNumber = request.MemberNumber ?? 0,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DateOfBirth = request.DateOfBirth,
            Occupation = request.Occupation,
            Phone = request.Phone,
            Email = email,
            Note = request.Note,
            HealthNote = request.HealthNote,
            GdprConsentGiven = request.GdprConsentGiven,
            GdprConsentDate = request.GdprConsentGiven ? request.GdprConsentDate : null,
            HomeCompanyId = request.HomeCompanyId,
            HomeTrainerId = request.HomeTrainerId,
            IsActive = true,
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedBy = userId
        };

        client.Tags = BuildTags(request.TagIds);

        try
        {
            // K1-3: bez ručnog broja handler dodjeljuje sljedeći pod lockom organizacije.
            // T1-9: dana suglasnost pri kreiranju je promjena (prije: nije dana) i ide u povijest klijenta, u istoj transakciji.
            List<ClientAuditLog> audit = GdprConsentAudit(client, userId, oldGiven: false, oldDate: null);
            await _clientHandler.Add(client, assignMemberNumber: !request.MemberNumber.HasValue, audit);
        }
        catch (DbUpdateException ex) when (IsEmailUniqueViolation(ex))
        {
            throw EmailTaken(email);
        }
        catch (DbUpdateException ex) when (IsMemberNumberUniqueViolation(ex))
        {
            throw MemberNumberTaken(client.MemberNumber);
        }

        return await GetById(organizationId, client.Id.GetValueOrDefault());
    }

    public async Task<ClientDto> Update(Guid organizationId, Guid userId, Guid id, ClientUpdateRequest request)
    {
        Client existing = await _clientHandler.GetByIdLight(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Client", id);

        EnsureNotAnonymized(existing);
        DateOnly today = await Today(organizationId);
        ValidateDateOfBirth(request.DateOfBirth, today);
        ValidateGdprConsent(request.GdprConsentGiven, request.GdprConsentDate, today);
        bool oldGdprGiven = existing.GdprConsentGiven;
        DateOnly? oldGdprDate = existing.GdprConsentDate;
        int memberNumber = request.MemberNumber ?? existing.MemberNumber;
        if (memberNumber != existing.MemberNumber)
            await EnsureManualMemberNumberAllowed(organizationId, memberNumber, excludeId: id, request.ConfirmMemberNumberJump);
        string email = EmailNormalizer.Normalize(request.Email);
        await EnsureEmailIsFree(organizationId, email, excludeId: id);
        // Nepromijenjena dodjela ostaje "grandfathered" i smije upućivati na sad-neaktivnu Company/Employee;
        // tek promjena na drugu (ili novo postavljanje) zahtijeva da meta bude aktivna.
        await EnsureHomeCompanyValid(organizationId, request.HomeCompanyId, existing.HomeCompanyId);
        await EnsureHomeTrainerValid(organizationId, request.HomeTrainerId, existing.HomeTrainerId);
        await EnsureTagsExist(organizationId, request.TagIds);

        existing.MemberNumber = memberNumber;
        existing.FirstName = request.FirstName;
        existing.LastName = request.LastName;
        existing.DateOfBirth = request.DateOfBirth;
        existing.Occupation = request.Occupation;
        existing.Phone = request.Phone;
        existing.Email = email;
        existing.Note = request.Note;
        existing.HealthNote = request.HealthNote;
        existing.GdprConsentGiven = request.GdprConsentGiven;
        existing.GdprConsentDate = request.GdprConsentGiven ? request.GdprConsentDate : null;
        existing.HomeCompanyId = request.HomeCompanyId;
        existing.HomeTrainerId = request.HomeTrainerId;
        existing.UpdatedAt = _timeProvider.GetUtcNow();
        existing.UpdatedBy = userId;

        List<ClientTagAssignment> newTags = BuildTags(request.TagIds);

        try
        {
            await _clientHandler.Update(existing, newTags, GdprConsentAudit(existing, userId, oldGdprGiven, oldGdprDate));
        }
        catch (DbUpdateException ex) when (IsEmailUniqueViolation(ex))
        {
            throw EmailTaken(email);
        }
        catch (DbUpdateException ex) when (IsMemberNumberUniqueViolation(ex))
        {
            throw MemberNumberTaken(memberNumber);
        }

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
            await _clientHandler.SetActiveAndStamp(organizationId, id, isActive, _timeProvider.GetUtcNow(), userId);

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
        {
            await EnsureNoActiveBusinessRelationships(organizationId, id);
            // T1-9: anonimizacija briše suglasnost — i to je promjena suglasnosti koja ide u povijest klijenta.
            DateTimeOffset anonymizedAt = _timeProvider.GetUtcNow();
            List<ClientAuditLog> audit = GdprConsentAudit(organizationId, id, userId, anonymizedAt,
                client.GdprConsentGiven, client.GdprConsentDate, newGiven: false, newDate: null, reason: "Anonimizacija");
            await _clientHandler.Anonymize(organizationId, id, anonymizedAt, userId, audit);
        }

        return await GetById(organizationId, id);
    }

    /// <summary>
    /// Anonimizacija ne smije ostaviti operativno aktivno poslovno stanje vezano na anonimizirani identitet.
    /// Namjerno NE otkazuje/uklanja ništa automatski (nema skrivenih scheduling/financijskih posljedica) —
    /// samo odbija operaciju dok se te relacije eksplicitno ne razriješe u vlastitim modulima (termini/grupe/paketi).
    /// </summary>
    private async Task EnsureNoActiveBusinessRelationships(Guid organizationId, Guid clientId)
    {
        // Paket nije vezan uz poslovnicu — "danas" za valjanost paketa je današnji datum u kalendaru organizacije.
        DateOnly today = (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(_timeProvider.GetUtcNow());

        bool hasFutureScheduledAppointments = await _appointmentHandler.HasFutureScheduledForClient(organizationId, clientId);
        bool hasActiveGroupMemberships = await _groupHandler.HasActiveMembershipForClient(organizationId, clientId);
        bool hasUsablePackages = await _clientPackageHandler.HasUsableForClient(organizationId, clientId, today);
        bool hasActiveWaitlistEntries = await _waitlistHandler.HasActiveWaitingForClient(organizationId, clientId);

        if (!hasFutureScheduledAppointments && !hasActiveGroupMemberships && !hasUsablePackages && !hasActiveWaitlistEntries)
            return;

        throw new BusinessRuleException(
            ErrorCodes.ClientHasActiveRelationships,
            "Klijent ima aktivne poslovne relacije (budući termini, aktivno članstvo u grupi, iskoristiv paket i/ili aktivna lista čekanja) i ne može se anonimizirati dok se one ne razriješe.",
            new
            {
                futureAppointments = hasFutureScheduledAppointments,
                activeGroupMemberships = hasActiveGroupMemberships,
                activePackages = hasUsablePackages,
                activeWaitlistEntries = hasActiveWaitlistEntries
            });
    }

    public async Task<List<ClientBirthdayDto>> GetBirthdays(Guid organizationId, DateOnly from, DateOnly to)
    {
        List<Client> candidates = await _clientHandler.GetBirthdayCandidates(organizationId);

        List<ClientBirthdayDto> result = new List<ClientBirthdayDto>();
        foreach (Client client in candidates)
        {
            (bool isInRange, DateOnly occurrence) = FindOccurrenceInRange(client.DateOfBirth.Value, from, to);
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

    /// <summary>T1-7: "danas" = poslovni dan organizacije (poslovni sat, zona organizacije).</summary>
    private static void ValidateDateOfBirth(DateOnly? dateOfBirth, DateOnly today)
    {
        if (dateOfBirth.HasValue && dateOfBirth.Value > today)
            throw new ValidationAppException("Datum rođenja ne smije biti u budućnosti.");
    }

    /// <summary>T1-9: datum je obavezan kad je suglasnost dana i ne smije biti nakon današnjeg dana organizacije (poslovni sat).
    /// Bez suglasnosti se datum ne sprema, pa se ni ne provjerava.</summary>
    private static void ValidateGdprConsent(bool consentGiven, DateOnly? consentDate, DateOnly today)
    {
        if (consentGiven && !consentDate.HasValue)
            throw new ValidationAppException("Datum GDPR suglasnosti je obavezan kad je suglasnost dana.");
        if (consentGiven && consentDate.Value > today)
            throw new ValidationAppException(ErrorCodes.GdprConsentDateInFuture,
                $"Datum GDPR suglasnosti ({consentDate.Value:dd.MM.yyyy.}) ne smije biti nakon današnjeg dana ({today:dd.MM.yyyy.}).");
    }

    /// <summary>T1-9: zapis povijesti za promjenu zastavice i/ili datuma GDPR suglasnosti (staro → novo, tko, kada); prazno kad
    /// se ništa nije promijenilo.</summary>
    private List<ClientAuditLog> GdprConsentAudit(Client client, Guid userId, bool oldGiven, DateOnly? oldDate) =>
        GdprConsentAudit(client.OrganizationId, client.Id.GetValueOrDefault(), userId, _timeProvider.GetUtcNow(),
            oldGiven, oldDate, client.GdprConsentGiven, client.GdprConsentDate, reason: null);

    private static List<ClientAuditLog> GdprConsentAudit(
        Guid organizationId, Guid clientId, Guid userId, DateTimeOffset changedAt,
        bool oldGiven, DateOnly? oldDate, bool newGiven, DateOnly? newDate, string reason)
    {
        if (oldGiven == newGiven && oldDate == newDate)
            return new List<ClientAuditLog>();
        return new List<ClientAuditLog>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ClientId = clientId,
                ChangeType = ClientAuditChangeTypes.GdprConsent,
                OldValue = ClientAuditChangeTypes.GdprValue(oldGiven, oldDate),
                NewValue = ClientAuditChangeTypes.GdprValue(newGiven, newDate),
                Reason = reason,
                ChangedAt = changedAt,
                ChangedBy = userId
            }
        };
    }

    private async Task<DateOnly> Today(Guid organizationId) =>
        (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(_timeProvider.GetUtcNow());

    private static void EnsureNotAnonymized(Client client)
    {
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran i više se ne može uređivati.");
    }

    /// <summary>K1-3 — ručni broj (prijenos iz Excela): ≥ 1 i slobodan; broj veći od dosadašnjeg najvećeg za više od
    /// <see cref="MemberNumberJumpThreshold"/> traži svjesnu potvrdu jer automatsko brojanje nastavlja od njega.</summary>
    private async Task EnsureManualMemberNumberAllowed(Guid organizationId, int memberNumber, Guid? excludeId, bool confirmJump)
    {
        if (memberNumber < 1)
            throw new ValidationAppException("Broj člana mora biti pozitivan.");

        bool taken = await _clientHandler.IsMemberNumberTaken(organizationId, memberNumber, excludeId);
        if (taken)
            throw MemberNumberTaken(memberNumber);

        int currentMax = await _clientHandler.GetNextMemberNumber(organizationId) - 1;
        if (!confirmJump && memberNumber > currentMax + MemberNumberJumpThreshold)
            throw new BusinessRuleException(ErrorCodes.MemberNumberJumpNotConfirmed,
                $"Broj člana {memberNumber} je znatno veći od dosadašnjeg najvećeg ({currentMax}); automatsko brojanje bi nastavilo od {memberNumber + 1}. Potvrdite ako je to namjera.",
                new { memberNumber, currentMax });
    }

    private const int MemberNumberJumpThreshold = 1000;

    private static BusinessRuleException MemberNumberTaken(int memberNumber) =>
        new(ErrorCodes.DuplicateMemberNumber, $"Broj člana {memberNumber} je već zauzet.");

    /// <summary>K1-3 — utrka dva upisa istog broja (provjera iznad je prošla za oba) → isti kod kao provjera, ne 500.</summary>
    private static bool IsMemberNumberUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ClientMemberNumberUniqueIndex };

    private const string ClientMemberNumberUniqueIndex = "ux_clients_org_member_number";

    /// <summary>ADR-0020 — email klijenta je jedinstven unutar organizacije, trimano i bez obzira na velika/mala slova.
    /// Klijent bez emaila (null) je uvijek dopušten.</summary>
    private async Task EnsureEmailIsFree(Guid organizationId, string email, Guid? excludeId)
    {
        if (email == null)
            return;

        bool taken = await _clientHandler.IsEmailTaken(organizationId, EmailNormalizer.ComparisonKey(email), excludeId);
        if (taken)
            throw EmailTaken(email);
    }

    private static BusinessRuleException EmailTaken(string email) =>
        new(ErrorCodes.ClientEmailAlreadyInUse, $"Klijent s email adresom {email} već postoji u organizaciji.");

    /// <summary>Utrka dva istovremena upisa istog emaila — provjera iznad je prošla za oba, unique indeks hvata drugi.</summary>
    private static bool IsEmailUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ClientEmailUniqueIndex };

    private const string ClientEmailUniqueIndex = "ux_clients_organization_email";

    private async Task EnsureHomeCompanyValid(Guid organizationId, Guid? homeCompanyId, Guid? previousHomeCompanyId)
    {
        if (!homeCompanyId.HasValue)
            return;

        Company company = await _companyHandler.GetById(organizationId, homeCompanyId.Value);
        if (company == null)
            throw new NotFoundAppException("Company", homeCompanyId.Value);

        bool isNewAssignment = homeCompanyId != previousHomeCompanyId;
        if (isNewAssignment && !company.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Tvrtka '{company.Name}' nije aktivna — ne može se postaviti kao matična.");
    }

    private async Task EnsureHomeTrainerValid(Guid organizationId, Guid? homeTrainerId, Guid? previousHomeTrainerId)
    {
        if (!homeTrainerId.HasValue)
            return;

        Employee employee = await _employeeHandler.GetByIdLight(organizationId, homeTrainerId.Value);
        if (employee == null)
            throw new NotFoundAppException("Employee", homeTrainerId.Value);

        bool isNewAssignment = homeTrainerId != previousHomeTrainerId;
        if (isNewAssignment && !employee.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveEmployee, $"Zaposlenik '{employee.FirstName} {employee.LastName}' nije aktivan — ne može se postaviti kao matični trener.");
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
    /// <summary>T1-7: čisti kalendarski dani (DateOnly), oba kraja uključena — bez offseta, pa nema pomaka dana.</summary>
    private static (bool IsInRange, DateOnly Occurrence) FindOccurrenceInRange(DateOnly dateOfBirth, DateOnly from, DateOnly to)
    {
        foreach (int year in new[] { from.Year, from.Year + 1 })
        {
            int day = dateOfBirth.Day;
            int month = dateOfBirth.Month;
            if (month == 2 && day == 29 && !DateTime.IsLeapYear(year))
                day = 28;

            DateOnly occurrence = new DateOnly(year, month, day);
            if (occurrence >= from && occurrence <= to)
                return (true, occurrence);
        }

        return (false, default);
    }

    private static ClientDto ToDto(Client client, int noShowCount)
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
            HomeCompanyId = client.HomeCompanyId,
            HomeCompanyName = client.HomeCompany?.Name,
            HomeTrainerId = client.HomeTrainerId,
            HomeTrainerName = client.HomeTrainer != null ? $"{client.HomeTrainer.FirstName} {client.HomeTrainer.LastName}" : null,
            IsActive = client.IsActive,
            IsAnonymized = client.IsAnonymized,
            AnonymizedAt = client.AnonymizedAt,
            AnonymizedBy = client.AnonymizedBy,
            Tags = client.Tags.Select(t => new ClientTagRefDto
            {
                TagId = t.TagId,
                Name = t.Tag?.Name,
                ColorHex = t.Tag?.ColorHex
            }).ToList(),
            NoShowCount = noShowCount,
            CreatedAt = client.CreatedAt,
            CreatedBy = client.CreatedBy,
            UpdatedAt = client.UpdatedAt,
            UpdatedBy = client.UpdatedBy
        };
    }
}
