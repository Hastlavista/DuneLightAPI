#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Shared.Exceptions;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>Assertions for the error contract of the scheduling services (the machine-readable code is the contract
/// the API middleware turns into the HTTP error body, so the code — not the localized message — is what is pinned).</summary>
public static class SchedulingAssert
{
    public static async Task<BusinessRuleException> BusinessRule(string expectedCode, Func<Task> action)
    {
        BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(action);
        Assert.Equal(expectedCode, ex.Code);
        return ex;
    }

    public static async Task<ValidationAppException> Validation(Func<Task> action) =>
        await Assert.ThrowsAsync<ValidationAppException>(action);

    public static async Task<NotFoundAppException> NotFound(Func<Task> action) =>
        await Assert.ThrowsAsync<NotFoundAppException>(action);

    /// <summary>The per-date reasons carried by a RECURRING_CONFLICT error (its Details is an anonymous { conflicts } object).</summary>
    public static List<string> ConflictReasons(BusinessRuleException ex)
    {
        object conflicts = ex.Details?.GetType().GetProperty("conflicts")?.GetValue(ex.Details);
        Assert.NotNull(conflicts);
        return ((IEnumerable<RecurringConflictDetail>)conflicts).Select(c => c.Reason).ToList();
    }

    public static void HasWarning(AppointmentDto dto, string code) =>
        Assert.Contains(dto.Warnings, w => w.Code == code);

    public static void HasNoWarnings(AppointmentDto dto) => Assert.Empty(dto.Warnings);
}
