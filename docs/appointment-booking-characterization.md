# Appointment & Booking — characterization test suite and current-behaviour findings

Scope: a **behaviour-preserving safety net** around the CURRENT Appointment/Booking implementation, built before the
Appointment/Booking redesign. No production code was changed to write it. The tests describe what the code does **today**,
including behaviour that looks unintended (those are labelled *FINDING* in the test names/comments and listed below). A test
that starts failing after a refactor means observable behaviour changed — decide consciously whether that is intended; do not
"fix" the test to make it green.

## Running

The tests are integration tests against a real PostgreSQL, like the rest of `BlueDragon.DuneLight.UnitTests`
(connection string `Host=localhost;Database=postgres;Username=postgres;Password=root1234`, schema created by the
FluentMigrator project):

```bash
dotnet run --project BlueDragon.DuneLight.DatabaseMigration -- --d=PostgreSQL --s="Host=localhost;Database=postgres;Username=postgres;Password=root1234"
dotnet test BlueDragon.DuneLight.sln                                   # everything
dotnet test BlueDragon.DuneLight.UnitTests --filter "FullyQualifiedName~Scheduling"   # only this suite
```

## Design of the suite (`BlueDragon.DuneLight.UnitTests/Scheduling/`)

* **Real code paths.** `SchedulingTestHost` builds a real DI container over the *production* handlers and services (registered by
  reflection, so a new constructor dependency cannot drift from this container). Nothing is mocked: real transactions, real
  `FOR UPDATE` locks, real outbox writes.
* **One tenant per test.** `SchedulingWorld.Create` seeds a fresh Organization (+ company, working hours, service, employee,
  client, admin user) and deletes everything it created on dispose (catalog-driven `DELETE ... WHERE organization_id`), so test
  classes run in parallel without interfering and leave no rows behind.
* **Behaviour through services, state from the database.** Behaviour is driven through the public service methods
  (`IAppointmentService`, `IBookingService`, `IGroupService`, `ICheckoutService`, ...). Assertions read the persisted rows back
  through a fresh `DbContext` (bookings, payments, allocations, commission entries, audit log, outbox, notifications, package
  counters), not only the returned DTOs. Direct seeding (`SeedAppointment`, `SeedCoverageApplied`) is used only for starting
  states the flows cannot produce, and is called out where used.
* **Deterministic time.** All calendar values are UTC constants: `FutureDay` (2031-03-03) and `PastDay` (2020-03-02). Only two
  tests use the wall clock (late-cancellation classification: 30 min vs 3 h ahead against a 60-min cutoff), and the package
  "expired today" cases use dates safely in the past/future of the real clock.
* **Nullable context disabled** in these files, like the production code they mirror (`Guid? Id`, no annotations).

## Test map

| File | Matrix area(s) |
|---|---|
| `AppointmentCreateCharacterizationTests` | A — create, snapshots, structural eligibility, authorization scope |
| `AppointmentOverlapCharacterizationTests` | B, C, D — employee / client / room overlap; DB-level constraints |
| `AppointmentWorkforceAvailabilityCharacterizationTests` | E — working hours, absence, break, holiday, override, per-flow differences |
| `AppointmentUpdateCharacterizationTests` | F — full edit |
| `AppointmentMoveCharacterizationTests` | G — move |
| `GroupOccurrenceGenerationCharacterizationTests` | H — Group → Slot → occurrence → Bookings |
| `GroupCapacityCharacterizationTests` | I — hard capacity (roster, occurrence, return-to-Confirmed, concurrent race) |
| `GroupWaitlistCharacterizationTests` | S (+ I) — waitlist, FIFO promotion |
| `BookingStatusVersioningCharacterizationTests` | J — `StatusVersion` |
| `IndividualCompletionCharacterizationTests` | K, T — individual completion, group close asymmetry |
| `BookingNoShowAndCancellationCharacterizationTests` | L, M — no-show, cancellation, outbox → notification |
| `BookingCorrectionCharacterizationTests` | N, Q — corrections, payment reversal |
| `PackageCoverageCharacterizationTests` | O — package coverage and the package-XOR-money rule |
| `BookingSettlementCharacterizationTests` | P, Q — settlement chain, derived financials |
| `CommissionCharacterizationTests` | R — commissions |
| `GroupAttendanceCharacterizationTests` | group attendance representation |
| `AppointmentRecurringCharacterizationTests` | recurring series |
| `AppointmentReadModelCharacterizationTests` | schedule feed, detail/history DTOs, available slots, delete |
| `TenantIsolationCharacterizationTests` | cross-organization ids |

## Current behaviour findings

Legend — **Test**: the characterization test(s) that pin it. **Later**: whether it must be decided during target-model work.

### Defect-like

