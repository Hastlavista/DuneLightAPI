using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Phase M1E — SEGMENTNE naredbe višesegmentnog termina (izvršni podaci se mijenjaju po SegmentId-u) i naredbe sudionika
/// (klijent se pridružuje ODABRANIM segmentima; uklanja se jedno sudjelovanje). Zajednički obrazac:
/// <list type="number">
/// <item>pročitano stanje + strukturna validacija + dostupnost radne snage (prije transakcije);</item>
/// <item>transakcija: subjekti rasporeda (unija STARIH i NOVIH zaposlenika/klijenata/prostorija/resursa, SchedulingLockOrder)
/// i tvrde invarijante (preklapanje zaposlenika/klijenta, kapacitet prostorije/resursa — isključuje se SAMO segment koji se
/// prepisuje, sestrinski segmenti ostaju vidljivi);</item>
/// <item>Appointment redak (FOR UPDATE) + provjera da se segment nije promijenio od čitanja (SegmentSnapshot →
/// CONCURRENCY_CONFLICT);</item>
/// <item>izmjena praćenog agregata, ponovno izvođenje životnog ciklusa termina, commit.</item>
/// </list>
/// Vlasništvo: own-opseg smije mijenjati segment kojem je dodijeljen (i dodijeliti novi segment samo sebi).
/// </summary>
public partial class AppointmentService
{
    /// <summary>Ciljno stanje segmenta koji se prepisuje. PricingSource null = izvor cijene segmenta se ne mijenja.</summary>
    private sealed record SegmentTarget(
        DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd, Guid ServiceId, IReadOnlyList<Guid> EmployeeIds, Guid? RoomId,
        IReadOnlyList<ResourceClaim> Resources, bool Reprice, string Change, PricingSourceValue? PricingSource = null);

    public Task<AppointmentDto> ChangeSegmentTime(
        Guid organizationId, Guid userId, bool hasFullScope, Guid segmentId, AppointmentSegmentTimeChangeRequest request) =>
        RewriteSegment(organizationId, userId, hasFullScope, segmentId, async (appointment, segment, resources) =>
        {
            DateTimeOffset end = request.PlannedEnd ?? request.PlannedStart + (segment.PlannedEnd - segment.PlannedStart);
            if (end <= request.PlannedStart)
                throw new ValidationAppException("Kraj segmenta mora biti nakon početka.");
            List<Guid> employees = segment.Employees.Select(e => e.EmployeeId).ToList();
            List<WarningDto> warnings = await EnsureWorkforceAvailability(
                organizationId, employees, appointment.CompanyId, request.PlannedStart, end, request.OverrideAvailability && hasFullScope);
            return (new SegmentTarget(request.PlannedStart, end, segment.ServiceId, employees, segment.RoomId, resources, Reprice: true,
                $"time:{request.PlannedStart:O}-{end:O}"), warnings);
        });

    public Task<AppointmentDto> ChangeSegmentService(
        Guid organizationId, Guid userId, bool hasFullScope, Guid segmentId, AppointmentSegmentServiceChangeRequest request) =>
        RewriteSegment(organizationId, userId, hasFullScope, segmentId, async (appointment, segment, resources) =>
        {
            ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
            List<Guid> employees = segment.Employees.Select(e => e.EmployeeId).ToList();
            foreach (Guid employeeId in employees)
                await EnsureStructuralEligibility(organizationId, service, appointment.CompanyId, employeeId);

            DateTimeOffset end = request.PlannedEnd
                ?? (request.UseServiceDuration ? segment.PlannedStart.AddMinutes(service.DefaultDurationMinutes) : segment.PlannedEnd);
            if (end <= segment.PlannedStart)
                throw new ValidationAppException("Kraj segmenta mora biti nakon početka.");
            List<WarningDto> warnings = end == segment.PlannedEnd
                ? new List<WarningDto>()
                : await EnsureWorkforceAvailability(
                    organizationId, employees, appointment.CompanyId, segment.PlannedStart, end, request.OverrideAvailability && hasFullScope);
            return (new SegmentTarget(segment.PlannedStart, end, request.ServiceId, employees, segment.RoomId, resources, Reprice: true,
                $"service:{request.ServiceId}"), warnings);
        });

