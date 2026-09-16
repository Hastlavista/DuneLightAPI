using System;
using System.Collections.Generic;
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

    /// <summary>Booking po vlastitom Id-u (ne appointmentId+clientId) s uključenim Appointment.Service — koristi
    /// ICheckoutService.AddBookingItem koje adresira Booking izravno (CheckoutAddBookingItemRequest.BookingId).</summary>
    Task<Booking> GetBookingById(Guid organizationId, Guid id);

    /// <summary>Kao <see cref="GetBookingById(Guid, Guid)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
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

    /// <summary>Puna izmjena uklj. popis klijenata (samo Form=Individual) — spaja postojeće Booking retke, uklanja
    /// izbačene (hard delete — nikad nisu bili odrađeni), dodaje nove kao Confirmed. Amount/suggestedAmount/
    /// overridden se primjenjuju na SVE preživjele Booking retke (postojeće I nove) čiji status NIJE terminalan
    /// (Completed/Cancelled/NoShow) — re-cijenjenje termina prije naplate (vidi spec section 18/20); već
    /// naplaćeni/otkazani/izostali retci se ne diraju. Izostavi (0/0/false) kad pozivatelj svejedno odmah nakon
    /// prepisuje sve retke (npr. CompleteExisting).</summary>
    Task UpdateWithBookings(
        Appointment appointment, List<Guid> clientIds, decimal amount = 0, decimal suggestedAmount = 0, bool overridden = false);

    /// <summary>Kao <see cref="UpdateWithBookings(Appointment, List{Guid}, decimal, decimal, bool)"/>, ali unutar
    /// zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task UpdateWithBookings(
        IUnitOfWork uow, Appointment appointment, List<Guid> clientIds,
        decimal amount = 0, decimal suggestedAmount = 0, bool overridden = false);

    Task Delete(Appointment appointment);

    Task<List<Appointment>> GetOverlappingForEmployee(Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    /// <summary>Termini gdje BAREM JEDAN od zadanih klijenata ima AKTIVAN Booking (Confirmed/Completed — ne
    /// Cancelled/NoShow) koji se preklapa s traženim intervalom. Appointment.Status != Cancelled dodatno filtrira
    /// (cijeli otkazan termin ne blokira nikoga bez obzira na booking retke).</summary>
    Task<List<Appointment>> GetOverlappingForClients(Guid organizationId, List<Guid> clientIds, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    Task<List<Appointment>> GetOverlappingForRoom(Guid organizationId, Guid roomId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    /// <summary>Svi termini trenera unutar raspona (bez otkazanih) — kandidati za preklapanje cijelog
    /// recurring niza odjednom, precizna provjera po occurrenceu radi se u servisu u memoriji.</summary>
    Task<List<Appointment>> GetForEmployeeInRange(Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Kao <see cref="GetForEmployeeInRange"/>, ali po prostoriji — za /recurring provjeru sudara prostorije
    /// cijelog niza odjednom.</summary>
    Task<List<Appointment>> GetForRoomInRange(Guid organizationId, Guid roomId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Kao <see cref="GetForEmployeeInRange"/>, ali za više zaposlenika u jednom upitu (bez otkazanih)
    /// — za available-slots, izbjegava upit po zaposleniku u petlji.</summary>
    Task<List<Appointment>> GetForEmployeesInRange(Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Svi termini s AKTIVNIM Bookingom bilo kojeg od klijenata unutar raspona — kandidati za
    /// preklapanje cijelog recurring niza odjednom, precizna provjera po occurrenceu radi se u servisu u memoriji.</summary>
    Task<List<Appointment>> GetForClientsInRange(Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    Task<List<Appointment>> GetForSchedule(Guid organizationId, AppointmentScheduleQuery query);

    /// <summary>Termini jedne Company unutar [dayStart, dayEnd) s punim financijskim grafom (Bookings.CheckoutItems.
    /// Allocations.Payment) uz Group.Members(IsActive) — za OperationalDashboardService (vidi spec section 27/28,
    /// zaseban od GetForSchedule jer ta metoda namjerno NE učitava financijski graf za jeftinije kalendarske upite).</summary>
    Task<List<Appointment>> GetForDashboard(Guid organizationId, Guid companyId, DateTimeOffset dayStart, DateTimeOffset dayEnd);

    /// <summary>Batch insert za recurring niz — appointment.Bookings mora biti popunjen za svaki termin prije poziva, jedan SaveChangesAsync za cijeli niz.</summary>
    Task AddRange(List<Appointment> appointments);

    Task<(List<Appointment> Items, int TotalCount)> GetByClient(Guid organizationId, Guid clientId, PagedRequest request);

    /// <summary>Povijest odrađenih termina po zaposleniku (samo Completed), najnoviji prvi — vidi GetByClient.</summary>
    Task<(List<Appointment> Items, int TotalCount)> GetByEmployee(Guid organizationId, Guid employeeId, PagedRequest request);

    /// <summary>Budući, još neodržani termini grupe (Scheduled, StartsAt u budućnosti) s uključenim Bookings —
    /// za sinkronizaciju Booking redaka kod pridruživanja/napuštanja člana (GroupService.AddMember/RemoveMember).
    /// Deaktivacija grupe ih NE dira (vidi GroupService.SetActive) — ostaju netaknuti do eksplicitnog
    /// Appointment cancel/delete. Unutar zajedničke transakcije.</summary>
    Task<List<Appointment>> GetFutureScheduledForGroup(IUnitOfWork uow, Guid organizationId, Guid groupId);

    Task<bool> HasFutureScheduledForEmployee(Guid organizationId, Guid employeeId);

    Task<bool> HasAnyForClient(Guid organizationId, Guid clientId);

    /// <summary>Ima li klijent ijedan budući termin statusa Scheduled s aktivnim Bookingom — koristi ClientService.Anonymize.</summary>
    Task<bool> HasFutureScheduledForClient(Guid organizationId, Guid clientId);

    /// <summary>Batch broj Booking redaka statusa NoShow po klijentu, u jednom upitu (GROUP BY) — izbjegava N+1 kod liste/detalja klijenata.
    /// Klijent bez ijednog NoShow bookinga izostaje iz rezultata.</summary>
    Task<Dictionary<Guid, int>> GetNoShowCountsByClientIds(Guid organizationId, List<Guid> clientIds);

    /// <summary>Zaključava Appointment redak (SELECT ... FOR UPDATE) unutar zajedničke transakcije, s uključenim
    /// Group — koristi IWaitlistService.PromoteEligibleWaiters da serijalizira konkurentne promocije/otkazivanja
    /// na ISTOM terminu (vidi tamo za točnu garanciju). Null ako termin ne postoji.</summary>
    Task<Appointment> GetForUpdateWithGroup(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Zaključava Appointment redak (SELECT ... FOR UPDATE) unutar zajedničke transakcije, bare redak bez
    /// ikakvih navigacija — najuži lock za prijelaze koji ne trebaju čitati Bookings (npr. CompleteExisting/
    /// CompleteGroupAppointment status re-check prije mutacije, vidi AppointmentService). Isti obrazac kao
    /// ICheckoutHandler.GetForUpdate. Null ako termin ne postoji.</summary>
    Task<Appointment> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Kao <see cref="GetForUpdate"/>, ali s uključenim Bookings (bez daljnjih ThenInclude) — za prijelaze
    /// koji trebaju čitati/mijenjati Booking retke pod istim lockom (npr. ChangeToTerminalStatus Cancel/MarkNoShow,
    /// vidi AppointmentService). Null ako termin ne postoji.</summary>
    Task<Appointment> GetForUpdateWithBookings(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Broj Booking redaka statusa Confirmed na terminu — jedini izvor istine za "koliko je mjesta
    /// zauzeto" na grupnom occurrenceu (vidi IWaitlistService, ne duplicirati ovu logiku drugdje).</summary>
    Task<int> CountConfirmedBookings(Guid organizationId, Guid appointmentId);

    /// <summary>Kao <see cref="CountConfirmedBookings(Guid, Guid)"/>, ali čita iz uow.Context unutar zajedničke
    /// transakcije (npr. odmah nakon <see cref="GetForUpdateWithGroup"/> zaključavanja) umjesto da otvara drugi
    /// DbContext usred transakcije — koristi WaitlistService.PromoteEligibleWaiters i BookingService (ad-hoc/
    /// check-in guest Booking) da ne dupliciraju istu COUNT logiku.</summary>
    Task<int> CountConfirmedBookings(IUnitOfWork uow, Guid organizationId, Guid appointmentId);

    /// <summary>Agregirane brojke dolazaka jednog klijenta za Povijest klijenta — jedan upit nad Booking (nakon
    /// uvođenja Bookinga individualni i grupni termini dijele istu tablicu, za razliku od stare podjele
    /// AppointmentClient/AppointmentAttendance). activeGroupIds ulazi u izračun NextVisitAt jer budući grupni
    /// termini (već generirani) sad IMAJU Confirmed Booking odmah po generiranju (vidi GroupService.GenerateAppointments),
    /// ali parametar je zadržan radi kompatibilnosti poziva/testiranja buduće grupne logike.</summary>
    Task<ClientAppointmentStatsDto> GetStatsForClient(Guid organizationId, Guid clientId, List<Guid> activeGroupIds);
}
