using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ClientHandler : IClientHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ClientHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<Client> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? tagId, Guid? homeTrainerId, Guid? homeCompanyId,
        Guid? mineFirstEmployeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<Client> query = context.Clients
            .Include(c => c.HomeCompany)
            .Include(c => c.HomeTrainer)
            .Include(c => c.Tags).ThenInclude(t => t.Tag)
            .Where(c => c.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(c =>
                EF.Functions.ILike(c.FirstName, $"%{request.Search}%") ||
                EF.Functions.ILike(c.LastName, $"%{request.Search}%") ||
                EF.Functions.ILike(c.FirstName + " " + c.LastName, $"%{request.Search}%") ||
                (c.Phone != null && EF.Functions.ILike(c.Phone, $"%{request.Search}%")) ||
                (c.Email != null && EF.Functions.ILike(c.Email, $"%{request.Search}%")));

        if (request.IsActive.HasValue)
            query = query.Where(c => c.IsActive == request.IsActive.Value);

        if (tagId.HasValue)
            query = query.Where(c => c.Tags.Any(t => t.TagId == tagId.Value));

        if (homeTrainerId.HasValue)
            query = query.Where(c => c.HomeTrainerId == homeTrainerId.Value);

        if (homeCompanyId.HasValue)
            query = query.Where(c => c.HomeCompanyId == homeCompanyId.Value);

        int totalCount = await query.CountAsync();

        IOrderedQueryable<Client> ordered = mineFirstEmployeeId.HasValue
            ? query.OrderBy(c => c.HomeTrainerId == mineFirstEmployeeId.Value ? 0 : 1).ThenBy(c => c.LastName).ThenBy(c => c.FirstName)
            : query.OrderBy(c => c.LastName).ThenBy(c => c.FirstName);

        List<Client> items = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Client> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Clients
            .Include(c => c.HomeCompany)
            .Include(c => c.HomeTrainer)
            .Include(c => c.Tags).ThenInclude(t => t.Tag)
            .SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id);
    }

    public async Task<Client> GetByIdLight(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Clients.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id);
    }

    public async Task<List<Client>> GetByIds(Guid organizationId, List<Guid> ids)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Clients
            .Where(c => c.OrganizationId == organizationId && c.Id.HasValue && ids.Contains(c.Id.Value))
            .ToListAsync();
    }

    public async Task Add(Client client)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Clients.Add(client);
        await context.SaveChangesAsync();
    }

    public async Task Update(Client client, List<ClientTagAssignment> newTags)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        Client trackedClient = await context.Clients.SingleAsync(c => c.Id == client.Id && c.OrganizationId == client.OrganizationId);
        trackedClient.MemberNumber = client.MemberNumber;
        trackedClient.FirstName = client.FirstName;
        trackedClient.LastName = client.LastName;
        trackedClient.DateOfBirth = client.DateOfBirth;
        trackedClient.Occupation = client.Occupation;
        trackedClient.Phone = client.Phone;
        trackedClient.Email = client.Email;
        trackedClient.Note = client.Note;
        trackedClient.HealthNote = client.HealthNote;
        trackedClient.GdprConsentGiven = client.GdprConsentGiven;
        trackedClient.GdprConsentDate = client.GdprConsentDate;
        trackedClient.HomeCompanyId = client.HomeCompanyId;
        trackedClient.HomeTrainerId = client.HomeTrainerId;
        trackedClient.IsActive = client.IsActive;
        trackedClient.IsAnonymized = client.IsAnonymized;
        trackedClient.AnonymizedAt = client.AnonymizedAt;
        trackedClient.UpdatedAt = client.UpdatedAt;
        trackedClient.UpdatedBy = client.UpdatedBy;

        // Reconcile in place rather than delete-all/insert-all: recreating a row for a tag
        // that didn't change would delete and re-insert the same (client_id, tag_id) pair in
        // one batch, which can violate ux_client_tag_assignments_client_tag depending on
        // statement ordering within the batch.
        List<ClientTagAssignment> existingTags = await context.ClientTagAssignments
            .Where(t => t.ClientId == client.Id)
            .ToListAsync();
        HashSet<Guid> existingTagIds = existingTags.Select(t => t.TagId).ToHashSet();
        HashSet<Guid> newTagIds = newTags.Select(t => t.TagId).ToHashSet();

        foreach (ClientTagAssignment existing in existingTags)
            if (!newTagIds.Contains(existing.TagId))
                context.ClientTagAssignments.Remove(existing);

        foreach (ClientTagAssignment tag in newTags)
            if (!existingTagIds.Contains(tag.TagId))
            {
                tag.ClientId = client.Id.GetValueOrDefault();
                context.ClientTagAssignments.Add(tag);
            }

        await context.SaveChangesAsync();
    }

    public async Task SetActiveAndStamp(Guid organizationId, Guid clientId, bool isActive, DateTimeOffset updatedAt, Guid? updatedBy)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        Client client = await context.Clients.SingleOrDefaultAsync(c => c.Id == clientId && c.OrganizationId == organizationId);
        if (client == null)
            throw new ArgumentException($"Client with id {clientId} does not exist");

        client.IsActive = isActive;
        client.UpdatedAt = updatedAt;
        client.UpdatedBy = updatedBy;

        await context.SaveChangesAsync();
    }

    public async Task Anonymize(Guid organizationId, Guid clientId, DateTimeOffset anonymizedAt, Guid? anonymizedBy)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        Client client = await context.Clients.SingleOrDefaultAsync(c => c.Id == clientId && c.OrganizationId == organizationId);
        if (client == null)
            throw new ArgumentException($"Client with id {clientId} does not exist");

        List<ClientTagAssignment> tags = await context.ClientTagAssignments
            .Where(t => t.ClientId == clientId)
            .ToListAsync();
        context.ClientTagAssignments.RemoveRange(tags);

        client.FirstName = "Anonimizirani";
        client.LastName = "klijent";
        client.DateOfBirth = null;
        client.Occupation = null;
        client.Phone = null;
        client.Email = null;
        client.Note = null;
        client.HealthNote = null;
        client.GdprConsentGiven = false;
        client.GdprConsentDate = null;
        client.HomeCompanyId = null;
        client.HomeTrainerId = null;
        client.IsActive = false;
        client.IsAnonymized = true;
        client.AnonymizedAt = anonymizedAt;
        client.UpdatedAt = anonymizedAt;
        client.UpdatedBy = anonymizedBy;

        await context.SaveChangesAsync();
    }

    public async Task Delete(Client client)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Clients.Remove(client);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsMemberNumberTaken(Guid organizationId, int memberNumber, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Clients.AnyAsync(c =>
            c.OrganizationId == organizationId &&
            c.MemberNumber == memberNumber &&
            (excludeId == null || c.Id != excludeId));
    }

    public async Task<int> GetNextMemberNumber(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        bool anyClients = await context.Clients.AnyAsync(c => c.OrganizationId == organizationId);
        if (!anyClients)
            return 1;

        int max = await context.Clients
            .Where(c => c.OrganizationId == organizationId)
            .MaxAsync(c => c.MemberNumber);

        return max + 1;
    }

    public async Task<List<Client>> GetBirthdayCandidates(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Clients
            .Where(c => c.OrganizationId == organizationId && c.IsActive && c.DateOfBirth != null)
            .ToListAsync();
    }
}
