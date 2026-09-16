using System;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Commissions;

public class CommissionRuleDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public CommissionSubjectType SubjectType { get; set; }
    public Guid? ServiceId { get; set; }
    public string ServiceName { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; }
    public Guid? PackageId { get; set; }
    public string PackageName { get; set; }
    public CommissionCalculationType CalculationType { get; set; }
    public decimal Value { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class CommissionRuleCreateRequest
{
    public Guid EmployeeId { get; set; }
    public CommissionSubjectType SubjectType { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? PackageId { get; set; }
    public CommissionCalculationType CalculationType { get; set; }
    public decimal Value { get; set; }
}

/// <summary>Mijenja samo CalculationType/Value — EmployeeId/SubjectType/predmet se ne mijenjaju (deaktivirajte
/// i stvorite novi redak za promjenu vlasništva pravila, vidi CommissionRuleService).</summary>
public class CommissionRuleUpdateRequest
{
    public CommissionCalculationType CalculationType { get; set; }
    public decimal Value { get; set; }
}

public class CommissionRuleQuery
{
    public Guid? EmployeeId { get; set; }
    public CommissionSubjectType? SubjectType { get; set; }
    public bool? IsActive { get; set; }
}
