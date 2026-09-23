using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Services;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Covers the GET /api/employees/me contract (EmployeeService.GetMe, via its extracted pure mapping
/// helpers - see their own doc) after the P0 onboarding-deadlock fix: a User without an Employee profile yet
/// (always the organization's founder right after Register, see AuthService.Register's Admin starter GrantGroup
/// assignment) must get a 200 with their real grants, not a 404 that leaves those grants unreadable. Authorization
/// belongs to the User (UserGrantGroup), never to the Employee - Employee is an optional business/workforce
/// profile. No Owner/founder bypass: a User with no Employee and no/minimal grants gets exactly those grants,
/// nothing more (see the negative-case test below).</summary>
public class EmployeeMeContractTests
{
    private static readonly HashSet<string> AdminStarterGrants = new()
    {
        "permissions.manage", "catalog.companies.manage", "catalog.companies.view",
        "employees.engagement-types.manage", "employees.engagement-types.view",
    };

    [Fact]
    public void Freshly_registered_founder_with_no_Employee_row_gets_HasProfile_false_and_real_grants()
    {
        User user = new() { Id = Guid.NewGuid(), Role = UserRole.Admin, PinHash = null };

        EmployeeMeDto dto = EmployeeService.ToNoProfileMeDto(user, AdminStarterGrants);

        Assert.False(dto.HasProfile);
        Assert.Null(dto.EmployeeId);
        Assert.Null(dto.FirstName);
        Assert.Null(dto.LastName);
        Assert.Empty(dto.Companies);
        // The whole point of the fix: grants are NOT withheld just because there's no Employee yet.
        Assert.Contains("permissions.manage", dto.Grants);
        Assert.Contains("catalog.companies.manage", dto.Grants);
        Assert.Contains("employees.engagement-types.manage", dto.Grants);
    }

    [Fact]
    public void No_Employee_row_still_reports_HasPinSet_from_the_User_since_PinHash_lives_on_User_not_Employee()
    {
        User withPin = new() { Id = Guid.NewGuid(), Role = UserRole.Admin, PinHash = "hashed" };
        User withoutPin = new() { Id = Guid.NewGuid(), Role = UserRole.Admin, PinHash = null };

        Assert.True(EmployeeService.ToNoProfileMeDto(withPin, AdminStarterGrants).HasPinSet);
        Assert.False(EmployeeService.ToNoProfileMeDto(withoutPin, AdminStarterGrants).HasPinSet);
    }

    [Fact]
    public void An_active_User_without_Employee_and_without_the_relevant_grant_still_gets_only_their_real_grants()
    {
        // Not an onboarding bypass: a User with no Employee and minimal grants gets exactly those grants -
        // the backend's own [RequireGrant] on catalog/permissions endpoints still rejects them normally,
        // this dto layer never invents extra access.
        User user = new() { Id = Guid.NewGuid(), Role = UserRole.Member, PinHash = null };
        HashSet<string> minimalGrants = new() { "roster.entries.view" };

        EmployeeMeDto dto = EmployeeService.ToNoProfileMeDto(user, minimalGrants);

        Assert.False(dto.HasProfile);
        Assert.DoesNotContain("catalog.companies.manage", dto.Grants);
        Assert.DoesNotContain("permissions.manage", dto.Grants);
        Assert.Single(dto.Grants);
    }

    [Fact]
    public void Missing_Employee_alone_grants_nothing_the_grants_set_passed_in_is_the_sole_source_of_truth()
    {
        EmployeeMeDto dto = EmployeeService.ToNoProfileMeDto(null, new HashSet<string>());

        Assert.Empty(dto.Grants);
        Assert.False(dto.HasProfile);
    }

    [Fact]
    public void GrantGroup_name_is_irrelevant_only_the_resolved_grant_keys_matter()
    {
        // ToNoProfileMeDto never sees GrantGroup names at all - only the already-resolved raw grant
        // keys (GrantGroupHandler.ResolveEffective's output). A GrantGroup named e.g. "Vlasnik" or
        // "Owner" carries no special meaning; only its actual grants would show up here.
        User user = new() { Id = Guid.NewGuid(), Role = UserRole.Member, PinHash = null };
        HashSet<string> grants = new() { "catalog.companies.manage" };

        EmployeeMeDto dto = EmployeeService.ToNoProfileMeDto(user, grants);

        Assert.Contains("catalog.companies.manage", dto.Grants);
        Assert.Single(dto.Grants);
    }

    [Fact]
    public void With_an_Employee_profile_normal_existing_behavior_is_preserved()
    {
        User user = new() { Id = Guid.NewGuid(), Role = UserRole.Member, PinHash = "hashed" };
        Employee employee = new()
        {
            Id = Guid.NewGuid(),
            FirstName = "Ana",
            LastName = "Test",
            ColorHex = "#123456",
            User = user,
            Companies = new List<EmployeeCompany>
            {
                new() { CompanyId = Guid.NewGuid(), IsPrimary = true, Company = new BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Company { Name = "Centar" } },
            },
        };

        EmployeeMeDto dto = EmployeeService.ToProfileMeDto(employee, new HashSet<string> { "roster.entries.view" });

        Assert.True(dto.HasProfile);
        Assert.Equal(employee.Id, dto.EmployeeId);
        Assert.Equal("Ana", dto.FirstName);
        Assert.Equal("Test", dto.LastName);
        Assert.True(dto.HasPinSet);
        Assert.Single(dto.Companies);
        Assert.Equal("Centar", dto.Companies[0].CompanyName);
    }
}
