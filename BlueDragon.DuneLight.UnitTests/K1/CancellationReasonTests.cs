#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests.K1;

/// <summary>
/// K1-4 (12.3) — šifrarnik razloga: šifra vrijedi samo za odabrane događaje i dok je aktivna; sudjelovanje pamti naziv iz
/// trenutka događaja; obaveznost po postavci organizacije (samo kad postoji aktivna šifra za događaj); za otkaz studija je
/// dovoljna šifra ILI tekst; vrijedi i za otkaz cijelog termina.
/// </summary>
public class CancellationReasonTests
{
    private static ICancellationReasonService Reasons(SchedulingWorld w) => w.Resolve<ICancellationReasonService>();

    private static Task<CancellationReasonDto> AddReason(SchedulingWorld w, string name, bool client = false, bool business = false, bool noShow = false) =>
        Reasons(w).Create(w.OrganizationId, w.ActorUserId, new CancellationReasonUpsertRequest
        {
            Name = name, AppliesToClientCancellation = client, AppliesToBusinessCancellation = business, AppliesToNoShow = noShow
        });

    private static async Task<Guid> Booked(SchedulingWorld w) =>
        await w.ParticipationIdOnOnlySegment((await w.CreateAppointment(SchedulingWorld.Future(10))).Id, w.Client.Id.Value);

    private static Task<BookingDto> ClientCancel(SchedulingWorld w, Guid participationId, Guid? codeId) =>
        w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId, new BookingSetStatusRequest
        {
            Status = BookingStatus.Cancelled, CancellationInitiator = CancellationInitiator.Client, CancellationReasonCodeId = codeId
        });

    [Fact]
    public async Task ClientCancellation_StoresTheCodeAndANameSnapshot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientCancellation_StoresTheCodeAndANameSnapshot));
        CancellationReasonDto illness = await AddReason(w, "Bolest", client: true);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        BookingDto cancelled = await ClientCancel(w, await w.ParticipationIdOnOnlySegment(created.Id, w.Client.Id.Value), illness.Id);

        BookingParticipationDto p = Assert.Single(cancelled.Participations);
        Assert.Equal(illness.Id, p.CancellationReasonCodeId);
        Assert.Equal("Bolest", p.CancellationReasonCodeName);

        // Preimenovanje šifre ne mijenja povijest.
        await Reasons(w).Update(w.OrganizationId, w.ActorUserId, illness.Id,
            new CancellationReasonUpsertRequest { Name = "Bolest (s potvrdom)", AppliesToClientCancellation = true });
        BookingDto reread = Assert.Single(await w.Bookings.GetForAppointment(w.OrganizationId, created.Id));
        Assert.Equal("Bolest", Assert.Single(reread.Participations).CancellationReasonCodeName);
    }

    [Fact]
    public async Task Code_MustBeActive_AndApplyToTheEvent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Code_MustBeActive_AndApplyToTheEvent));
        CancellationReasonDto noShowOnly = await AddReason(w, "Nije došao", noShow: true);
        CancellationReasonDto retired = await AddReason(w, "Staro", client: true);
        await Reasons(w).SetActive(w.OrganizationId, w.ActorUserId, retired.Id, false);
        Guid participationId = await Booked(w);

        await SchedulingAssert.BusinessRule(ErrorCodes.CancellationReasonNotApplicable, () => ClientCancel(w, participationId, noShowOnly.Id));
        await SchedulingAssert.BusinessRule(ErrorCodes.CancellationReasonNotApplicable, () => ClientCancel(w, participationId, retired.Id));
    }

    [Fact]
    public async Task Required_OnlyWhenTheSettingIsOn_AndAnActiveCodeExistsForTheEvent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Required_OnlyWhenTheSettingIsOn_AndAnActiveCodeExistsForTheEvent));
        await w.Resolve<IOrganizationSettingsService>().UpdateCancellationReasonRules(w.OrganizationId, w.ActorUserId,
            new OrganizationCancellationReasonRulesUpdateRequest { RequiredForClientCancellation = true });

        // Bez ijedne šifre za otkaz klijenta nema što odabrati — prolazi.
        Assert.Equal(BookingStatusSummary.Cancelled, (await ClientCancel(w, await Booked(w), null)).Status);

        await AddReason(w, "Bolest", client: true);
        Guid second = await w.ParticipationIdOnOnlySegment((await w.CreateAppointment(SchedulingWorld.Future(14))).Id, w.Client.Id.Value);
        ValidationAppException ex = await Assert.ThrowsAsync<ValidationAppException>(() => ClientCancel(w, second, null));
        Assert.Equal(ErrorCodes.CancellationReasonRequired, ex.Code);
    }

    [Fact]
    public async Task BusinessCancellation_OfTheWholeAppointment_AcceptsACodeInsteadOfText()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BusinessCancellation_OfTheWholeAppointment_AcceptsACodeInsteadOfText));
        CancellationReasonDto trainerSick = await AddReason(w, "Trener bolestan", business: true);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto cancelled = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentCancelRequest { CancellationInitiator = CancellationInitiator.Business, CancellationReasonCodeId = trainerSick.Id });

        Assert.Equal(trainerSick.Id, cancelled.CancellationReasonCodeId);
        Assert.Equal("Trener bolestan", cancelled.CancellationReasonCodeName);
        BookingParticipationDto p = Assert.Single(Assert.Single(cancelled.Bookings).Participations);
        Assert.Equal(trainerSick.Id, p.CancellationReasonCodeId);

        // Bez teksta i bez šifre i dalje se odbija.
        AppointmentDto other = await w.CreateAppointment(SchedulingWorld.Future(14));
        await Assert.ThrowsAsync<ValidationAppException>(() => w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, other.Id,
            new AppointmentCancelRequest { CancellationInitiator = CancellationInitiator.Business }));
    }

    [Fact]
    public async Task CorrectionBackToConfirmed_ClearsTheCode()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CorrectionBackToConfirmed_ClearsTheCode));
        CancellationReasonDto illness = await AddReason(w, "Bolest", client: true);
        Guid participationId = await Booked(w);
        await ClientCancel(w, participationId, illness.Id);

        BookingDto confirmed = await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId,
            new BookingSetStatusRequest { Status = BookingStatus.Confirmed });

        BookingParticipationDto p = Assert.Single(confirmed.Participations);
        Assert.Null(p.CancellationReasonCodeId);
        Assert.Null(p.CancellationReasonCodeName);
    }
}
