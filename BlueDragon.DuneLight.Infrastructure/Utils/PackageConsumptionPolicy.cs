using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Phase D3B3A — na kojem prijelazu životnog ciklusa sudjelovanja postavka organizacije troši paket.</summary>
public static class PackageConsumptionPolicy
{
    public static bool ConsumesOn(PackageConsumptionTiming timing, BookingStatus trigger) => timing switch
    {
        PackageConsumptionTiming.OnCompletion => trigger == BookingStatus.Completed,
        _ => false
    };
}
