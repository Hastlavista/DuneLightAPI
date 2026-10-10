#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Controllers.Clients;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D3B3A.1 — package validity is exclusively a CALENDAR-DATE rule: a package is valid for a participation when the
/// service's local date (segment start in the effective zone of the appointment's Company — Company.TimeZone ??
/// Organization.TimeZone) is &lt;= ClientPackage.ValidUntilDate (PostgreSQL date). ValidUntilDate is computed at sale from
/// the purchase's business date in the sale Company's calendar (the Organization's only when there is no Company).
/// Inventory status never depends on the clock. All instants below are explicit UTC so nothing depends on the host zone.
/// </summary>
public class PackageValidityCalendarTests
{
    private const string Zagreb = "Europe/Zagreb";
    private const string NewYork = "America/New_York";
    private static readonly DateOnly ValidUntil = new(2020, 3, 2);

    private static async Task SetTimeZone(SchedulingWorld w, Company company, string timeZone)
    {
        await using DatabaseContext db = w.NewDb();
        Company tracked = await db.Companies.SingleAsync(c => c.Id == company.Id);
        tracked.TimeZone = timeZone;
        await db.SaveChangesAsync();
    }

    /// <summary>A second company in the same organization where the default employee can perform the default service.</summary>
    private static async Task<Company> SecondCompany(SchedulingWorld w, string timeZone)
    {
        Company company = await w.AddCompany("Second");
        await SetTimeZone(w, company, timeZone);
        await w.AssignEmployeeToCompany(w.Employee, company);
        await w.MakeServiceAvailableAt(w.Service, company);
        return company;
    }

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2020, month, day, hour, minute, 0, TimeSpan.Zero);

    private static async Task<bool> Covers(SchedulingWorld w, ClientPackage package, DateTimeOffset startsAt, Company company = null)
    {
        try
        {
            // K1-1: "Upiši odrađeno" validira radno vrijeme i za prošlost; granični UTC trenuci ovih testova padaju izvan radnog
            // vremena poslovnice, a test je o valjanosti paketa — override (appointments.write.all) je postavka, ne ponašanje.
            await w.CompleteNew(w.CompleteRequest(startsAt, company: company, clientPackageId: package.Id, overrideAvailability: true));
            return true;
        }
        catch (BusinessRuleException ex) when (ex.Code == ErrorCodes.PackageNotEligible)
        {
            return false;
        }
    }

    #region Schema

    [Fact]
    public async Task Expiry_IsAPostgresDate_AndTheConsumptionRecordsItsServiceDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Expiry_IsAPostgresDate_AndTheConsumptionRecordsItsServiceDate));
        await using DatabaseContext db = w.NewDb();

        List<string> columns = await db.Database.SqlQueryRaw<string>(@"
            SELECT table_name || '.' || column_name || ':' || data_type || ':' || is_nullable AS ""Value""
              FROM information_schema.columns
             WHERE table_schema = 'dunelight'
               AND ((table_name = 'client_packages' AND column_name IN ('valid_until_date', 'expiry_date'))
                 OR (table_name = 'package_consumptions' AND column_name = 'service_date'))
             ORDER BY 1").ToListAsync();

        Assert.Equal(new[] { "client_packages.valid_until_date:date:NO", "package_consumptions.service_date:date:NO" }, columns);
        Assert.Equal(typeof(DateOnly), typeof(ClientPackage).GetProperty(nameof(ClientPackage.ValidUntilDate))!.PropertyType);
    }

    #endregion

    #region Eligibility = service local date <= ValidUntilDate

    [Fact]
    public async Task APackage_IsValidOnItsValidUntilDate_AndInvalidOnTheNextLocalDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(APackage_IsValidOnItsValidUntilDate_AndInvalidOnTheNextLocalDate));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, ValidUntil);

        Assert.True(await Covers(w, package, Utc(3, 2, 19, 30)));   // last minutes of 2 March (UTC organization)
        Assert.False(await Covers(w, package, Utc(3, 3, 0, 30)));   // 3 March
    }

    [Fact]
    public async Task ZagrebCompany_JudgesTheLocalDate_NotTheUtcDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ZagrebCompany_JudgesTheLocalDate_NotTheUtcDate));
        await SetTimeZone(w, w.Company, Zagreb); // UTC organization, Zagreb company (+01:00 in March 2020)
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, ValidUntil);

        Assert.True(await Covers(w, package, Utc(3, 2, 21, 30)));   // 22:30 local on 2 March
        Assert.False(await Covers(w, package, Utc(3, 2, 23, 30)));  // 00:30 local on 3 March — still 2 March in UTC
    }

    [Fact]
    public async Task NewYorkCompany_JudgesTheLocalDate_NotTheUtcDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(NewYorkCompany_JudgesTheLocalDate_NotTheUtcDate));
        await SetTimeZone(w, w.Company, NewYork); // -05:00 in early March 2020
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, ValidUntil);

        Assert.True(await Covers(w, package, Utc(3, 3, 3, 0)));     // 22:00 local on 2 March — already 3 March in UTC
        Assert.False(await Covers(w, package, Utc(3, 3, 6, 0)));    // 01:00 local on 3 March
    }

    [Fact]
    public async Task TwoCompaniesOfOneOrganization_JudgeTheSameInstantByTheirOwnLocalDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TwoCompaniesOfOneOrganization_JudgeTheSameInstantByTheirOwnLocalDate));
        await SetTimeZone(w, w.Company, Zagreb);
        Company newYork = await SecondCompany(w, NewYork);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, ValidUntil);
        DateTimeOffset instant = Utc(3, 2, 23, 30); // Zagreb: 3 March 00:30; New York: 2 March 18:30

        Assert.False(await Covers(w, package, instant, w.Company));
        Assert.True(await Covers(w, package, instant, newYork));

        // The eligibility query (used by /eligible) applies the same Company context.
        Assert.Empty(await w.ClientPackages.GetEligibleForService(w.OrganizationId, w.Client.Id.Value, w.Service.Id.Value, instant, w.Company.Id.Value));
        Assert.Single(await w.ClientPackages.GetEligibleForService(w.OrganizationId, w.Client.Id.Value, w.Service.Id.Value, instant, newYork.Id.Value));
    }

    [Fact]
    public async Task Consumption_UsesTheSameDateRule_AndRecordsTheServiceDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Consumption_UsesTheSameDateRule_AndRecordsTheServiceDate));
        await SetTimeZone(w, w.Company, Zagreb);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, ValidUntil);
        // Seeded (bypasses the pre-transaction eligibility) so the LEDGER's own validity check is what decides.
        Appointment lastDay = await w.SeedAppointment(Utc(3, 2, 21, 30), bookings: (w.Client, BookingStatus.Confirmed, 50m));
        Client other = await w.AddClient("Other", "Client");
        ClientPackage otherPackage = await w.AddClientPackage(other, w.Service, 5, ValidUntil);
        Appointment nextLocalDay = await w.SeedAppointment(Utc(3, 2, 23, 30), bookings: (other, BookingStatus.Confirmed, 50m));

        PackageConsumption consumed = await Consume(w, lastDay.Id.Value, w.Client, package);
        Assert.Equal(new DateOnly(2020, 3, 2), consumed.ServiceDate);

        BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Consume(w, nextLocalDay.Id.Value, other, otherPackage));
        Assert.Equal(ErrorCodes.PackageNotEligible, ex.Code);
        Assert.Equal(5, (await w.LoadClientPackage(otherPackage.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task AnUnlimitedPackage_StillRespectsValidUntilDate_AndCancellation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AnUnlimitedPackage_StillRespectsValidUntilDate_AndCancellation));
        ClientPackage expired = await w.AddClientPackage(w.Client, w.Service, entries: null, ValidUntil);
        ClientPackage cancelled = await w.AddClientPackage(w.Client, w.Service, entries: null, new DateOnly(2035, 1, 1), status: ClientPackageStatus.Cancelled);
        Assert.False(await Covers(w, expired, Utc(3, 3, 10)));
        Appointment seeded = await w.SeedAppointment(Utc(3, 3, 10), bookings: (w.Client, BookingStatus.Confirmed, 50m));
        foreach (ClientPackage package in new[] { expired, cancelled })
            Assert.Equal(ErrorCodes.PackageNotEligible,
                (await Assert.ThrowsAsync<BusinessRuleException>(() => Consume(w, seeded.Id.Value, w.Client, package))).Code);
    }

    private static async Task<PackageConsumption> Consume(SchedulingWorld w, Guid appointmentId, Client client, ClientPackage package)
    {
        await using IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin();
        Appointment appointment = await uow.Context.Appointments
            .Include(a => a.Segments).ThenInclude(s => s.Employees).Include(a => a.Bookings)
            .SingleAsync(a => a.Id == appointmentId);
        Booking booking = appointment.Bookings.Single(b => b.ClientId == client.Id);
        PackageConsumption consumption = await w.Resolve<IPackageConsumptionLedgerService>().Consume(
            uow, w.OrganizationId, w.ActorUserId, booking.Participations.Single(), ExecutionContextResolver.ForParticipation(appointment, booking, booking.Participations.Single()), package.Id.Value,
            BookingStatus.Completed);
        await uow.CommitAsync();
        return consumption;
    }

    #endregion

    #region Sale: ValidUntilDate from the sale Company's business date

    private static async Task<Package> CatalogPackage(SchedulingWorld w, PackageValidityType type, int? days = null)
    {
        await using DatabaseContext db = w.NewDb();
        Package package = new()
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, Name = $"Sale-{Guid.NewGuid():N}",
            EntryMode = PackageEntryMode.PerService, ValidityType = type, ValidityDays = days,
            DefaultPrice = 100m, IsActive = true, CreatedAt = TestClock.UtcNow
        };
        package.Services.Add(new PackageServiceItem { Id = Guid.NewGuid(), PackageId = package.Id.Value, ServiceId = w.Service.Id.Value, EntryCount = 5 });
        db.Packages.Add(package);
        await db.SaveChangesAsync();
        return package;
    }

    private static Task<ClientPackageDto> Sell(SchedulingWorld w, Package package, DateOnly? purchasedOn, Guid? companyId) =>
        w.ClientPackages.Create(w.OrganizationId, w.ActorUserId, w.Client.Id.Value, new ClientPackageCreateRequest
        {
            PackageId = package.Id.Value, PurchaseDate = purchasedOn, PaidPrice = 100m, CompanyId = companyId
        });

    [Fact]
    public async Task Sale_UsesTheSaleCompanysLocalDate_AndTheOrganizationOnlyWithoutACompany()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Sale_UsesTheSaleCompanysLocalDate_AndTheOrganizationOnlyWithoutACompany), NewYork);
        await SetTimeZone(w, w.Company, Zagreb);
        Package dayCount = await CatalogPackage(w, PackageValidityType.DayCount, days: 10);
        Package endOfMonth = await CatalogPackage(w, PackageValidityType.EndOfMonth);
        // CHANGED in T1: PurchaseDate je poslovni DAN kupnje (DateOnly). Zadan dan se koristi točno takav (bez zone); bez njega je
        // to današnji dan po poslovnom satu u zoni poslovnice prodaje (organizacije kad prodaja nema poslovnicu).
        // 31 Jan 23:30 UTC = 1 Feb 00:30 in Zagreb (the sale company) = 31 Jan 18:30 in New York (the organization).
        using (w.ClockAt(new DateTimeOffset(2031, 1, 31, 23, 30, 0, TimeSpan.Zero)))
        {
            // CHANGED in T1 (T1-11): "10 dana" uključuje dan kupnje → kupnja + 9 (prije + 10).
            ClientPackageDto atCompany = await Sell(w, dayCount, null, w.Company.Id);
            Assert.Equal((new DateOnly(2031, 2, 1), new DateOnly(2031, 2, 10)), (atCompany.PurchaseDate, atCompany.ValidUntilDate));
            Assert.Equal(new DateOnly(2031, 2, 28), (await Sell(w, endOfMonth, null, w.Company.Id)).ValidUntilDate);
            ClientPackageDto withoutCompany = await Sell(w, dayCount, null, null);
            Assert.Equal((new DateOnly(2031, 1, 31), new DateOnly(2031, 2, 9)), (withoutCompany.PurchaseDate, withoutCompany.ValidUntilDate));
            Assert.Equal(new DateOnly(2031, 1, 31), (await Sell(w, endOfMonth, null, null)).ValidUntilDate);
        }

        // An explicit day is used as-is, whatever the zones.
        // CHANGED in T1 (T1-9): ručni dan kupnje ne smije biti nakon današnjeg dana ni prije njega bez granta, a paket ne smije biti
        // istekao pri upisu — sat se postavlja na taj dan (31 Jan 12:00 UTC = 13:00 u Zagrebu, zona poslovnice prodaje).
        using (w.ClockAt(new DateTimeOffset(2031, 1, 31, 12, 0, 0, TimeSpan.Zero)))
        {
            ClientPackageDto explicitDay = await Sell(w, dayCount, new DateOnly(2031, 1, 31), w.Company.Id);
            Assert.Equal((new DateOnly(2031, 1, 31), new DateOnly(2031, 2, 9)), (explicitDay.PurchaseDate, explicitDay.ValidUntilDate));
            Assert.Equal(new DateOnly(2031, 1, 31), (await Sell(w, endOfMonth, new DateOnly(2031, 1, 31), w.Company.Id)).ValidUntilDate);
        }
    }

    [Fact]
    public void ExpiryCalculator_KeepsTheExistingDurationSemantics()
    {
        DateOnly purchase = new(2031, 1, 31);

        // CHANGED in T1 (T1-11): DayCount = N kalendarskih dana uključujući dan kupnje (31.1. + 10 dana → 9.2.; prije 10.2.).
        Assert.Equal(new DateOnly(2031, 2, 9), PackageExpiryCalculator.CalculateValidUntilDate(PackageValidityType.DayCount, purchase, 10, null));
        Assert.Equal(new DateOnly(2031, 1, 31), PackageExpiryCalculator.CalculateValidUntilDate(PackageValidityType.EndOfMonth, purchase, null, null));
        Assert.Equal(new DateOnly(2032, 2, 29), PackageExpiryCalculator.CalculateValidUntilDate(PackageValidityType.EndOfMonth, new DateOnly(2032, 2, 1), null, null));
        Assert.Equal(new DateOnly(2031, 6, 30), PackageExpiryCalculator.CalculateValidUntilDate(PackageValidityType.FixedDate, purchase, null, new DateOnly(2031, 6, 30)));

        // D3B3A.2: FixedDate is already a calendar date — used as-is whatever the purchase day.
        // CHANGED in T1: ForSale prima poslovni dan kupnje (DateOnly, ClientPackage.PurchaseDate) umjesto instanta + kalendara.
        Package fixedDate = new() { ValidityType = PackageValidityType.FixedDate, ValidityFixedDate = new DateOnly(2031, 6, 30) };
        Assert.Equal(new DateOnly(2031, 6, 30), PackageExpiryCalculator.ForSale(fixedDate, new DateOnly(2031, 1, 31)));
        Assert.Equal(new DateOnly(2031, 6, 30), PackageExpiryCalculator.ForSale(fixedDate, new DateOnly(2031, 2, 1)));
    }

    #endregion

    #region /eligible requires the Company context

    [Fact]
    public async Task Eligible_RequiresAnExistingCompany_AndTheEndpointBindsItAsRequired()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Eligible_RequiresAnExistingCompany_AndTheEndpointBindsItAsRequired));

        await SchedulingAssert.NotFound(() => w.ClientPackages.GetEligibleForService(
            w.OrganizationId, w.Client.Id.Value, w.Service.Id.Value, TestClock.UtcNow, Guid.NewGuid()));

        ParameterInfo companyId = typeof(ClientPackagesController).GetMethod(nameof(ClientPackagesController.GetEligible))!
            .GetParameters().Single(p => p.Name == "companyId");
        Assert.Equal(typeof(Guid), companyId.ParameterType);
        Assert.NotNull(companyId.GetCustomAttribute<BindRequiredAttribute>());
    }

    #endregion

    #region Reversal status does not depend on the clock

    [Fact]
    public async Task AReversedExpiredPackage_WithARestoredEntry_IsActive_ButStillIneligibleAfterValidUntilDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AReversedExpiredPackage_WithARestoredEntry_IsActive_ButStillIneligibleAfterValidUntilDate));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 1, ValidUntil); // long expired today
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(Utc(3, 2, 10), clientPackageId: package.Id));
        Assert.Equal(ClientPackageStatus.Depleted, (await w.LoadClientPackage(package.Id.Value)).Status);

        await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed); // correction reverses the consumption

        ClientPackage restored = await w.LoadClientPackage(package.Id.Value);
        Assert.Equal(1, restored.ServiceEntries.Single().RemainingEntries);
        Assert.Equal(ClientPackageStatus.Active, restored.Status); // before D3B3A.1: stayed Depleted because "now" > expiry
        Assert.Equal(ClientPackageStatus.Expired,
            (await w.ClientPackages.GetById(w.OrganizationId, w.Client.Id.Value, package.Id.Value)).Status); // effective status

        Assert.False(await Covers(w, package, Utc(3, 3, 10)));     // after ValidUntilDate: ineligible
        Assert.True(await Covers(w, package, Utc(3, 2, 15)));      // its own validity window still works
    }

    #endregion
}
