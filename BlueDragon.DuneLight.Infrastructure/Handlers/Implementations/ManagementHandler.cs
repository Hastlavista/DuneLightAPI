using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ManagementHandler : IManagementHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ManagementHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<ManagementOverviewDto> GetOverviewCounts()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        return new ManagementOverviewDto
        {
            TotalOrganizations = await context.Organizations.CountAsync(),
            TotalUsers = await context.Users.CountAsync(),
            TotalCompanies = await context.Companies.CountAsync()
        };
    }

    public async Task<(List<ManagementOrganizationListItemDto> Items, int TotalCount)> GetOrganizationsPaged(PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<Organization> query = context.Organizations.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(o => EF.Functions.ILike(o.Name, $"%{request.Search}%") || EF.Functions.ILike(o.Slug, $"%{request.Search}%"));

        int totalCount = await query.CountAsync();

        // Correlated subquery per redak (ne N+1 na app-razini) — jedan SQL upit s dva scalar subquerya, vidi
        // klasnu napomenu zašto je cross-tenant projekcija ovdje namjerno, umjesto po-org loop-a.
        List<ManagementOrganizationListItemDto> items = await query
            .OrderBy(o => o.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(o => new ManagementOrganizationListItemDto
            {
                OrganizationId = o.Id.GetValueOrDefault(),
                Name = o.Name,
                Slug = o.Slug,
                CreatedAt = o.CreatedAt,
                CompanyCount = context.Companies.Count(c => c.OrganizationId == o.Id),
                UserCount = context.Users.Count(u => u.OrganizationId == o.Id)
            })
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<ManagementOrganizationDetailDto> GetOrganizationDetail(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        Organization organization = await context.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId);
        if (organization == null)
            return null;

        int userCount = await context.Users.CountAsync(u => u.OrganizationId == organizationId);

        List<ManagementOrganizationCompanyDto> companies = await context.Companies
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ManagementOrganizationCompanyDto
            {
                CompanyId = c.Id.GetValueOrDefault(),
                Name = c.Name,
                IsActive = c.IsActive
            })
            .ToListAsync();

        return new ManagementOrganizationDetailDto
        {
            OrganizationId = organization.Id.GetValueOrDefault(),
            Name = organization.Name,
            Slug = organization.Slug,
            PrimaryColor = organization.PrimaryColor,
            SecondaryColor = organization.SecondaryColor,
            CreatedAt = organization.CreatedAt,
            UserCount = userCount,
            Companies = companies
        };
    }

    public async Task<(List<ManagementUserListItemDto> Items, int TotalCount)> GetUsersPaged(PagedRequest request, Guid? organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<User> query = context.Users.AsQueryable();
        if (organizationId.HasValue)
            query = query.Where(u => u.OrganizationId == organizationId.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(u => EF.Functions.ILike(u.Email, $"%{request.Search}%"));
        if (request.IsActive.HasValue)
            query = query.Where(u => u.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        // Correlated subqueries za OrganizationName/EmployeeName (Employee je OPCIONALAN — User i Employee su
        // odvojeni koncepti, vidi ManagementUserListItemDto napomenu, FirstOrDefault vraća null bez Employee-a).
        List<ManagementUserListItemDto> items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new ManagementUserListItemDto
            {
                UserId = u.Id.GetValueOrDefault(),
                Email = u.Email,
                OrganizationId = u.OrganizationId,
                OrganizationName = context.Organizations.Where(o => o.Id == u.OrganizationId).Select(o => o.Name).FirstOrDefault(),
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                EmployeeName = context.Employees.Where(e => e.UserId == u.Id).Select(e => e.FirstName + " " + e.LastName).FirstOrDefault()
            })
            .ToListAsync();

        return (items, totalCount);
    }
}
