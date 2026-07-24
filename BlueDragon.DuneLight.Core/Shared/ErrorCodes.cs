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

    // Poslovna pravila (409) — domenski specifično, umjesto generičkog CONFLICT
    public const string ReferencedCannotDelete = "REFERENCED_CANNOT_DELETE";
    public const string DuplicateName = "DUPLICATE_NAME";
    public const string DuplicateMemberNumber = "DUPLICATE_MEMBER_NUMBER";
    public const string EmailAlreadyInUse = "EMAIL_ALREADY_IN_USE";
    public const string UserAlreadyLinked = "USER_ALREADY_LINKED";
    public const string LastActiveAdmin = "LAST_ACTIVE_ADMIN";
    public const string LastActiveLocation = "LAST_ACTIVE_LOCATION";
    public const string LastActiveSlot = "LAST_ACTIVE_SLOT";
    public const string AlreadyMember = "ALREADY_MEMBER";
    public const string AlreadyCompleted = "ALREADY_COMPLETED";
    public const string SameDayOnly = "SAME_DAY_ONLY";
    public const string NotOwner = "NOT_OWNER";
    public const string PackageNotEligible = "PACKAGE_NOT_ELIGIBLE";
    public const string PackageServiceNotCovered = "PACKAGE_SERVICE_NOT_COVERED";
    public const string CategoryInUse = "CATEGORY_IN_USE";
    public const string InactiveEmployee = "INACTIVE_EMPLOYEE";
    public const string InactiveType = "INACTIVE_TYPE";
    public const string PriceOverlap = "PRICE_OVERLAP";
    public const string ClientAnonymized = "CLIENT_ANONYMIZED";
    public const string AppointmentNotMovable = "APPOINTMENT_NOT_MOVABLE";
}
