using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Izvor cijene segmenta ili predloška grupe: način + zaposlenik čije razine cjenika se koriste (samo za Employee).</summary>
public readonly record struct PricingSourceValue(SegmentPricingMode Mode, Guid? EmployeeId)
{
    public static readonly PricingSourceValue Standard = new(SegmentPricingMode.Standard, null);

    /// <summary>Zaposlenik za razrješavanje cijene (null = Standard, razine zaposlenika se preskaču).</summary>
    public Guid? PricingEmployeeId => Mode == SegmentPricingMode.Employee ? EmployeeId : null;
}

/// <summary>
/// Phase M1G — JEDINO pravilo izvora cijene segmenta (i predloška grupe) prema broju zaposlenika:
/// <list type="bullet">
/// <item>0 zaposlenika → Standard (zahtjev smije izostaviti izvor ili navesti Standard bez zaposlenika);</item>
/// <item>1 zaposlenik → AUTOMATSKI Employee/taj zaposlenik (zahtjev smije izostaviti izvor ili navesti upravo to; Standard
/// NIJE dopušten — izvođač je jednoznačan);</item>
/// <item>2+ zaposlenika → izvor je OBAVEZAN i EKSPLICITAN: Standard (bez zaposlenika) ili Employee + jedan od zaposlenika
/// segmenta. Bez izvora: PRICING_SOURCE_REQUIRED — nikad "prvi", najjeftiniji, prosjek, abecedno ili korisnik provizije.</item>
/// </list>
/// Svaka izmjena skupa zaposlenika koja rezultira s 2+ zaposlenika traži izvor ponovno (i kad prethodni zaposlenik izvora
/// ostaje dodijeljen) — namjera se ne zaključuje. Izvor NIJE vlasništvo, "glavni" zaposlenik ni korisnik provizije.
/// </summary>
public static class SegmentPricingSource
{
    public static PricingSourceValue Normalize(IReadOnlyCollection<Guid> employeeIds, SegmentPricingMode? mode, Guid? pricingEmployeeId)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        if (mode == null && pricingEmployeeId.HasValue)
            throw Invalid("PricingEmployeeId zahtijeva PricingMode = Employee.");
        if (mode == SegmentPricingMode.Standard && pricingEmployeeId.HasValue)
            throw Invalid("Standardni izvor cijene ne smije imati PricingEmployeeId.");
        if (mode == SegmentPricingMode.Employee && !pricingEmployeeId.HasValue)
            throw Invalid("Izvor cijene Employee zahtijeva PricingEmployeeId.");
        if (pricingEmployeeId.HasValue && !employeeIds.Contains(pricingEmployeeId.Value))
            throw Invalid("Zaposlenik izvora cijene mora biti dodijeljen segmentu.");

        switch (employeeIds.Count)
        {
            case 0:
                if (mode == SegmentPricingMode.Employee)
                    throw Invalid("Segment bez zaposlenika ima isključivo standardni izvor cijene.");
                return PricingSourceValue.Standard;
            case 1:
                if (mode == SegmentPricingMode.Standard)
                    throw Invalid("Segment s jednim zaposlenikom uvijek koristi cijenu tog zaposlenika (izvor se ne bira).");
                return new PricingSourceValue(SegmentPricingMode.Employee, employeeIds.Single());
            default:
                if (mode == null)
                    throw new ValidationAppException(ErrorCodes.PricingSourceRequired,
                        "Segment s više zaposlenika zahtijeva eksplicitan izvor cijene (PricingMode: Standard ili Employee uz PricingEmployeeId).");
                return mode == SegmentPricingMode.Standard
                    ? PricingSourceValue.Standard
                    : new PricingSourceValue(SegmentPricingMode.Employee, pricingEmployeeId);
        }
    }

    public static PricingSourceValue Of(AppointmentSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return new PricingSourceValue(segment.PricingMode, segment.PricingEmployeeId);
    }

    /// <summary>Zaposlenik za razrješavanje cijene segmenta (null = Standard).</summary>
    public static Guid? PricingEmployeeOf(AppointmentSegment segment) => Of(segment).PricingEmployeeId;

    public static void Apply(AppointmentSegment segment, PricingSourceValue source, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.PricingMode == source.Mode && segment.PricingEmployeeId == source.EmployeeId)
            return;
        segment.PricingMode = source.Mode;
        segment.PricingEmployeeId = source.EmployeeId;
        segment.UpdatedAt = updatedAt;
    }

    private static ValidationAppException Invalid(string message) => new(ErrorCodes.InvalidPricingSource, message);
}
