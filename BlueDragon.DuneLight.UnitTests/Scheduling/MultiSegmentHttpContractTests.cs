#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API;
using BlueDragon.DuneLight.Infrastructure.Outbox;
using BlueDragon.DuneLight.Infrastructure.Services.Management;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1E.1 — HTTP contract of the M1E multi-segment surface through the REAL API pipeline: the production
/// <see cref="Startup"/> (routing, JSON options, JWT authentication, [RequireGrant], ExceptionHandlingMiddleware, model
/// validation) hosted in-process on Kestrel (loopback, random port) — no new test framework or package. Background hosted
/// services (outbox processor, platform bootstrapper) are removed so the host only serves requests. Data is arranged through
/// <see cref="SchedulingWorld"/>; requests carry a JWT signed with the API's settings for a user whose grants come from a
/// real GrantGroup (grant resolution is not mocked).
/// </summary>
public class MultiSegmentHttpContractTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    internal static readonly JwtSettings Jwt = new()
    {
        SecretKey = "M1E1-http-contract-test-signing-key-that-is-long-enough-256-bits!",
        Issuer = "BlueDragon.DuneLight",
        Audience = "BlueDragon.DuneLight.Client",
        ExpirationHours = 1
    };

    /// <summary>The production API pipeline on a loopback Kestrel port, shared by the class.</summary>
    public sealed class ApiHost : IAsyncLifetime
    {
        private IHost _host;
        public HttpClient Client { get; private set; }

        public async Task InitializeAsync()
        {
            Dictionary<string, string> settings = new()
            {
                ["DatabaseSettings:ConnectionString"] = SchedulingTestHost.ConnectionString,
                ["JwtSettings:SecretKey"] = Jwt.SecretKey,
                ["JwtSettings:Issuer"] = Jwt.Issuer,
                ["JwtSettings:Audience"] = Jwt.Audience,
                ["JwtSettings:ExpirationHours"] = "1",
                ["PlatformJwtSettings:SecretKey"] = "M1E1-http-contract-platform-key-distinct-and-long-enough-256!!",
                ["PlatformJwtSettings:Issuer"] = "BlueDragon.DuneLight.Platform",
                ["PlatformJwtSettings:Audience"] = "BlueDragon.DuneLight.Platform.Management",
                ["PlatformJwtSettings:ExpirationHours"] = "1",
                ["BrandingSettings:StoragePath"] = "wwwroot/branding",
                ["BrandingSettings:PublicBasePath"] = "/uploads/branding",
                ["BrandingSettings:MaxFileSizeBytes"] = "2097152"
            };

            _host = Host.CreateDefaultBuilder()
                .UseEnvironment("Production")
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureAppConfiguration(c => c.Sources.Clear())
                .ConfigureAppConfiguration(c => c.AddInMemoryCollection(settings))
                .UseSerilog((_, configuration) => configuration.MinimumLevel.Fatal())
                .UseDefaultServiceProvider(o => o.ValidateScopes = false)
                .ConfigureWebHostDefaults(web => web
                    .UseStartup<Startup>()
                    .UseKestrel()
                    .UseUrls("http://127.0.0.1:0"))
                .ConfigureServices(services =>
                {
                    // Only the web server stays: no outbox polling or platform bootstrap against the shared test database.
                    foreach (ServiceDescriptor background in services
                                 .Where(d => d.ServiceType == typeof(IHostedService) &&
                                             (d.ImplementationType == typeof(OutboxProcessorService) ||
                                              d.ImplementationType == typeof(PlatformAccountBootstrapper)))
                                 .ToList())
                        services.Remove(background);
                })
                .Build();
            await _host.StartAsync();

            string address = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
            Client = new HttpClient { BaseAddress = new Uri(address) };
        }

        public async Task DisposeAsync()
        {
            Client?.Dispose();
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
    }

    private readonly HttpClient _http;

    public MultiSegmentHttpContractTests(ApiHost host)
    {
        _http = host.Client;
    }

    #region Arrangement

    /// <summary>A world with a Massage(A)+Physio(B) appointment for the main client, and tokens per grant set.</summary>
    private sealed class Arranged : IAsyncDisposable
    {
        public SchedulingWorld World { get; init; }
        public ServiceEntity Massage { get; init; }
        public ServiceEntity Physio { get; init; }
        public Employee B { get; init; }
        public AppointmentDto Appointment { get; init; }
        public string WriteAll { get; init; }
        public string WriteOwnAsA { get; init; }
        public string ViewOnly { get; init; }
        public List<Guid> GrantGroups { get; } = new();

        public Guid SegmentOf(ServiceEntity service) => Appointment.Segments.Single(s => s.ServiceId == service.Id).Id;

        public async ValueTask DisposeAsync()
        {
            // grant_group_grants / user_grant_groups carry no organization_id — remove them before the world's cleanup.
            await using (DatabaseContext db = World.NewDb())
            {
                await db.UserGrantGroups.Where(u => GrantGroups.Contains(u.GrantGroupId)).ExecuteDeleteAsync();
                await db.GrantGroupGrants.Where(g => GrantGroups.Contains(g.GrantGroupId)).ExecuteDeleteAsync();
                await db.GrantGroups.Where(g => GrantGroups.Contains(g.Id.Value)).ExecuteDeleteAsync();
            }

            await World.DisposeAsync();
        }
    }

    private static async Task<Arranged> Arrange(string testName)
    {
        SchedulingWorld w = await SchedulingWorld.Create(testName);
        ServiceEntity massage = await w.AddService(60, 80m, name: "Massage");
        ServiceEntity physio = await w.AddService(30, 40m, name: "Physio");
        await w.AssignEmployeeToService(w.Employee, massage);
        Employee b = await w.AddEmployee("B", serviceId: physio.Id);
        AppointmentDto appointment = await w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, new AppointmentCreateRequest
        {
            CompanyId = w.Company.Id.Value,
            Segments = new List<AppointmentSegmentCreateRequest>
            {
                Segment(massage, SchedulingWorld.Future(9), w.Employee, w.Client),
                Segment(physio, SchedulingWorld.Future(10), b, w.Client)
            }
        });

        List<Guid> groups = new();
        string writeAll = await Token(w, w.ActorUserId, groups, Grants.AppointmentsView, Grants.AppointmentsWriteAll);
        string writeOwn = await Token(w, w.Employee.UserId, groups, Grants.AppointmentsView, Grants.AppointmentsWriteOwn);
        string viewOnly = await Token(w, b.UserId, groups, Grants.AppointmentsView);
        Arranged arranged = new()
        {
            World = w, Massage = massage, Physio = physio, B = b, Appointment = appointment,
            WriteAll = writeAll, WriteOwnAsA = writeOwn, ViewOnly = viewOnly
        };
        arranged.GrantGroups.AddRange(groups);
        return arranged;
    }

    private static AppointmentSegmentCreateRequest Segment(ServiceEntity service, DateTimeOffset start, Employee employee, params Client[] clients) => new()
    {
        ServiceId = service.Id.Value,
        PlannedStart = start,
        EmployeeIds = new List<Guid> { employee.Id.Value },
        Participants = clients.Select(c => new AppointmentParticipantCreateRequest { ClientId = c.Id.Value }).ToList()
    };

    /// <summary>A real GrantGroup with exactly <paramref name="grants"/> assigned to the user, and a JWT for that user.</summary>
    private static async Task<string> Token(SchedulingWorld w, Guid userId, List<Guid> groups, params string[] grants)
    {
        Guid groupId = Guid.NewGuid();
        await using (DatabaseContext db = w.NewDb())
        {
            db.GrantGroups.Add(new GrantGroup
            {
                Id = groupId, OrganizationId = w.OrganizationId, Name = $"http-{groupId:N}", CreatedAt = DateTimeOffset.UtcNow,
                Grants = grants.Select(g => new GrantGroupGrant { GrantGroupId = groupId, GrantKey = g }).ToList()
            });
            db.UserGrantGroups.Add(new UserGrantGroup { UserId = userId, GrantGroupId = groupId });
            await db.SaveChangesAsync();
        }

        groups.Add(groupId);
        return new JwtService(Jwt).GenerateToken(userId, $"{userId:N}@http.test", w.OrganizationId, "Member");
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, string url, string token, object body = null, string rawBody = null) =>
        Send(_http, method, url, token, body, rawBody);

    /// <summary>Shared by the M1F group contract tests (same host, same envelope reading).</summary>
    internal static async Task<(HttpStatusCode Status, JsonElement Body)> Send(
        HttpClient http, HttpMethod method, string url, string token, object body = null, string rawBody = null)
    {
        using HttpRequestMessage request = new(method, url);
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (rawBody != null)
            request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        else if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        JsonElement json = string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, json);
    }

    internal static void AssertError(HttpStatusCode expectedStatus, string expectedCode, (HttpStatusCode Status, JsonElement Body) response)
    {
        Assert.Equal(expectedStatus, response.Status);
        Assert.True(response.Body.ValueKind == JsonValueKind.Object && response.Body.TryGetProperty("error", out _),
            $"Expected the error envelope, got: {(response.Body.ValueKind == JsonValueKind.Undefined ? "<empty body>" : response.Body.GetRawText())}");
        Assert.Equal(expectedCode, response.Body.GetProperty("error").GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(response.Body.GetProperty("error").GetProperty("message").GetString()));
    }

    private static int SegmentCount(JsonElement appointment) => appointment.GetProperty("segments").GetArrayLength();

    private static JsonElement SegmentById(JsonElement appointment, Guid id) =>
        appointment.GetProperty("segments").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);

    #endregion

    [Fact]
    public async Task EveryNewRoute_IsMapped_BindsRouteIdsAndBodies_AndReturnsTheAppointment()
    {
        await using Arranged a = await Arrange(nameof(EveryNewRoute_IsMapped_BindsRouteIdsAndBodies_AndReturnsTheAppointment));
        SchedulingWorld w = a.World;
        Guid appointmentId = a.Appointment.Id;
        Guid massage = a.SegmentOf(a.Massage);
        Guid physio = a.SegmentOf(a.Physio);
        Client partner = await w.AddClient("Partner");
        Employee c = await w.AddEmployee("C", serviceId: a.Physio.Id);
        Room room = await w.AddRoom(capacity: 3);
        Resource table = await w.AddResource(capacity: 2, name: "Table");

        // POST /api/appointments/{id}/segments
        var added = await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/segments", a.WriteAll, new
        {
            serviceId = w.Service.Id, plannedStart = SchedulingWorld.Future(11), employeeIds = new[] { w.Employee.Id },
            participants = new[] { new { clientId = w.Client.Id } }
        });
        Assert.Equal(HttpStatusCode.OK, added.Status);
        Assert.Equal(appointmentId, added.Body.GetProperty("id").GetGuid());
        Assert.Equal(3, SegmentCount(added.Body));
        Guid third = added.Body.GetProperty("segments").EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() != massage && s.GetProperty("id").GetGuid() != physio).GetProperty("id").GetGuid();

        // A segment may be added without participants (M1E rule) — the HTTP contract must allow it too.
        var empty = await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/segments", a.WriteAll, new
        {
            serviceId = a.Physio.Id, plannedStart = SchedulingWorld.Future(13), employeeIds = new[] { a.B.Id }
        });
        Assert.Equal(HttpStatusCode.OK, empty.Status);
        Assert.Equal(4, SegmentCount(empty.Body));
        Guid emptySegment = empty.Body.GetProperty("segments").EnumerateArray()
            .Single(s => s.GetProperty("plannedStart").GetDateTimeOffset() == SchedulingWorld.Future(13)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, $"/api/segments/{emptySegment}", a.WriteAll)).Status);

        // PATCH /api/segments/{id}/time
        var timed = await Send(HttpMethod.Patch, $"/api/segments/{third}/time", a.WriteAll, new { plannedStart = SchedulingWorld.Future(12) });
        Assert.Equal(HttpStatusCode.OK, timed.Status);
        Assert.Equal(SchedulingWorld.Future(12), SegmentById(timed.Body, third).GetProperty("plannedStart").GetDateTimeOffset());

        // PATCH /api/segments/{id}/employees
        var staffed = await Send(HttpMethod.Patch, $"/api/segments/{physio}/employees", a.WriteAll, new { employeeIds = new[] { c.Id } });
        Assert.Equal(HttpStatusCode.OK, staffed.Status);
        Assert.Equal(c.Id, SegmentById(staffed.Body, physio).GetProperty("employees")[0].GetProperty("employeeId").GetGuid());

        // PATCH /api/segments/{id}/room
        var roomed = await Send(HttpMethod.Patch, $"/api/segments/{physio}/room", a.WriteAll, new { roomId = room.Id });
        Assert.Equal(HttpStatusCode.OK, roomed.Status);
        Assert.Equal(room.Id, SegmentById(roomed.Body, physio).GetProperty("roomId").GetGuid());

        // PUT /api/segments/{id}/resources
        var equipped = await Send(HttpMethod.Put, $"/api/segments/{physio}/resources", a.WriteAll,
            new { resources = new[] { new { resourceId = table.Id, quantityRequired = 2 } } });
        Assert.Equal(HttpStatusCode.OK, equipped.Status);
        Assert.Equal(2, SegmentById(equipped.Body, physio).GetProperty("resources")[0].GetProperty("quantityRequired").GetInt32());

        // PATCH /api/segments/{id}/service
        var serviced = await Send(HttpMethod.Patch, $"/api/segments/{third}/service", a.WriteAll, new { serviceId = a.Massage.Id, useServiceDuration = true });
        Assert.Equal(HttpStatusCode.OK, serviced.Status);
        Assert.Equal(a.Massage.Id, SegmentById(serviced.Body, third).GetProperty("serviceId").GetGuid());

        // POST /api/appointments/{id}/clients
        var joined = await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/clients", a.WriteAll,
            new { clientId = partner.Id, participations = new[] { new { segmentId = physio, amount = 25m } } });
        Assert.Equal(HttpStatusCode.OK, joined.Status);
        JsonElement partnerBooking = joined.Body.GetProperty("bookings").EnumerateArray().Single(b => b.GetProperty("clientId").GetGuid() == partner.Id);
        JsonElement partnerParticipation = partnerBooking.GetProperty("participations")[0];
        Assert.Equal(physio, partnerParticipation.GetProperty("appointmentSegmentId").GetGuid());
        Assert.Equal(25m, partnerParticipation.GetProperty("amount").GetDecimal());
        Assert.Equal("Confirmed", partnerParticipation.GetProperty("status").GetString()); // string enums (API JSON options)

        // DELETE /api/participations/{id}
        var left = await Send(HttpMethod.Delete, $"/api/participations/{partnerParticipation.GetProperty("id").GetGuid()}", a.WriteAll);
        Assert.Equal(HttpStatusCode.OK, left.Status);
        Assert.DoesNotContain(left.Body.GetProperty("bookings").EnumerateArray(), b => b.GetProperty("clientId").GetGuid() == partner.Id);

        // DELETE /api/segments/{id}
        var removed = await Send(HttpMethod.Delete, $"/api/segments/{third}", a.WriteAll);
        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Equal(2, SegmentCount(removed.Body));
    }

    public static IEnumerable<object[]> Endpoints() => new[]
    {
        new object[] { "POST", "/api/appointments/{appointment}/segments" },
        new object[] { "POST", "/api/appointments/{appointment}/clients" },
        new object[] { "PATCH", "/api/segments/{physio}/time" },
        new object[] { "PATCH", "/api/segments/{physio}/service" },
        new object[] { "PATCH", "/api/segments/{physio}/employees" },
        new object[] { "PATCH", "/api/segments/{physio}/room" },
        new object[] { "PUT", "/api/segments/{physio}/resources" },
        new object[] { "DELETE", "/api/segments/{physio}" },
        new object[] { "DELETE", "/api/participations/{participation}" }
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Grants_AreEnforced_NoTokenIs401_ViewOnlyIs403_AndNothingChanges(string method, string template)
    {
        await using Arranged a = await Arrange(nameof(Grants_AreEnforced_NoTokenIs401_ViewOnlyIs403_AndNothingChanges) + method + template.Length);
        Guid physio = a.SegmentOf(a.Physio);
        string url = template
            .Replace("{appointment}", a.Appointment.Id.ToString())
            .Replace("{physio}", physio.ToString())
            .Replace("{participation}", a.Appointment.Bookings[0].Participations.Single(p => p.AppointmentSegmentId == physio).Id.ToString());
        object body = new { plannedStart = SchedulingWorld.Future(15) };

        // 401 without a token. NOTE (pre-existing, API-wide, not changed here): [RequireGrant] answers an anonymous request with
        // UnauthorizedResult, which MVC turns into a ProblemDetails body, so ExceptionHandlingMiddleware (which only wraps
        // EMPTY 401/403 responses) leaves it as is — the status is the contract, the body is not the error envelope.
        var anonymous = await Send(new HttpMethod(method), url, token: null, body);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.Status);
        Assert.Equal(401, anonymous.Body.GetProperty("status").GetInt32());
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(new HttpMethod(method), url, a.ViewOnly, body));

        AppointmentDto after = await a.World.Appointments.GetById(a.World.OrganizationId, a.Appointment.Id);
        Assert.Equal(2, after.Segments.Count);
        Assert.Equal(SchedulingWorld.Future(10), after.Segments.Single(s => s.Id == physio).PlannedStart);
        Assert.Equal(2, Assert.Single(after.Bookings).Participations.Count);
    }

    [Fact]
    public async Task OwnScope_IsCheckedPerSegment_AndWholeAppointmentCancelNeedsWriteAll()
    {
        await using Arranged a = await Arrange(nameof(OwnScope_IsCheckedPerSegment_AndWholeAppointmentCancelNeedsWriteAll));
        Guid massage = a.SegmentOf(a.Massage);
        Guid physio = a.SegmentOf(a.Physio);

        // Employee A (write.own) is not assigned to the physio segment.
        AssertError(HttpStatusCode.Conflict, ErrorCodes.NotOwner,
            await Send(HttpMethod.Patch, $"/api/segments/{physio}/time", a.WriteOwnAsA, new { plannedStart = SchedulingWorld.Future(15) }));
        // ...but owns the massage segment.
        var own = await Send(HttpMethod.Patch, $"/api/segments/{massage}/time", a.WriteOwnAsA, new { plannedStart = SchedulingWorld.Future(8) });
        Assert.Equal(HttpStatusCode.OK, own.Status);
        // Whole-appointment cancel: write.all only.
        AssertError(HttpStatusCode.Conflict, ErrorCodes.NotOwner,
            await Send(HttpMethod.Post, $"/api/appointments/{a.Appointment.Id}/cancel", a.WriteOwnAsA, new { }));
    }

    [Fact]
    public async Task BusinessErrors_MapTo409WithTheErrorEnvelope_LegacyMultiSegmentCallsAreNever500()
    {
        await using Arranged a = await Arrange(nameof(BusinessErrors_MapTo409WithTheErrorEnvelope_LegacyMultiSegmentCallsAreNever500));
        SchedulingWorld w = a.World;
        Guid id = a.Appointment.Id;

        AssertError(HttpStatusCode.Conflict, ErrorCodes.SegmentSelectionRequired, await Send(HttpMethod.Put, $"/api/appointments/{id}", a.WriteAll, new
        {
            startsAt = SchedulingWorld.Future(12), serviceId = a.Massage.Id, employeeId = w.Employee.Id, companyId = w.Company.Id,
            clientIds = new[] { w.Client.Id }
        }));
        AssertError(HttpStatusCode.Conflict, ErrorCodes.SegmentSelectionRequired,
            await Send(HttpMethod.Patch, $"/api/appointments/{id}/move", a.WriteAll, new { startsAt = SchedulingWorld.Future(12) }));
        AssertError(HttpStatusCode.Conflict, ErrorCodes.SegmentSelectionRequired, await Send(HttpMethod.Patch, $"/api/appointments/{id}/complete", a.WriteAll, new
        {
            startsAt = SchedulingWorld.Future(9), serviceId = a.Massage.Id, employeeId = w.Employee.Id, companyId = w.Company.Id,
            clientIds = new[] { w.Client.Id }, settlements = new[] { new { clientId = w.Client.Id } }
        }));
        Client partner = await w.AddClient("Partner");
        AssertError(HttpStatusCode.Conflict, ErrorCodes.SegmentSelectionRequired,
            await Send(HttpMethod.Post, $"/api/appointments/{id}/bookings", a.WriteAll, new { clientId = partner.Id }));

        // A segment-native business rule.
        Guid physio = a.SegmentOf(a.Physio);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, $"/api/segments/{physio}", a.WriteAll)).Status);
        AssertError(HttpStatusCode.Conflict, ErrorCodes.LastSegmentCannotBeRemoved,
            await Send(HttpMethod.Delete, $"/api/segments/{a.SegmentOf(a.Massage)}", a.WriteAll));
    }

    [Fact]
    public async Task UnknownIds_Return404_AndNonGuidRouteIdsDoNotMatchARoute()
    {
        await using Arranged a = await Arrange(nameof(UnknownIds_Return404_AndNonGuidRouteIdsDoNotMatchARoute));
        Guid unknown = Guid.NewGuid();

        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound,
            await Send(HttpMethod.Patch, $"/api/segments/{unknown}/time", a.WriteAll, new { plannedStart = SchedulingWorld.Future(12) }));
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound, await Send(HttpMethod.Delete, $"/api/segments/{unknown}", a.WriteAll));
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound, await Send(HttpMethod.Delete, $"/api/participations/{unknown}", a.WriteAll));
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound, await Send(HttpMethod.Post, $"/api/appointments/{unknown}/segments", a.WriteAll, new
        {
            serviceId = a.Physio.Id, plannedStart = SchedulingWorld.Future(12), employeeIds = new[] { a.B.Id }
        }));
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound, await Send(HttpMethod.Post, $"/api/appointments/{a.Appointment.Id}/clients", a.WriteAll,
            new { clientId = a.World.Client.Id, participations = new[] { new { segmentId = unknown } } }));

        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Patch, "/api/segments/not-a-guid/time", a.WriteAll, new { })).Status);
    }

    [Fact]
    public async Task ValidationFailures_Are400WithTheErrorEnvelope()
    {
        await using Arranged a = await Arrange(nameof(ValidationFailures_Are400WithTheErrorEnvelope));
        Guid physio = a.SegmentOf(a.Physio);

        // Malformed body → automatic model validation, same envelope.
        var malformed = await Send(HttpMethod.Patch, $"/api/segments/{physio}/time", a.WriteAll, rawBody: "{\"plannedStart\":\"not-a-date\"}");
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError, malformed);
        Assert.Equal(JsonValueKind.Object, malformed.Body.GetProperty("error").GetProperty("details").ValueKind);

        // Domain validation with its own code.
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.MultiEmployeeNotSupported,
            await Send(HttpMethod.Patch, $"/api/segments/{physio}/employees", a.WriteAll, new { employeeIds = new[] { a.B.Id, a.World.Employee.Id } }));
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError,
            await Send(HttpMethod.Put, $"/api/segments/{physio}/resources", a.WriteAll,
                new { resources = new[] { new { resourceId = Guid.NewGuid(), quantityRequired = 0 } } }));
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError,
            await Send(HttpMethod.Post, $"/api/appointments/{a.Appointment.Id}/clients", a.WriteAll,
                new { clientId = a.World.Client.Id, participations = Array.Empty<object>() }));
        // Create still requires a participant per segment (domain rule; the add-segment contract does not).
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError, await Send(HttpMethod.Post, "/api/appointments", a.WriteAll, new
        {
            companyId = a.World.Company.Id,
            segments = new[] { new { serviceId = a.Physio.Id, plannedStart = SchedulingWorld.Future(15), employeeIds = new[] { a.B.Id } } }
        }));
    }
}
