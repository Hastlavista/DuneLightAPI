using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>DB-backed verification for two "safe P2" Data/Lifecycle Consistency Cleanup fixes:
/// 1. CompanyHandler.IsReferenced now includes Checkout.CompanyId (previously a Company referenced ONLY via a
///    Checkout would pass IsReferenced==false and hit a raw Postgres FK error on hard-delete instead of the
///    clean REFERENCED_CANNOT_DELETE business error).
/// 2. WaitlistHandler.HasAnyForClient (consumed by ClientFutureActivityProvider) now blocks Client hard-delete
///    for a client referenced by ANY waitlist row (any status) — waitlist_entries.client_id has no ON DELETE
///    rule, same gap class as the already-fixed group_members.client_id check.
/// Same isolated-organization pattern as GrantGroupAssignedUserCountTests/GroupActiveMemberCountTests.</summary>
public class LifecycleCleanupSafeFixesTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static CompanyHandler CreateCompanyHandler() => new(new DatabaseSettings { ConnectionString = LocalConnectionString });
    private static WaitlistHandler CreateWaitlistHandler() => new(new DatabaseSettings { ConnectionString = LocalConnectionString });

    private static async Task<(Guid OrganizationId, Guid CompanyId, Guid ServiceId, Guid ClientId, Func<Task> Cleanup)> CreateIsolatedFixture(string testName)
    {
        Guid organizationId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid serviceId = Guid.NewGuid();
        Guid clientId = Guid.NewGuid();

        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);

        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = $"LifecycleSafeFixesTest-{testName}",
            Slug = $"lifecycle-safe-fixes-test-{organizationId:N}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        context.Companies.Add(new Company
        {
            Id = companyId,
            OrganizationId = organizationId,
            Name = "Test Company",
            Country = "HR",
            IsActive = true
        });
        context.Services.Add(new Service
        {
            Id = serviceId,
            OrganizationId = organizationId,
            Name = "Test Service",
            ExecutionMode = ServiceExecutionMode.Individual,
            DefaultDurationMinutes = 30,
            DefaultPrice = 0,
            IsActive = true,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.Clients.Add(new Client
        {
            Id = clientId,
            OrganizationId = organizationId,
            MemberNumber = new Random().Next(1_000_000, int.MaxValue),
            FirstName = "Test",
            LastName = "Client",
            IsActive = true,
            GdprConsentGiven = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        Func<Task> cleanup = async () =>
        {
            await using DatabaseContext cleanupContext = DatabaseContext.GenerateContext(LocalConnectionString);

            cleanupContext.WaitlistEntries.RemoveRange(cleanupContext.WaitlistEntries.Where(w => w.OrganizationId == organizationId));
            cleanupContext.Checkouts.RemoveRange(cleanupContext.Checkouts.Where(c => c.OrganizationId == organizationId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Appointments.RemoveRange(cleanupContext.Appointments.Where(a => a.OrganizationId == organizationId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Clients.RemoveRange(cleanupContext.Clients.Where(c => c.OrganizationId == organizationId));
            cleanupContext.Services.RemoveRange(cleanupContext.Services.Where(s => s.Id == serviceId));
            cleanupContext.Companies.RemoveRange(cleanupContext.Companies.Where(c => c.Id == companyId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Organizations.RemoveRange(cleanupContext.Organizations.Where(o => o.Id == organizationId));
            await cleanupContext.SaveChangesAsync();
        };

        return (organizationId, companyId, serviceId, clientId, cleanup);
    }

    [Fact]
    public async Task CompanyIsReferenced_TrueWhenOnlyReferencedByCheckout()
    {
        (Guid organizationId, Guid companyId, _, Guid clientId, Func<Task> cleanup) = await CreateIsolatedFixture(nameof(CompanyIsReferenced_TrueWhenOnlyReferencedByCheckout));
        try
        {
            bool referencedBefore = await CreateCompanyHandler().IsReferenced(organizationId, companyId);
            Assert.False(referencedBefore);

            await using (DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString))
            {
                context.Checkouts.Add(new Checkout
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    CompanyId = companyId,
                    ClientId = clientId,
                    Status = CheckoutStatus.Open,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await context.SaveChangesAsync();
            }

            bool referencedAfter = await CreateCompanyHandler().IsReferenced(organizationId, companyId);
            Assert.True(referencedAfter);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task WaitlistHandler_HasAnyForClient_TrueForAnyStatusIncludingCancelled()
    {
        (Guid organizationId, Guid companyId, Guid serviceId, Guid clientId, Func<Task> cleanup) = await CreateIsolatedFixture(nameof(WaitlistHandler_HasAnyForClient_TrueForAnyStatusIncludingCancelled));
        try
        {
            WaitlistHandler handler = CreateWaitlistHandler();
            bool hasAnyBefore = await handler.HasAnyForClient(organizationId, clientId);
            Assert.False(hasAnyBefore);

            Guid appointmentId = Guid.NewGuid();
            await using (DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString))
            {
                context.Appointments.Add(new Appointment
                {
                    Id = appointmentId,
                    OrganizationId = organizationId,
                    ServiceId = serviceId,
                    CompanyId = companyId,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-30),
                    DurationMinutes = 30,
                    Status = AppointmentStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await context.SaveChangesAsync();

                // A CANCELLED (non-active) waitlist row - the exact gap: HasActiveWaitingForClient (Waiting-only,
                // used by anonymization) would correctly say false here, but the hard-delete guard must still see
                // this row, since the FK has no ON DELETE rule regardless of status.
                context.WaitlistEntries.Add(new WaitlistEntry
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    AppointmentId = appointmentId,
                    ClientId = clientId,
                    Status = WaitlistEntryStatus.Cancelled,
                    JoinedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
                });
                await context.SaveChangesAsync();
            }

            bool hasAnyAfter = await handler.HasAnyForClient(organizationId, clientId);
            Assert.True(hasAnyAfter);

            bool hasActiveWaiting = await handler.HasActiveWaitingForClient(organizationId, clientId);
            Assert.False(hasActiveWaiting);
        }
        finally
        {
            await cleanup();
        }
    }
}
