#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-9 — ručni upis paketa (budućnost odbijena, unatrag uz clients.packages.write.past, istekao pri upisu odbijen, bez provizije,
/// audit upisa unatrag), pokriće paketa tek od PurchaseDate, bez retroaktivnog pokrića, i GDPR suglasnost (ne u budućnosti,
/// svaka promjena u povijesti klijenta).
/// </summary>
public class T1PackagesGdprTests
{
    // Dan prodaje u testovima: 2031-03-05 u podne UTC (organizacija i poslovnica u UTC-u).
    private static readonly DateOnly SaleDay = new(2031, 3, 5);
    private static readonly DateTimeOffset SaleNoon = new(2031, 3, 5, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Package> CatalogPackage(SchedulingWorld w, PackageValidityType type = PackageValidityType.DayCount, int? days = 30,
        DateOnly? fixedDate = null)
    {
        await using DatabaseContext db = w.NewDb();
        Package package = new()
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, Name = $"T19-{Guid.NewGuid():N}",
            EntryMode = PackageEntryMode.PerService, ValidityType = type, ValidityDays = type == PackageValidityType.DayCount ? days : null,
            ValidityFixedDate = fixedDate, DefaultPrice = 100m, IsActive = true, CreatedAt = TestClock.UtcNow
        };
        package.Services.Add(new PackageServiceItem { Id = Guid.NewGuid(), PackageId = package.Id.Value, ServiceId = w.Service.Id.Value, EntryCount = 5 });
        db.Packages.Add(package);
        await db.SaveChangesAsync();
        return package;
    }

    private static Task<ClientPackageDto> Issue(SchedulingWorld w, Package package, DateOnly? purchaseDate, Guid? userId = null) =>
        w.ClientPackages.Create(w.OrganizationId, userId ?? w.ActorUserId, w.Client.Id.Value, new ClientPackageCreateRequest
        {
            PackageId = package.Id.Value, PurchaseDate = purchaseDate, PaidPrice = 100m, CompanyId = w.Company.Id
        });

    private static Task<List<ClientAuditLog>> ClientAudit(SchedulingWorld w, Guid clientId) =>
        w.Resolve<IClientAuditLogHandler>().GetByClient(w.OrganizationId, clientId);

    #region A — ručni upis paketa

