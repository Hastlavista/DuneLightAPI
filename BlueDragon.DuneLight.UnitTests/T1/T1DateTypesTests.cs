#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-7 — tipovi datuma i vremena: trenutak = DateTimeOffset, kalendarski dan = DateOnly ("yyyy-MM-dd"), vrijeme dana = TimeOnly
/// ("HH:mm:ss"). Dan cjenika termina je lokalni datum POČETKA u zoni poslovnice termina (oba kraja stavke uključena); izvještaj
/// provizija broji dane organizacije (oba kraja uključena); kraj ponavljajućeg niza je zadnji lokalni dan (uključivo).
/// </summary>
public class T1DateTypesTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private const string Zagreb = "Europe/Zagreb";
    private const string NewYork = "America/New_York";

    private readonly MultiSegmentHttpContractTests.ApiHost _host;

    public T1DateTypesTests(MultiSegmentHttpContractTests.ApiHost host)
    {
        _host = host;
    }

    private static DateTimeOffset Z(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    private static async Task SetCompanyTimeZone(SchedulingWorld w, string timeZone)
    {
        await using DatabaseContext db = w.NewDb();
        Company tracked = await db.Companies.SingleAsync(c => c.Id == w.Company.Id);
        tracked.TimeZone = timeZone;
        await db.SaveChangesAsync();
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, string url, string token, object body = null) =>
        MultiSegmentHttpContractTests.Send(_host.Client, method, url, token, body);

    private static async Task<string> TokenFor(SchedulingWorld w, params string[] grants)
    {
        Guid userId = await w.AddMemberUser();
        await w.GrantUser(userId, grants);
        return new JwtService(MultiSegmentHttpContractTests.Jwt).GenerateToken(userId, $"{userId:N}@http.test", w.OrganizationId);
    }

    #region (a) Cjenik: lokalni datum početka segmenta u zoni poslovnice, ValidTo uključen

    [Fact]
    public async Task PriceResolution_UsesTheCompanyLocalDateOfTheSegmentStart_AndValidToIsInclusive()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceResolution_UsesTheCompanyLocalDateOfTheSegmentStart_AndValidToIsInclusive), Zagreb);
        DateOnly day = new(2031, 3, 10); // CET (+01:00)
        await w.AddPriceListItem(w.Service, 11m, day.AddDays(-30), companyId: w.Company.Id, validTo: day.AddDays(-1));
        await w.AddPriceListItem(w.Service, 22m, day, companyId: w.Company.Id, validTo: day);
        await w.AddPriceListItem(w.Service, 33m, day.AddDays(1), companyId: w.Company.Id);
        IPricingService pricing = w.Resolve<IPricingService>();
        Task<decimal> PriceAt(DateTimeOffset start) =>
            pricing.ResolveForServiceStart(w.OrganizationId, w.Service.Id.Value, w.Company.Id.Value, null, start).ContinueWith(t => t.Result.Price);

        Assert.Equal(11m, await PriceAt(Z(2031, 3, 9, 22, 30)));  // 9.3. 23:30 lokalno
        Assert.Equal(22m, await PriceAt(Z(2031, 3, 9, 23, 30)));  // 10.3. 00:30 lokalno (UTC je još 9.3.)
        Assert.Equal(22m, await PriceAt(Z(2031, 3, 10, 22, 30))); // 10.3. 23:30 lokalno — ValidTo = 10.3. je uključen
        Assert.Equal(33m, await PriceAt(Z(2031, 3, 10, 23, 30))); // 11.3. 00:30 lokalno (UTC je još 10.3.)

        // Kraj do kraja: termin u 23:30 lokalno (preko ponoći, 30 min) pripada danu početka.
        AppointmentDto late = await w.CreateAppointment(w.CreateRequest(Z(2031, 3, 10, 22, 30), overrideAvailability: true));
        AppointmentDto early = await w.CreateAppointment(w.CreateRequest(Z(2031, 3, 9, 23, 30), overrideAvailability: true));
        Assert.Equal(22m, Assert.Single(late.Bookings).Amount);
        Assert.Equal(22m, Assert.Single(early.Bookings).Amount);

        // Zona poslovnice termina, ne organizacije: New York 10.3. 23:30 EDT = 11.3. 03:30Z.
        await SetCompanyTimeZone(w, NewYork);
        Assert.Equal(22m, await PriceAt(Z(2031, 3, 11, 3, 30)));
        Assert.Equal(11m, await PriceAt(Z(2031, 3, 10, 3, 30))); // 9.3. 23:30 EDT
    }

    [Fact]
    public void PriceResolution_IsAPureDayRule_BothEndsInclusive()
    {
        PriceResolutionService resolver = new();
        DateOnly from = new(2031, 3, 1);
        DateOnly to = new(2031, 3, 31);
        List<PriceCandidate> candidates = new() { new PriceCandidate { Price = 40m, ValidFrom = from, ValidTo = to, IsActive = true } };

        Assert.Equal(40m, resolver.Resolve(candidates, 10m, null, null, from).Price);
        Assert.Equal(40m, resolver.Resolve(candidates, 10m, null, null, to).Price);
        Assert.Equal((10m, PriceSource.Default), (resolver.Resolve(candidates, 10m, null, null, to.AddDays(1)).Price,
            resolver.Resolve(candidates, 10m, null, null, to.AddDays(1)).Source));
        Assert.Equal(10m, resolver.Resolve(candidates, 10m, null, null, from.AddDays(-1)).Price);
    }

    #endregion

    #region (b) Preklapanje na granici

    [Fact]
    public async Task PriceListOverlap_AtTheBoundaryDay_IsRejected_TheNextDayIsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceListOverlap_AtTheBoundaryDay_IsRejected_TheNextDayIsAllowed));
        IPricingService pricing = w.Resolve<IPricingService>();
        PriceListItemCreateRequest Item(DateOnly from, DateOnly? to) => new()
        {
            SubjectType = PricingSubjectType.Service, ServiceId = w.Service.Id, CompanyId = w.Company.Id, Price = 30m, ValidFrom = from, ValidTo = to
        };

        PriceListItemDto first = await pricing.Create(w.OrganizationId, w.ActorUserId, Item(new DateOnly(2031, 1, 1), new DateOnly(2031, 1, 31)));
        Assert.Equal((new DateOnly(2031, 1, 1), new DateOnly(2031, 1, 31)), (first.ValidFrom, first.ValidTo.Value));

        // B.ValidFrom == A.ValidTo: oba kraja su uključena, pa se dijele dan 31.1.
        await SchedulingAssert.BusinessRule(ErrorCodes.PriceOverlap,
            () => pricing.Create(w.OrganizationId, w.ActorUserId, Item(new DateOnly(2031, 1, 31), null)));
        await pricing.Create(w.OrganizationId, w.ActorUserId, Item(new DateOnly(2031, 2, 1), null));

        Assert.True(DateRangeOverlap.Overlaps(new DateOnly(2031, 1, 1), new DateOnly(2031, 1, 31), new DateOnly(2031, 1, 31), null));
        Assert.False(DateRangeOverlap.Overlaps(new DateOnly(2031, 1, 1), new DateOnly(2031, 1, 31), new DateOnly(2031, 2, 1), null));
    }

    #endregion

    #region (c) Izvještaj provizija — dani organizacije, oba kraja uključena

    [Fact]
    public async Task CommissionReport_FromTo_AreInclusiveOrganizationDays_AndAReversalCountsInItsOwnPeriod()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CommissionReport_FromTo_AreInclusiveOrganizationDays_AndAReversalCountsInItsOwnPeriod), Zagreb);
        await w.Resolve<ICommissionRuleService>().Create(w.OrganizationId, w.ActorUserId, new CommissionRuleCreateRequest
        {
            EmployeeId = w.Employee.Id.Value, SubjectType = CommissionSubjectType.Service, ServiceId = w.Service.Id,
            CalculationType = CommissionCalculationType.Percentage, Value = 10m
        });
        foreach (int hour in new[] { 9, 10, 11, 12 })
            await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(hour)));

        // Zarade na granicama lokalnog dana 10.3.2031 (Zagreb, CET): 9.3. 23:30, 10.3. 00:30, 10.3. 23:30, 11.3. 00:30 lokalno.
        DateTimeOffset[] earnedAt = { Z(2031, 3, 9, 22, 30), Z(2031, 3, 9, 23, 30), Z(2031, 3, 10, 22, 30), Z(2031, 3, 10, 23, 30) };
        List<Guid> ids;
        await using (DatabaseContext db = w.NewDb())
        {
            List<CommissionEntry> entries = await db.CommissionEntries.Where(e => e.OrganizationId == w.OrganizationId).OrderBy(e => e.CreatedAt).ToListAsync();
            Assert.Equal(4, entries.Count);
            for (int i = 0; i < entries.Count; i++)
                entries[i].EarnedAt = earnedAt[i];
            // Storno zarade od 10.3. 00:30 dogodio se 15.3. — ide u razdoblje storna, 10.3. ostaje nepromijenjen.
            entries[1].Status = CommissionEntryStatus.Reversed;
            entries[1].ReversedAt = Z(2031, 3, 15, 11);
            entries[1].ReversalReason = "test";
            await db.SaveChangesAsync();
            ids = entries.Select(e => e.Id.Value).ToList();
        }

        ICommissionService ledger = w.Resolve<ICommissionService>();
        DateOnly day = new(2031, 3, 10);

        PagedResult<CommissionEntryDto> onTheDay = await ledger.GetEntries(w.OrganizationId, new CommissionEntryQuery { From = day, To = day, PageSize = 50 });
        Assert.Equal(new[] { ids[1], ids[2] }.OrderBy(x => x), onTheDay.Items.Select(e => e.Id).OrderBy(x => x));

        PagedResult<CommissionEntryDto> twoDays = await ledger.GetEntries(w.OrganizationId, new CommissionEntryQuery { From = day.AddDays(-1), To = day, PageSize = 50 });
        Assert.Equal(3, twoDays.TotalCount);

        CommissionSummaryResultDto summaryOnTheDay = await ledger.GetSummary(w.OrganizationId, new CommissionSummaryQuery { From = day, To = day });
        EmployeeCommissionSummaryDto employeeOnTheDay = Assert.Single(summaryOnTheDay.Employees);
        Assert.Equal((10m, 0m, 2), (employeeOnTheDay.EarnedAmount, employeeOnTheDay.ReversedAmount, employeeOnTheDay.EntryCount));

        CommissionSummaryResultDto reversalDay = await ledger.GetSummary(w.OrganizationId, new CommissionSummaryQuery { From = new DateOnly(2031, 3, 15), To = new DateOnly(2031, 3, 15) });
        EmployeeCommissionSummaryDto employeeOnReversal = Assert.Single(reversalDay.Employees);
        Assert.Equal((0m, 5m, -5m), (employeeOnReversal.EarnedAmount, employeeOnReversal.ReversedAmount, employeeOnReversal.NetAmount));
    }

    #endregion

    #region (d) Rođendan — DateOnly bez pomaka dana (HTTP)

    [Fact]
    public async Task Birthday_AsDateOnly_RoundTripsWithoutADayShift_OverHttp()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Birthday_AsDateOnly_RoundTripsWithoutADayShift_OverHttp), "Pacific/Auckland");
        string token = await TokenFor(w, Grants.ClientsView, Grants.ClientsManage);

        (HttpStatusCode status, JsonElement created) = await Send(HttpMethod.Post, "/api/clients", token, new
        {
            firstName = "Rođendan", lastName = $"T1-{Guid.NewGuid():N}", dateOfBirth = "1990-03-03", gdprConsentGiven = true, gdprConsentDate = "2026-01-15"
        });
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("1990-03-03", created.GetProperty("dateOfBirth").GetString());
        Assert.Equal("2026-01-15", created.GetProperty("gdprConsentDate").GetString());
        Guid id = created.GetProperty("id").GetGuid();

        (_, JsonElement read) = await Send(HttpMethod.Get, $"/api/clients/{id}", token);
        Assert.Equal("1990-03-03", read.GetProperty("dateOfBirth").GetString());

        await using (DatabaseContext db = w.NewDb())
            Assert.Equal(new DateOnly(1990, 3, 3), (await db.Clients.SingleAsync(c => c.Id == id)).DateOfBirth);

        (HttpStatusCode birthdaysStatus, JsonElement birthdays) = await Send(HttpMethod.Get, "/api/clients/birthdays?from=2031-03-03&to=2031-03-03", token);
        Assert.Equal(HttpStatusCode.OK, birthdaysStatus);
        JsonElement row = birthdays.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == id);
        Assert.Equal(("1990-03-03", "2031-03-03"), (row.GetProperty("dateOfBirth").GetString(), row.GetProperty("nextOccurrence").GetString()));

        (_, JsonElement dayBefore) = await Send(HttpMethod.Get, "/api/clients/birthdays?from=2031-03-01&to=2031-03-02", token);
        Assert.DoesNotContain(dayBefore.EnumerateArray(), b => b.GetProperty("id").GetGuid() == id);
        (_, JsonElement dayAfter) = await Send(HttpMethod.Get, "/api/clients/birthdays?from=2031-03-04&to=2031-03-05", token);
        Assert.DoesNotContain(dayAfter.EnumerateArray(), b => b.GetProperty("id").GetGuid() == id);
    }

    #endregion

    #region (e) Ponavljanje: EndDate je zadnji lokalni dan, uključivo

    [Fact]
    public async Task RecurringAppointments_EndDate_IsTheInclusiveLastLocalDayOfTheCompany()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RecurringAppointments_EndDate_IsTheInclusiveLastLocalDayOfTheCompany), Zagreb);
        await SetCompanyTimeZone(w, NewYork);
        // 17.3.2031 23:30 EDT = 18.3. 03:30Z — lokalni dan poslovnice je 17.3., UTC dan je 18.3.
        DateTimeOffset first = Z(2031, 3, 18, 3, 30);
        RecurringAppointmentCreateRequest Series(DateOnly endDate, DateTimeOffset start, RecurrenceType type = RecurrenceType.Daily) => new()
        {
            RecurrenceType = type, ServiceId = w.Service.Id.Value, EmployeeId = w.Employee.Id.Value, CompanyId = w.Company.Id.Value,
            ClientIds = new List<Guid> { w.Client.Id.Value }, FirstOccurrenceStartsAt = start, EndDate = endDate, OverrideAvailability = true
        };

        // EndDate = lokalni dan prve pojave → točno jedna pojava (iako je njezin UTC datum dan kasnije).
        List<AppointmentDto> single = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(new DateOnly(2031, 3, 17), first));
        Assert.Equal(new[] { first }, single.Select(a => a.StartsAt).ToArray());

        // EndDate prije lokalnog dana prve pojave je greška.
        await SchedulingAssert.Validation(() => w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(new DateOnly(2031, 3, 16), first)));

        // Tjedno do 31.3. uključivo, u 22:30 EDT (= 02:30Z sljedećeg UTC dana): 17.3., 24.3. i 31.3.
        List<AppointmentDto> weekly = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true,
            Series(new DateOnly(2031, 3, 31), Z(2031, 3, 18, 2, 30), RecurrenceType.Weekly));
        Assert.Equal(new[] { Z(2031, 3, 18, 2, 30), Z(2031, 3, 25, 2, 30), Z(2031, 4, 1, 2, 30) },
            weekly.Select(a => a.StartsAt).OrderBy(s => s).ToArray());
    }

    #endregion

    #region Ugovor: JSON, query string i Swagger

    [Fact]
    public async Task AvailableSlots_BindTheDayFromTheQuery_AndReturnTimeOnlyValues()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AvailableSlots_BindTheDayFromTheQuery_AndReturnTimeOnlyValues));
        string token = await TokenFor(w, Grants.AppointmentsWriteAll);

        (HttpStatusCode status, JsonElement body) = await Send(HttpMethod.Get,
            $"/api/appointments/available-slots?serviceId={w.Service.Id}&companyId={w.Company.Id}&date=2031-03-03", token);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement slots = body.EnumerateArray().Single().GetProperty("slots");
        Assert.Equal(("08:00:00", "08:30:00"), (slots[0].GetProperty("start").GetString(), slots[0].GetProperty("end").GetString()));

        // Instant u parametru dana više nije dan.
        (HttpStatusCode invalid, _) = await Send(HttpMethod.Get,
            $"/api/appointments/available-slots?serviceId={w.Service.Id}&companyId={w.Company.Id}&date=2031-03-03T00:00:00Z", token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
    }

    [Fact]
    public void Swagger_DescribesDaysAsDate_AndTimesOfDayAsTime()
    {
        OpenApiDocument document = _host.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1.0");
        string Format(Type type, string property) => document.Components.Schemas[type.FullName!].Properties[property].Format;

        Assert.Equal("date", Format(typeof(ClientDto), "dateOfBirth"));
        Assert.Equal("date", Format(typeof(PriceListItemDto), "validFrom"));
        Assert.Equal("date", Format(typeof(ClientPackageDto), "purchaseDate"));
        Assert.Equal("date", Format(typeof(LeaveFundDto), "expiresAt"));
        Assert.Equal("time", Format(typeof(AvailableSlotDto), "start"));
        Assert.Equal("time", Format(typeof(RosterEntryDto), "startTime"));
        Assert.Equal("date-time", Format(typeof(Core.DTOs.ScheduleBreaks.ScheduleBreakDto), "startsAt")); // instant ostaje DateTimeOffset

        IOpenApiParameter dateParameter = document.Paths["/api/appointments/available-slots"].Operations[HttpMethod.Get].Parameters
            .Single(p => string.Equals(p.Name, "date", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("date", dateParameter.Schema.Format);
    }

    #endregion
}
