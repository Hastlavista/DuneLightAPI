namespace BlueDragon.DuneLight.DatabaseMigration.Models;

public static class Tables
{
    public const string Organizations = "organizations";
    public const string Users = "users";

    public const string Companies = "companies";
    public const string ServiceCategories = "service_categories";
    public const string Services = "services";
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
    public const string AppointmentClients = "appointment_clients";
    public const string AppointmentAttendances = "appointment_attendances";
    public const string AppointmentAuditLog = "appointment_audit_log";

    public const string Groups = "groups";
    public const string GroupSlots = "group_slots";
    public const string GroupMembers = "group_members";
    public const string GroupAuditLog = "group_audit_log";

    public const string RosterTypes = "roster_types";
    public const string RosterEntries = "roster_entries";
    public const string RosterAuditLog = "roster_audit_log";
    public const string WorkingHoursTemplates = "working_hours_templates";
    public const string WorkingHoursIntervals = "working_hours_intervals";
    public const string EmployeeLeaveSettings = "employee_leave_settings";
    public const string LeaveFunds = "leave_funds";
    public const string LeaveFundUsages = "leave_fund_usages";

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