    public Task<AppointmentDto> ChangeSegmentEmployees(
        Guid organizationId, Guid userId, bool hasFullScope, Guid segmentId, AppointmentSegmentEmployeesChangeRequest request) =>
        RewriteSegment(organizationId, userId, hasFullScope, segmentId, async (appointment, segment, resources) =>
        {
            List<Guid> employees = (request.EmployeeIds ?? new List<Guid>()).ToList();
            EnsureEmployeeSet(employees, allowEmpty: appointment.Form == AppointmentForm.Group);
            // Phase M1G: rezultat s 2+ zaposlenika uvijek traži eksplicitan izvor cijene (bez zaključivanja namjere).
            PricingSourceValue pricingSource = SegmentPricingSource.Normalize(employees, request.PricingMode, request.PricingEmployeeId);
            // Own-opseg (vlasnik segmenta) ne smije dodavati NI uklanjati suradnike: stari i novi skup smiju sadržavati samo
            // pozivatelja — izmjena popisa zaposlenika s drugim zaposlenicima zahtijeva appointments.write.all.
            await AppointmentOwnership.EnsureCallerIsEmployee(_employeeHandler, organizationId, userId, hasFullScope,
                employees.Concat(segment.Employees.Select(e => e.EmployeeId)), NotOwnerMessage);
            if (!employees.ToHashSet().SetEquals(segment.Employees.Select(e => e.EmployeeId)))
                EnsureNoExecutionHistory(appointment, segment);

            ServiceEntity service = await LoadServiceOrThrow(organizationId, segment.ServiceId);
            foreach (Guid employeeId in employees)
                await EnsureStructuralEligibility(organizationId, service, appointment.CompanyId, employeeId);
            List<WarningDto> warnings = await EnsureWorkforceAvailability(
                organizationId, employees, appointment.CompanyId, segment.PlannedStart, segment.PlannedEnd, request.OverrideAvailability && hasFullScope);
            return (new SegmentTarget(segment.PlannedStart, segment.PlannedEnd, segment.ServiceId, employees, segment.RoomId, resources, Reprice: true,
                $"employees:{string.Join(",", employees.OrderBy(id => id))};pricing:{pricingSource.Mode}:{pricingSource.EmployeeId}", pricingSource), warnings);
        });

    /// <summary>Phase M1G — samo izvor cijene segmenta (skup zaposlenika ostaje isti, zauzetost se ne mijenja — validacija
    /// zauzetosti je no-op nad nepromijenjenim stanjem). Aktivna sudjelovanja se ponovno cijene.</summary>
    public Task<AppointmentDto> ChangeSegmentPricingSource(
        Guid organizationId, Guid userId, bool hasFullScope, Guid segmentId, AppointmentSegmentPricingSourceChangeRequest request) =>
        RewriteSegment(organizationId, userId, hasFullScope, segmentId, (appointment, segment, resources) =>
        {
            if (request.PricingMode == null)
                throw new ValidationAppException(ErrorCodes.PricingSourceRequired, "PricingMode je obavezan.");
            List<Guid> employees = segment.Employees.Select(e => e.EmployeeId).ToList();
            PricingSourceValue pricingSource = SegmentPricingSource.Normalize(employees, request.PricingMode, request.PricingEmployeeId);
            return Task.FromResult((new SegmentTarget(segment.PlannedStart, segment.PlannedEnd, segment.ServiceId, employees, segment.RoomId,
                resources, Reprice: true, $"pricing:{pricingSource.Mode}:{pricingSource.EmployeeId}", pricingSource), new List<WarningDto>()));
        });

