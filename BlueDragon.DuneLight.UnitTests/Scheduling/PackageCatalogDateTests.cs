#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Controllers.Clients;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D3B3A.2 — Package.ValidityFixedDate is a CALENDAR DATE (DateOnly / PostgreSQL date): the last valid day configured
/// in the catalog, with no timezone and no time of day, never converted through UTC / Organization / Company zones. And
/// /eligible never substitutes "now" for the service-performance date — it must be supplied (with the Company).
/// </summary>
public class PackageCatalogDateTests
{
    private static readonly DateOnly FixedDate = new(2031, 6, 30);

    private static Task<PackageDto> CreateFixedDatePackage(SchedulingWorld w) =>
        w.Resolve<IPackageService>().Create(w.OrganizationId, w.ActorUserId, new PackageCreateRequest
        {
            Name = $"Fixed-{Guid.NewGuid():N}",
            EntryMode = PackageEntryMode.PerService,
            ValidityType = PackageValidityType.FixedDate,
            ValidityFixedDate = FixedDate,
            DefaultPrice = 100m,
            Services = new List<PackageServiceItemRequest> { new() { ServiceId = w.Service.Id.Value, EntryCount = 5 } }
        });

    private static async Task SetCompanyTimeZone(SchedulingWorld w, string timeZone)
    {
        await using DatabaseContext db = w.NewDb();
        Company tracked = await db.Companies.SingleAsync(c => c.Id == w.Company.Id);
        tracked.TimeZone = timeZone;
        await db.SaveChangesAsync();
    }

    private static Task<ClientPackageDto> Sell(SchedulingWorld w, PackageDto package, DateTimeOffset purchasedAt, Guid? companyId) =>
        w.ClientPackages.Create(w.OrganizationId, w.ActorUserId, w.Client.Id.Value, new ClientPackageCreateRequest
        {
            PackageId = package.Id, PurchaseDate = purchasedAt, PaidPrice = 100m, CompanyId = companyId
        });

