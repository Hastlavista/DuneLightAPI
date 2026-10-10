namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedini izvor istine za sve "code" vrijednosti unutar WarningDto ({ "code", "details" }).
/// Isti ugovor kao ErrorCodes, ali za neblokirajuća upozorenja vraćena uz uspješan odgovor
/// (create/update i sl.) — stabilan ugovor prema frontendu (i18n se veže na code), ne mijenjati
/// postojeće vrijednosti, samo dodavati nove.
/// </summary>
public static class WarningCodes
{
    // Appointments i Groups — planiranje izvan pravila ne blokira spremanje, samo se prijavljuje.
    public const string EmployeeOnBreak = "EMPLOYEE_ON_BREAK";
    public const string EmployeeAbsent = "EMPLOYEE_ABSENT";

    /// <summary>Konkretan termin/instanca izvan radnog vremena zaposlenika ili poslovnice. Za definiciju
    /// grupnog slota (dan-u-tjednu + vrijeme, bez konkretnog datuma) nosi WarningSlotDetails u details.</summary>
    public const string OutsideWorkingHours = "OUTSIDE_WORKING_HOURS_WARNING";
    public const string CompanyClosedHoliday = "COMPANY_CLOSED_HOLIDAY";

    // Groups
    public const string GroupCapacityExceeded = "GROUP_CAPACITY_EXCEEDED";
    public const string GroupAppointmentUnresolvedBookings = "GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS";
    /// <summary>Phase M1G — close-out grupne sesije: zaposlenik segmenta ima aktivno pravilo provizije koje grupna sesija ne
    /// podržava (Percentage — za grupnu sesiju ne postoji osnovica; ne izmišlja se). Unos se NE stvara; details:
    /// WarningGroupCommissionRuleDetails.</summary>
    public const string GroupCommissionRuleNotSupported = "GROUP_COMMISSION_RULE_NOT_SUPPORTED";

    /// <summary>P2 (2F) — deaktivirano pravilo za uslugu: od datuma deaktivacije za tu uslugu vrijedi opće pravilo zaposlenika.
    /// Za isključenje usluge treba odabrati "Bez provizije".</summary>
    public const string CommissionServiceRuleGeneralApplies = "COMMISSION_SERVICE_RULE_GENERAL_APPLIES";

    /// <summary>T1-10 — spremljene postavke provizija s isključenim "oduzmi popuste članstva": provizija za posjete pokrivene
    /// članarinom računa se od cijene sesije (cjenik / ručni iznos) bez obzira na iznos članarine i NIJE ograničena na primljeni
    /// iznos (jedina iznimka načela "provizija nikad nije veća od primljenog", izričit izbor studija). Spremanje se ne odbija.
    /// T1-11: isto vrijedi za sesije pokrivene NEOGRANIČENIM paketom (nema cijene jedinice, obračun kao članarina); Details =
    /// <see cref="WarningCommissionListPriceSessionsDetails"/> (AppliesTo: Membership, UnlimitedPackage). Kod ostaje isti.</summary>
    public const string CommissionMembershipSessionsAtListPrice = "COMMISSION_MEMBERSHIP_SESSIONS_AT_LIST_PRICE";

    // Roster
    public const string RosterEntryOverlap = "ROSTER_ENTRY_OVERLAP";

    // Employees
    public const string EmployeeHasFutureAppointments = "EMPLOYEE_HAS_FUTURE_APPOINTMENTS";

