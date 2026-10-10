#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-10 (odluka "Tri odluke T1 (1)", ADR-0035 "Uključiv kraj") — svaki "vrijedi do" dan uključuje taj dan. Fond godišnjeg se
/// smije trošiti i na dan <c>ExpiresAt</c>; istekao je tek dan nakon.
/// </summary>
public class T1InclusiveEndTests
{
    private static readonly DateOnly ExpiresAt = new(2027, 6, 30);

    [Fact]
    public void LeaveFund_IsValidOnItsExpiryDay_AndExpiredTheDayAfter()
    {
        LeaveFund fund = new() { ExpiresAt = ExpiresAt };

        // CHANGED in T1 (T1-10): prije je fond na dan ExpiresAt već bio istekao (ExpiresAt <= danas).
        Assert.False(LeaveFundYearCalculator.IsExpired(fund, ExpiresAt));
        Assert.False(LeaveFundYearCalculator.IsExpired(fund, ExpiresAt.AddDays(-1)));
        Assert.True(LeaveFundYearCalculator.IsExpired(fund, ExpiresAt.AddDays(1)));
    }

    [Fact]
    public async Task LeaveFund_IsEligibleForDeductionOnItsExpiryDay_NotTheDayAfter()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LeaveFund_IsEligibleForDeductionOnItsExpiryDay_NotTheDayAfter));
        await using (DatabaseContext db = w.NewDb())
        {
            db.LeaveFunds.Add(new LeaveFund
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, EmployeeId = w.Employee.Id.Value, FundYear = 2026,
                OpenedAt = new DateOnly(2026, 1, 1), ExpiresAt = ExpiresAt, AllocatedDays = 20, UsedDays = 0, CreatedAt = TestClock.UtcNow
            });
            await db.SaveChangesAsync();
        }

        ILeaveFundHandler handler = w.Resolve<ILeaveFundHandler>();
        await using IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin();
        // CHANGED in T1 (T1-10): prije je GetEligible tražio ExpiresAt > asOf, pa fond na dan isteka nije bio prihvatljiv.
        List<LeaveFund> onExpiryDay = await handler.GetEligible(uow, w.OrganizationId, w.Employee.Id.Value, ExpiresAt);
        List<LeaveFund> dayAfter = await handler.GetEligible(uow, w.OrganizationId, w.Employee.Id.Value, ExpiresAt.AddDays(1));
        Assert.Single(onExpiryDay);
        Assert.Empty(dayAfter);
    }
}