* **F-01 `Update` does not persist a changed Service or Employee.**
  `AppointmentService.Update` validates the *requested* service/employee, derives duration and re-priced amounts from the
  requested service and writes an `EmployeeId` audit row — but `Appointment.ServiceId` / `EmployeeId` keep the old values.
  Result: old service, new service's duration, new service's price, an audit row claiming a change.
  Cause (observed): `AppointmentHandler.GetWithBookingsForMutation` loads the `Service`/`Employee` navigations, and
  `context.Appointments.Update(graph)` in `UpdateWithBookingsCore` lets the stale navigation win over the changed FK. `Move` and
  `CompleteExisting` (no such navigations) persist the same change correctly.
  Test: `AppointmentUpdateCharacterizationTests.Update_ChangingTheService_*`, `Update_ChangingTheEmployee_*`,
  `Update_AllFieldsAtOnce_*`; contrast `Move_CanChangeTheEmployee_*`, `CompleteExisting_RewritesTheFrame_*`.
  Later: **yes** — do not patch piecemeal; the edit path is replaced by the new model.
* **F-02 `Update` has no lifecycle or form guard.** It edits Completed and Cancelled appointments (Move rejects Cancelled), and on
  a **Group** occurrence it hard-deletes the Confirmed member Bookings missing from `ClientIds` — no cancellation, no outbox event,
  no waitlist promotion, no audit row. Test: `Update_OnACompletedAppointment_*`, `Update_OnACancelledAppointment_*`,
  `Update_OnAGroupOccurrence_*`. Later: yes.
* **F-03 `Move` gaps.** Only Cancelled blocks it (a Completed, paid, commissioned appointment can be dragged); a room can never be
  cleared (null = "unchanged"); a trainer-less group occurrence cannot be re-timed unless a trainer is supplied (effective
  employee becomes `Guid.Empty` → "Employee not found"). Test: `Move_ACompletedAppointment_*`, `Move_CannotClearTheRoom_*`,
  `Move_ATrainerlessGroupOccurrence*`. Later: yes.
* **F-04 Ownership is checked against the current employee only** (Update, Move). An own-scope caller can hand their appointment to
  someone else; the new employee is not ownership-checked. Test: `*_OwnScopeCaller_*Reassign*/*Hand*`. Later: yes (authorization
  model changes with multi-employee).
* **F-05 Re-pricing side effects.** A full `Update` that does not resend `Amount` silently drops a manual price override; it
  re-prices Bookings that already have a partial manual payment; and the `CheckoutItem` snapshot then disagrees with
  `Booking.Amount` (two calculators, two "amount" sources → booking says 10 outstanding, checkout says 30).
  Test: `Update_WithoutAnExplicitAmount_*`, `Update_RePricesAConfirmedBooking...PartialManualPayment_*`,
  `ARePriceAfterACheckoutItemWasCreated_*`. Later: yes (settlement redesign).
* **F-06 `CompleteExisting` edge cases.** Completes a *Cancelled* appointment (cancelled Bookings jump to Completed, payment and
  commission generated); hard-deletes Confirmed Bookings of clients omitted from the request (Group close, by contrast, only warns
  about unresolved Bookings); replaces note/room from the request. Test: `CompleteExisting_OnACancelledIndividualAppointment_*`,
  `CompleteExisting_HardDeletesConfirmedBookings*`, `CompleteExisting_WithoutARoom*`. Later: yes (closing model).
* **F-07 Inconsistent audit/version between completion paths.** `CompleteExisting` writes no `BookingPackageCoverageApplied` audit
  row (CompleteNew and group check-in do). `CompleteNew` creates Bookings directly as Completed at `StatusVersion 0` (and commission
  `SourceVersion 0`) whereas the same booking completed through `CompleteExisting` is version 1.
  Test: `CompleteExisting_WithAnEligiblePackage_*`, `Individual_CompleteNew_CreatesTheBookingAlreadyCompleted_AtVersionZero`.
  Later: yes (notification/commission identity).
* **F-08 Package clock and link inconsistencies.** Eligibility is judged at the *appointment date*, deduction at *now* — back-dating a
  completion onto a package that expired since is rejected at the last step. After coverage is returned, `Booking.ClientPackageId`
  stays set and the check-in payment guard tests only that column, so re-completing the same booking as a cash sale fails with
  `PAYMENT_NOT_ALLOWED`. Test: `Individual_APackageValidOnAPastAppointmentDateButExpiredToday_*`,
  `Xor_ACheckInPaymentCannotBeRecordedForABookingThatCarriesAPackage_*`. Later: yes (settlement redesign / mixed settlement).
* **F-09 Delete leaks a persistence error.** Same-day delete of an appointment whose booking already sits on a checkout item fails
  with a raw `DbUpdateException` (FK) instead of a business error. Test: `Delete_OfAnAppointmentWhoseBookingWasAddedToACheckout_*`.

### Individual vs Group asymmetries (all pinned, none normalized)

