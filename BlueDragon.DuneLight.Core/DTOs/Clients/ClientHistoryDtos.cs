using System;

namespace BlueDragon.DuneLight.Core.DTOs.Clients;

/// <summary>
/// Sažetak povijesti klijenta — dopuna postojećim granularnim endpointima (GET /api/appointments/by-client/{id}
/// za kronološki niz termina, GET /api/clients/{id}/packages za pakete, GET /api/clients/{id}/groups za
/// članstva u grupama). Ovaj DTO nosi samo brojke koje bi inače frontend morao sam izračunati iz tih lista.
/// </summary>
public class ClientHistorySummaryDto
{
    /// <summary>Datum kad je klijent unesen u sustav (Client.CreatedAt).</summary>
    public DateTimeOffset ClientSince { get; set; }

    /// <summary>Zbroj odrađenih individualnih termina (Status=Completed) i odrađenih grupnih dolazaka (Attended=true).</summary>
    public int CompletedVisitsCount { get; set; }

    /// <summary>Zbroj individualnih (Status=NoShow) i grupnih (Attended=false) izostanaka.</summary>
    public int NoShowCount { get; set; }

    /// <summary>Otkazani individualni termini na kojima je klijent bio (Status=Cancelled). Grupni termini
    /// nemaju per-klijent otkazivanje pa nisu uključeni.</summary>
    public int CancelledCount { get; set; }

    /// <summary>Datum zadnjeg odrađenog dolaska (individualni ili grupni), null ako klijent nikad nije odradio termin.</summary>
    public DateTimeOffset? LastVisitAt { get; set; }

    /// <summary>Datum sljedećeg zakazanog termina (individualni Scheduled, ili već generirani budući termin
    /// aktivne grupe kojoj je klijent član), null ako ništa nije zakazano.</summary>
    public DateTimeOffset? NextVisitAt { get; set; }

    /// <summary>Broj paketa sa Status=Active i ExpiryDate u budućnosti (isti kriterij kao GetEligibleForService).</summary>
    public int ActivePackagesCount { get; set; }

    /// <summary>Broj grupa u kojima je klijent trenutno aktivan član (GroupMember.IsActive).</summary>
    public int ActiveGroupMembershipsCount { get; set; }
}
