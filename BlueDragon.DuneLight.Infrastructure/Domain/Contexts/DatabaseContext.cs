using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Contexts;

public class DatabaseContext : DbContext
{
    public DbSet<Organization> Organizations { get; set; }
    public DbSet<User> Users { get; set; }

    public DbSet<Location> Locations { get; set; }
    public DbSet<ServiceCategory> ServiceCategories { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<PriceListItem> PriceListItems { get; set; }
    public DbSet<PriceListItemHistory> PriceListItemHistory { get; set; }
    public DbSet<Package> Packages { get; set; }
    public DbSet<PackageServiceItem> PackageServiceItems { get; set; }

    public DbSet<EngagementType> EngagementTypes { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<EmployeeLocation> EmployeeLocations { get; set; }
    public DbSet<EmployeeServiceAssignment> EmployeeServiceAssignments { get; set; }
    public DbSet<EmployeeAuditLog> EmployeeAuditLog { get; set; }

    public DbSet<ClientTag> ClientTags { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<ClientTagAssignment> ClientTagAssignments { get; set; }
    public DbSet<ClientPackage> ClientPackages { get; set; }
    public DbSet<ClientPackageServiceEntry> ClientPackageServiceEntries { get; set; }

    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<AppointmentClient> AppointmentClients { get; set; }
    public DbSet<AppointmentAttendance> AppointmentAttendances { get; set; }
    public DbSet<AppointmentAuditLog> AppointmentAuditLog { get; set; }

    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupSlot> GroupSlots { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<GroupAuditLog> GroupAuditLog { get; set; }

    public DbSet<RosterType> RosterTypes { get; set; }
    public DbSet<RosterEntry> RosterEntries { get; set; }
    public DbSet<RosterAuditLog> RosterAuditLog { get; set; }

    public DatabaseContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dunelight");

        modelBuilder.Entity<Organization>().HasKey(o => new { o.Id });
        modelBuilder.Entity<Organization>().HasIndex(o => o.Slug).IsUnique();

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
        ConfigureGroups(modelBuilder);
        ConfigureRoster(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Location>().HasKey(l => l.Id);
        modelBuilder.Entity<Location>().HasIndex(l => l.OrganizationId);

        modelBuilder.Entity<ServiceCategory>().HasKey(c => c.Id);
        modelBuilder.Entity<ServiceCategory>()
            .Property(c => c.ExecutionMode)
            .HasConversion(v => v.ToString(), v => Enum.Parse<ServiceExecutionMode>(v));
        modelBuilder.Entity<ServiceCategory>()
            .HasIndex(c => new { c.OrganizationId, c.Name })
            .IsUnique()
            .HasFilter("is_active = true");

        modelBuilder.Entity<Service>().HasKey(s => s.Id);
        modelBuilder.Entity<Service>()
            .HasOne(s => s.ServiceCategory)
            .WithMany()
            .HasForeignKey(s => s.ServiceCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Service>()
            .HasIndex(s => new { s.OrganizationId, s.Name })
            .IsUnique()
            .HasFilter("is_active = true");

        modelBuilder.Entity<PriceListItem>().HasKey(p => p.Id);
        modelBuilder.Entity<PriceListItem>()
            .HasIndex(p => new { p.OrganizationId, p.ServiceId, p.PackageId, p.LocationId });
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
            .HasOne(p => p.Location)
            .WithMany()
            .HasForeignKey(p => p.LocationId)
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

        modelBuilder.Entity<EmployeeLocation>().HasKey(el => el.Id);
        modelBuilder.Entity<EmployeeLocation>()
            .HasIndex(el => new { el.EmployeeId, el.LocationId })
            .IsUnique();
        // Točno jedna matična lokacija po zaposleniku.
        modelBuilder.Entity<EmployeeLocation>()
            .HasIndex(el => el.EmployeeId)
            .IsUnique()
            .HasFilter("is_primary = true");
        modelBuilder.Entity<EmployeeLocation>()
            .HasOne(el => el.Employee)
            .WithMany(e => e.Locations)
            .HasForeignKey(el => el.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<EmployeeLocation>()
            .HasOne(el => el.Location)
            .WithMany()
            .HasForeignKey(el => el.LocationId)
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
        modelBuilder.Entity<ClientTag>().HasKey(t => t.Id);
        modelBuilder.Entity<ClientTag>()
            .HasIndex(t => new { t.OrganizationId, t.Name })
            .IsUnique()
            .HasFilter("is_active = true");

        modelBuilder.Entity<Client>().HasKey(c => c.Id);
        modelBuilder.Entity<Client>()
            .HasIndex(c => new { c.OrganizationId, c.MemberNumber })
            .IsUnique();
        modelBuilder.Entity<Client>()
            .HasIndex(c => new { c.OrganizationId, c.LastName, c.FirstName });
        modelBuilder.Entity<Client>()
            .HasOne(c => c.HomeLocation)
            .WithMany()
            .HasForeignKey(c => c.HomeLocationId)
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
    }

    private static void ConfigureAppointments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
        modelBuilder.Entity<Appointment>().HasIndex(a => new { a.OrganizationId, a.LocationId, a.StartsAt });
        modelBuilder.Entity<Appointment>().HasIndex(a => new { a.OrganizationId, a.EmployeeId, a.StartsAt });
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Form)
            .HasConversion(v => v.ToString(), v => Enum.Parse<AppointmentForm>(v));
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Status)
            .HasConversion(v => v.ToString(), v => Enum.Parse<AppointmentStatus>(v));
        modelBuilder.Entity<Appointment>()
            .Property(a => a.PaymentMethod)
            .HasConversion(
                v => v == null ? null : v.ToString(),
                v => v == null ? (PaymentMethod?)null : Enum.Parse<PaymentMethod>(v));
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
            .HasOne(a => a.Location)
            .WithMany()
            .HasForeignKey(a => a.LocationId)
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

        modelBuilder.Entity<AppointmentClient>().HasKey(ac => ac.Id);
        modelBuilder.Entity<AppointmentClient>()
            .HasIndex(ac => new { ac.AppointmentId, ac.ClientId })
            .IsUnique();
        modelBuilder.Entity<AppointmentClient>()
            .HasOne(ac => ac.Appointment)
            .WithMany(a => a.Clients)
            .HasForeignKey(ac => ac.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AppointmentClient>()
            .HasOne(ac => ac.Client)
            .WithMany()
            .HasForeignKey(ac => ac.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AppointmentClient>()
            .HasOne(ac => ac.ClientPackage)
            .WithMany()
            .HasForeignKey(ac => ac.ClientPackageId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AppointmentAttendance>().HasKey(aa => aa.Id);
        modelBuilder.Entity<AppointmentAttendance>()
            .HasIndex(aa => new { aa.AppointmentId, aa.ClientId })
            .IsUnique();
        modelBuilder.Entity<AppointmentAttendance>()
            .Property(aa => aa.CoverageType)
            .HasConversion(
                v => v == null ? null : v.ToString(),
                v => v == null ? (AttendanceCoverageType?)null : Enum.Parse<AttendanceCoverageType>(v));
        modelBuilder.Entity<AppointmentAttendance>()
            .HasOne(aa => aa.Appointment)
            .WithMany(a => a.Attendances)
            .HasForeignKey(aa => aa.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AppointmentAttendance>()
            .HasOne(aa => aa.Client)
            .WithMany()
            .HasForeignKey(aa => aa.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AppointmentAttendance>()
            .HasOne(aa => aa.ClientPackage)
            .WithMany()
            .HasForeignKey(aa => aa.ClientPackageId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AppointmentAuditLog>().HasKey(a => a.Id);
        modelBuilder.Entity<AppointmentAuditLog>().HasIndex(a => a.AppointmentId);
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
            .HasOne(g => g.Location)
            .WithMany()
            .HasForeignKey(g => g.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Group>()
            .HasOne(g => g.DefaultTrainer)
            .WithMany()
            .HasForeignKey(g => g.DefaultTrainerId)
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
    }

    private static void ConfigureRoster(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RosterType>().HasKey(t => t.Id);
        modelBuilder.Entity<RosterType>()
            .HasIndex(t => new { t.OrganizationId, t.Name })
            .IsUnique()
            .HasFilter("is_active = true");

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
    }

    public static DatabaseContext GenerateContext(string connectionString)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        DbContextOptionsBuilder<DatabaseContext> builder = new DbContextOptionsBuilder<DatabaseContext>();
        builder.UseNpgsql(connectionString);
        return new DatabaseContext(builder.Options);
    }
}