    /// <summary>Phase M1G — povijesno izvršenje se ne prepisuje: zaposlenici segmenta s odrađenim sudjelovanjem (zarađena
    /// individualna provizija pripada TIM zaposlenicima) ili zatvorene grupne sesije (grupna provizija po segmentu) se ne
    /// mijenjaju. Korekcija odrađenog sudjelovanja (Completed → Confirmed) najprije reverzira proviziju i otključava segment.</summary>
    private static void EnsureNoExecutionHistory(Appointment appointment, AppointmentSegment segment)
    {
        bool completed = appointment.Bookings.SelectMany(b => b.Participations)
            .Any(p => p.AppointmentSegmentId == segment.Id && p.Status == ParticipationStatus.Completed);
        if (completed || appointment.ClosedOutAt.HasValue)
            throw new BusinessRuleException(ErrorCodes.SegmentExecutionHistoryLocked,
                "Segment ima izvršnu povijest (odrađeno sudjelovanje ili zatvorena sesija) — zaposlenici se ne mogu mijenjati.");
    }

    public Task<AppointmentDto> ChangeSegmentRoom(
        Guid organizationId, Guid userId, bool hasFullScope, Guid segmentId, AppointmentSegmentRoomChangeRequest request) =>
        RewriteSegment(organizationId, userId, hasFullScope, segmentId, async (appointment, segment, resources) =>
        {
            await EnsureRoomExists(organizationId, appointment.CompanyId, request.RoomId);
            return (new SegmentTarget(segment.PlannedStart, segment.PlannedEnd, segment.ServiceId,
                segment.Employees.Select(e => e.EmployeeId).ToList(), request.RoomId, resources, Reprice: false,
                $"room:{request.RoomId?.ToString() ?? "-"}"), new List<WarningDto>());
        });

    public Task<AppointmentDto> ChangeSegmentResources(
        Guid organizationId, Guid userId, bool hasFullScope, Guid segmentId, AppointmentSegmentResourcesChangeRequest request) =>
        RewriteSegment(organizationId, userId, hasFullScope, segmentId, async (appointment, segment, _) =>
        {
            List<AppointmentSegmentResourceRequest> requested = request.Resources ?? new List<AppointmentSegmentResourceRequest>();
            if (requested.Any(r => r.QuantityRequired <= 0))
                throw new ValidationAppException("Količina resursa mora biti veća od 0.");
            if (requested.Select(r => r.ResourceId).Distinct().Count() != requested.Count)
                throw new ValidationAppException("Isti resurs se na segmentu smije navesti samo jednom.");
            foreach (AppointmentSegmentResourceRequest resource in requested)
                await EnsureResourceUsable(organizationId, appointment.CompanyId, resource.ResourceId);

            return (new SegmentTarget(segment.PlannedStart, segment.PlannedEnd, segment.ServiceId,
                segment.Employees.Select(e => e.EmployeeId).ToList(), segment.RoomId,
                requested.Select(r => new ResourceClaim(r.ResourceId, r.QuantityRequired)).ToList(), Reprice: false,
                $"resources:{string.Join(",", requested.Select(r => $"{r.ResourceId}x{r.QuantityRequired}"))}"), new List<WarningDto>());
        });

    /// <summary>Zajednički tok prepisivanja JEDNOG segmenta (vidi klasnu napomenu).</summary>
    private async Task<AppointmentDto> RewriteSegment(
        Guid organizationId, Guid userId, bool hasFullScope, Guid segmentId,
        Func<Appointment, AppointmentSegment, IReadOnlyList<ResourceClaim>, Task<(SegmentTarget Target, List<WarningDto> Warnings)>> plan)
    {
        (Appointment appointment, AppointmentSegment segment) = await LoadForSegmentCommand(organizationId, segmentId);
        EnsureExecutionEditable(appointment);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, new[] { segment }, NotOwnerMessage);

