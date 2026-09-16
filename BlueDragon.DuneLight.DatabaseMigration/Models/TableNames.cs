namespace BlueDragon.DuneLight.DatabaseMigration.Models;

public static class Tables
{
    public const string Organizations = "organizations";
    public const string Users = "users";

    public const string Companies = "companies";
    public const string Rooms = "rooms";
    public const string ServiceCategories = "service_categories";
    public const string Services = "services";
    public const string ServiceCompanies = "service_companies";
    public const string Packages = "packages";
    public const string PackageServices = "package_services";
    public const string PriceListItems = "price_list_items";
    public const string PriceListItemHistory = "price_list_item_history";

    public const string EngagementTypes = "engagement_types";
    public const string Employees = "employees";
    public const string EmployeeCompanies = "employee_companies";
    public const string EmployeeServices = "employee_services";
    public const string EmployeeAuditLog = "employee_audit_log";

    public const string ClientTags = "client_tags";
    public const string Clients = "clients";
    public const string ClientTagAssignments = "client_tag_assignments";
    public const string ClientPackages = "client_packages";
    public const string ClientPackageServiceEntries = "client_package_service_entries";

    public const string Appointments = "appointments";
    public const string Bookings = "bookings";
    public const string Payments = "payments";
    public const string AppointmentAuditLog = "appointment_audit_log";
    public const string ScheduleBreaks = "schedule_breaks";

    public const string Checkouts = "checkouts";
    public const string CheckoutItems = "checkout_items";
    public const string PaymentAllocations = "payment_allocations";
    public const string CheckoutAuditLog = "checkout_audit_log";

    public const string Products = "products";
    public const string ProductStock = "product_stock";
    public const string StockMovements = "stock_movements";

    /// <summary>
    /// Stari nazivi (prije uvođenja Bookinga u Migration_2026_09_15_IntroduceBookingModel — vidi tamo). Ne
    /// dirati i ne brisati: koriste ih već odrađene migracije (redoslijed 25-26 na 2026-07-21, i
    /// AlterAppointmentAttendancesForGroups na 2026-07) da na praznoj bazi vjerno reproduciraju shemu kakva je
    /// postojala prije zamjene Bookingom — isti obrazac kao Tables.Locations (vidi napomenu ispod).
    /// </summary>
    public const string AppointmentClients = "appointment_clients";
    public const string AppointmentAttendances = "appointment_attendances";

    public const string Groups = "groups";
    public const string GroupSlots = "group_slots";
    public const string GroupMembers = "group_members";
    public const string GroupAuditLog = "group_audit_log";
    public const string WaitlistEntries = "waitlist_entries";

    public const string RosterTypes = "roster_types";
    public const string RosterEntries = "roster_entries";
    public const string RosterAuditLog = "roster_audit_log";
    public const string WorkingHoursTemplates = "working_hours_templates";
    public const string WorkingHoursIntervals = "working_hours_intervals";
    public const string EmployeeLeaveSettings = "employee_leave_settings";
    public const string LeaveFunds = "leave_funds";
    public const string LeaveFundUsages = "leave_fund_usages";
    public const string CompanyHolidays = "company_holidays";

    public const string OrganizationBrandingAuditLog = "organization_branding_audit_log";
    public const string OrganizationSettings = "organization_settings";

    public const string CommissionRules = "commission_rules";
    public const string CommissionEntries = "commission_entries";

    public const string GrantGroups = "grant_groups";
    public const string GrantGroupGrants = "grant_group_grants";
    public const string UserGrantGroups = "user_grant_groups";
    public const string Roles = "roles";
    public const string UserRoleAssignments = "user_role_assignments";

    /// <summary>
    /// Stari nazivi (prije preimenovanja Location -> Company u RenameLocationToCompany). Ne dirati i ne brisati:
    /// koriste ih već odrađene migracije (redoslijed 3-41) da na praznoj bazi vjerno reproduciraju shemu kakva je
    /// postojala u trenutku kad su izvorno pokrenute, prije preimenovanja.
    /// </summary>
    public const string Locations = "locations";
    public const string EmployeeLocations = "employee_locations";

    public static class Schemas
    {
        public const string DuneLight = "dunelight";
    }
}