* **F-10 Corrections.** Individual Completed→Confirmed keeps `Amount`, refuses when a manual POS payment exists, reverses the
  commission and re-opens the appointment; Group resets `Amount`/`SuggestedAmount` to 0, has **no** manual-payment guard (a manual
  payment stays attached to a zero-priced booking), and never touches commission. Individual has no `Cancelled → Confirmed` path,
  Group does. Individual repeats of a terminal status throw; Group is idempotent (but overwrites the reason). Package return on
  cancel is opt-in for Individual, automatic for Group. Additionally, an Individual "confirm again" on a plain Confirmed booking
  with a manual partial payment throws `BOOKING_HAS_NON_REVERSIBLE_PAYMENT` (the retry runs the full correction body).
  Test: `BookingCorrectionCharacterizationTests`, `IndividualCancel_CannotBeUndone_*`, `Individual_RepeatingATerminalStatus_*`.
* **F-11 Commission.** Individual: one Earned entry per completed Booking on the *retail* `Booking.Amount` (also when a package
  covers it), reversed on correction, re-earned at a new `SourceVersion`. Group: one Fixed-only entry per *occurrence*, no booking
  link, base 0, created by closing the occurrence, never reversed, earned even with zero attendees.
  Test: `CommissionCharacterizationTests`. Later: yes.
* **F-12 Closing.** Group close never touches Bookings and only warns (`GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS`); Individual close
  reconciles Bookings (F-06). Test: `CompleteGroupAppointment_*`.

### Capacity and waitlist

* **F-13 Capacity lives on the Group, is read live, and counts only Confirmed.** Lowering `Group.Capacity` immediately blocks bookings
  on already-generated occurrences; Completed/NoShow Bookings do not occupy a seat; a guest recorded straight as attended bypasses
  the check; historical (started) occurrences ignore capacity on correction. A NoShow frees the count but does **not** promote the
  waitlist, so a queued client can wait while `Join` for a new client says `CAPACITY_AVAILABLE`.
  Test: `GroupCapacityCharacterizationTests`, `ANoShow_DoesNotPromoteAnyone_*`. Later: yes (soft capacity redesign).
* **F-14 Concurrency.** The occurrence capacity guard (row lock + count under lock) is safe: two concurrent bookings for the last seat
  → exactly one wins (`ConcurrentGuestBookings_ForTheLastSeat_ExactlyOneWins`).

### Scheduling rules vs the business-rules document (`docs/poslovna-logika-pregled.md`)

* **F-15 The document is stale in places.** Schedule breaks and working hours are **hard blocks** unless a full-scope caller sets
  `OverrideAvailability` (doc §4.4 says warning); there is no `NoShow` appointment status (doc §"AppointmentStatus"); group
  deactivation does **not** delete future occurrences (doc says it does); the error code and warning code for working hours differ
  (`OUTSIDE_WORKING_HOURS` vs `OUTSIDE_WORKING_HOURS_WARNING`); `ValidatePackageSelections` is now `ValidateSettlements`.
  Test: `AppointmentWorkforceAvailabilityCharacterizationTests`, `GeneratedOccurrence_SurvivesGroupDeactivationAndSlotRemoval`.
* **F-16 Workforce checks differ per flow.** Create/Update/Move always check; `CompleteNew` checks only for a future start;
  `CompleteExisting` never checks; recurring/group generation report aggregated `RECURRING_CONFLICT` (and skip holidays silently for
  groups); the recurring **client** overlap still surfaces as `APPOINTMENT_OVERLAP`. A missing working-hours template means "never
  available". Absence with `DateTo = null` is open-ended.

### Persistence and races

* **F-17 Overlap protection is application-level only** (employee, room, client): Create/Update/Move are not transactional and take no
  lock, so two concurrent requests can both pass the check. The database has no exclusion constraint (a double-booked employee/room
  can be written directly); it does enforce `ux_bookings_appointment_client` and `ux_appointments_group_slot_startsat`.
  Test: `Database_HasNoExclusionConstraint_*`, `Database_DoesEnforce_*`, `DuplicateOccurrence_IsAlsoStoppedByTheDatabase*`.
  The race itself is **not** asserted (non-deterministic); only the absence of a database guard is.
* **F-18 Small hazards.** Appointment-wide `Cancel` twice silently overwrites the appointment's `CancellationReason`; `AddBooking` on
  an Individual appointment has no capacity concept; available-slot search ignores rooms and clients and lists absent employees
  with empty slot lists; read models join Service/Employee/Room names live (no name snapshot).

## Known limits of the suite

* **Race conditions on Create/Update/Move** cannot be asserted deterministically without changing production code (see F-17).
* **OutboxProcessorService** (claim/lock/retry) is not exercised — handlers are invoked directly, so the outbox → Notification step
  is covered but delivery mechanics are not.
* **HTTP layer / `RequireGrant` attributes** are not exercised; services take `hasFullScope` directly, which is the boundary the
  controllers use.
* **Time zones**: everything is UTC; production uses `startsAt.Date` / `TimeOfDay` in the request's offset, so non-UTC offsets are
  untested.
* **Calendar constants** (2031 appointments, 2035 package expiry) are "far future" today; they must be moved forward if the real
  clock approaches them.
* Group slot management (`AddSlot/UpdateSlot`), the operational dashboard and the future-activity providers are outside this suite.