        List<ResourceClaim> resources = await _schedulingOccupancyHandler.GetSegmentResources(segmentId);
        SegmentSnapshot.State snapshot = SegmentSnapshot.Capture(appointment, segment, resources);
        (SegmentTarget target, List<WarningDto> warnings) = await plan(appointment, segment, resources);

        // Segment koji se prepisuje traži CIJELO svoje ciljno stanje (osobe = svi zaposlenici + zauzimajući klijenti, svi
        // resursi); isključuje se samo on — sestrinski segmenti istog termina ostaju vidljivi kao sudari/zauzetost.
        SegmentClaim claim = new SegmentClaim(segmentId, target.PlannedStart, target.PlannedEnd, target.EmployeeIds, snapshot.OccupyingClientIds)
        {
            RoomId = target.RoomId,
            RoomPeople = RoomPeopleCount.Of(target.EmployeeIds.Count, snapshot.OccupyingClientIds.Count),
            Resources = target.Resources
        };
        SchedulingSubjects released = new SchedulingSubjects(
            snapshot.EmployeeIds, Array.Empty<Guid>(),
            segment.RoomId.HasValue ? new[] { segment.RoomId.Value } : Array.Empty<Guid>(),
            resources.Select(r => r.ResourceId).ToList());

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await SchedulingConflictGuard.Claim(_schedulingOccupancyHandler, uow, organizationId, new[] { claim }, alsoLock: released);

            Appointment locked = await _appointmentHandler.GetForSegmentMutation(uow, organizationId, appointment.Id.GetValueOrDefault())
                ?? throw new NotFoundAppException("Appointment", appointment.Id.GetValueOrDefault());
            AppointmentSegment lockedSegment = locked.Segments.SingleOrDefault(s => s.Id == segmentId)
                ?? throw new NotFoundAppException("Segment", segmentId);
            SegmentSnapshot.EnsureUnchanged(snapshot, SegmentSnapshot.CaptureTracked(locked, lockedSegment));

