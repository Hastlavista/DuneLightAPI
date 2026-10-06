#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Auth;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Employees;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// ADR-0020 — email klijenta i korisničkog računa je jedinstven unutar organizacije, trimano i bez obzira na velika/mala
/// slova (servisna provjera + DB unique indeksi ux_clients_organization_email / ux_users_organization_email), a prijava
/// po emailu ne razlikuje velika/mala slova.
/// </summary>
public class UniqueEmailTests
{
    private static int _memberNumber = 900000;

    private static ClientCreateRequest NewClient(string email) => new()
    {
        MemberNumber = Interlocked.Increment(ref _memberNumber),
        FirstName = "Email",
        LastName = "Test",
        Email = email
    };

    private static ClientUpdateRequest UpdateOf(ClientDto client, string email) => new()
    {
        MemberNumber = client.MemberNumber,
        FirstName = client.FirstName,
        LastName = client.LastName,
        Email = email
    };

    private static async Task<string> AssertBusinessRule(Func<Task> action)
    {
        BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(action);
        return ex.Code;
    }

    #region Clients

    [Fact]
    public async Task Client_DuplicateEmail_IgnoringCaseAndWhitespace_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_DuplicateEmail_IgnoringCaseAndWhitespace_IsRejected));
        IClientService clients = w.Resolve<IClientService>();

        await clients.Create(w.OrganizationId, w.ActorUserId, NewClient("Ana.Anic@Test.hr"));

        string code = await AssertBusinessRule(() => clients.Create(w.OrganizationId, w.ActorUserId, NewClient("  ana.anic@test.HR ")));
        Assert.Equal(ErrorCodes.ClientEmailAlreadyInUse, code);
    }

    [Fact]
    public async Task Client_Email_IsStoredTrimmed_AndBlankMeansNoEmail()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_Email_IsStoredTrimmed_AndBlankMeansNoEmail));
        IClientService clients = w.Resolve<IClientService>();

        ClientDto trimmed = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient("  Trim.Me@Test.hr  "));
        Assert.Equal("Trim.Me@Test.hr", trimmed.Email);

        // Clients without an email are always allowed — any number of them.
        ClientDto blank = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient("   "));
        ClientDto none1 = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(null));
        ClientDto none2 = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient(null));
        Assert.Null(blank.Email);
        Assert.Null(none1.Email);
        Assert.Null(none2.Email);
    }

    [Fact]
    public async Task Client_Update_ToAnotherClientsEmail_IsRejected_ButOwnEmailInOtherCaseIsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_Update_ToAnotherClientsEmail_IsRejected_ButOwnEmailInOtherCaseIsAllowed));
        IClientService clients = w.Resolve<IClientService>();

        await clients.Create(w.OrganizationId, w.ActorUserId, NewClient("first@test.hr"));
        ClientDto second = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient("second@test.hr"));

        string code = await AssertBusinessRule(() => clients.Update(w.OrganizationId, w.ActorUserId, second.Id, UpdateOf(second, "FIRST@test.hr")));
        Assert.Equal(ErrorCodes.ClientEmailAlreadyInUse, code);

        ClientDto recased = await clients.Update(w.OrganizationId, w.ActorUserId, second.Id, UpdateOf(second, " Second@Test.hr "));
        Assert.Equal("Second@Test.hr", recased.Email);
    }

    [Fact]
    public async Task Client_SameEmail_InDifferentOrganizations_IsAllowed()
    {
        await using SchedulingWorld a = await SchedulingWorld.Create(nameof(Client_SameEmail_InDifferentOrganizations_IsAllowed) + "_A");
        await using SchedulingWorld b = await SchedulingWorld.Create(nameof(Client_SameEmail_InDifferentOrganizations_IsAllowed) + "_B");

        await a.Resolve<IClientService>().Create(a.OrganizationId, a.ActorUserId, NewClient("shared@test.hr"));
        ClientDto other = await b.Resolve<IClientService>().Create(b.OrganizationId, b.ActorUserId, NewClient("SHARED@test.hr"));

        Assert.Equal("SHARED@test.hr", other.Email);
    }

    [Fact]
    public async Task Client_Anonymization_ReleasesTheEmail()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_Anonymization_ReleasesTheEmail));
        IClientService clients = w.Resolve<IClientService>();

        ClientDto original = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient("gdpr@test.hr"));
        await clients.Anonymize(w.OrganizationId, w.ActorUserId, original.Id);

        ClientDto reused = await clients.Create(w.OrganizationId, w.ActorUserId, NewClient("gdpr@test.hr"));
        Assert.Equal("gdpr@test.hr", reused.Email);
    }

    [Fact]
    public async Task Client_DatabaseIndex_RejectsCaseDuplicates_EvenWhenTheServiceCheckIsBypassed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_DatabaseIndex_RejectsCaseDuplicates_EvenWhenTheServiceCheckIsBypassed));

        await using DatabaseContext db = w.NewDb();
        db.Clients.Add(RawClient(w, "race@test.hr"));
        db.Clients.Add(RawClient(w, "RACE@test.hr"));

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        PostgresException pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Equal("ux_clients_organization_email", pg.ConstraintName);
    }

    private static Client RawClient(SchedulingWorld w, string email) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = w.OrganizationId,
        MemberNumber = Interlocked.Increment(ref _memberNumber),
        FirstName = "Raw",
        LastName = "Client",
        Email = email,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    #endregion

    #region User accounts

    [Fact]
    public async Task User_EmailExists_IgnoresCaseAndWhitespace()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(User_EmailExists_IgnoresCaseAndWhitespace));
        await AddUser(w, "Marko.Login@Test.hr", "Lozinka123!");

        IAuthHandler auth = w.Resolve<IAuthHandler>();
        Assert.True(await auth.EmailExists(w.OrganizationId, " marko.login@TEST.hr "));
        Assert.False(await auth.EmailExists(w.OrganizationId, "someone.else@test.hr"));
    }

    [Fact]
    public async Task User_Login_FindsTheAccountIgnoringCaseAndWhitespace()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(User_Login_FindsTheAccountIgnoringCaseAndWhitespace));
        Guid userId = await AddUser(w, "Iva.Login@Test.hr", "Lozinka123!");

        AuthResponse response = await NewAuthService(w).Login(new LoginRequest
        {
            OrganizationSlug = $"sched-test-{w.OrganizationId:N}",
            Email = "  iva.LOGIN@test.hr ",
            Password = "Lozinka123!"
        });

        Assert.Equal(userId, response.UserId);
        Assert.Equal("Iva.Login@Test.hr", response.Email);
    }

    [Fact]
    public async Task User_CreateWithLogin_WithAnExistingEmailInOtherCase_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(User_CreateWithLogin_WithAnExistingEmailInOtherCase_IsRejected));
        await AddUser(w, "taken@test.hr", "Lozinka123!");

        string code = await AssertBusinessRule(() => w.Resolve<IEmployeeService>().CreateWithLogin(w.OrganizationId, w.ActorUserId,
            new EmployeeWithLoginCreateRequest
            {
                FirstName = "Novi",
                LastName = "Zaposlenik",
                Email = " TAKEN@test.hr ",
                EmploymentStartDate = DateTimeOffset.UtcNow,
                EngagementTypeId = w.Employee.EngagementTypeId,
                CompanyIds = new List<Guid> { w.Company.Id.Value },
                PrimaryCompanyId = w.Company.Id.Value,
                Password = "Lozinka123!",
                GrantGroupIds = new List<Guid> { Guid.NewGuid() }
            }));
        Assert.Equal(ErrorCodes.EmailAlreadyInUse, code);
    }

    [Fact]
    public async Task User_DatabaseIndex_RejectsCaseDuplicates()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(User_DatabaseIndex_RejectsCaseDuplicates));

        await using DatabaseContext db = w.NewDb();
        db.Users.Add(RawUser(w, "dup@test.hr", "x"));
        db.Users.Add(RawUser(w, "DUP@test.hr", "x"));

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        PostgresException pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("ux_users_organization_email", pg.ConstraintName);
    }

    private static async Task<Guid> AddUser(SchedulingWorld w, string email, string password)
    {
        await using DatabaseContext db = w.NewDb();
        User user = RawUser(w, email, PasswordHasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id.Value;
    }

    private static User RawUser(SchedulingWorld w, string email, string passwordHash)
    {
        Guid id = Guid.NewGuid();
        return new User
        {
            Id = id,
            OrganizationId = w.OrganizationId,
            Email = email,
            PasswordHash = passwordHash,
            ApiKey = $"unique-email-test-{id:N}",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static AuthService NewAuthService(SchedulingWorld w) => new(
        w.Resolve<IAuthHandler>(),
        w.Resolve<IRosterTypeHandler>(),
        w.Resolve<IGrantGroupHandler>(),
        new JwtService(MultiSegmentHttpContractTests.Jwt),
        MultiSegmentHttpContractTests.Jwt,
        w.Resolve<IUnitOfWorkFactory>());

    #endregion
}
