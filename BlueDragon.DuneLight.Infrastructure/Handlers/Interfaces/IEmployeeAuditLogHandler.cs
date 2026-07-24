using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IEmployeeAuditLogHandler
{
    Task Add(EmployeeAuditLog entry);
    Task<bool> HasEntries(Guid employeeId);
}