            DateTimeOffset now = DateTimeOffset.UtcNow;
            SegmentMutator.ChangeService(lockedSegment, target.ServiceId, now);
            SegmentMutator.ChangeTime(lockedSegment, target.PlannedStart, target.PlannedEnd, now);
            // Phase M1G: povijest se ponovno provjerava POD lockom termina (sudjelovanje je moglo biti odrađeno od čitanja).
            if (!target.EmployeeIds.ToHashSet().SetEquals(lockedSegment.Employees.Select(e => e.EmployeeId)))
                EnsureNoExecutionHistory(locked, lockedSegment);
            SegmentMutator.AssignEmployees(lockedSegment, target.EmployeeIds.ToList(), now);
            if (target.PricingSource is PricingSourceValue pricingSource)
                SegmentPricingSource.Apply(lockedSegment, pricingSource, now);
            SegmentMutator.ChangeRoom(lockedSegment, target.RoomId, now);
            SegmentMutator.ReplaceResources(lockedSegment, target.Resources.Select(r => (r.ResourceId, r.Quantity)).ToList(), now);
            if (target.Reprice)
                await RepriceSegment(organizationId, locked, lockedSegment);
            locked.UpdatedAt = now;
            locked.UpdatedBy = userId;

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = locked.Id.GetValueOrDefault(),
                ChangeType = "SegmentChanged",
                OldValue = segmentId.ToString(),
                NewValue = target.Change,
                ChangedAt = now,
                ChangedBy = userId
            });

            await uow.Context.SaveChangesAsync();

            // P2 (2D, Q6.3): promjena vremena ili usluge segmenta — svako potvrđeno sudjelovanje se ponovno evaluira (vlastiti
            // claim se stornira i uzima na novom datumu/usluzi ako limiti dopuštaju; inače sljedeći izvor). Bez članarine no-op.
            if (target.PlannedStart != segment.PlannedStart || target.ServiceId != segment.ServiceId)
                foreach (Booking booking in locked.Bookings.OrderBy(b => b.ClientId))
                foreach (BookingSegmentParticipation participation in booking.Participations
                             .Where(p => p.AppointmentSegmentId == segmentId && p.Status == ParticipationStatus.Confirmed))
                    await _membershipCoverage.ReevaluateParticipation(uow, organizationId, userId, locked, booking, participation);

            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, locked.Id.GetValueOrDefault(), userId);
            await uow.CommitAsync();
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, appointment.Id.GetValueOrDefault());
        dto.Warnings = warnings;
        return dto;
    }

    public async Task<AppointmentDto> AddSegment(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, AppointmentSegmentAddRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId)
            ?? throw new NotFoundAppException("Appointment", appointmentId);
        EnsureExecutionEditable(appointment);
        EnsureGenericAppointment(appointment);
        EnsureSegmentProductLimits(request, requireParticipants: false);
        await AppointmentOwnership.EnsureCallerIsEmployee(_employeeHandler, organizationId, userId, hasFullScope, request.EmployeeIds, NotOwnerMessage);

        ValidatedSegment validated = await ValidateSegment(organizationId, appointment.CompanyId, request, PricingMode.WithManualOverride);
        List<WarningDto> warnings = await EnsureWorkforceAvailability(
            organizationId, validated.Plan.EmployeeIds, appointment.CompanyId, validated.Plan.PlannedStart, validated.Plan.PlannedEnd,
            request.OverrideAvailability && hasFullScope);

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Novi segment: zaposlenici, sudionici, prostorija i resursi — sudari s postojećim (i sestrinskim) segmentima.
            await SchedulingConflictGuard.Claim(_schedulingOccupancyHandler, uow, organizationId, new[] { SegmentClaim.ForNew(validated.Plan) },
                clientId => validated.Clients.FirstOrDefault(c => c.Id == clientId) is Client c ? $"{c.FirstName} {c.LastName}" : null);

            Appointment locked = await _appointmentHandler.GetForSegmentMutation(uow, organizationId, appointmentId)
                ?? throw new NotFoundAppException("Appointment", appointmentId);
            EnsureExecutionEditable(locked);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            (AppointmentSegment segment, List<Booking> newBookings, List<BookingSegmentParticipation> added) =
                AppointmentFactory.AddSegment(locked, validated.Plan, now);
            uow.Context.AppointmentSegments.Add(segment);
            uow.Context.Bookings.AddRange(newBookings);
            uow.Context.BookingSegmentParticipations.AddRange(added);
            locked.UpdatedAt = now;
            locked.UpdatedBy = userId;
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(), AppointmentId = appointmentId, ChangeType = "SegmentAdded",
                NewValue = segment.Id.ToString(), ChangedAt = now, ChangedBy = userId
            });

            await uow.Context.SaveChangesAsync();
            // P2 (2D): claim na rezervaciji za nova sudjelovanja segmenta (no-op bez članarine).
            foreach (BookingSegmentParticipation participation in added.OrderBy(p => p.Id))
                await _membershipCoverage.SyncParticipation(uow, organizationId, userId, locked,
                    locked.Bookings.Single(b => b.Id == participation.BookingId), participation,
                    MembershipCoverageEvent.Booking, MembershipCoverageMode.Interactive);
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, appointmentId, userId);
            await uow.CommitAsync();
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, appointmentId);
        dto.Warnings = warnings;
        return dto;
    }

    public async Task<AppointmentDto> RemoveSegment(Guid organizationId, Guid userId, bool hasFullScope, Guid segmentId)
    {
        (Appointment appointment, AppointmentSegment segment) = await LoadForSegmentCommand(organizationId, segmentId);
        EnsureGenericAppointment(appointment);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, new[] { segment }, NotOwnerMessage);

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Uklanjanje samo smanjuje zauzetost — dovoljan je Appointment lock (serijalizira s upisima na isti termin).
            Appointment locked = await _appointmentHandler.GetForSegmentMutation(uow, organizationId, appointment.Id.GetValueOrDefault())
                ?? throw new NotFoundAppException("Appointment", appointment.Id.GetValueOrDefault());
            AppointmentSegment lockedSegment = locked.Segments.SingleOrDefault(s => s.Id == segmentId)
                ?? throw new NotFoundAppException("Segment", segmentId);
            if (locked.Segments.Count <= 1)
                throw new BusinessRuleException(ErrorCodes.LastSegmentCannotBeRemoved, "Termin mora zadržati barem jedan segment.");

            List<BookingSegmentParticipation> participations = locked.Bookings
                .SelectMany(b => b.Participations).Where(p => p.AppointmentSegmentId == segmentId).ToList();
            // P2 (2D): claim netaknutog sudjelovanja se vraća prije brisanja (no-op bez članarine).
            await _membershipCoverage.ReleaseForRemoval(uow, organizationId, userId, participations);
            await ParticipationHistory.RemoveUntouchedParticipations(uow.Context, locked.Bookings, participations,
                "Segment ima sudjelovanja s poviješću (status, paket, naplata) — ne može se obrisati; otkažite sudjelovanja.");

            uow.Context.AppointmentSegmentEmployees.RemoveRange(lockedSegment.Employees);
            uow.Context.AppointmentSegmentResources.RemoveRange(lockedSegment.Resources);
            uow.Context.AppointmentSegments.Remove(lockedSegment);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            locked.UpdatedAt = now;
            locked.UpdatedBy = userId;
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(), AppointmentId = locked.Id.GetValueOrDefault(), ChangeType = "SegmentRemoved",
                OldValue = segmentId.ToString(), ChangedAt = now, ChangedBy = userId
            });

            await uow.Context.SaveChangesAsync();
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, locked.Id.GetValueOrDefault(), userId);
            await uow.CommitAsync();
        }

        return await GetByIdInternal(organizationId, appointment.Id.GetValueOrDefault());
    }

    public async Task<AppointmentDto> AddClient(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, AppointmentClientAddRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId)
            ?? throw new NotFoundAppException("Appointment", appointmentId);
        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan termin se ne može dopunjavati novim rezervacijama.");
        EnsureGenericAppointment(appointment);

        List<AppointmentClientParticipationRequest> selections = request.Participations ?? new List<AppointmentClientParticipationRequest>();
        if (selections.Count == 0)
            throw new ValidationAppException("Potreban je barem jedan segment.");
        if (selections.Select(p => p.SegmentId).Distinct().Count() != selections.Count)
            throw new ValidationAppException("Isti segment se smije navesti samo jednom.");

        List<AppointmentSegment> segments = selections
            .Select(p => appointment.Segments.SingleOrDefault(s => s.Id == p.SegmentId)
                         ?? throw new NotFoundAppException("Segment", p.SegmentId))
            .ToList();
        Client client = (await EnsureClientsExist(organizationId, new List<Guid> { request.ClientId })).Single();
        Booking existing = appointment.Bookings.FirstOrDefault(b => b.ClientId == request.ClientId);
        if (existing != null && existing.Participations.Any(p => segments.Any(s => s.Id == p.AppointmentSegmentId)))
            throw new BusinessRuleException(ErrorCodes.DuplicateParticipation, "Klijent već sudjeluje u odabranom segmentu.");

        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, segments, NotOwnerMessage);

        // Cijena PO SUDJELOVANJU: usluga i početak NJEGOVOG segmenta, poslovnica termina, opcionalni ručni iznos.
        List<(AppointmentSegment Segment, SegmentSnapshot.State Snapshot, BookingPricing Pricing, SegmentClaim Claim)> plans = new();
        foreach ((AppointmentSegment segment, AppointmentClientParticipationRequest selection) in segments.Zip(selections))
        {
            List<ResourceClaim> resources = await _schedulingOccupancyHandler.GetSegmentResources(segment.Id.GetValueOrDefault());
            BookingPricing pricing = BookingPricing.FromResolution(
                await ResolveServicePrice(organizationId, segment.ServiceId, appointment.CompanyId, SegmentPricingSource.PricingEmployeeOf(segment), segment.PlannedStart), selection.Amount);
            plans.Add((segment, SegmentSnapshot.Capture(appointment, segment, resources), pricing,
                SegmentClaim.ForParticipationActivation(appointment, segment, request.ClientId, resources)));
        }

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await SchedulingConflictGuard.Claim(_schedulingOccupancyHandler, uow, organizationId, plans.Select(p => p.Claim).ToList(),
                _ => $"{client.FirstName} {client.LastName}");

            Appointment locked = await _appointmentHandler.GetForSegmentMutation(uow, organizationId, appointmentId)
                ?? throw new NotFoundAppException("Appointment", appointmentId);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Booking booking = locked.Bookings.FirstOrDefault(b => b.ClientId == request.ClientId);
            bool newBooking = booking == null;
            if (newBooking)
            {
                booking = BookingFactory.NewContainer(organizationId, appointmentId, request.ClientId, now);
                locked.Bookings.Add(booking);
            }

            foreach ((AppointmentSegment segment, SegmentSnapshot.State snapshot, BookingPricing pricing, _) in plans)
            {
                AppointmentSegment lockedSegment = locked.Segments.SingleOrDefault(s => s.Id == segment.Id)
                    ?? throw new NotFoundAppException("Segment", segment.Id.GetValueOrDefault());
                SegmentSnapshot.EnsureUnchanged(snapshot, SegmentSnapshot.CaptureTracked(locked, lockedSegment));
                if (booking.Participations.Any(p => p.AppointmentSegmentId == segment.Id))
                    throw new BusinessRuleException(ErrorCodes.DuplicateParticipation, "Klijent već sudjeluje u odabranom segmentu.");
                BookingSegmentParticipation participation = BookingFactory.AddParticipation(booking, lockedSegment, ParticipationStatus.Confirmed, pricing, now);
                if (!newBooking)
                    uow.Context.BookingSegmentParticipations.Add(participation);
            }

            if (newBooking)
                uow.Context.Bookings.Add(booking);
            // Booking (i sudjelovanja) se upisuju PRIJE audita koji ga referencira (FK audit → booking).
            await uow.Context.SaveChangesAsync();
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(), AppointmentId = appointmentId, BookingId = booking.Id, ChangeType = "ClientAddedToSegments",
                NewValue = string.Join(",", plans.Select(p => p.Segment.Id)), ChangedAt = now, ChangedBy = userId
            });

            await uow.Context.SaveChangesAsync();
            // P2 (2D): claim na rezervaciji, redom po početku segmenta (no-op bez članarine).
            foreach (BookingSegmentParticipation participation in booking.Participations
                         .Where(p => plans.Any(x => x.Segment.Id == p.AppointmentSegmentId))
                         .OrderBy(p => locked.Segments.Single(s => s.Id == p.AppointmentSegmentId).PlannedStart))
                await _membershipCoverage.SyncParticipation(uow, organizationId, userId, locked, booking, participation,
                    MembershipCoverageEvent.Booking, MembershipCoverageMode.Interactive);
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, appointmentId, userId);
            await uow.CommitAsync();
        }

        return await GetByIdInternal(organizationId, appointmentId);
    }

    public async Task<AppointmentDto> RemoveParticipation(Guid organizationId, Guid userId, bool hasFullScope, Guid participationId)
    {
        Guid appointmentId = await _participationHandler.GetAppointmentIdOf(organizationId, participationId)
            ?? throw new NotFoundAppException("Participation", participationId);
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId)
            ?? throw new NotFoundAppException("Appointment", appointmentId);
        EnsureGenericAppointment(appointment);
        BookingSegmentParticipation addressed = appointment.Bookings.SelectMany(b => b.Participations).Single(p => p.Id == participationId);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope,
            appointment.Segments.Where(s => s.Id == addressed.AppointmentSegmentId), NotOwnerMessage);

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            Appointment locked = await _appointmentHandler.GetForSegmentMutation(uow, organizationId, appointmentId)
                ?? throw new NotFoundAppException("Appointment", appointmentId);
            BookingSegmentParticipation participation = locked.Bookings.SelectMany(b => b.Participations).SingleOrDefault(p => p.Id == participationId)
                ?? throw new NotFoundAppException("Participation", participationId);
            Booking booking = locked.Bookings.Single(b => b.Id == participation.BookingId);
            await _membershipCoverage.ReleaseForRemoval(uow, organizationId, userId, new[] { participation });
            await ParticipationHistory.RemoveUntouchedParticipations(uow.Context, new[] { booking }, new[] { participation },
                "Sudjelovanje ima povijest (status, paket, naplata) — ne može se ukloniti; koristite otkazivanje.");

            DateTimeOffset now = DateTimeOffset.UtcNow;
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(), AppointmentId = appointmentId, ChangeType = "ParticipationRemoved",
                OldValue = participationId.ToString(), ChangedAt = now, ChangedBy = userId
            });

            await uow.Context.SaveChangesAsync();
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, appointmentId, userId);
            await uow.CommitAsync();
        }

        return await GetByIdInternal(organizationId, appointmentId);
    }

    private async Task<(Appointment Appointment, AppointmentSegment Segment)> LoadForSegmentCommand(Guid organizationId, Guid segmentId)
    {
        AppointmentSegment found = await _appointmentSegmentHandler.GetById(organizationId, segmentId)
            ?? throw new NotFoundAppException("Segment", segmentId);
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, found.AppointmentId)
            ?? throw new NotFoundAppException("Appointment", found.AppointmentId);
        return (appointment, appointment.Segments.Single(s => s.Id == segmentId));
    }

    /// <summary>Izvršni podaci eksplicitno otkazanog termina se ne mijenjaju (otkazan termin je zaključan).</summary>
    private static void EnsureExecutionEditable(Appointment appointment)
    {
        if (appointment.IsExplicitlyCancelled)
            throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan termin se ne može mijenjati.");
    }

    /// <summary>Grupni occurrence je jednosegmentan do GroupSegmentTemplates (M1F): dodavanje/uklanjanje segmenata i
    /// sudionika ide kroz grupne tokove (članstvo, lista čekanja, gost).</summary>
    private static void EnsureGenericAppointment(Appointment appointment)
    {
        if (appointment.Form == AppointmentForm.Group)
            throw new ValidationAppException("Grupni termin ima jedan segment; sudionici se dodaju kroz članstvo, listu čekanja ili kao gost.");
    }

    /// <summary>Aktivna (Confirmed) sudjelovanja segmenta se cijene po trenutnoj usluzi segmenta; ručni iznos se čuva (samo
    /// se osvježava predložena cijena). Terminalna sudjelovanja zadržavaju povijesnu cijenu.</summary>
    private async Task RepriceSegment(Guid organizationId, Appointment locked, AppointmentSegment segment)
    {
        ResolvePriceResponse resolved = await ResolveServicePrice(organizationId, segment.ServiceId, locked.CompanyId, SegmentPricingSource.PricingEmployeeOf(segment), segment.PlannedStart);
        foreach (BookingSegmentParticipation participation in locked.Bookings.SelectMany(b => b.Participations)
                     .Where(p => p.AppointmentSegmentId == segment.Id && p.Status == ParticipationStatus.Confirmed))
            ParticipationPrice.Apply(participation, participation.IsAmountManuallyOverridden
                ? BookingPricing.FromResolution(resolved, participation.Amount)
                : BookingPricing.AtSuggested(resolved));
    }
}
