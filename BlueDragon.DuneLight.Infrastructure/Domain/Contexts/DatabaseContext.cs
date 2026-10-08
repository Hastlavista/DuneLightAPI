using System;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Contexts;

public class DatabaseContext : DbContext
{
    public DbSet<Organization> Organizations { get; set; }
    public DbSet<User> Users { get; set; }

    public DbSet<Company> Companies { get; set; }
    public DbSet<Room> Rooms { get; set; }
    public DbSet<Resource> Resources { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<ServiceCompany> ServiceCompanies { get; set; }
    public DbSet<PriceListItem> PriceListItems { get; set; }
    public DbSet<PriceListItemHistory> PriceListItemHistory { get; set; }
    public DbSet<Package> Packages { get; set; }
    public DbSet<PackageServiceItem> PackageServiceItems { get; set; }

    public DbSet<EngagementType> EngagementTypes { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<EmployeeCompany> EmployeeCompanies { get; set; }
    public DbSet<EmployeeServiceAssignment> EmployeeServiceAssignments { get; set; }
    public DbSet<EmployeeAuditLog> EmployeeAuditLog { get; set; }

    public DbSet<ClientTag> ClientTags { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<ClientTagAssignment> ClientTagAssignments { get; set; }
    public DbSet<ClientPackage> ClientPackages { get; set; }
    public DbSet<ClientPackageServiceEntry> ClientPackageServiceEntries { get; set; }
    public DbSet<PackageConsumption> PackageConsumptions { get; set; }
    public DbSet<ClientMembership> ClientMemberships { get; set; }
    public DbSet<MembershipPause> MembershipPauses { get; set; }
    public DbSet<ClientMembershipAuditLog> ClientMembershipAuditLog { get; set; }
    public DbSet<ClientMembershipPeriod> ClientMembershipPeriods { get; set; }
    public DbSet<MembershipCharge> MembershipCharges { get; set; }
    public DbSet<MembershipUsage> MembershipUsages { get; set; }
    public DbSet<ParticipationMembershipCoverage> ParticipationMembershipCoverages { get; set; }
    public DbSet<GroupOccurrenceMembershipSkip> GroupOccurrenceMembershipSkips { get; set; }

    public DbSet<CancellationPolicy> CancellationPolicies { get; set; }
    public DbSet<CancellationPolicyVersion> CancellationPolicyVersions { get; set; }
    public DbSet<CancellationPolicyAssignment> CancellationPolicyAssignments { get; set; }
    public DbSet<ParticipationPolicyConsequence> ParticipationPolicyConsequences { get; set; }

    public DbSet<MembershipPlan> MembershipPlans { get; set; }
    public DbSet<MembershipPlanVersion> MembershipPlanVersions { get; set; }
    public DbSet<MembershipPlanVersionService> MembershipPlanVersionServices { get; set; }
    public DbSet<MembershipPlanVersionCompany> MembershipPlanVersionCompanies { get; set; }
    public DbSet<MembershipPlanUsageLimit> MembershipPlanUsageLimits { get; set; }
    public DbSet<MembershipPlanPriceBenefit> MembershipPlanPriceBenefits { get; set; }

    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<Booking> Bookings { get; set; }
    public DbSet<AppointmentSegment> AppointmentSegments { get; set; }
    public DbSet<AppointmentSegmentEmployee> AppointmentSegmentEmployees { get; set; }
    public DbSet<AppointmentSegmentResource> AppointmentSegmentResources { get; set; }
    public DbSet<BookingSegmentParticipation> BookingSegmentParticipations { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<AppointmentAuditLog> AppointmentAuditLog { get; set; }
    public DbSet<ScheduleBreak> ScheduleBreaks { get; set; }

    public DbSet<Checkout> Checkouts { get; set; }
    public DbSet<CheckoutItem> CheckoutItems { get; set; }
    public DbSet<PaymentAllocation> PaymentAllocations { get; set; }
    public DbSet<CheckoutAuditLog> CheckoutAuditLog { get; set; }

    public DbSet<Product> Products { get; set; }
    public DbSet<ProductStock> ProductStock { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }

    public DbSet<CommissionRule> CommissionRules { get; set; }
    public DbSet<CommissionEntry> CommissionEntries { get; set; }
    public DbSet<CommissionRuleTier> CommissionRuleTiers { get; set; }

    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupSlot> GroupSlots { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<GroupAuditLog> GroupAuditLog { get; set; }
    public DbSet<WaitlistEntry> WaitlistEntries { get; set; }
    public DbSet<GroupSegmentTemplate> GroupSegmentTemplates { get; set; }
    public DbSet<GroupSegmentTemplateResource> GroupSegmentTemplateResources { get; set; }
    public DbSet<GroupSegmentTemplateEmployee> GroupSegmentTemplateEmployees { get; set; }
    public DbSet<GroupMemberSegmentTemplate> GroupMemberSegmentTemplates { get; set; }

    public DbSet<RosterType> RosterTypes { get; set; }
    public DbSet<RosterEntry> RosterEntries { get; set; }
    public DbSet<RosterAuditLog> RosterAuditLog { get; set; }
    public DbSet<WorkingHoursTemplate> WorkingHoursTemplates { get; set; }
    public DbSet<WorkingHoursInterval> WorkingHoursIntervals { get; set; }
    public DbSet<EmployeeLeaveSettings> EmployeeLeaveSettings { get; set; }
    public DbSet<LeaveFund> LeaveFunds { get; set; }
    public DbSet<LeaveFundUsage> LeaveFundUsages { get; set; }
    public DbSet<CompanyHoliday> CompanyHolidays { get; set; }

    public DbSet<OrganizationBrandingAuditLog> OrganizationBrandingAuditLog { get; set; }
    public DbSet<OrganizationSettings> OrganizationSettings { get; set; }

    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<Notification> Notifications { get; set; }

    public DbSet<GrantGroup> GrantGroups { get; set; }
    public DbSet<GrantGroupGrant> GrantGroupGrants { get; set; }
    public DbSet<UserGrantGroup> UserGrantGroups { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<UserRoleAssignment> UserRoleAssignments { get; set; }

    public DbSet<PlatformAccount> PlatformAccounts { get; set; }

    public DatabaseContext(DbContextOptions options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dunelight");

        modelBuilder.Entity<Organization>().HasKey(o => new { o.Id });
        modelBuilder.Entity<Organization>().HasIndex(o => o.Slug).IsUnique();

        modelBuilder.Entity<OrganizationBrandingAuditLog>().HasKey(a => a.Id);
        modelBuilder.Entity<OrganizationBrandingAuditLog>().HasIndex(a => a.OrganizationId);

        modelBuilder.Entity<OrganizationSettings>().HasKey(s => s.Id);
        modelBuilder.Entity<OrganizationSettings>().HasIndex(s => s.OrganizationId).IsUnique();
        modelBuilder.Entity<OrganizationSettings>()
            .Property(s => s.PackageConsumptionTiming)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PackageConsumptionTiming>(v));
        modelBuilder.Entity<OrganizationSettings>()
            .Property(s => s.MembershipDebtBehavior)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipDebtBehavior>(v));
        modelBuilder.Entity<OrganizationSettings>()
            .Property(s => s.MembershipLimitExceededBehavior)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipLimitExceededBehavior>(v));
        modelBuilder.Entity<OrganizationSettings>()
            .Property(s => s.CommissionLateCancellation)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionLateCancellationMode>(v));

        modelBuilder.Entity<User>().HasKey(u => new { u.Id });
        // ADR-0020 — jedinstvenost emaila je u bazi funkcijski indeks ux_users_organization_email (organization_id,
        // lower(email)), koji EF ne modelira; shema se ionako gradi FluentMigrator migracijama.

        ConfigureCatalog(modelBuilder);
        ConfigureEmployees(modelBuilder);
        ConfigureClients(modelBuilder);
        ConfigureAppointments(modelBuilder);
        ConfigureCheckouts(modelBuilder);
        ConfigureProducts(modelBuilder);
        ConfigureCommissions(modelBuilder);
        ConfigureScheduleBreaks(modelBuilder);
        ConfigureGroups(modelBuilder);
        ConfigureRoster(modelBuilder);
        ConfigurePermissions(modelBuilder);
        ConfigureOutboxAndNotifications(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>().HasKey(l => l.Id);
        modelBuilder.Entity<Company>().HasIndex(l => l.OrganizationId);

        // P1 (ADR-0015) — politike otkazivanja: profil, nepromjenjive verzije, dodjele po scopeu.
        modelBuilder.Entity<CancellationPolicy>().HasKey(p => p.Id);
        modelBuilder.Entity<CancellationPolicy>()
            .HasMany(p => p.Versions)
            .WithOne(v => v.Policy)
            .HasForeignKey(v => v.CancellationPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CancellationPolicyVersion>().HasKey(v => v.Id);
        modelBuilder.Entity<CancellationPolicyVersion>()
            .HasIndex(v => new { v.CancellationPolicyId, v.Version })
            .IsUnique();
        modelBuilder.Entity<CancellationPolicyVersion>()
            .Property(v => v.LateCancellationFeeType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationFeeType>(v));
        modelBuilder.Entity<CancellationPolicyVersion>()
            .Property(v => v.NoShowFeeType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationFeeType>(v));
        modelBuilder.Entity<CancellationPolicyVersion>()
            .Property(v => v.LateCancellationPackageAction)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationPackageAction>(v));
        modelBuilder.Entity<CancellationPolicyVersion>()
            .Property(v => v.NoShowPackageAction)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationPackageAction>(v));
        modelBuilder.Entity<CancellationPolicyVersion>()
            .Property(v => v.LateCancellationMembershipAction)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationMembershipAction>(v));
        modelBuilder.Entity<CancellationPolicyVersion>()
            .Property(v => v.NoShowMembershipAction)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationMembershipAction>(v));
        modelBuilder.Entity<CancellationPolicyAssignment>().HasKey(a => a.Id);
        modelBuilder.Entity<CancellationPolicyAssignment>()
            .HasOne(a => a.Policy)
            .WithMany()
            .HasForeignKey(a => a.CancellationPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CancellationPolicyAssignment>()
            .HasOne(a => a.Company)
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CancellationPolicyAssignment>()
            .HasOne(a => a.Service)
            .WithMany()
            .HasForeignKey(a => a.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // P2 (faza 2A) — planovi članarina: profil, nepromjenjive verzije uvjeta, pokrivene usluge, poslovnice i limiti.
        // Jedinstveni aktivni naziv i unique limita (verzija, usluga ili plan, prozor) su raw SQL indeksi u migraciji.
        modelBuilder.Entity<MembershipPlan>().HasKey(p => p.Id);
        modelBuilder.Entity<MembershipPlan>()
            .HasMany(p => p.Versions)
            .WithOne(v => v.Plan)
            .HasForeignKey(v => v.MembershipPlanId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipPlanVersion>().HasKey(v => v.Id);
        modelBuilder.Entity<MembershipPlanVersion>()
            .HasIndex(v => new { v.MembershipPlanId, v.Version })
            .IsUnique();
        modelBuilder.Entity<MembershipPlanVersion>()
            .Property(v => v.BillingInterval)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipBillingInterval>(v));
        modelBuilder.Entity<MembershipPlanVersion>()
            .Property(v => v.RenewalAnchor)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipRenewalAnchor>(v));
        modelBuilder.Entity<MembershipPlanVersion>()
            .Property(v => v.CompanyScope)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipCompanyScope>(v));
        modelBuilder.Entity<MembershipPlanVersion>()
            .HasMany(v => v.Services)
            .WithOne(s => s.PlanVersion)
            .HasForeignKey(s => s.MembershipPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipPlanVersion>()
            .HasMany(v => v.Companies)
            .WithOne(c => c.PlanVersion)
            .HasForeignKey(c => c.MembershipPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipPlanVersion>()
            .HasMany(v => v.UsageLimits)
            .WithOne(l => l.PlanVersion)
            .HasForeignKey(l => l.MembershipPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        // P2 (2E) — pravila cjenovne pogodnosti verzije plana.
        modelBuilder.Entity<MembershipPlanVersion>()
            .HasMany(v => v.PriceBenefits)
            .WithOne(b => b.PlanVersion)
            .HasForeignKey(b => b.MembershipPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipPlanPriceBenefit>().HasKey(b => b.Id);
        modelBuilder.Entity<MembershipPlanPriceBenefit>()
            .Property(b => b.Scope)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipPriceBenefitScope>(v));
        modelBuilder.Entity<MembershipPlanPriceBenefit>()
            .Property(b => b.Type)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipPriceBenefitType>(v));
        modelBuilder.Entity<MembershipPlanPriceBenefit>()
            .HasOne(b => b.Service)
            .WithMany()
            .HasForeignKey(b => b.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipPlanVersionService>().HasKey(s => s.Id);
        modelBuilder.Entity<MembershipPlanVersionService>()
            .HasIndex(s => new { s.MembershipPlanVersionId, s.ServiceId })
            .IsUnique();
        modelBuilder.Entity<MembershipPlanVersionService>()
            .HasOne(s => s.Service)
            .WithMany()
            .HasForeignKey(s => s.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipPlanVersionCompany>().HasKey(c => c.Id);
        modelBuilder.Entity<MembershipPlanVersionCompany>()
            .HasIndex(c => new { c.MembershipPlanVersionId, c.CompanyId })
            .IsUnique();
        modelBuilder.Entity<MembershipPlanVersionCompany>()
            .HasOne(c => c.Company)
            .WithMany()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipPlanUsageLimit>().HasKey(l => l.Id);
        modelBuilder.Entity<MembershipPlanUsageLimit>()
            .Property(l => l.Window)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipUsageWindow>(v));
        modelBuilder.Entity<MembershipPlanUsageLimit>()
            .HasOne(l => l.Service)
            .WithMany()
            .HasForeignKey(l => l.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Room>().HasKey(r => r.Id);
        modelBuilder.Entity<Room>().HasIndex(r => new { r.OrganizationId, r.CompanyId });
        modelBuilder.Entity<Room>()
            .HasOne(r => r.Company)
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Isti obrazac kao Room: aktivni normalizirani unique naziv po (Organization, Company) je raw SQL indeks
        // (ux_resources_org_company_name_active) u migraciji, a capacity >= 1 je CHECK (ck_resources_capacity_positive).
        modelBuilder.Entity<Resource>().HasKey(r => r.Id);
        modelBuilder.Entity<Resource>().HasIndex(r => new { r.OrganizationId, r.CompanyId });
        modelBuilder.Entity<Resource>()
            .HasOne(r => r.Company)
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Service>().HasKey(s => s.Id);
        modelBuilder.Entity<Service>().HasIndex(s => s.OrganizationId);
        modelBuilder.Entity<Service>()
            .Property(s => s.ExecutionMode)
            .HasConversion(v => v.ToString(), v => Enum.Parse<ServiceExecutionMode>(v));
        // Aktivni normalizirani (trim + case-insensitive) unique naziv po Organization je izražen kao
        // raw SQL expression indeks (ux_services_org_name_active) u migraciji, ne ovdje — EF Core HasIndex
        // ne zna izraziti lower(trim(name)), pa bi deklaracija ovdje bila netočna metapodatka. Isti obrazac
        // kao Company/Room (vidi ServiceHandler.NameExistsAmongActive i AddServiceActiveNameUniqueIndex).

        // ServiceCompany: eksplicitna dostupnost usluge po poslovnici — prazno = dostupna nigdje (vidi
        // domensku napomenu na ServiceCompany). Cascade s obje strane jer je ovo konfiguracijski, ne
        // povijesni zapis — hard-delete inače neiskorištenog Service/Company ne smije biti blokiran samo
        // zbog ovih redaka (vidi ServiceHandler/CompanyHandler.IsReferenced, koji namjerno ne provjerava
        // service_companies).
        modelBuilder.Entity<ServiceCompany>().HasKey(sc => sc.Id);
        modelBuilder.Entity<ServiceCompany>()
            .HasIndex(sc => new { sc.ServiceId, sc.CompanyId })
            .IsUnique();
        modelBuilder.Entity<ServiceCompany>()
            .HasIndex(sc => sc.CompanyId);
        modelBuilder.Entity<ServiceCompany>()
            .HasOne(sc => sc.Service)
            .WithMany()
            .HasForeignKey(sc => sc.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ServiceCompany>()
            .HasOne(sc => sc.Company)
            .WithMany()
            .HasForeignKey(sc => sc.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PriceListItem>().HasKey(p => p.Id);
        modelBuilder.Entity<PriceListItem>()
            .HasIndex(p => new { p.OrganizationId, p.ServiceId, p.PackageId, p.CompanyId });
        modelBuilder.Entity<PriceListItem>()
            .HasOne(p => p.Service)
            .WithMany()
            .HasForeignKey(p => p.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PriceListItem>()
            .HasOne(p => p.Package)
            .WithMany()
            .HasForeignKey(p => p.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PriceListItem>()
            .HasOne(p => p.Company)
            .WithMany()
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PriceListItem>()
            .HasOne(p => p.Employee)
            .WithMany()
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PriceListItemHistory>().HasKey(h => h.Id);
        modelBuilder.Entity<PriceListItemHistory>().HasIndex(h => h.PriceListItemId);

        modelBuilder.Entity<Package>().HasKey(p => p.Id);
        modelBuilder.Entity<Package>()
            .Property(p => p.EntryMode)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PackageEntryMode>(v));
        modelBuilder.Entity<Package>()
            .Property(p => p.ValidityType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PackageValidityType>(v));

        modelBuilder.Entity<PackageServiceItem>().HasKey(ps => ps.Id);
        modelBuilder.Entity<PackageServiceItem>()
            .HasOne(ps => ps.Package)
            .WithMany(p => p.Services)
            .HasForeignKey(ps => ps.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PackageServiceItem>()
            .HasOne(ps => ps.Service)
            .WithMany()
            .HasForeignKey(ps => ps.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureEmployees(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EngagementType>().HasKey(e => e.Id);
        modelBuilder.Entity<EngagementType>()
            .HasIndex(e => new { e.OrganizationId, e.Name })
            .IsUnique()
            .HasFilter("is_active = true");

        modelBuilder.Entity<Employee>().HasKey(e => e.Id);
        modelBuilder.Entity<Employee>().HasIndex(e => e.OrganizationId);
        modelBuilder.Entity<Employee>().HasIndex(e => e.UserId).IsUnique();
        modelBuilder.Entity<Employee>()
            .HasOne(e => e.EngagementType)
            .WithMany()
            .HasForeignKey(e => e.EngagementTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Employee>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EmployeeCompany>().HasKey(el => el.Id);
        modelBuilder.Entity<EmployeeCompany>()
            .HasIndex(el => new { el.EmployeeId, el.CompanyId })
            .IsUnique();
        // Točno jedna matična tvrtka po zaposleniku.
        modelBuilder.Entity<EmployeeCompany>()
            .HasIndex(el => el.EmployeeId)
            .IsUnique()
            .HasFilter("is_primary = true");
        modelBuilder.Entity<EmployeeCompany>()
            .HasOne(el => el.Employee)
            .WithMany(e => e.Companies)
            .HasForeignKey(el => el.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<EmployeeCompany>()
            .HasOne(el => el.Company)
            .WithMany()
            .HasForeignKey(el => el.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EmployeeServiceAssignment>().HasKey(es => es.Id);
        modelBuilder.Entity<EmployeeServiceAssignment>()
            .HasIndex(es => new { es.EmployeeId, es.ServiceId })
            .IsUnique();
        modelBuilder.Entity<EmployeeServiceAssignment>()
            .HasOne(es => es.Employee)
            .WithMany(e => e.Services)
            .HasForeignKey(es => es.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<EmployeeServiceAssignment>()
            .HasOne(es => es.Service)
            .WithMany()
            .HasForeignKey(es => es.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EmployeeAuditLog>().HasKey(a => a.Id);
        modelBuilder.Entity<EmployeeAuditLog>().HasIndex(a => a.EmployeeId);
    }

    private static void ConfigureClients(ModelBuilder modelBuilder)
    {
        // Normalizirana (trim + case-insensitive) uniqueness živi u DB-u kao partial index preko lower(trim(name))
        // (vidi AddClientTagActiveNameUniqueIndex) — isti obrazac kao Company/Service/Room/Package, namjerno bez
        // fluent HasIndex ovdje jer EF ne zna izraziti lower(trim(...)) izraz u indeksu.
        modelBuilder.Entity<ClientTag>().HasKey(t => t.Id);
        modelBuilder.Entity<ClientTag>().HasIndex(t => t.OrganizationId);

        modelBuilder.Entity<Client>().HasKey(c => c.Id);
        modelBuilder.Entity<Client>()
            .HasIndex(c => new { c.OrganizationId, c.MemberNumber })
            .IsUnique();
        modelBuilder.Entity<Client>()
            .HasIndex(c => new { c.OrganizationId, c.LastName, c.FirstName });
        modelBuilder.Entity<Client>()
            .HasOne(c => c.HomeCompany)
            .WithMany()
            .HasForeignKey(c => c.HomeCompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Client>()
            .HasOne(c => c.HomeTrainer)
            .WithMany()
            .HasForeignKey(c => c.HomeTrainerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClientTagAssignment>().HasKey(cta => cta.Id);
        modelBuilder.Entity<ClientTagAssignment>()
            .HasIndex(cta => new { cta.ClientId, cta.TagId })
            .IsUnique();
        modelBuilder.Entity<ClientTagAssignment>()
            .HasOne(cta => cta.Client)
            .WithMany(c => c.Tags)
            .HasForeignKey(cta => cta.ClientId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ClientTagAssignment>()
            .HasOne(cta => cta.Tag)
            .WithMany()
            .HasForeignKey(cta => cta.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClientPackage>().HasKey(cp => cp.Id);
        modelBuilder.Entity<ClientPackage>().HasIndex(cp => new { cp.OrganizationId, cp.ClientId });
        modelBuilder.Entity<ClientPackage>()
            .Property(cp => cp.EntryMode)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PackageEntryMode>(v));
        modelBuilder.Entity<ClientPackage>()
            .Property(cp => cp.ValidityType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PackageValidityType>(v));
        modelBuilder.Entity<ClientPackage>()
            .Property(cp => cp.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<ClientPackageStatus>(v));
        modelBuilder.Entity<ClientPackage>()
            .HasOne(cp => cp.Client)
            .WithMany()
            .HasForeignKey(cp => cp.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientPackage>()
            .HasOne(cp => cp.Package)
            .WithMany()
            .HasForeignKey(cp => cp.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        // Postgres sistemska kolona xmin kao optimistic-concurrency token (bez migracije) — RemainingSharedEntries/
        // RemainingEntries se čitaju-mijenjaju-spremaju u odvojenim DbContextima (vidi ClientPackageService), pa bez
        // ovoga paralelni check-in-i mogu izgubiti jedan od dva dekrementa (lost update).
        modelBuilder.Entity<ClientPackage>()
            .Property(cp => cp.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // P2 (faza 2B) — članstva: uvjeti su nepromjenjiva verzija plana; stanje se izvodi (Utils.MembershipState).
        modelBuilder.Entity<ClientMembership>().HasKey(m => m.Id);
        modelBuilder.Entity<ClientMembership>().HasIndex(m => new { m.OrganizationId, m.ClientId });
        modelBuilder.Entity<ClientMembership>()
            .Property(m => m.FirstSaleCommissionOutcome)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipFirstSaleCommissionOutcome>(v));
        modelBuilder.Entity<ClientMembership>()
            .Property(m => m.SoldVia)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipSaleChannel>(v));
        modelBuilder.Entity<ClientMembership>()
            .Property(m => m.PendingSource)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipPendingChangeSource>(v));
        modelBuilder.Entity<ClientMembership>()
            .Property(m => m.EndReason)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipEndReason>(v));
        modelBuilder.Entity<ClientMembership>()
            .HasOne(m => m.Client)
            .WithMany()
            .HasForeignKey(m => m.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientMembership>()
            .HasOne(m => m.Plan)
            .WithMany()
            .HasForeignKey(m => m.MembershipPlanId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientMembership>()
            .HasOne(m => m.PlanVersion)
            .WithMany()
            .HasForeignKey(m => m.PlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientMembership>()
            .HasOne(m => m.PendingPlanVersion)
            .WithMany()
            .HasForeignKey(m => m.PendingPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientMembership>()
            .HasOne(m => m.DisplacedPlanVersion)
            .WithMany()
            .HasForeignKey(m => m.DisplacedPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientMembership>()
            .HasOne(m => m.PlanUpdateSkippedVersion)
            .WithMany()
            .HasForeignKey(m => m.PlanUpdateSkippedVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientMembership>()
            .HasMany(m => m.Pauses)
            .WithOne(p => p.Membership)
            .HasForeignKey(p => p.ClientMembershipId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipPause>().HasKey(p => p.Id);
        modelBuilder.Entity<MembershipPause>()
            .Property(p => p.Kind)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipPauseKind>(v));
        modelBuilder.Entity<MembershipPause>()
            .Property(p => p.CancellationReason)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipPauseCancellationReason>(v));
        // P2 (2C) — otvoreni periodi i zaduženja; stavka checkouta tipa MembershipCharge plaća zaduženje.
        modelBuilder.Entity<ClientMembershipPeriod>().HasKey(p => p.Id);
        modelBuilder.Entity<ClientMembershipPeriod>()
            .HasIndex(p => new { p.ClientMembershipId, p.StartsOn })
            .IsUnique();
        modelBuilder.Entity<ClientMembershipPeriod>()
            .HasOne(p => p.Membership)
            .WithMany(m => m.Periods)
            .HasForeignKey(p => p.ClientMembershipId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientMembershipPeriod>()
            .HasOne(p => p.PlanVersion)
            .WithMany()
            .HasForeignKey(p => p.PlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipCharge>().HasKey(c => c.Id);
        modelBuilder.Entity<MembershipCharge>()
            .Property(c => c.Kind)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipChargeKind>(v));
        modelBuilder.Entity<MembershipCharge>()
            .Property(c => c.Lifecycle)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipChargeLifecycle>(v));
        modelBuilder.Entity<MembershipCharge>()
            .Property(c => c.SettlementStatus)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipChargeSettlementStatus>(v));
        modelBuilder.Entity<MembershipCharge>()
            .HasOne(c => c.Membership)
            .WithMany(m => m.Charges)
            .HasForeignKey(c => c.ClientMembershipId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MembershipCharge>()
            .HasOne(c => c.Period)
            .WithMany()
            .HasForeignKey(c => c.PeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClientMembershipAuditLog>().HasKey(a => a.Id);
        modelBuilder.Entity<ClientMembershipAuditLog>().HasIndex(a => a.ClientMembershipId);
        // Veza bez navigacije: EF mora znati za FK da zapis povijesti upiše NAKON novog članstva u istoj transakciji.
        modelBuilder.Entity<ClientMembershipAuditLog>()
            .HasOne<ClientMembership>()
            .WithMany()
            .HasForeignKey(a => a.ClientMembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClientPackageServiceEntry>().HasKey(e => e.Id);
        modelBuilder.Entity<ClientPackageServiceEntry>()
            .HasIndex(e => new { e.ClientPackageId, e.ServiceId })
            .IsUnique();
        modelBuilder.Entity<ClientPackageServiceEntry>()
            .HasOne(e => e.ClientPackage)
            .WithMany(cp => cp.ServiceEntries)
            .HasForeignKey(e => e.ClientPackageId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ClientPackageServiceEntry>()
            .HasOne(e => e.Service)
            .WithMany()
            .HasForeignKey(e => e.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientPackageServiceEntry>()
            .Property(e => e.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }

    private static void ConfigureAppointments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
        // Phase D3A: izvršni okvir (usluga/zaposlenik/prostorija/vrijeme) živi isključivo na AppointmentSegment —
        // appointments više nema te stupce ni indekse/FK-ove nad njima.
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Form)
            .HasConversion(v => v.ToString(), v => Enum.Parse<AppointmentForm>(v));
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<AppointmentStatus>(v));
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Company)
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Group)
            .WithMany()
            .HasForeignKey(a => a.GroupId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.GroupSlot)
            .WithMany()
            .HasForeignKey(a => a.GroupSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase D1 — segmenti termina (additivno, nije autoritativno). Segment je dio termina (Cascade kao Booking);
        // katalog/zaposlenik su Restrict. CHECK ograničenja vremena/količine i indeksi su u migraciji
        // (Migration_2026_10_07_AppointmentSegments).
        modelBuilder.Entity<AppointmentSegment>().HasKey(s => s.Id);
        modelBuilder.Entity<AppointmentSegment>()
            .HasOne(s => s.Appointment)
            .WithMany(a => a.Segments)
            .HasForeignKey(s => s.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AppointmentSegment>()
            .HasOne(s => s.Service)
            .WithMany()
            .HasForeignKey(s => s.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AppointmentSegment>()
            .HasOne(s => s.Room)
            .WithMany()
            .HasForeignKey(s => s.RoomId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AppointmentSegment>()
            .Property(s => s.PricingMode)
            .HasConversion(v => v.ToString(), v => Enum.Parse<SegmentPricingMode>(v));
        modelBuilder.Entity<AppointmentSegment>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(s => s.PricingEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AppointmentSegment>()
            .HasOne(s => s.GroupSegmentTemplate)
            .WithMany()
            .HasForeignKey(s => s.GroupSegmentTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AppointmentSegmentEmployee>().HasKey(e => new { e.AppointmentSegmentId, e.EmployeeId });
        modelBuilder.Entity<AppointmentSegmentEmployee>()
            .HasOne(e => e.Segment)
            .WithMany(s => s.Employees)
            .HasForeignKey(e => e.AppointmentSegmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AppointmentSegmentEmployee>()
            .HasOne(e => e.Employee)
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AppointmentSegmentResource>().HasKey(r => new { r.AppointmentSegmentId, r.ResourceId });
        modelBuilder.Entity<AppointmentSegmentResource>()
            .HasOne(r => r.Segment)
            .WithMany(s => s.Resources)
            .HasForeignKey(r => r.AppointmentSegmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AppointmentSegmentResource>()
            .HasOne(r => r.Resource)
            .WithMany()
            .HasForeignKey(r => r.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase D2 — sudjelovanja (additivno, nije autoritativno). Restrict prema Bookingu i segmentu: povijest
        // sudjelovanja se nikad tiho ne briše kaskadom (brisanje termina/bookinga/segmenta s njom je blokirano).
        modelBuilder.Entity<BookingSegmentParticipation>().HasKey(p => p.Id);
        modelBuilder.Entity<BookingSegmentParticipation>()
            .HasIndex(p => new { p.BookingId, p.AppointmentSegmentId })
            .IsUnique();
        modelBuilder.Entity<BookingSegmentParticipation>()
            .Property(p => p.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<ParticipationStatus>(v));
        modelBuilder.Entity<BookingSegmentParticipation>()
            .Property(p => p.BaseAmountSource)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PriceSource>(v));
        modelBuilder.Entity<BookingSegmentParticipation>()
            .Property(p => p.PricingMode)
            .HasConversion(v => v.ToString(), v => Enum.Parse<SegmentPricingMode>(v));
        modelBuilder.Entity<BookingSegmentParticipation>()
            .Property(p => p.CancellationInitiator)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationInitiator>(v));
        modelBuilder.Entity<BookingSegmentParticipation>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.PricingEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BookingSegmentParticipation>()
            .HasOne(p => p.Booking)
            .WithMany(b => b.Participations)
            .HasForeignKey(p => p.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BookingSegmentParticipation>()
            .HasOne(p => p.Segment)
            .WithMany(s => s.Participations)
            .HasForeignKey(p => p.AppointmentSegmentId)
            .OnDelete(DeleteBehavior.Restrict);
        // Phase D3B3A: povijest potrošnje paketa se učitava uvijek sa sudjelovanjem (pa i s Bookingom), da nijedan upit
        // ne vidi sudjelovanje "bez paketa" samo zato što je zaboravio Include.
        modelBuilder.Entity<BookingSegmentParticipation>().Navigation(p => p.PackageConsumptions).AutoInclude();
        // P1: dug sudjelovanja ovisi o aktivnoj posljedici politike — učitava se uvijek sa sudjelovanjem (kao potrošnja paketa).
        modelBuilder.Entity<BookingSegmentParticipation>().Navigation(p => p.PolicyConsequences).AutoInclude();

        // P1 (ADR-0017) — ledger posljedica politike. Restrict: povijest se nikad ne briše kaskadom. Unique (sudjelovanje,
        // SourceVersion) i najviše jedna Active posljedica po sudjelovanju.
        modelBuilder.Entity<ParticipationPolicyConsequence>().HasKey(c => c.Id);
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .HasIndex(c => new { c.BookingSegmentParticipationId, c.SourceVersion })
            .IsUnique();
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .Property(c => c.Event)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PolicyConsequenceEvent>(v));
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .Property(c => c.FeeType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationFeeType>(v));
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .Property(c => c.PackageAction)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationPackageAction>(v));
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .Property(c => c.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PolicyConsequenceStatus>(v));
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .HasOne(c => c.Participation)
            .WithMany(p => p.PolicyConsequences)
            .HasForeignKey(c => c.BookingSegmentParticipationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .HasOne<ClientPackage>()
            .WithMany()
            .HasForeignKey(c => c.ClientPackageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .HasOne<CancellationPolicy>()
            .WithMany()
            .HasForeignKey(c => c.CancellationPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ParticipationPolicyConsequence>()
            .Property(c => c.MembershipAction)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CancellationMembershipAction>(v));

        // P2 (2D) — ledger korištenja članarine i projekcija pokrića sudjelovanja. Namjerno BEZ relacije prema sudjelovanju u
        // bazi (vidi migraciju 2D); projekcija je u modelu 1:1 navigacija sudjelovanja (AutoInclude), bez kaskade.
        modelBuilder.Entity<MembershipUsage>().HasKey(u => u.Id);
        modelBuilder.Entity<MembershipUsage>()
            .Property(u => u.EntryType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipUsageEntryType>(v));
        modelBuilder.Entity<MembershipUsage>()
            .Property(u => u.ReleaseReason)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipUsageReleaseReason>(v));
        modelBuilder.Entity<ParticipationMembershipCoverage>().HasKey(c => c.ParticipationId);
        modelBuilder.Entity<ParticipationMembershipCoverage>()
            .Property(c => c.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipCoverageStatus>(v));
        modelBuilder.Entity<ParticipationMembershipCoverage>()
            .Property(c => c.Reason)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipCoverageReason>(v));
        modelBuilder.Entity<ParticipationMembershipCoverage>()
            .Property(c => c.ChangedByEvent)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipCoverageEvent>(v));
        modelBuilder.Entity<ParticipationMembershipCoverage>()
            .Property(c => c.LimitWindow)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipUsageWindow>(v));
        modelBuilder.Entity<ParticipationMembershipCoverage>()
            .Property(c => c.LastPriceChangeEvent)
            .HasConversion(v => v.ToString(), v => Enum.Parse<MembershipCoverageEvent>(v));
        modelBuilder.Entity<ParticipationMembershipCoverage>()
            .Property(c => c.PriceProtectedReason)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PriceProtectionReason>(v));
        // P2 (2E) — prilagodba cijene sudjelovanja (snapshot pravila i evaluacija su jsonb).
        modelBuilder.Entity<BookingSegmentParticipation>()
            .Property(p => p.AdjustmentType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PriceAdjustmentType>(v));
        modelBuilder.Entity<BookingSegmentParticipation>().Property(p => p.AdjustmentRuleSnapshot).HasColumnType("jsonb");
        modelBuilder.Entity<BookingSegmentParticipation>().Property(p => p.AdjustmentEvaluation).HasColumnType("jsonb");
        modelBuilder.Entity<BookingSegmentParticipation>()
            .HasOne(p => p.MembershipCoverage)
            .WithOne()
            .HasForeignKey<ParticipationMembershipCoverage>(c => c.ParticipationId)
            .OnDelete(DeleteBehavior.ClientNoAction);
        modelBuilder.Entity<BookingSegmentParticipation>().Navigation(p => p.MembershipCoverage).AutoInclude();
        modelBuilder.Entity<GroupOccurrenceMembershipSkip>().HasKey(s => s.Id);
        modelBuilder.Entity<GroupOccurrenceMembershipSkip>()
            .Property(s => s.Resolution)
            .HasConversion(v => v.ToString(), v => Enum.Parse<GroupMembershipSkipResolution>(v));

        // Phase D3B3A — PackageConsumption ledger. Restrict prema svemu: povijest potrošnje se nikad ne briše kaskadom.
        // Najviše jedan aktivan (Consumed) zapis po sudjelovanju — idempotentnost potrošnje i poništenja u bazi.
        modelBuilder.Entity<PackageConsumption>().HasKey(c => c.Id);
        modelBuilder.Entity<PackageConsumption>().HasIndex(c => c.ClientPackageId);
        modelBuilder.Entity<PackageConsumption>()
            .HasIndex(c => c.BookingSegmentParticipationId)
            .HasDatabaseName("ux_package_consumptions_active_participation")
            .HasFilter("status = 'Consumed'")
            .IsUnique();
        modelBuilder.Entity<PackageConsumption>()
            .Property(c => c.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PackageConsumptionStatus>(v));
        modelBuilder.Entity<PackageConsumption>()
            .Property(c => c.Trigger)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PackageConsumptionTrigger>(v));
        modelBuilder.Entity<PackageConsumption>()
            .HasOne<ParticipationPolicyConsequence>()
            .WithMany()
            .HasForeignKey(c => c.ParticipationPolicyConsequenceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PackageConsumption>()
            .Property(c => c.ReversalReason)
            .HasConversion(
                v => v == null ? null : v.ToString(),
                v => v == null ? (PackageConsumptionReversalReason?)null : Enum.Parse<PackageConsumptionReversalReason>(v));
        modelBuilder.Entity<PackageConsumption>()
            .HasOne(c => c.ClientPackage)
            .WithMany()
            .HasForeignKey(c => c.ClientPackageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PackageConsumption>()
            .HasOne(c => c.Participation)
            .WithMany(p => p.PackageConsumptions)
            .HasForeignKey(c => c.BookingSegmentParticipationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PackageConsumption>()
            .HasOne(c => c.Service)
            .WithMany()
            .HasForeignKey(c => c.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>().HasKey(b => b.Id);
        // Phase D3B1: životni ciklus Bookinga živi na njegovom (jedinom) sudjelovanju — učitava se uvijek s Bookingom, pa
        // nijedan upit ne može "zaboraviti" Include i tiho vidjeti Booking bez statusa (BookingParticipations bi bacio).
        modelBuilder.Entity<Booking>().Navigation(b => b.Participations).AutoInclude();
        modelBuilder.Entity<Booking>().HasIndex(b => new { b.OrganizationId, b.ClientId });
        modelBuilder.Entity<Booking>()
            .HasIndex(b => new { b.AppointmentId, b.ClientId })
            .IsUnique();
        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Appointment)
            .WithMany(a => a.Bookings)
            .HasForeignKey(b => b.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Client)
            .WithMany()
            .HasForeignKey(b => b.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Payment>().HasKey(p => p.Id);
        modelBuilder.Entity<Payment>().HasIndex(p => new { p.OrganizationId, p.CheckoutId });
        modelBuilder.Entity<Payment>()
            .Property(p => p.Method)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PaymentMethod>(v));
        modelBuilder.Entity<Payment>()
            .Property(p => p.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<PaymentStatus>(v));
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Checkout)
            .WithMany(c => c.Payments)
            .HasForeignKey(p => p.CheckoutId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AppointmentAuditLog>().HasKey(a => a.Id);
        modelBuilder.Entity<AppointmentAuditLog>().HasIndex(a => a.AppointmentId);
        modelBuilder.Entity<AppointmentAuditLog>().HasIndex(a => a.BookingId);
        modelBuilder.Entity<AppointmentAuditLog>().HasIndex(a => a.WaitlistEntryId);
    }

    private static void ConfigureCheckouts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Checkout>().HasKey(c => c.Id);
        modelBuilder.Entity<Checkout>().HasIndex(c => new { c.OrganizationId, c.ClientId });
        modelBuilder.Entity<Checkout>().HasIndex(c => new { c.OrganizationId, c.CompanyId, c.Status });
        modelBuilder.Entity<Checkout>()
            .Property(c => c.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CheckoutStatus>(v));
        modelBuilder.Entity<Checkout>()
            .HasOne(c => c.Company)
            .WithMany()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Checkout>()
            .HasOne(c => c.Client)
            .WithMany()
            .HasForeignKey(c => c.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        // Vlastita stavka nosi točno jedan tipizirani subjekt (BookingId XOR PackageId, prema Type) — CHECK
        // constraint ide raw SQL-om u migraciji (isto obrazac kao PriceListItem.ServiceId/PackageId), ovdje
        // samo FK/navigacije.
        modelBuilder.Entity<CheckoutItem>().HasKey(ci => ci.Id);
        modelBuilder.Entity<CheckoutItem>().HasIndex(ci => new { ci.OrganizationId, ci.CheckoutId });
        // Djelomični unique indeks (locks_participation = true) sprječava isto sudjelovanje u dva istovremeno Open
        // checkouta — vidi CheckoutItem.LocksParticipation domensku napomenu i spec section 29/60.
        modelBuilder.Entity<CheckoutItem>()
            .HasIndex(ci => ci.BookingSegmentParticipationId)
            .IsUnique()
            .HasFilter("locks_participation = true");
        modelBuilder.Entity<CheckoutItem>()
            .Property(ci => ci.Type)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CheckoutItemType>(v));
        modelBuilder.Entity<CheckoutItem>()
            .HasOne(ci => ci.Checkout)
            .WithMany(c => c.Items)
            .HasForeignKey(ci => ci.CheckoutId)
            .OnDelete(DeleteBehavior.Cascade);
        // Phase D3B3B: stavka usluge namiruje SUDJELOVANJE — Restrict: povijest namirenja nikad ne nestaje kaskadom.
        modelBuilder.Entity<CheckoutItem>()
            .HasOne(ci => ci.Participation)
            .WithMany(p => p.CheckoutItems)
            .HasForeignKey(ci => ci.BookingSegmentParticipationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CheckoutItem>()
            .HasOne(ci => ci.Package)
            .WithMany()
            .HasForeignKey(ci => ci.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CheckoutItem>()
            .HasOne(ci => ci.Product)
            .WithMany()
            .HasForeignKey(ci => ci.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CheckoutItem>()
            .HasOne(ci => ci.ClientPackage)
            .WithMany()
            .HasForeignKey(ci => ci.ClientPackageId)
            .OnDelete(DeleteBehavior.Restrict);
        // P2 (2C) — stavka plaćanja zaduženja članarine; djelomični unique (locks_membership_charge) kao kod sudjelovanja.
        modelBuilder.Entity<CheckoutItem>()
            .HasOne(ci => ci.MembershipCharge)
            .WithMany(c => c.CheckoutItems)
            .HasForeignKey(ci => ci.MembershipChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PaymentAllocation>().HasKey(a => a.Id);
        modelBuilder.Entity<PaymentAllocation>().HasIndex(a => a.PaymentId);
        modelBuilder.Entity<PaymentAllocation>().HasIndex(a => a.CheckoutItemId);
        modelBuilder.Entity<PaymentAllocation>()
            .HasOne(a => a.Payment)
            .WithMany(p => p.Allocations)
            .HasForeignKey(a => a.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PaymentAllocation>()
            .HasOne(a => a.CheckoutItem)
            .WithMany(ci => ci.Allocations)
            .HasForeignKey(a => a.CheckoutItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CheckoutAuditLog>().HasKey(a => a.Id);
        modelBuilder.Entity<CheckoutAuditLog>().HasIndex(a => a.CheckoutId);
    }

    private static void ConfigureProducts(ModelBuilder modelBuilder)
    {
        // Aktivni normalizirani (trim + case-insensitive) unique naziv i opcionalni normalizirani unique SKU
        // po Organization su izraženi kao raw SQL expression indeksi u migraciji (ux_products_org_name_active/
        // ux_products_org_sku) — isti obrazac kao Service/Company/Room (vidi ProductHandler.NameExistsAmongActive/
        // SkuExists), EF fluent HasIndex ne zna izraziti lower(trim(...)).
        modelBuilder.Entity<Product>().HasKey(p => p.Id);
        modelBuilder.Entity<Product>().HasIndex(p => p.OrganizationId);

        // Točno jedan ProductStock redak po (ProductId, CompanyId) — vidi ProductStock.cs klasnu napomenu.
        // Unique indeks je ujedno ON CONFLICT cilj u ProductStockHandler.GetOrCreateForUpdate.
        modelBuilder.Entity<ProductStock>().HasKey(s => s.Id);
        modelBuilder.Entity<ProductStock>()
            .HasIndex(s => new { s.ProductId, s.CompanyId })
            .IsUnique();
        modelBuilder.Entity<ProductStock>()
            .HasIndex(s => new { s.OrganizationId, s.CompanyId });
        modelBuilder.Entity<ProductStock>()
            .HasOne(s => s.Product)
            .WithMany()
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductStock>()
            .HasOne(s => s.Company)
            .WithMany()
            .HasForeignKey(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StockMovement>().HasKey(m => m.Id);
        modelBuilder.Entity<StockMovement>()
            .HasIndex(m => new { m.OrganizationId, m.ProductId, m.CreatedAt });
        modelBuilder.Entity<StockMovement>()
            .Property(m => m.Type)
            .HasConversion(v => v.ToString(), v => Enum.Parse<StockMovementType>(v));
        // Djelomični unique indeks (type = 'Sale') sprječava dvostruki decrement iste CheckoutItem stavke —
        // izražen kao raw SQL partial index u migraciji (isti obrazac kao ux_checkout_items_locks_booking),
        // ovdje samo FK/navigacija.
        modelBuilder.Entity<StockMovement>()
            .HasOne(m => m.Product)
            .WithMany()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockMovement>()
            .HasOne(m => m.Company)
            .WithMany()
            .HasForeignKey(m => m.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockMovement>()
            .HasOne(m => m.RelatedCompany)
            .WithMany()
            .HasForeignKey(m => m.RelatedCompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockMovement>()
            .HasOne(m => m.CheckoutItem)
            .WithMany()
            .HasForeignKey(m => m.CheckoutItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCommissions(ModelBuilder modelBuilder)
    {
        // Vlasnik predmeta je točno jedno od Service/Product/PackageId prema SubjectType (CHECK constraint u
        // migraciji, isto obrazac kao CheckoutItem) — najviše jedno AKTIVNO pravilo po Employee+Subject je
        // djelomični unique indeks preko COALESCE u raw SQL-u (EF fluent HasIndex ne zna izraziti COALESCE),
        // ovdje samo FK/navigacije. Sve tri Restrict jer su to konfiguracijski katalog-referenciraju retci čije
        // hard-delete guardove (IsReferenced) proširuje ovaj modul (vidi ServiceHandler/ProductHandler/
        // PackageHandler/EmployeeHandler).
        modelBuilder.Entity<CommissionRule>().HasKey(r => r.Id);
        modelBuilder.Entity<CommissionRule>().HasIndex(r => new { r.OrganizationId, r.EmployeeId });
        modelBuilder.Entity<CommissionRule>()
            .Property(r => r.SubjectType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionSubjectType>(v));
        modelBuilder.Entity<CommissionRule>()
            .Property(r => r.CalculationType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionCalculationType>(v));
        modelBuilder.Entity<CommissionRule>()
            .HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionRule>()
            .HasOne(r => r.Service)
            .WithMany()
            .HasForeignKey(r => r.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionRule>()
            .HasOne(r => r.Product)
            .WithMany()
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionRule>()
            .HasOne(r => r.Package)
            .WithMany()
            .HasForeignKey(r => r.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionRule>()
            .Property(r => r.Kind)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionRuleKind>(v));
        modelBuilder.Entity<CommissionRule>()
            .HasOne(r => r.MembershipPlan)
            .WithMany()
            .HasForeignKey(r => r.MembershipPlanId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionRule>()
            .HasMany(r => r.Tiers)
            .WithOne()
            .HasForeignKey(t => t.CommissionRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // P2 (2F, Vagaro) — razine općeg pravila; unique (pravilo, prag) je u migraciji.
        modelBuilder.Entity<CommissionRuleTier>().HasKey(t => t.Id);
        modelBuilder.Entity<CommissionRuleTier>()
            .Property(t => t.CalculationType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionCalculationType>(v));

        // Izvor je točno jedno od Booking(+Appointment)/Appointment(samo, GroupService)/CheckoutItem prema
        // SourceType (CHECK constraint u migraciji) — idempotencija preko tri unique indeksa (dva standardna
        // nullable, jedan djelomični za GroupService, vidi migraciju/CommissionEntry.cs). Appointment/Booking su
        // Cascade (isto ponašanje kao AppointmentAuditLog — "isti dan" hard-delete termina briše i commission
        // trag), Employee/Company/CommissionRule/CheckoutItem su Restrict (povijesni/katalog retci se ne
        // hard-brišu ispod commission povijesti bez eksplicitnog guarda, vidi CommissionRuleHandler.IsReferenced/
        // CompanyHandler.IsReferenced/EmployeeHandler.HasBusinessReferences).
        modelBuilder.Entity<CommissionEntry>().HasKey(e => e.Id);
        modelBuilder.Entity<CommissionEntry>().HasIndex(e => new { e.OrganizationId, e.EmployeeId, e.EarnedAt });
        modelBuilder.Entity<CommissionEntry>().HasIndex(e => new { e.OrganizationId, e.CompanyId, e.EarnedAt });
        modelBuilder.Entity<CommissionEntry>()
            .Property(e => e.SourceType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionSourceType>(v));
        modelBuilder.Entity<CommissionEntry>()
            .Property(e => e.CalculationType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionCalculationType>(v));
        modelBuilder.Entity<CommissionEntry>()
            .Property(e => e.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionEntryStatus>(v));
        modelBuilder.Entity<CommissionEntry>()
            .HasOne(e => e.Employee)
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionEntry>()
            .HasOne(e => e.Company)
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionEntry>()
            .HasOne(e => e.CommissionRule)
            .WithMany()
            .HasForeignKey(e => e.CommissionRuleId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionEntry>()
            .HasOne(e => e.Appointment)
            .WithMany()
            .HasForeignKey(e => e.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CommissionEntry>()
            .HasOne(e => e.Booking)
            .WithMany()
            .HasForeignKey(e => e.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CommissionEntry>()
            .HasOne<AppointmentSegment>()
            .WithMany()
            .HasForeignKey(e => e.AppointmentSegmentId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<CommissionEntry>()
            .HasOne(e => e.CheckoutItem)
            .WithMany()
            .HasForeignKey(e => e.CheckoutItemId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionEntry>()
            .Property(e => e.PaymentSource)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionPaymentSource>(v));
        modelBuilder.Entity<CommissionEntry>()
            .Property(e => e.AppliedRuleScope)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CommissionRuleScope>(v));
        modelBuilder.Entity<CommissionEntry>().Property(e => e.RuleEvaluation).HasColumnType("jsonb");
        modelBuilder.Entity<CommissionEntry>()
            .HasOne<ClientMembership>()
            .WithMany()
            .HasForeignKey(e => e.ClientMembershipId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CommissionEntry>()
            .HasOne<ParticipationPolicyConsequence>()
            .WithMany()
            .HasForeignKey(e => e.ParticipationPolicyConsequenceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureScheduleBreaks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScheduleBreak>().HasKey(b => b.Id);
        modelBuilder.Entity<ScheduleBreak>().HasIndex(b => new { b.OrganizationId, b.EmployeeId, b.StartsAt });
        modelBuilder.Entity<ScheduleBreak>().HasIndex(b => new { b.OrganizationId, b.CompanyId, b.StartsAt });
        modelBuilder.Entity<ScheduleBreak>()
            .HasOne(b => b.Employee)
            .WithMany()
            .HasForeignKey(b => b.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ScheduleBreak>()
            .HasOne(b => b.Company)
            .WithMany()
            .HasForeignKey(b => b.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureGroups(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Group>().HasKey(g => g.Id);
        modelBuilder.Entity<Group>().HasIndex(g => g.OrganizationId);
        // Phase M1F.1: revizija članstva je isključivo vlasništvo atomičnog SQL inkrementa (GroupService) — EF je nikad ne
        // upisuje (ni Update(graph) zastarjele grupe ne smije vratiti reviziju unatrag).
        modelBuilder.Entity<Group>().Property(g => g.MembershipVersion).HasDefaultValue(0L);
        modelBuilder.Entity<Group>().Property(g => g.MembershipVersion).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        modelBuilder.Entity<Group>().Property(g => g.MembershipVersion).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        modelBuilder.Entity<Group>()
            .HasOne(g => g.Company)
            .WithMany()
            .HasForeignKey(g => g.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase M1F — predlošci segmenata (CHECK-ovi, kompozitni FK-ovi odabira i jedinstvenosti su u migraciji
        // Migration_2026_10_20_GroupSegmentTemplates).
        modelBuilder.Entity<GroupSegmentTemplate>().HasKey(t => t.Id);
        modelBuilder.Entity<GroupSegmentTemplate>()
            .HasOne(t => t.Group)
            .WithMany(g => g.SegmentTemplates)
            .HasForeignKey(t => t.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GroupSegmentTemplate>()
            .HasOne(t => t.Service)
            .WithMany()
            .HasForeignKey(t => t.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GroupSegmentTemplate>()
            .HasOne(t => t.Room)
            .WithMany()
            .HasForeignKey(t => t.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GroupSegmentTemplate>()
            .Property(t => t.PricingMode)
            .HasConversion(v => v.ToString(), v => Enum.Parse<SegmentPricingMode>(v));
        modelBuilder.Entity<GroupSegmentTemplate>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(t => t.PricingEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase M1G — osoblje predloška (Migration_2026_10_22_MultiEmployeeSegments).
        modelBuilder.Entity<GroupSegmentTemplateEmployee>().HasKey(e => new { e.GroupSegmentTemplateId, e.EmployeeId });
        modelBuilder.Entity<GroupSegmentTemplateEmployee>()
            .HasOne(e => e.Template)
            .WithMany(t => t.Employees)
            .HasForeignKey(e => e.GroupSegmentTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GroupSegmentTemplateEmployee>()
            .HasOne(e => e.Employee)
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GroupSegmentTemplateResource>().HasKey(r => new { r.GroupSegmentTemplateId, r.ResourceId });
        modelBuilder.Entity<GroupSegmentTemplateResource>()
            .HasOne(r => r.Template)
            .WithMany(t => t.Resources)
            .HasForeignKey(r => r.GroupSegmentTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GroupSegmentTemplateResource>()
            .HasOne(r => r.Resource)
            .WithMany()
            .HasForeignKey(r => r.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GroupMemberSegmentTemplate>().HasKey(x => new { x.GroupMemberId, x.GroupSegmentTemplateId });
        modelBuilder.Entity<GroupMemberSegmentTemplate>()
            .HasOne(x => x.Member)
            .WithMany(m => m.SegmentTemplates)
            .HasForeignKey(x => x.GroupMemberId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GroupMemberSegmentTemplate>()
            .HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.GroupSegmentTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GroupSlot>().HasKey(gs => gs.Id);
        modelBuilder.Entity<GroupSlot>().HasIndex(gs => new { gs.GroupId, gs.IsActive });
        modelBuilder.Entity<GroupSlot>()
            .Property(gs => gs.DayOfWeek)
            .HasConversion(v => v.ToString(), v => Enum.Parse<DayOfWeek>(v));
        modelBuilder.Entity<GroupSlot>()
            .HasOne(gs => gs.Group)
            .WithMany(g => g.Slots)
            .HasForeignKey(gs => gs.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GroupMember>().HasKey(gm => gm.Id);
        modelBuilder.Entity<GroupMember>()
            .HasIndex(gm => new { gm.GroupId, gm.ClientId })
            .IsUnique()
            .HasFilter("is_active = true");
        modelBuilder.Entity<GroupMember>()
            .HasOne(gm => gm.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(gm => gm.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GroupMember>()
            .HasOne(gm => gm.Client)
            .WithMany()
            .HasForeignKey(gm => gm.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GroupAuditLog>().HasKey(a => a.Id);
        modelBuilder.Entity<GroupAuditLog>().HasIndex(a => a.GroupId);

        modelBuilder.Entity<WaitlistEntry>().HasKey(w => w.Id);
        modelBuilder.Entity<WaitlistEntry>().HasIndex(w => new { w.AppointmentId, w.Status, w.JoinedAt });
        modelBuilder.Entity<WaitlistEntry>()
            .HasIndex(w => new { w.AppointmentSegmentId, w.ClientId })
            .IsUnique()
            .HasFilter("status = 'Waiting'");
        modelBuilder.Entity<WaitlistEntry>()
            .HasOne(w => w.Segment)
            .WithMany()
            .HasForeignKey(w => w.AppointmentSegmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WaitlistEntry>()
            .Property(w => w.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<WaitlistEntryStatus>(v));
        modelBuilder.Entity<WaitlistEntry>()
            .HasOne(w => w.Appointment)
            .WithMany()
            .HasForeignKey(w => w.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WaitlistEntry>()
            .HasOne(w => w.Client)
            .WithMany()
            .HasForeignKey(w => w.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WaitlistEntry>()
            .HasOne(w => w.PromotedBooking)
            .WithMany()
            .HasForeignKey(w => w.PromotedBookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRoster(ModelBuilder modelBuilder)
    {
        // Normalizirana (trim + case-insensitive) uniqueness živi u DB-u kao partial index preko lower(trim(name))
        // (vidi AddRosterTypeActiveNameUniqueIndex) — isti obrazac kao Company/Service/Room/Package/ClientTag,
        // namjerno bez fluent HasIndex ovdje jer EF ne zna izraziti lower(trim(...)) izraz u indeksu.
        modelBuilder.Entity<RosterType>().HasKey(t => t.Id);
        modelBuilder.Entity<RosterType>().HasIndex(t => t.OrganizationId);

        modelBuilder.Entity<RosterEntry>().HasKey(e => e.Id);
        modelBuilder.Entity<RosterEntry>().HasIndex(e => new { e.OrganizationId, e.EmployeeId, e.DateFrom });
        modelBuilder.Entity<RosterEntry>()
            .HasOne(e => e.Employee)
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RosterEntry>()
            .HasOne(e => e.RosterType)
            .WithMany()
            .HasForeignKey(e => e.RosterTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Namjerno bez FK/navigacije na RosterEntry — audit mora preživjeti brisanje retka (vidi RosterAuditLog).
        modelBuilder.Entity<RosterAuditLog>().HasKey(a => a.Id);
        modelBuilder.Entity<RosterAuditLog>().HasIndex(a => a.RosterEntryId);

        // Vlasnik je točno jedno od EmployeeId/CompanyId (isto kao PriceListItem.ServiceId/PackageId) — CHECK
        // constraint ide raw SQL-om u migraciji, ovdje samo djelomični unique indeksi (singleton po vlasniku).
        modelBuilder.Entity<WorkingHoursTemplate>().HasKey(t => t.Id);
        modelBuilder.Entity<WorkingHoursTemplate>()
            .HasIndex(t => t.EmployeeId).IsUnique().HasFilter("employee_id IS NOT NULL");
        modelBuilder.Entity<WorkingHoursTemplate>()
            .HasIndex(t => t.CompanyId).IsUnique().HasFilter("company_id IS NOT NULL");
        modelBuilder.Entity<WorkingHoursTemplate>()
            .Property(t => t.CycleType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<WorkingHoursCycleType>(v));
        modelBuilder.Entity<WorkingHoursTemplate>()
            .HasOne(t => t.Employee)
            .WithMany()
            .HasForeignKey(t => t.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WorkingHoursTemplate>()
            .HasOne(t => t.Company)
            .WithMany()
            .HasForeignKey(t => t.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkingHoursInterval>().HasKey(i => i.Id);
        modelBuilder.Entity<WorkingHoursInterval>().HasIndex(i => i.WorkingHoursTemplateId);
        modelBuilder.Entity<WorkingHoursInterval>()
            .Property(i => i.DayOfWeek)
            .HasConversion(v => v.ToString(), v => Enum.Parse<DayOfWeek>(v));
        modelBuilder.Entity<WorkingHoursInterval>()
            .HasOne(i => i.WorkingHoursTemplate)
            .WithMany(t => t.Intervals)
            .HasForeignKey(i => i.WorkingHoursTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EmployeeLeaveSettings>().HasKey(s => s.Id);
        modelBuilder.Entity<EmployeeLeaveSettings>().HasIndex(s => s.EmployeeId).IsUnique();
        modelBuilder.Entity<EmployeeLeaveSettings>()
            .HasOne(s => s.Employee)
            .WithMany()
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LeaveFund>().HasKey(f => f.Id);
        modelBuilder.Entity<LeaveFund>()
            .HasIndex(f => new { f.OrganizationId, f.EmployeeId, f.FundYear })
            .IsUnique();
        modelBuilder.Entity<LeaveFund>()
            .HasOne(f => f.Employee)
            .WithMany()
            .HasForeignKey(f => f.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        // Postgres sistemska kolona xmin kao optimistic-concurrency token (bez migracije) — isti obrazac kao ClientPackage.Version.
        modelBuilder.Entity<LeaveFund>()
            .Property(f => f.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        modelBuilder.Entity<LeaveFundUsage>().HasKey(u => u.Id);
        modelBuilder.Entity<LeaveFundUsage>().HasIndex(u => u.RosterEntryId);
        modelBuilder.Entity<LeaveFundUsage>().HasIndex(u => u.LeaveFundId);
        modelBuilder.Entity<LeaveFundUsage>()
            .HasOne<RosterEntry>()
            .WithMany()
            .HasForeignKey(u => u.RosterEntryId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LeaveFundUsage>()
            .HasOne(u => u.LeaveFund)
            .WithMany()
            .HasForeignKey(u => u.LeaveFundId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompanyHoliday>().HasKey(h => h.Id);
        modelBuilder.Entity<CompanyHoliday>()
            .HasIndex(h => new { h.OrganizationId, h.CompanyId, h.Date })
            .IsUnique();
        modelBuilder.Entity<CompanyHoliday>()
            .HasOne(h => h.Company)
            .WithMany()
            .HasForeignKey(h => h.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePermissions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GrantGroup>().HasKey(g => g.Id);
        modelBuilder.Entity<GrantGroup>()
            .HasIndex(g => new { g.OrganizationId, g.Name })
            .IsUnique();
        modelBuilder.Entity<GrantGroup>()
            .HasIndex(g => new { g.OrganizationId, g.SystemKey })
            .IsUnique()
            .HasFilter("system_key IS NOT NULL");

        modelBuilder.Entity<GrantGroupGrant>().HasKey(g => g.Id);
        modelBuilder.Entity<GrantGroupGrant>()
            .HasIndex(g => new { g.GrantGroupId, g.GrantKey })
            .IsUnique();
        modelBuilder.Entity<GrantGroupGrant>()
            .HasOne(g => g.GrantGroup)
            .WithMany(gg => gg.Grants)
            .HasForeignKey(g => g.GrantGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserGrantGroup>().HasKey(u => u.Id);
        modelBuilder.Entity<UserGrantGroup>()
            .HasIndex(u => new { u.UserId, u.GrantGroupId })
            .IsUnique();
        modelBuilder.Entity<UserGrantGroup>()
            .HasOne(u => u.User)
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserGrantGroup>()
            .HasOne(u => u.GrantGroup)
            .WithMany(gg => gg.UserGrantGroups)
            .HasForeignKey(u => u.GrantGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Role>().HasKey(r => r.Id);
        modelBuilder.Entity<Role>()
            .HasIndex(r => new { r.OrganizationId, r.Name })
            .IsUnique();

        modelBuilder.Entity<UserRoleAssignment>().HasKey(u => u.Id);
        modelBuilder.Entity<UserRoleAssignment>()
            .HasIndex(u => new { u.UserId, u.RoleId })
            .IsUnique();
        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(u => u.User)
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserRoleAssignment>()
            .HasOne(u => u.Role)
            .WithMany()
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureOutboxAndNotifications(ModelBuilder modelBuilder)
    {
        // Idempotency partial unique indeks (organization_id, type, idempotency_key WHERE idempotency_key IS
        // NOT NULL) je raw SQL u migraciji (isti obrazac kao ostali "djelomični unique preko EF-neizraziva
        // uvjeta" slučajevi u ovoj bazi) — ovdje samo standardni indeksi za poll upit.
        modelBuilder.Entity<OutboxMessage>().HasKey(m => m.Id);
        modelBuilder.Entity<OutboxMessage>().HasIndex(m => new { m.Status, m.AvailableAt });
        modelBuilder.Entity<OutboxMessage>()
            .Property(m => m.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<OutboxMessageStatus>(v));
        modelBuilder.Entity<OutboxMessage>().Property(m => m.Payload).HasColumnType("jsonb");

        modelBuilder.Entity<Notification>().HasKey(n => n.Id);
        modelBuilder.Entity<Notification>().HasIndex(n => new { n.OrganizationId, n.ClientId, n.CreatedAt });
        // Idempotencija — najviše jedan Notification po KONKRETNOJ poslovnoj pojavi, uključujući SourceVersion
        // (vidi spec section 28/5-18, Notification.cs).
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.OrganizationId, n.Type, n.SourceType, n.SourceId, n.SourceVersion })
            .IsUnique();
        modelBuilder.Entity<Notification>()
            .Property(n => n.Type)
            .HasConversion(v => v.ToString(), v => Enum.Parse<NotificationType>(v));
        modelBuilder.Entity<Notification>()
            .Property(n => n.SourceType)
            .HasConversion(v => v.ToString(), v => Enum.Parse<NotificationSourceType>(v));
        modelBuilder.Entity<Notification>()
            .Property(n => n.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<NotificationStatus>(v));
        modelBuilder.Entity<Notification>().Property(n => n.Data).HasColumnType("jsonb");
        modelBuilder.Entity<Notification>()
            .HasOne(n => n.Client)
            .WithMany()
            .HasForeignKey(n => n.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PlatformAccount>().HasKey(a => a.Id);
        modelBuilder.Entity<PlatformAccount>().HasIndex(a => a.Email).IsUnique();
    }

    public static DatabaseContext GenerateContext(string connectionString)
    {
        DbContextOptionsBuilder<DatabaseContext> builder = new DbContextOptionsBuilder<DatabaseContext>();
        builder.UseNpgsql(connectionString);
        return new DatabaseContext(builder.Options);
    }
}

public sealed class UtcDateTimeOffsetConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter() : base(v => v.ToUniversalTime(), v => v.ToUniversalTime()) { }
}
