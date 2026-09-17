using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Contexts;

public class DatabaseContext : DbContext
{
    public DbSet<Organization> Organizations { get; set; }
    public DbSet<User> Users { get; set; }

    public DbSet<Company> Companies { get; set; }
    public DbSet<Room> Rooms { get; set; }
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

    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<Booking> Bookings { get; set; }
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

    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupSlot> GroupSlots { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<GroupAuditLog> GroupAuditLog { get; set; }
    public DbSet<WaitlistEntry> WaitlistEntries { get; set; }

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

    public DatabaseContext(DbContextOptions options) : base(options)
    {
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

        modelBuilder.Entity<User>().HasKey(u => new { u.Id });
        modelBuilder.Entity<User>().HasIndex(u => new { u.OrganizationId, u.Email }).IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasConversion(
                v => v.ToString(),
                v => Enum.Parse<UserRole>(v));

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

        modelBuilder.Entity<Room>().HasKey(r => r.Id);
        modelBuilder.Entity<Room>().HasIndex(r => new { r.OrganizationId, r.CompanyId });
        modelBuilder.Entity<Room>()
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
        modelBuilder.Entity<Appointment>().HasIndex(a => new { a.OrganizationId, a.CompanyId, a.StartsAt });
        modelBuilder.Entity<Appointment>().HasIndex(a => new { a.OrganizationId, a.EmployeeId, a.StartsAt });
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Form)
            .HasConversion(v => v.ToString(), v => Enum.Parse<AppointmentForm>(v));
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<AppointmentStatus>(v));
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Service)
            .WithMany()
            .HasForeignKey(a => a.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Employee)
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Company)
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Room)
            .WithMany()
            .HasForeignKey(a => a.RoomId)
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

        modelBuilder.Entity<Booking>().HasKey(b => b.Id);
        modelBuilder.Entity<Booking>().HasIndex(b => new { b.OrganizationId, b.ClientId });
        modelBuilder.Entity<Booking>()
            .HasIndex(b => new { b.AppointmentId, b.ClientId })
            .IsUnique();
        modelBuilder.Entity<Booking>()
            .Property(b => b.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<BookingStatus>(v));
        modelBuilder.Entity<Booking>()
            .Property(b => b.CoverageType)
            .HasConversion(
                v => v == null ? null : v.ToString(),
                v => v == null ? (AttendanceCoverageType?)null : Enum.Parse<AttendanceCoverageType>(v));
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
        modelBuilder.Entity<Booking>()
            .HasOne(b => b.ClientPackage)
            .WithMany()
            .HasForeignKey(b => b.ClientPackageId)
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
        // Djelomični unique indeks (locks_booking = true) sprječava isti Booking u dva istovremeno Open
        // checkouta — vidi CheckoutItem.LocksBooking domensku napomenu i spec section 29/60. Izražen kao raw
        // SQL partial index u migraciji (EF fluent HasFilter podržava samo statičan SQL fragment, što je ovdje
        // dovoljno — "locks_booking = true").
        modelBuilder.Entity<CheckoutItem>()
            .HasIndex(ci => ci.BookingId)
            .IsUnique()
            .HasFilter("locks_booking = true");
        modelBuilder.Entity<CheckoutItem>()
            .Property(ci => ci.Type)
            .HasConversion(v => v.ToString(), v => Enum.Parse<CheckoutItemType>(v));
        modelBuilder.Entity<CheckoutItem>()
            .HasOne(ci => ci.Checkout)
            .WithMany(c => c.Items)
            .HasForeignKey(ci => ci.CheckoutId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CheckoutItem>()
            .HasOne(ci => ci.Booking)
            .WithMany(b => b.CheckoutItems)
            .HasForeignKey(ci => ci.BookingId)
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
            .HasOne(e => e.CheckoutItem)
            .WithMany()
            .HasForeignKey(e => e.CheckoutItemId)
            .OnDelete(DeleteBehavior.Restrict);
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
        modelBuilder.Entity<Group>()
            .HasOne(g => g.Service)
            .WithMany()
            .HasForeignKey(g => g.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Group>()
            .HasOne(g => g.Company)
            .WithMany()
            .HasForeignKey(g => g.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Group>()
            .HasOne(g => g.DefaultTrainer)
            .WithMany()
            .HasForeignKey(g => g.DefaultTrainerId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Group>()
            .HasOne(g => g.DefaultRoom)
            .WithMany()
            .HasForeignKey(g => g.DefaultRoomId)
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
            .HasIndex(w => new { w.AppointmentId, w.ClientId })
            .IsUnique()
            .HasFilter("status = 'Waiting'");
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
    }

    public static DatabaseContext GenerateContext(string connectionString)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        DbContextOptionsBuilder<DatabaseContext> builder = new DbContextOptionsBuilder<DatabaseContext>();
        builder.UseNpgsql(connectionString);
        return new DatabaseContext(builder.Options);
    }
}
