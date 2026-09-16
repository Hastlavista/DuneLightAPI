namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedini izvor istine za sve "code" vrijednosti u { "error": { "code", "message", "details" } }.
/// Ovi kodovi su stabilan ugovor prema frontendu (i18n se veže na njih) — ne mijenjati postojeće
/// vrijednosti, samo dodavati nove. Message polje smije se mijenjati slobodno, kôd ne.
/// </summary>
public static class ErrorCodes
{
    // Opći (svi moduli)
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string InternalError = "INTERNAL_ERROR";

    // Auth (/api/public/Auth/*)
    public const string AuthInvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string AuthOrganizationSlugTaken = "AUTH_ORGANIZATION_SLUG_TAKEN";
    public const string AuthCurrentPasswordInvalid = "AUTH_CURRENT_PASSWORD_INVALID";
    public const string AuthInvalidPin = "AUTH_INVALID_PIN";

    // Poslovna pravila (409) — domenski specifično, umjesto generičkog CONFLICT
    public const string ReferencedCannotDelete = "REFERENCED_CANNOT_DELETE";
    public const string DuplicateName = "DUPLICATE_NAME";
    public const string DuplicateMemberNumber = "DUPLICATE_MEMBER_NUMBER";
    public const string EmailAlreadyInUse = "EMAIL_ALREADY_IN_USE";
    public const string UserAlreadyLinked = "USER_ALREADY_LINKED";
    public const string LastActiveAdmin = "LAST_ACTIVE_ADMIN";
    public const string LastActiveCompany = "LAST_ACTIVE_COMPANY";
    public const string LastActiveSlot = "LAST_ACTIVE_SLOT";
    public const string AlreadyMember = "ALREADY_MEMBER";
    public const string AlreadyCompleted = "ALREADY_COMPLETED";
    public const string SameDayOnly = "SAME_DAY_ONLY";
    public const string NotOwner = "NOT_OWNER";
    public const string PackageNotEligible = "PACKAGE_NOT_ELIGIBLE";
    public const string PackageServiceNotCovered = "PACKAGE_SERVICE_NOT_COVERED";
    public const string InactiveEmployee = "INACTIVE_EMPLOYEE";
    public const string InactiveType = "INACTIVE_TYPE";
    public const string InactiveService = "INACTIVE_SERVICE";
    public const string InactivePackage = "INACTIVE_PACKAGE";
    public const string InactiveCompany = "INACTIVE_COMPANY";
    public const string PriceOverlap = "PRICE_OVERLAP";
    public const string ClientAnonymized = "CLIENT_ANONYMIZED";
    public const string InactiveClient = "INACTIVE_CLIENT";
    public const string ClientHasActiveRelationships = "CLIENT_HAS_ACTIVE_RELATIONSHIPS";
    public const string AppointmentNotMovable = "APPOINTMENT_NOT_MOVABLE";
    public const string AppointmentOverlap = "APPOINTMENT_OVERLAP";
    public const string RecurringConflict = "RECURRING_CONFLICT";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string OutsideWorkingHours = "OUTSIDE_WORKING_HOURS";
    public const string LeaveSettingsNotConfigured = "LEAVE_SETTINGS_NOT_CONFIGURED";
    public const string LeaveFundExceeded = "LEAVE_FUND_EXCEEDED";
    public const string LeaveFundAllocatedBelowUsed = "LEAVE_FUND_ALLOCATED_BELOW_USED";
    public const string LeaveFundTypeMustBeAbsence = "LEAVE_FUND_TYPE_MUST_BE_ABSENCE";
    public const string LeaveFundEntryRequiresEndDate = "LEAVE_FUND_ENTRY_REQUIRES_END_DATE";
    public const string DuplicateHolidayDate = "DUPLICATE_HOLIDAY_DATE";
    public const string HolidayCatalogNotDefinedForCountry = "HOLIDAY_CATALOG_NOT_DEFINED_FOR_COUNTRY";
    public const string RoomCompanyMismatch = "ROOM_COMPANY_MISMATCH";
    public const string EmployeeMissingPrimaryCompany = "EMPLOYEE_MISSING_PRIMARY_COMPANY";
    public const string EmployeeNotAssignedToCompany = "EMPLOYEE_NOT_ASSIGNED_TO_COMPANY";
    public const string EmployeeNotAssignedToService = "EMPLOYEE_NOT_ASSIGNED_TO_SERVICE";
    public const string ServiceNotGroupMode = "SERVICE_NOT_GROUP_MODE";
    public const string ServiceExecutionModeLocked = "SERVICE_EXECUTION_MODE_LOCKED";
    public const string ServiceNotAvailableAtCompany = "SERVICE_NOT_AVAILABLE_AT_COMPANY";
    public const string InactiveRoom = "INACTIVE_ROOM";
    public const string GroupCapacityReached = "GROUP_CAPACITY_REACHED";
    public const string AttendanceBeforeStart = "ATTENDANCE_BEFORE_START";