    [Fact]
    public async Task ManualIssue_PurchaseDateAfterToday_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualIssue_PurchaseDateAfterToday_IsRejected));
        Package package = await CatalogPackage(w);
        await w.GrantUser(w.ActorUserId, Grants.ClientsPackagesWritePast); // ni grant za prošlost ne otvara budućnost

        using (w.ClockAt(SaleNoon))
        {
            ValidationAppException ex = await Assert.ThrowsAsync<ValidationAppException>(() => Issue(w, package, SaleDay.AddDays(1)));
            Assert.Equal(ErrorCodes.PackagePurchaseDateInFuture, ex.Code);
            Assert.Contains("06.03.2031.", ex.Message);
            Assert.Contains("05.03.2031.", ex.Message);
        }

        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.ClientPackages.AnyAsync(cp => cp.OrganizationId == w.OrganizationId));
    }

    [Fact]
    public async Task ManualIssue_Today_NeedsNoPastGrant_AndWritesNoAudit()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualIssue_Today_NeedsNoPastGrant_AndWritesNoAudit));
        Package package = await CatalogPackage(w);

        using (w.ClockAt(SaleNoon))
        {
            Assert.Equal(SaleDay, (await Issue(w, package, SaleDay)).PurchaseDate);
            Assert.Equal(SaleDay, (await Issue(w, package, null)).PurchaseDate);
        }

        Assert.Empty(await ClientAudit(w, w.Client.Id.Value));
    }

    [Fact]
    public async Task ManualIssue_BeforeToday_NeedsWritePastGrant_WithoutLimit_AndIsAudited()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualIssue_BeforeToday_NeedsWritePastGrant_WithoutLimit_AndIsAudited));
        Package fixedPackage = await CatalogPackage(w, PackageValidityType.FixedDate, fixedDate: new DateOnly(2035, 1, 1));

        using (w.ClockAt(SaleNoon))
        {
            ForbiddenAppException refused = await Assert.ThrowsAsync<ForbiddenAppException>(() => Issue(w, fixedPackage, SaleDay.AddDays(-1)));
            Assert.Contains(Grants.ClientsPackagesWritePast, refused.Message);
            Assert.Equal((ForbiddenReason.MissingGrant, Grants.ClientsPackagesWritePast), (refused.Details.Reason, Assert.Single(refused.Details.RequiredGrants)));
        }

        await w.GrantUser(w.ActorUserId, Grants.ClientsPackagesWritePast);
        ClientPackageDto issued;
        using (w.ClockAt(SaleNoon))
        {
            issued = await Issue(w, fixedPackage, SaleDay.AddDays(-1));
            // Bez granice unatrag.
            Assert.Equal(new DateOnly(2024, 1, 1), (await Issue(w, fixedPackage, new DateOnly(2024, 1, 1))).PurchaseDate);
        }

        Assert.Equal(SaleDay.AddDays(-1), issued.PurchaseDate);
        List<ClientAuditLog> audit = await ClientAudit(w, w.Client.Id.Value);
        Assert.Equal(2, audit.Count);
        ClientAuditLog first = audit.Single(a => a.NewValue.Contains(issued.Id.ToString()));
        Assert.Equal(ClientAuditChangeTypes.PackageIssuedBackdated, first.ChangeType);
        Assert.Equal($"clientPackageId={issued.Id};purchaseDate=2031-03-04", first.NewValue);
        Assert.Equal(w.ActorUserId, first.ChangedBy);
        Assert.Equal(w.OrganizationId, first.OrganizationId);
    }

    [Fact]
    public async Task ManualIssue_AlreadyExpiredAtIssue_IsRejectedWithDates()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualIssue_AlreadyExpiredAtIssue_IsRejectedWithDates));
        await w.GrantUser(w.ActorUserId, Grants.ClientsPackagesWritePast);
        Package tenDays = await CatalogPackage(w, days: 10);
        Package endOfMonth = await CatalogPackage(w, PackageValidityType.EndOfMonth);

        using (w.ClockAt(SaleNoon))
        {
            // 2031-02-20 + 10 dana = 2031-03-02 < 2031-03-05.
            BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Issue(w, tenDays, new DateOnly(2031, 2, 20)));
            Assert.Equal(ErrorCodes.PackageExpiredAtIssue, ex.Code);
            Assert.Contains("02.03.2031.", ex.Message);
            // Kraj veljače je prošao.
            Assert.Equal(ErrorCodes.PackageExpiredAtIssue,
                (await Assert.ThrowsAsync<BusinessRuleException>(() => Issue(w, endOfMonth, new DateOnly(2031, 2, 27)))).Code);

            // Granica: istječe danas → još vrijedi i upisuje se.
            Assert.Equal(SaleDay, (await Issue(w, tenDays, new DateOnly(2031, 2, 23))).ValidUntilDate);
        }

        // Nijedan odbijeni upis nije ostavio paket ni audit.
        await using DatabaseContext db = w.NewDb();
        Assert.Equal(1, await db.ClientPackages.CountAsync(cp => cp.OrganizationId == w.OrganizationId));
        Assert.Single(await ClientAudit(w, w.Client.Id.Value));
    }

    [Fact]
    public async Task ManualIssue_Today_OfFixedDatePackageAlreadyPast_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualIssue_Today_OfFixedDatePackageAlreadyPast_IsRejected));
        Package package = await CatalogPackage(w, PackageValidityType.FixedDate, fixedDate: new DateOnly(2031, 3, 1));

        using (w.ClockAt(SaleNoon))
        {
            BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Issue(w, package, null));
            Assert.Equal(ErrorCodes.PackageExpiredAtIssue, ex.Code);
        }
    }

    [Fact]
    public async Task ManualIssue_WithoutCheckout_CreatesNoSaleCommission()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualIssue_WithoutCheckout_CreatesNoSaleCommission));
        Package package = await CatalogPackage(w);
        // Pravilo provizije na prodaju upravo tog paketa postoji (kao u checkoutu) i akter radi kao zaposlenik.
        await w.Resolve<ICommissionRuleService>().Create(w.OrganizationId, w.ActorUserId, new CommissionRuleCreateRequest
        {
            EmployeeId = w.Employee.Id.Value, SubjectType = CommissionSubjectType.Package, PackageId = package.Id,
            CalculationType = CommissionCalculationType.Fixed, Value = 8m
        });
        await using (DatabaseContext db = w.NewDb())
        {
            (await db.Employees.SingleAsync(e => e.Id == w.Employee.Id)).UserId = w.ActorUserId;
            await db.SaveChangesAsync();
        }
        await w.GrantUser(w.ActorUserId, Grants.ClientsPackagesWritePast);

        await Issue(w, package, null);
        using (w.ClockAt(SaleNoon))
            await Issue(w, package, SaleDay.AddDays(-3));

        Assert.Empty(await w.LoadCommissionEntries());
    }

    #endregion

    #region B/C — pokriće tek od PurchaseDate, bez retroaktivnog pokrića

    [Fact]
    public void Validity_IsFromPurchaseDateToValidUntilDate_BothInclusive()
    {
        ClientPackage package = new() { PurchaseDate = new DateOnly(2031, 3, 4), ValidUntilDate = new DateOnly(2031, 3, 10) };

        Assert.False(PackageValidity.IsValidOn(package, new DateOnly(2031, 3, 3)));
        Assert.True(PackageValidity.IsValidOn(package, new DateOnly(2031, 3, 4)));
        Assert.True(PackageValidity.IsValidOn(package, new DateOnly(2031, 3, 10)));
        Assert.False(PackageValidity.IsValidOn(package, new DateOnly(2031, 3, 11)));
    }

    [Fact]
    public async Task BackdatedPackage_DoesNotCoverServicesBeforePurchaseDate_CoversFromPurchaseDateOn()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BackdatedPackage_DoesNotCoverServicesBeforePurchaseDate_CoversFromPurchaseDateOn));
        await w.GrantUser(w.ActorUserId, Grants.ClientsPackagesWritePast);
        Package catalog = await CatalogPackage(w);
        // Termini 3. i 4. ožujka 2031. (FutureDay = 3. ožujka).
        var before = await w.CreateAppointment(SchedulingWorld.Future(10));
        var onPurchaseDay = await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(1));

        ClientPackageDto package;
        using (w.ClockAt(SaleNoon))
            package = await Issue(w, catalog, new DateOnly(2031, 3, 4)); // upis unatrag

        // Eligibility (SQL) i potrošnja (ledger) slijede isto pravilo.
        Assert.Empty(await w.ClientPackages.GetEligibleForService(w.OrganizationId, w.Client.Id.Value, w.Service.Id.Value,
            SchedulingWorld.Future(23, 59), w.Company.Id.Value));
        Assert.Single(await w.ClientPackages.GetEligibleForService(w.OrganizationId, w.Client.Id.Value, w.Service.Id.Value,
            SchedulingWorld.Future(0).AddDays(1), w.Company.Id.Value));

        BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            w.SetBookingStatus(before.Id, w.Client, BookingStatus.Completed, clientPackageId: package.Id));
        Assert.Equal(ErrorCodes.PackageNotEligible, ex.Code);
        Assert.Empty(await w.LoadPackageConsumptions(before.Id));

        await w.SetBookingStatus(onPurchaseDay.Id, w.Client, BookingStatus.Completed, clientPackageId: package.Id);
        PackageConsumption consumption = Assert.Single(await w.LoadPackageConsumptions(onPurchaseDay.Id));
        Assert.Equal((package.Id, new DateOnly(2031, 3, 4)), (consumption.ClientPackageId, consumption.ServiceDate));
    }

    [Fact]
    public async Task BackdatedPackage_DoesNotTouchAlreadyCompletedOrUnpaidParticipations()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BackdatedPackage_DoesNotTouchAlreadyCompletedOrUnpaidParticipations));
        await w.GrantUser(w.ActorUserId, Grants.ClientsPackagesWritePast);
        Package catalog = await CatalogPackage(w);
        // Odrađeno i neplaćeno (dug) 4. ožujka, prije upisa paketa.
        var done = await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(1));
        await w.SetBookingStatus(done.Id, w.Client, BookingStatus.Completed, isPaid: false);
        BookingSegmentParticipation beforeIssue = Assert.Single(await w.LoadParticipations(done.Id, w.Client));

        ClientPackageDto package;
        using (w.ClockAt(SaleNoon))
            package = await Issue(w, catalog, new DateOnly(2031, 3, 1)); // pokriva dan termina, ali upis ne mijenja ništa unatrag

        BookingSegmentParticipation afterIssue = Assert.Single(await w.LoadParticipations(done.Id, w.Client));
        Assert.Equal((ParticipationStatus.Completed, beforeIssue.Amount), (afterIssue.Status, afterIssue.Amount));
        Assert.Empty(await w.LoadPackageConsumptions(done.Id));
        Assert.Empty(await w.LoadPayments(Assert.Single(await w.LoadParticipations(done.Id, w.Client)).BookingId));
        Assert.Equal(5, (await w.ClientPackages.GetById(w.OrganizationId, w.Client.Id.Value, package.Id)).ServiceEntries.Single().RemainingEntries);
    }

    #endregion

    #region D/E — GDPR suglasnost

    private static ClientCreateRequest NewClient(bool given, DateOnly? date) => new()
    {
        FirstName = "Gdpr", LastName = $"T19-{Guid.NewGuid():N}", GdprConsentGiven = given, GdprConsentDate = date
    };

    private static ClientUpdateRequest UpdateOf(ClientDto c, bool given, DateOnly? date) => new()
    {
        FirstName = c.FirstName, LastName = c.LastName, GdprConsentGiven = given, GdprConsentDate = date
    };

    [Fact]
    public async Task GdprConsentDate_InTheFuture_IsRejected_OnCreateAndUpdate_TodayIsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GdprConsentDate_InTheFuture_IsRejected_OnCreateAndUpdate_TodayIsAllowed), "Pacific/Auckland");
        IClientService clients = w.Resolve<IClientService>();
        // 5. ožujka 12:00 UTC = 6. ožujka 01:00 u Aucklandu (zona organizacije) → danas je 6. ožujka.
        using (w.ClockAt(SaleNoon))
        {
            ValidationAppException ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
                clients.Create(w.OrganizationId, w.ActorUserId, NewClient(true, new DateOnly(2031, 3, 7))));
            Assert.Equal(ErrorCodes.GdprConsentDateInFuture, ex.Code);

            ClientDto created = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(true, new DateOnly(2031, 3, 6)));
            Assert.Equal(new DateOnly(2031, 3, 6), created.GdprConsentDate);

            Assert.Equal(ErrorCodes.GdprConsentDateInFuture, (await Assert.ThrowsAsync<ValidationAppException>(() =>
                clients.Update(w.OrganizationId, w.ActorUserId, created.Id, UpdateOf(created, true, new DateOnly(2031, 3, 7))))).Code);

            // Postojeće pravilo ostaje: datum je obavezan kad je suglasnost dana.
            Assert.Null((await Assert.ThrowsAsync<ValidationAppException>(() =>
                clients.Create(w.OrganizationId, w.ActorUserId, NewClient(true, null)))).Code);
        }
    }

    [Fact]
    public async Task GdprConsent_EveryChangeOfFlagOrDate_IsInTheClientAudit()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GdprConsent_EveryChangeOfFlagOrDate_IsInTheClientAudit));
        IClientService clients = w.Resolve<IClientService>();
        Guid otherUser = await w.AddMemberUser();

        ClientDto withoutConsent = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(false, null));
        Assert.Empty(await ClientAudit(w, withoutConsent.Id)); // bez suglasnosti nema promjene

        ClientDto created = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(true, new DateOnly(2026, 1, 10)));
        ClientDto updated = await clients.Update(w.OrganizationId, otherUser, created.Id, UpdateOf(created, true, new DateOnly(2026, 1, 12)));
        await clients.Update(w.OrganizationId, w.ActorUserId, created.Id, UpdateOf(updated, true, new DateOnly(2026, 1, 12))); // bez promjene
        await clients.Update(w.OrganizationId, w.ActorUserId, created.Id, UpdateOf(updated, false, new DateOnly(2026, 1, 12))); // povlačenje
        await clients.Anonymize(w.OrganizationId, w.ActorUserId, withoutConsent.Id); // suglasnosti nije bilo → nema zapisa

        List<ClientAuditLog> audit = await ClientAudit(w, created.Id);
        Assert.All(audit, a => Assert.Equal(ClientAuditChangeTypes.GdprConsent, a.ChangeType));
        Assert.Equal(new[]
        {
            ("given=false;date=", "given=true;date=2026-01-10", w.ActorUserId),
            ("given=true;date=2026-01-10", "given=true;date=2026-01-12", otherUser),
            ("given=true;date=2026-01-12", "given=false;date=", w.ActorUserId)
        }, audit.Select(a => (a.OldValue, a.NewValue, a.ChangedBy.Value)).ToArray());
        Assert.Empty(await ClientAudit(w, withoutConsent.Id));
    }

    [Fact]
    public async Task GdprConsent_ClearedByAnonymization_IsAuditedWithReason()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GdprConsent_ClearedByAnonymization_IsAuditedWithReason));
        IClientService clients = w.Resolve<IClientService>();
        ClientDto created = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(true, new DateOnly(2026, 1, 10)));

        await clients.Anonymize(w.OrganizationId, w.ActorUserId, created.Id);

        ClientAuditLog last = (await ClientAudit(w, created.Id)).Last();
        Assert.Equal(("given=true;date=2026-01-10", "given=false;date=", "Anonimizacija"), (last.OldValue, last.NewValue, last.Reason));
    }

    #endregion
}
