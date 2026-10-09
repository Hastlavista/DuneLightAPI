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

    /// <summary>K1-3 — ručni broj člana je veći od dosadašnjeg najvećeg za više od 1000: automatsko brojanje bi nastavilo od
    /// njega. Ništa nije spremljeno; ponoviti uz ConfirmMemberNumberJump = true. Details: memberNumber, currentMax. 409.</summary>
    public const string MemberNumberJumpNotConfirmed = "MEMBER_NUMBER_JUMP_NOT_CONFIRMED";
    public const string EmailAlreadyInUse = "EMAIL_ALREADY_IN_USE";
    public const string ClientEmailAlreadyInUse = "CLIENT_EMAIL_ALREADY_IN_USE";
    public const string UserAlreadyLinked = "USER_ALREADY_LINKED";
    public const string LastPermissionAdminRequired = "LAST_PERMISSION_ADMIN_REQUIRED";
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
    /// <summary>Phase D2: Booking i segment sudjelovanja pripadaju različitim terminima.</summary>
    public const string ParticipationAppointmentMismatch = "PARTICIPATION_APPOINTMENT_MISMATCH";
    /// <summary>Phase D2: Booking već sudjeluje u tom segmentu.</summary>
    public const string DuplicateParticipation = "DUPLICATE_PARTICIPATION";
    /// <summary>Phase M1E: termin mora zadržati barem jedan segment.</summary>
    public const string LastSegmentCannotBeRemoved = "LAST_SEGMENT_CANNOT_BE_REMOVED";
    /// <summary>Phase M1G — segment s 2+ zaposlenika zahtijeva eksplicitan izvor cijene (PricingMode, uz PricingEmployeeId za
    /// Employee) — nikad se ne pogađa.</summary>
    public const string PricingSourceRequired = "PRICING_SOURCE_REQUIRED";

    /// <summary>Phase M1G — izvor cijene ne odgovara zaposlenicima segmenta/predloška (Standard uz zaposlenika, Employee bez
    /// zaposlenika ili sa zaposlenikom koji nije dodijeljen, Standard za segment s točno jednim zaposlenikom...).</summary>
    public const string InvalidPricingSource = "INVALID_PRICING_SOURCE";

    /// <summary>Phase M1G — zaposlenici segmenta s izvršnom poviješću (odrađeno sudjelovanje ili zatvorena grupna sesija) se
    /// ne mijenjaju — povijesno izvršenje i provizija se ne prepisuju.</summary>
    public const string SegmentExecutionHistoryLocked = "SEGMENT_EXECUTION_HISTORY_LOCKED";
    /// <summary>Phase M1D: istovremeni broj OSOBA u prostoriji (zaposlenici + zauzimajući klijenti) premašio bi Room.Capacity —
    /// tvrdo ograničenje, bez override-a.</summary>
    public const string RoomCapacityExceeded = "ROOM_CAPACITY_EXCEEDED";
    /// <summary>Phase M1D: istovremeni zbroj QuantityRequired premašio bi Resource.Capacity — tvrdo ograničenje, bez override-a.</summary>
    public const string ResourceCapacityExceeded = "RESOURCE_CAPACITY_EXCEEDED";
    /// <summary>Phase M1D.1: smanjenje Room.Capacity ispod vršne zauzetosti (osobe) tekućih/budućih segmenata.</summary>
    public const string RoomCapacityBelowScheduledUsage = "ROOM_CAPACITY_BELOW_SCHEDULED_USAGE";
    /// <summary>Phase M1D.1: smanjenje Resource.Capacity ispod vršne zauzetosti (QuantityRequired) tekućih/budućih segmenata.</summary>
    public const string ResourceCapacityBelowScheduledUsage = "RESOURCE_CAPACITY_BELOW_SCHEDULED_USAGE";
    public const string InactiveResource = "INACTIVE_RESOURCE";
    public const string ResourceCompanyMismatch = "RESOURCE_COMPANY_MISMATCH";
    /// <summary>Phase M1B: operacija nad aktivnim sudjelovanjima (npr. bulk no-show termina) nema nijedno aktivno
    /// (Confirmed) sudjelovanje.</summary>
    public const string NoActiveParticipations = "NO_ACTIVE_PARTICIPATIONS";
    public const string EmployeeMissingPrimaryCompany = "EMPLOYEE_MISSING_PRIMARY_COMPANY";
    public const string EmployeeNotAssignedToCompany = "EMPLOYEE_NOT_ASSIGNED_TO_COMPANY";
    public const string EmployeeNotAssignedToService = "EMPLOYEE_NOT_ASSIGNED_TO_SERVICE";
    public const string ServiceNotGroupMode = "SERVICE_NOT_GROUP_MODE";
    public const string ServiceExecutionModeLocked = "SERVICE_EXECUTION_MODE_LOCKED";
    public const string ServiceNotAvailableAtCompany = "SERVICE_NOT_AVAILABLE_AT_COMPANY";
    public const string InactiveRoom = "INACTIVE_ROOM";
    public const string GroupCapacityReached = "GROUP_CAPACITY_REACHED";
    /// <summary>Phase M1F — grupa mora zadržati barem jedan predložak segmenta.</summary>
    public const string LastGroupSegmentTemplate = "LAST_GROUP_SEGMENT_TEMPLATE";
    public const string AttendanceBeforeStart = "ATTENDANCE_BEFORE_START";

    /// <summary>K1-5 — "vrati termin" samo za eksplicitno otkazan termin. 409.</summary>
    public const string AppointmentNotCancelled = "APPOINTMENT_NOT_CANCELLED";

    /// <summary>K1-4 — postavka organizacije traži odabir šifre razloga za ovaj događaj (a postoji barem jedna aktivna). 400.</summary>
    public const string CancellationReasonRequired = "CANCELLATION_REASON_REQUIRED";

    /// <summary>K1-4 — odabrana šifra razloga nije aktivna ili ne vrijedi za ovaj događaj (otkaz klijenta / studija / izostanak). 409.</summary>
    public const string CancellationReasonNotApplicable = "CANCELLATION_REASON_NOT_APPLICABLE";

    /// <summary>K1-2 — dolazak se označava samo na Confirmed ili Completed sudjelovanju (ne na otkazanom/izostalom). 409.</summary>
    public const string ParticipationArrivalNotAllowed = "PARTICIPATION_ARRIVAL_NOT_ALLOWED";
    /// <summary>P1 (D3) — klijentsko otkazivanje u trenutku ili nakon početka segmenta sudjelovanja (Business/System smiju).</summary>
    public const string CancellationAfterStart = "CANCELLATION_AFTER_START";
    /// <summary>P1 (D6) — posljedica politike troši jedinicu paketa, a klijent ima više prihvatljivih brojenih paketa;
    /// potreban je eksplicitan ClientPackageId.</summary>
    public const string PackageSelectionRequired = "PACKAGE_SELECTION_REQUIRED";
    /// <summary>P1 (D11) — politika je zadana politika organizacije ili ima dodjelu, pa se ne može deaktivirati.</summary>
    public const string CancellationPolicyInUse = "CANCELLATION_POLICY_IN_USE";
    /// <summary>P1 (D11) — neaktivna politika se ne može dodijeliti niti postaviti kao zadana.</summary>
    public const string CancellationPolicyInactive = "CANCELLATION_POLICY_INACTIVE";
    /// <summary>P1 (D10) — sudjelovanje nema aktivnu posljedicu politike koja bi se mogla otpisati.</summary>
    public const string NoActivePolicyConsequence = "NO_ACTIVE_POLICY_CONSEQUENCE";
    public const string PackageAlreadyCancelled = "PACKAGE_ALREADY_CANCELLED";

    // P2 — članarine (docs/p2/P2_DECISION_RECORD.md)
    /// <summary>Q29 — plan s opsegom SelectedCompanies mora imati barem jednu poslovnicu (prazna lista nikad ne znači "sve"). 400.</summary>
    public const string MembershipPlanCompaniesRequired = "MEMBERSHIP_PLAN_COMPANIES_REQUIRED";
    /// <summary>Q17/Q49 — nedopuštena kombinacija prozora limita i kredita perioda (isti prozor kao period, kraći prozor s
    /// limitom koji nije manji od kredita, dulji prozor s limitom koji nije veći od kredita). 400.</summary>
    public const string MembershipUsageLimitInvalid = "MEMBERSHIP_USAGE_LIMIT_INVALID";
    /// <summary>Plan nije aktivan — nova prodaja ni promjena na taj plan nisu moguće. 409.</summary>
    public const string MembershipPlanInactive = "MEMBERSHIP_PLAN_INACTIVE";
    /// <summary>Plan je dosegnuo najveći broj aktivnih članstava. 409.</summary>
    public const string MembershipPlanFull = "MEMBERSHIP_PLAN_FULL";
    /// <summary>Q29.3 — nijedna odabrana poslovnica plana nije aktivna, prodaja nije moguća. 409.</summary>
    public const string MembershipPlanNoActiveCompany = "MEMBERSHIP_PLAN_NO_ACTIVE_COMPANY";
    /// <summary>Q10/Q46 — razdoblja važenja se preklapaju i dijele barem jednu uslugu u barem jednoj poslovnici;
    /// details: conflictingMembershipId, planName, sharedServiceIds, sharedCompanyIds. 409.</summary>
    public const string MembershipOverlappingCoverage = "MEMBERSHIP_OVERLAPPING_COVERAGE";
    /// <summary>Q45 — datum početka u prošlosti ili više od mjesec dana unaprijed. 400.</summary>
    public const string MembershipStartDateOutOfRange = "MEMBERSHIP_START_DATE_OUT_OF_RANGE";
    /// <summary>Članstvo je završilo ili je poništeno; naredba nije moguća. 409.</summary>
    public const string MembershipNotActive = "MEMBERSHIP_NOT_ACTIVE";
    /// <summary>Članstvo već ima zakazan završetak (otkaz ili raniji izlazak). 409.</summary>
    public const string MembershipEndAlreadyScheduled = "MEMBERSHIP_END_ALREADY_SCHEDULED";
    /// <summary>Nema zakazanog otkaza koji bi se mogao povući. 409.</summary>
    public const string MembershipNoScheduledCancellation = "MEMBERSHIP_NO_SCHEDULED_CANCELLATION";
    /// <summary>Q5/Q12 — pauza nije dopuštena (plan je ne dopušta, zakazan je završetak, preklapa se s drugom pauzom,
    /// pogrešne granice). 409.</summary>
    public const string MembershipPauseNotAllowed = "MEMBERSHIP_PAUSE_NOT_ALLOWED";
    /// <summary>Q5 — pauza bi prešla dopušteni zbroj dana/perioda ili broj pauza u 12 mjeseci od početka članstva. 409.</summary>
    public const string MembershipPauseLimitExceeded = "MEMBERSHIP_PAUSE_LIMIT_EXCEEDED";
    /// <summary>Pauza je već počela, završila ili je otkazana; naredba nije moguća. 409.</summary>
    public const string MembershipPauseNotPending = "MEMBERSHIP_PAUSE_NOT_PENDING";
    /// <summary>Raniji izlazak: datum mora biti od danas do izračunatog datuma otkaza. 400.</summary>
    public const string MembershipEndDateOutOfRange = "MEMBERSHIP_END_DATE_OUT_OF_RANGE";
    /// <summary>Promjena plana nije moguća (isti plan, završetak prije stupanja promjene na snagu). 409.</summary>
    public const string MembershipPlanChangeNotAllowed = "MEMBERSHIP_PLAN_CHANGE_NOT_ALLOWED";
    /// <summary>Nema zakazane promjene plana koju je zatražio klijent. 409.</summary>
    public const string MembershipNoScheduledPlanChange = "MEMBERSHIP_NO_SCHEDULED_PLAN_CHANGE";
    /// <summary>2C — zaduženje nije otvoreno (otpisano ili poništeno) ili nema preostalog duga. 409.</summary>
    public const string MembershipChargeNotOpen = "MEMBERSHIP_CHARGE_NOT_OPEN";
    /// <summary>2C — zaduženje je stavka otvorenog checkouta (dodavanje u drugi checkout, otpis ili poništavanje prodaje nisu
    /// mogući dok se stavka ne ukloni ili checkout ne zatvori). 409.</summary>
    public const string MembershipChargeInOpenCheckout = "MEMBERSHIP_CHARGE_IN_OPEN_CHECKOUT";
    /// <summary>2C (Q24.4/Q51) — prodaja ima aktivne alokacije plaćanja (ili korištenje); poništavanje nije moguće. 409.</summary>
    public const string MembershipSaleHasPayments = "MEMBERSHIP_SALE_HAS_PAYMENTS";
    /// <summary>Q5.3 — pauza nije dopuštena dok članstvo ima dug nakon isteka grace perioda. 409.</summary>
    public const string MembershipDelinquent = "MEMBERSHIP_DELINQUENT";
    /// <summary>2D (Q4) — limit članarine je iskorišten, a postavka organizacije je "odbij" (details: clientMembershipId,
    /// window, serviceId (null = limit plana), maxUses, used, plannedStart). 409.</summary>
    public const string MembershipLimitExceeded = "MEMBERSHIP_LIMIT_EXCEEDED";
    /// <summary>2D (Q27.2) — pokriće sudjelovanja čeka evaluaciju (termin iza horizonta); naplata nije moguća dok se ne
    /// evaluira. 409.</summary>
    public const string MembershipCoveragePending = "MEMBERSHIP_COVERAGE_PENDING";
    /// <summary>2D — sudjelovanje je pokriveno članarinom (članarina ima prednost pred paketom i novcem). 409.</summary>
    public const string ParticipationCoveredByMembership = "PARTICIPATION_COVERED_BY_MEMBERSHIP";
    /// <summary>2D (Q24.4) — članstvo ima korištenje (claim); poništavanje prodaje nije moguće, samo otkaz. 409.</summary>
    public const string MembershipSaleHasUsage = "MEMBERSHIP_SALE_HAS_USAGE";
    /// <summary>2D (Q15.4/Q54) — članarina koja bi pokrila sesiju je u dugu nakon grace perioda, a postavka organizacije je
    /// "blokiraj rezervaciju"; nadjačava se grantom appointments.membership-block.override (details: clientMembershipId). 409.</summary>
    public const string MembershipBookingBlocked = "MEMBERSHIP_BOOKING_BLOCKED";

    // Payment ledger (vidi Payment.cs/IPaymentService)
    public const string PaymentExceedsOutstandingAmount = "PAYMENT_EXCEEDS_OUTSTANDING_AMOUNT";
    public const string PaymentNotAllowed = "PAYMENT_NOT_ALLOWED";
    public const string PaymentAlreadyVoided = "PAYMENT_ALREADY_VOIDED";
    public const string PaymentVoidReasonRequired = "PAYMENT_VOID_REASON_REQUIRED";

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
    public const string BookingHasNonReversiblePayment = "BOOKING_HAS_NON_REVERSIBLE_PAYMENT";

    // Products & Stock (vidi Product.cs/ProductStock.cs/StockMovement.cs/IStockService)
    public const string DuplicateSku = "DUPLICATE_SKU";
    public const string InactiveProduct = "INACTIVE_PRODUCT";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string TransferSameCompany = "TRANSFER_SAME_COMPANY";
    public const string InvalidQuantity = "INVALID_QUANTITY";
    public const string StockAdjustmentReasonRequired = "STOCK_ADJUSTMENT_REASON_REQUIRED";

    // Commissions (vidi CommissionRule.cs/CommissionEntry.cs/ICommissionRuleService)
    public const string CommissionRuleAlreadyExists = "COMMISSION_RULE_ALREADY_EXISTS";
    public const string CommissionGroupPercentageNotSupported = "COMMISSION_GROUP_PERCENTAGE_NOT_SUPPORTED";

    /// <summary>P2 (2F, §16.3) — korisnik provizije na prodaju se nakon nastanka provizije mijenja samo korekcijom (Q50).</summary>
    public const string CommissionSaleAlreadyEarned = "COMMISSION_SALE_ALREADY_EARNED";

    /// <summary>P2 (2F, Q50) — korekcija korisnika moguća je samo za aktivnu (Earned) proviziju na prodaju.</summary>
    public const string CommissionEntryNotReassignable = "COMMISSION_ENTRY_NOT_REASSIGNABLE";

    /// <summary>P2 (2F) — provizija na prvu prodaju članarine je već evaluirana; korisnik se može naknadno dodijeliti samo ako ga
    /// nije bilo (naknadna dodjela, commissions.manage + razlog).</summary>
    public const string CommissionSaleAlreadyEvaluated = "COMMISSION_SALE_ALREADY_EVALUATED";

    /// <summary>P2 (2F) — naknadna dodjela korisnika provizije na prodaju moguća je samo kad provizija nije nastala jer korisnika nije
    /// bilo ("nema pravila" i "osnovica 0" su konačni).</summary>
    public const string CommissionSaleNotAssignable = "COMMISSION_SALE_NOT_ASSIGNABLE";

    /// <summary>P2 (2F-11) — korekcija bi prebacila proviziju na zaposlenika bez pravila (nova provizija ne bi nastala); ništa nije
    /// promijenjeno, ponoviti uz ConfirmWithoutCommission = true. Details: employeeId, ruleDate, subjectType, subjectId.</summary>
    public const string CommissionReassignWithoutRule = "COMMISSION_REASSIGN_WITHOUT_RULE";

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

    // Capability-aware GrantGroup authoring (vidi GrantGroupCapabilityAuthoringService, ADR-0023)
    public const string CapabilityUnknown = "CAPABILITY_UNKNOWN";
    public const string CapabilityScopeIllegal = "CAPABILITY_SCOPE_ILLEGAL";
    public const string DuplicateCapabilitySelection = "DUPLICATE_CAPABILITY_SELECTION";
    public const string GrantKeyUnknown = "GRANT_KEY_UNKNOWN";
    public const string GrantAlreadyCapabilityDerived = "GRANT_ALREADY_CAPABILITY_DERIVED";
}
