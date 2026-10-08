using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Groups;

[ApiController]
[Route("api/groups/appointments/{id:guid}")]
[Produces("application/json")]
public class GroupAppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public GroupAppointmentsController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    /// <summary>Close-out grupne sesije (poslovna činjenica ClosedOutAt/By; status termina se izvodi iz sudjelovanja) — ne dira
    /// nijedno sudjelovanje, svako se razrješava neovisno kroz /attendance. P1 (D9): nema automatskog izostanka —
    /// nerazriješena (Confirmed) sudjelovanja ostaju nepromijenjena uz upozorenje GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS.</summary>
    [HttpPatch("complete")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> Complete(Guid id)
    {
        return Ok(await _appointmentService.CompleteGroupAppointment(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), id));
    }
}