    [Fact]
    public async Task FixedDate_PersistsAsAPostgresDate_AndRoundTripsWithoutTimeOrZone()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(FixedDate_PersistsAsAPostgresDate_AndRoundTripsWithoutTimeOrZone), "Pacific/Auckland");
        PackageDto created = await CreateFixedDatePackage(w);

        await using DatabaseContext db = w.NewDb();
        Assert.Equal("date", Assert.Single(await db.Database.SqlQueryRaw<string>(@"
            SELECT data_type AS ""Value"" FROM information_schema.columns
             WHERE table_schema = 'dunelight' AND table_name = 'packages' AND column_name = 'validity_fixed_date'").ToListAsync()));
        Assert.Equal(new DateTime(2031, 6, 30), Assert.Single(await db.Database.SqlQueryRaw<DateTime>(
            "SELECT validity_fixed_date AS \"Value\" FROM dunelight.packages WHERE id = {0}", created.Id).ToListAsync()));
        Assert.Equal(typeof(DateOnly?), typeof(Package).GetProperty(nameof(Package.ValidityFixedDate))!.PropertyType);

        // API contract: a plain "yyyy-MM-dd" both ways — no time of day, no offset.
        PackageDto read = await w.Resolve<IPackageService>().GetById(w.OrganizationId, created.Id);
        Assert.Equal(FixedDate, read.ValidityFixedDate);
        string json = JsonSerializer.Serialize(read);
        Assert.Contains("\"ValidityFixedDate\":\"2031-06-30\"", json);
        Assert.Equal(FixedDate, JsonSerializer.Deserialize<PackageCreateRequest>("{\"ValidityFixedDate\":\"2031-06-30\"}")!.ValidityFixedDate);
        Assert.All(new[] { typeof(PackageDto), typeof(PackageCreateRequest), typeof(PackageUpdateRequest) },
            t => Assert.Equal(typeof(DateOnly?), t.GetProperty("ValidityFixedDate")!.PropertyType));
    }

    [Fact]
    public async Task FixedDateSale_UsesTheConfiguredDateExactly_WhateverTheOrganizationOrCompanyZone()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(FixedDateSale_UsesTheConfiguredDateExactly_WhateverTheOrganizationOrCompanyZone));
        PackageDto package = await CreateFixedDatePackage(w);
        DateTimeOffset purchasedAt = new(2031, 1, 31, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(FixedDate, (await Sell(w, package, purchasedAt, w.Company.Id)).ValidUntilDate);

        // Changing the Organization zone and the Company zone changes neither the catalog date nor the sold date.
        await w.Resolve<IOrganizationSettingsService>().UpdateTimeZone(w.OrganizationId, w.ActorUserId,
            new OrganizationTimeZoneUpdateRequest { TimeZone = "Pacific/Auckland" });
        await SetCompanyTimeZone(w, "America/New_York");

        Assert.Equal(FixedDate, (await w.Resolve<IPackageService>().GetById(w.OrganizationId, package.Id)).ValidityFixedDate);
        Assert.Equal(FixedDate, (await Sell(w, package, purchasedAt, w.Company.Id)).ValidUntilDate);
        Assert.Equal(FixedDate, (await Sell(w, package, purchasedAt, null)).ValidUntilDate);
    }

    [Fact]
    public async Task FixedDatePackage_IsStillJudgedOnTheServiceLocalDateInTheCompanyZone()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(FixedDatePackage_IsStillJudgedOnTheServiceLocalDateInTheCompanyZone));
        await SetCompanyTimeZone(w, "Europe/Zagreb"); // +02:00 in June
        PackageDto package = await CreateFixedDatePackage(w);
        ClientPackageDto sold = await Sell(w, package, new DateTimeOffset(2031, 1, 10, 12, 0, 0, TimeSpan.Zero), w.Company.Id);

        // 30 June 21:30 UTC = 23:30 local (last valid day); 30 June 22:30 UTC = 1 July 00:30 local.
        Assert.Single(await w.ClientPackages.GetEligibleForService(w.OrganizationId, w.Client.Id.Value, w.Service.Id.Value,
            new DateTimeOffset(2031, 6, 30, 21, 30, 0, TimeSpan.Zero), w.Company.Id.Value));
        Assert.Empty(await w.ClientPackages.GetEligibleForService(w.OrganizationId, w.Client.Id.Value, w.Service.Id.Value,
            new DateTimeOffset(2031, 6, 30, 22, 30, 0, TimeSpan.Zero), w.Company.Id.Value));
        Assert.Equal(FixedDate, sold.ValidUntilDate);
    }

    [Fact]
    public void Eligible_RequiresTheServiceDate_AndNeverFallsBackToTheClock()
    {
        MethodInfo endpoint = typeof(ClientPackagesController).GetMethod(nameof(ClientPackagesController.GetEligible))!;
        ParameterInfo date = endpoint.GetParameters().Single(p => p.Name == "date");
        ParameterInfo companyId = endpoint.GetParameters().Single(p => p.Name == "companyId");

        // Non-nullable + [BindRequired]: a missing value fails model binding ([ApiController] => 400) instead of becoming "now".
        Assert.Equal(typeof(DateTimeOffset), date.ParameterType);
        Assert.NotNull(date.GetCustomAttribute<BindRequiredAttribute>());
        Assert.False(date.HasDefaultValue);
        Assert.NotNull(companyId.GetCustomAttribute<BindRequiredAttribute>());

        // The service contract has no optional date either.
        ParameterInfo serviceDate = typeof(Core.Interfaces.Clients.IClientPackageService)
            .GetMethod(nameof(Core.Interfaces.Clients.IClientPackageService.GetEligibleForService))!
            .GetParameters().Single(p => p.Name == "date");
        Assert.Equal(typeof(DateTimeOffset), serviceDate.ParameterType);
        Assert.False(serviceDate.HasDefaultValue);
    }
}
