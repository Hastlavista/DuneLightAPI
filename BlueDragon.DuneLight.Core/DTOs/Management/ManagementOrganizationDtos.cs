using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.DTOs.Management;

/// <summary>Redak globalnog Organization direktorija — namjerno tanka projekcija (id/name/slug/created +
/// brojevi), ne puni Organization agregat, vidi zahtjev "Do not return complete Organization aggregates".</summary>
public class ManagementOrganizationListItemDto
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int CompanyCount { get; set; }
    public int UserCount { get; set; }
}

/// <summary>Management-specifična detalj projekcija — NAMJERNO bez password_hash/api_key/pin_hash (vidi
/// ManagementUserListItemDto istu napomenu) i bez subscription/plan/billing koncepata (zahtjev: ne izmišljati
/// placeholder poddomene prije nego što su poslovna pravila poznata).</summary>
public class ManagementOrganizationDetailDto
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string PrimaryColor { get; set; }
    public string SecondaryColor { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int UserCount { get; set; }
    public List<ManagementOrganizationCompanyDto> Companies { get; set; } = new();
}

public class ManagementOrganizationCompanyDto
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
}
