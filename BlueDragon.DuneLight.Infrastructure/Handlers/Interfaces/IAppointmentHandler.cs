using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IAppointmentHandler
{
    /// <summary>appointment.Bookings mora biti popunjen prije poziva — cascade insert.</summary>
    Task Add(Appointment appointment);

    /// <summary>Kao <see cref="Add(Appointment)"/>, ali unutar zajedničke transakcije (npr. termin + odbijanje ulaska
    /// iz paketa + audit log kao jedna atomična cjelina) — vidi IUnitOfWork.</summary>
    Task Add(IUnitOfWork uow, Appointment appointment);

    Task<Appointment> GetById(Guid organizationId, Guid id);

    /// <summary>Bare redak, BEZ Bookings/Service/Employee/Company navigacija — za pripremu mutacije (izbjegava EF tracking sudar).</summary>
    Task<Appointment> GetByIdLight(Guid organizationId, Guid id);

    /// <summary>Termin (bilo koji Form) s uključenim Group.Members(IsActive).Client i Bookings.Client — null ako
    /// ne postoji. Koristi BookingService/GroupAttendanceService za check-in/attendance flow.</summary>
    Task<Appointment> GetWithBookingsForMutation(Guid organizationId, Guid id);

    /// <summary>Bare Booking redak za pripremu mutacije, ili null ako ne postoji.</summary>
    Task<Booking> GetBooking(Guid organizationId, Guid appointmentId, Guid clientId);

    /// <summary>Booking po vlastitom Id-u unutar zajedničke transakcije, s terminom i segmentima (usluga, zaposlenici) —
    /// ICheckoutService.AddBookingItem ga čita za Booking kojem pripada adresirano sudjelovanje (ParticipationId).</summary>
    Task<Booking> GetBookingById(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Kao <see cref="GetBooking(Guid, Guid, Guid)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task<Booking> GetBooking(IUnitOfWork uow, Guid organizationId, Guid appointmentId, Guid clientId);

    /// <summary>Batch verzija za listu klijenata u jednom upitu (izbjegava N+1) — unutar zajedničke transakcije.</summary>
    Task<List<Booking>> GetBookings(IUnitOfWork uow, Guid organizationId, Guid appointmentId, List<Guid> clientIds);

    Task AddBooking(IUnitOfWork uow, Booking booking);

    Task UpdateBooking(Booking booking);

    /// <summary>Kao <see cref="UpdateBooking(Booking)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task UpdateBooking(IUnitOfWork uow, Booking booking);

    /// <summary>Samo skalarna polja termina, bez diranja Booking redaka — koristi se za Complete/Cancel prijelaze.</summary>
    Task UpdateScalar(Appointment appointment);

    /// <summary>Kao <see cref="UpdateScalar(Appointment)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task UpdateScalar(IUnitOfWork uow, Appointment appointment);

    Task Delete(Appointment appointment);

    Task<List<Appointment>> GetForSchedule(Guid organizationId, AppointmentScheduleQuery query);

    /// <summary>Termini jedne Company unutar [dayStart, dayEnd) s punim financijskim grafom (Bookings.Participations.CheckoutItems.
    /// Allocations.Payment) uz Group.Members(IsActive) — za OperationalDashboardService (vidi spec section 27/28,
    /// zaseban od GetForSchedule jer ta metoda namjerno NE učitava financijski graf za jeftinije kalendarske upite).</summary>
    Task<List<Appointment>> GetForDashboard(Guid organizationId, Guid companyId, DateTimeOffset dayStart, DateTimeOffset dayEnd);

    /// <summary>Batch insert za recurring niz — appointment.Bookings mora biti popunjen za svaki termin prije poziva, jedan SaveChangesAsync za cijeli niz.</summary>
    Task AddRange(IUnitOfWork uow, List<Appointment> appointments);

    Task<(List<Appointment> Items, int TotalCount)> GetByClient(Guid organizationId, Guid clientId, PagedRequest request);

    /// <summary>Povijest odrađenih termina po zaposleniku (Phase M1A: segment zaposlenika ima barem jedno Completed sudjelovanje), najnoviji prvi — vidi GetByClient.</summary>
    Task<(List<Appointment> Items, int TotalCount)> GetByEmployee(Guid organizationId, Guid employeeId, PagedRequest request);

    /// <summary>Budući, još neodržani termini grupe (Scheduled, StartsAt u budućnosti) s uključenim Bookings —
    /// za sinkronizaciju Booking redaka kod pridruživanja/napuštanja člana (GroupService.AddMember/RemoveMember).
    /// Deaktivacija grupe ih NE dira (vidi GroupService.SetActive) — ostaju netaknuti do eksplicitnog
    /// Appointment cancel/delete. Unutar zajedničke transakcije.</summary>
    Task<List<Appointment>> GetFutureScheduledForGroup(IUnitOfWork uow, Guid organizationId, Guid groupId);

    Task<bool> HasFutureScheduledForEmployee(Guid organizationId, Guid employeeId);

    Task<bool> HasAnyForClient(Guid organizationId, Guid clientId);

    /// <summary>Ima li klijent ijedno Confirmed sudjelovanje na budućem segmentu termina statusa Scheduled — koristi
    /// ClientService.Anonymize.</summary>
    Task<bool> HasFutureScheduledForClient(Guid organizationId, Guid clientId);

    /// <summary>Batch broj NoShow SUDJELOVANJA po klijentu (Phase M0: izvršna jedinica, ne Booking), u jednom upitu (GROUP
    /// BY) — izbjegava N+1 kod liste/detalja klijenata. Klijent bez ijednog NoShow sudjelovanja izostaje iz rezultata.</summary>
    Task<Dictionary<Guid, int>> GetNoShowCountsByClientIds(Guid organizationId, List<Guid> clientIds);

    /// <summary>Zaključava Appointment redak (SELECT ... FOR UPDATE) unutar zajedničke transakcije, s uključenim
    /// Group — koristi IWaitlistService.PromoteEligibleWaiters da serijalizira konkurentne promocije/otkazivanja
    /// na ISTOM terminu (vidi tamo za točnu garanciju). Null ako termin ne postoji.</summary>
    Task<Appointment> GetForUpdateWithGroup(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Zaključava Appointment redak (SELECT ... FOR UPDATE) unutar zajedničke transakcije, bare redak bez
    /// ikakvih navigacija — najuži lock za prijelaze koji ne trebaju čitati Bookings (npr. ChangeNote/
    /// CompleteGroupAppointment re-check prije mutacije, vidi AppointmentService). Isti obrazac kao
    /// ICheckoutHandler.GetForUpdate. Null ako termin ne postoji.</summary>
    Task<Appointment> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Phase M1E: zaključan (FOR UPDATE) i PRAĆEN agregat za segmentne naredbe — segmenti s dodjelama zaposlenika i
    /// resursa, Bookinzi sa sudjelovanjima (potrošnje paketa i stavke checkouta za pravilo povijesti). Pozivatelj je PRIJE
    /// zaključao subjekte rasporeda (SchedulingLockOrder).</summary>
    Task<Appointment> GetForSegmentMutation(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Phase M1E: zaključava Appointment redak (FOR UPDATE) i čita NEPRAĆENO svježe stanje segmenata (zaposlenici,
    /// resursi) i sudjelovanja — za <see cref="Utils.SegmentSnapshot"/> provjeru u tokovima koji već prate (zastarjeli)
    /// agregat ili ga kasnije spremaju kao nepraćen graf. Null ako termin ne postoji.</summary>
    Task<Appointment> GetLockedSegmentState(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Kao <see cref="GetForUpdate"/>, ali s uključenim Bookings (bez daljnjih ThenInclude) — za prijelaze
    /// koji trebaju čitati/mijenjati Booking retke pod istim lockom (npr. ChangeToTerminalStatus Cancel/MarkNoShow,
    /// vidi AppointmentService). Null ako termin ne postoji.</summary>
    Task<Appointment> GetForUpdateWithBookings(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Phase M0: broj sudjelovanja statusa Confirmed na SEGMENTU — jedini izvor istine za "koliko je mjesta
    /// zauzeto" na grupnom occurrenceu (vidi IWaitlistService, ne duplicirati ovu logiku drugdje). Segmentno, ne po
    /// Bookingu: Booking s više sudjelovanja zauzima mjesto samo na segmentima na kojima sudjeluje.</summary>
    Task<int> CountConfirmedOnSegment(Guid organizationId, Guid appointmentSegmentId);

    /// <summary>Kao <see cref="CountConfirmedOnSegment(Guid, Guid)"/>, ali čita iz uow.Context unutar zajedničke
    /// transakcije (npr. odmah nakon <see cref="GetForUpdateWithGroup"/> zaključavanja) umjesto da otvara drugi
    /// DbContext usred transakcije — koristi WaitlistService.PromoteEligibleWaiters i BookingService (ad-hoc/
    /// check-in guest Booking) da ne dupliciraju istu COUNT logiku.</summary>
    Task<int> CountConfirmedOnSegment(IUnitOfWork uow, Guid organizationId, Guid appointmentSegmentId);

    /// <summary>Agregirane brojke dolazaka jednog klijenta za Povijest klijenta — jedan upit nad Booking (nakon
    /// uvođenja Bookinga individualni i grupni termini dijele istu tablicu, za razliku od stare podjele
    /// AppointmentClient/AppointmentAttendance). activeGroupIds ulazi u izračun NextVisitAt jer budući grupni
    /// termini (već generirani) sad IMAJU Confirmed Booking odmah po generiranju (vidi GroupService.GenerateAppointments).</summary>
    Task<ClientAppointmentStatsDto> GetStatsForClient(Guid organizationId, Guid clientId, List<Guid> activeGroupIds);
}
