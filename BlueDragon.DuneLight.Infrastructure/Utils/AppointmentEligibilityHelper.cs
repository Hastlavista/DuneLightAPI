using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Klasifikacija "meke" radne-snage smetnje (odsutnost/pauza/praznik/izvan-radnog-vremena) za jedan
/// kandidat termina (pojedinačni ili jedan occurrence u batchu) — dijeli je AppointmentService
/// (pojedinačni Create/Update/Move i CreateRecurring batch) i GroupService (GenerateAppointments batch),
/// koji su prije ovoga svaki imali vlastitu, gotovo identičnu if/else logiku za isto pitanje ("zašto
/// zaposlenik/poslovnica nije dostupna"). Namjerno OSTAJE static utility (isti obrazac kao
/// WorkingHoursCalculator/ClientPackageEntryMutator u ovom projektu), ne novi DI servis — provjere same
/// (dohvat roster/break/holiday redaka, batch-loading po rasponu) i dalje žive u pozivateljima, jer su
/// prejako vezane uz specifičan oblik učitanih kandidata svakog pozivatelja.
///
/// Prije ovog zahvata sve četiri kategorije bile su isključivo upozorenja (Warnings u odgovoru), nikad
/// blokada — sad su tvrda blokada (throw) OSIM kad pozivatelj eksplicitno zatraži
/// OverrideAvailability=true (i ima ovlast, provjereno kod pozivatelja prije poziva ovamo), kad se
/// umjesto bacanja iznimke vraća isti WarningDto kao i prije (vidljivost bez blokade).
/// </summary>
public static class AppointmentEligibilityHelper
{
    public enum WorkforceViolation
    {
        None,
        EmployeeAbsent,
        EmployeeOnBreak,
        CompanyClosedHoliday,
        OutsideWorkingHours
    }

    public static WorkforceViolation Classify(bool absenceHit, bool breakHit, bool holidayHit, bool withinWorkingHours)
    {
        if (absenceHit)
            return WorkforceViolation.EmployeeAbsent;
        if (breakHit)
            return WorkforceViolation.EmployeeOnBreak;
        if (!withinWorkingHours)
            return holidayHit ? WorkforceViolation.CompanyClosedHoliday : WorkforceViolation.OutsideWorkingHours;

        return WorkforceViolation.None;
    }

    /// <summary>Baca strukturiranu iznimku kad !overrideAvailability, inače dodaje odgovarajući WarningDto u
    /// <paramref name="warnings"/> i vraća bez bacanja. Poziv s WorkforceViolation.None je no-op.</summary>
    public static void ThrowOrWarn(WorkforceViolation violation, bool overrideAvailability, System.Collections.Generic.List<WarningDto> warnings)
    {
        if (violation == WorkforceViolation.None)
            return;

        if (!overrideAvailability)
            throw new BusinessRuleException(ToErrorCode(violation), ToMessage(violation));

        warnings.Add(new WarningDto(ToWarningCode(violation)));
    }

    private static string ToErrorCode(WorkforceViolation violation) => violation switch
    {
        WorkforceViolation.EmployeeAbsent => ErrorCodes.EmployeeAbsent,
        WorkforceViolation.EmployeeOnBreak => ErrorCodes.EmployeeOnBreak,
        WorkforceViolation.CompanyClosedHoliday => ErrorCodes.CompanyClosedHoliday,
        _ => ErrorCodes.OutsideWorkingHours
    };

    private static string ToWarningCode(WorkforceViolation violation) => violation switch
    {
        WorkforceViolation.EmployeeAbsent => WarningCodes.EmployeeAbsent,
        WorkforceViolation.EmployeeOnBreak => WarningCodes.EmployeeOnBreak,
        WorkforceViolation.CompanyClosedHoliday => WarningCodes.CompanyClosedHoliday,
        _ => WarningCodes.OutsideWorkingHours
    };

    private static string ToMessage(WorkforceViolation violation) => violation switch
    {
        WorkforceViolation.EmployeeAbsent => "Zaposlenik je odsutan u ovom terminu.",
        WorkforceViolation.EmployeeOnBreak => "Zaposlenik ima pauzu u ovom terminu.",
        WorkforceViolation.CompanyClosedHoliday => "Poslovnica je zatvorena (praznik) u ovom terminu.",
        _ => "Termin je izvan radnog vremena zaposlenika ili poslovnice."
    };
}