    // P2 — planovi članarina (details: WarningMembershipLimitDetails / WarningMembershipPlanCompaniesDetails)
    /// <summary>Q49 — limit plana nema učinka: dulji prozor s limitom &gt;= kredit perioda × broj perioda u prozoru, ili limit
    /// usluge &gt;= limit cijelog plana za isti prozor.</summary>
    public const string MembershipLimitWithoutEffect = "MEMBERSHIP_LIMIT_WITHOUT_EFFECT";
    /// <summary>Q29 — nijedna odabrana poslovnica plana nije aktivna; nova prodaja nije moguća.</summary>
    public const string MembershipPlanNoActiveCompany = "MEMBERSHIP_PLAN_NO_ACTIVE_COMPANY";
    /// <summary>Prodaja na poslovnici u kojoj plan ne vrijedi (details: WarningMembershipPlanCompaniesDetails = poslovnice
    /// u kojima vrijedi).</summary>
    public const string MembershipPlanNotValidAtSaleCompany = "MEMBERSHIP_PLAN_NOT_VALID_AT_SALE_COMPANY";
    /// <summary>Otkaz, raniji izlazak ili poništavanje je poništilo zakazanu pauzu koja još nije počela (details:
    /// WarningMembershipPausesDetails).</summary>
    public const string MembershipScheduledPauseCancelled = "MEMBERSHIP_SCHEDULED_PAUSE_CANCELLED";
    /// <summary>Pregled 2B (#7) — rok najave izmjene plana kraći od 14 dana: nepovoljne izmjene (npr. povećanje cijene) se
    /// postojećim članovima primjenjuju bez ili s kratkom najavom (details: WarningMembershipChangeNoticeDetails).</summary>
    public const string MembershipChangeNoticeShort = "MEMBERSHIP_CHANGE_NOTICE_SHORT";
    /// <summary>2C — deaktiviran plan: postojeća članstva završavaju na sljedećoj obnovi ako plan tada još nije aktivan
    /// (details: WarningMembershipsEndingDetails).</summary>
    public const string MembershipPlanMembershipsEnding = "MEMBERSHIP_PLAN_MEMBERSHIPS_ENDING";
    /// <summary>2D (Q24.4) — poništena prodaja: budući termini koje je članarina pokrivala (ili čekali evaluaciju) postaju
    /// nepokriveni (normalna naplata), bez automatskog otkazivanja (details: WarningMembershipSessionsDetails).</summary>
    public const string MembershipVoidedSessionsUncovered = "MEMBERSHIP_VOIDED_SESSIONS_UNCOVERED";
    /// <summary>2E (pregled #1) — fiksna cijena za člana je viša ili jednaka cjeniku usluge u nekoj poslovnici plana, pa se tamo
    /// pogodnost neće primjenjivati (details: WarningMembershipBenefitDetails). Računa se pri čitanju plana (cjenik se mijenja).</summary>
    public const string MembershipBenefitWithoutEffect = "MEMBERSHIP_BENEFIT_WITHOUT_EFFECT";
    /// <summary>Otvoreni checkout: iznos stavke sesije razlikuje se od trenutnog duga te sesije (automatska promjena cijene zbog
    /// članarine ili ručna promjena cijene nakon dodavanja). Stavka se NE mijenja automatski; recepcija je može osvježiti (ukloniti i
    /// ponovno dodati) prije zatvaranja (details: WarningCheckoutItemPriceDetails). Računa se pri čitanju checkouta.</summary>
    public const string CheckoutItemPriceChanged = "CHECKOUT_ITEM_PRICE_CHANGED";

    /// <summary>K1-5 — vraćen termin: upisi liste čekanja istekli zbog otkaza termina NE vraćaju se automatski (details:
    /// WarningWaitlistEntriesDetails) — popis za recepciju.</summary>
    public const string AppointmentRestoredWaitlistNotRestored = "APPOINTMENT_RESTORED_WAITLIST_NOT_RESTORED";

    // K1-9 — dolazak i odrada sesije s neplaćenim dugom (details: WarningParticipationCoverageDetails)
    /// <summary>Sesija nije pokrivena ni paketom ni članarinom, a dug je &gt; 0 (klijent nema prihvatljiv paket; uz članarinu koja ne
    /// pokriva details nosi razlog pokrića). Plaćena ili besplatna sesija ne upozorava.</summary>
    public const string ParticipationNotCovered = "PARTICIPATION_NOT_COVERED";
    /// <summary>Klijent ima prihvatljiv paket za sesiju koji nije odabran/potrošen (details nose pakete) — odabrati ga pri odradi.</summary>
    public const string ParticipationPackageAvailable = "PARTICIPATION_PACKAGE_AVAILABLE";

    // T1-8 — pravila cijena
    /// <summary>Cijena sudjelovanja uzeta iz zadane cijene usluge jer (1) usluga ima stavke cjenika, ali nijedna ne pokriva dan
    /// termina, ili (2) je zadana cijena 0 € (details: WarningPriceNotDefinedDetails). Rezervacija se ne odbija.</summary>
    public const string PriceNotDefined = "PRICE_NOT_DEFINED";
    /// <summary>Zbirno upozorenje generiranja grupnih termina (i upisa člana u već generirane termine): broj termina i datumi na
    /// kojima je cijena uzeta iz zadane cijene usluge (details: WarningPriceNotDefinedOccurrencesDetails).</summary>
    public const string PriceNotDefinedOccurrences = "PRICE_NOT_DEFINED_OCCURRENCES";
    /// <summary>Segmentna naredba je ponovno pročitala cjenik i promijenila cijenu sudjelovanja (details:
    /// WarningParticipationPriceChangedDetails, jedno upozorenje po sudjelovanju).</summary>
    public const string ParticipationPriceChanged = "PARTICIPATION_PRICE_CHANGED";
    /// <summary>Nakon spremanja stavke cjenika postoji rupa (dani bez stavke) između spremljene i susjedne stavke istog
    /// predmeta i konteksta (details: WarningPriceListGapDetails). Spremanje se ne odbija.</summary>
    public const string PriceListGap = "PRICE_LIST_GAP";
    /// <summary>Zakazana buduća sudjelovanja (Confirmed) usluge i konteksta stavke s danom unutar važenja stavke ZADRŽAVAJU svoju
    /// spremljenu cijenu — primjena nove cijene na zakazane termine je P5 (details: WarningPriceListScheduledKeepDetails).</summary>
    public const string PriceListScheduledKeepOldPrice = "PRICE_LIST_SCHEDULED_KEEP_OLD_PRICE";
}
