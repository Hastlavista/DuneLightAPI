using System;

namespace BlueDragon.DuneLight.Core.DTOs.Management;

/// <summary>Redak globalnog User direktorija — NAMJERNO bez PasswordHash/ApiKey/PinHash (vidi zahtjev "Do not
/// expose authentication secrets/internals"). EmployeeName je opcionalan (null ako User nema Employee profil —
/// User i Employee su odvojeni koncepti, vidi zahtjev "Do not require Employee existence").</summary>
public class ManagementUserListItemDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; }
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? EmployeeName { get; set; }
}