    // Payment ledger (vidi Payment.cs/IPaymentService)
    public const string PaymentExceedsOutstandingAmount = "PAYMENT_EXCEEDS_OUTSTANDING_AMOUNT";
    public const string PaymentNotAllowed = "PAYMENT_NOT_ALLOWED";
    public const string PaymentAlreadyVoided = "PAYMENT_ALREADY_VOIDED";

    // Checkout / POS temelji (vidi Checkout.cs/ICheckoutService)
    public const string CheckoutNotOpen = "CHECKOUT_NOT_OPEN";
    public const string CheckoutHasActivePayments = "CHECKOUT_HAS_ACTIVE_PAYMENTS";
    public const string CheckoutOutstandingBalance = "CHECKOUT_OUTSTANDING_BALANCE";
    public const string CheckoutItemCompanyMismatch = "CHECKOUT_ITEM_COMPANY_MISMATCH";
    public const string CheckoutItemClientMismatch = "CHECKOUT_ITEM_CLIENT_MISMATCH";
    public const string CheckoutItemHasAllocations = "CHECKOUT_ITEM_HAS_ALLOCATIONS";
    public const string CheckoutItemNotEligible = "CHECKOUT_ITEM_NOT_ELIGIBLE";
    public const string BookingAlreadyInOpenCheckout = "BOOKING_ALREADY_IN_OPEN_CHECKOUT";
    public const string AllocationExceedsItemOutstanding = "ALLOCATION_EXCEEDS_ITEM_OUTSTANDING";
    public const string AllocationAmountMismatch = "ALLOCATION_AMOUNT_MISMATCH";
    public const string BookingAlreadyHasMonetaryPayment = "BOOKING_ALREADY_HAS_MONETARY_PAYMENT";

    // Products & Stock (vidi Product.cs/ProductStock.cs/StockMovement.cs/IStockService)
    public const string DuplicateSku = "DUPLICATE_SKU";
    public const string InactiveProduct = "INACTIVE_PRODUCT";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string TransferSameCompany = "TRANSFER_SAME_COMPANY";
    public const string InvalidQuantity = "INVALID_QUANTITY";

    // Commissions (vidi CommissionRule.cs/CommissionEntry.cs/ICommissionRuleService)
    public const string CommissionRuleAlreadyExists = "COMMISSION_RULE_ALREADY_EXISTS";
    public const string CommissionGroupPercentageNotSupported = "COMMISSION_GROUP_PERCENTAGE_NOT_SUPPORTED";

    // Waitlist (vidi WaitlistEntry/IWaitlistService)
    public const string WaitlistNotAvailable = "WAITLIST_NOT_AVAILABLE";
    public const string AlreadyWaitlisted = "ALREADY_WAITLISTED";
    public const string AlreadyBooked = "ALREADY_BOOKED";
    public const string CapacityAvailable = "CAPACITY_AVAILABLE";
    public const string WaitlistEntryNotActive = "WAITLIST_ENTRY_NOT_ACTIVE";

    // Termin/Booking eligibility chain (tvrde blokade, vidi AppointmentEligibilityHelper) — mogu se
    // zaobići samo eksplicitnim OverrideAvailability=true uz appointments.write.all / groups.manage.
    public const string EmployeeAbsent = "EMPLOYEE_ABSENT";
    public const string EmployeeOnBreak = "EMPLOYEE_ON_BREAK";
    public const string CompanyClosedHoliday = "COMPANY_CLOSED_HOLIDAY";

    // RECURRING_CONFLICT details.conflicts[].reason vrijednosti
    public const string RecurringConflictReasonAppointment = "EXISTING_APPOINTMENT";
    public const string RecurringConflictReasonRosterAbsence = "ROSTER_ABSENCE";
    public const string RecurringConflictReasonOutsideWorkingHours = "OUTSIDE_WORKING_HOURS";
    public const string RecurringConflictReasonScheduleBreak = "EXISTING_SCHEDULE_BREAK";
    public const string RecurringConflictReasonHoliday = "HOLIDAY";
    public const string RecurringConflictReasonRoom = "ROOM_OCCUPIED";
    public const string RecurringConflictReasonMemberConflict = "MEMBER_CONFLICT";
    public const string RecurringConflictReasonDuplicateOccurrence = "DUPLICATE_OCCURRENCE";
}
