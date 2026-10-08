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
| ~~`AppointmentUpdateCharacterizationTests`~~ | F — full edit: **deleted in M1H** (the flat `PUT /{id}` Update was removed) |
| ~~`AppointmentMoveCharacterizationTests`~~ | G — move: **deleted in M1H** (the flat `PATCH /{id}/move` was removed; segment commands own time/employees/room) |
| `GroupOccurrenceGenerationCharacterizationTests` | H — Group → Slot → occurrence → Bookings |
| `GroupCapacityCharacterizationTests` | I — hard capacity (roster, occurrence, return-to-Confirmed, concurrent race) |
| `GroupWaitlistCharacterizationTests` | S (+ I) — waitlist, FIFO promotion |
| `BookingStatusVersioningCharacterizationTests` | J — `StatusVersion` |
| `IndividualCompletionCharacterizationTests` | K, T — individual completion per participation and through CompleteNow, group close asymmetry |
| `BookingNoShowAndCancellationCharacterizationTests` | L, M — no-show, cancellation, outbox → notification |
| `BookingCorrectionCharacterizationTests` | N, Q — corrections, payment reversal |
| `PackageCoverageCharacterizationTests` | O — package coverage and the package-XOR-money rule |
| `BookingSettlementCharacterizationTests` | P, Q — settlement chain, derived financials |
| `CommissionCharacterizationTests` | R — commissions |
| `GroupAttendanceCharacterizationTests` | group attendance representation |
| `AppointmentRecurringCharacterizationTests` | recurring series |
| `AppointmentReadModelCharacterizationTests` | schedule feed, detail/history DTOs, available slots, delete |
| `TenantIsolationCharacterizationTests` | cross-organization ids |
| `ScheduleBreakOverlapCharacterizationTests` | schedule break vs appointment overlap (single, update, recurring) — added with S1 |
| `SchedulingOccupancyHandlerTests` | S1 occupancy read seam (`ISchedulingOccupancyHandler`) contract — not a characterization test of a service |
| `ExecutionContextResolverTests` | S2 execution-context seam (`ExecutionContextResolver`) mapping and guards — pure unit tests, no database |
| `ExecutionContextConsumerCharacterizationTests` | checkout item description; completion commission / package deduction after segment commands changed service+employee — added with S2 |
| `AppointmentWriteSeamTests` | S3 write seams (`AppointmentFactory`, `AppointmentFrameMutator`, `BookingFactory`, ownership predicate) — pure unit tests, no database |
| `AppointmentOwnershipCharacterizationTests` | appointment own scope through `AppointmentOwnership` (helper + Cancel / booking SetStatus / waitlist Join messages) — added with S3 |
| `OrganizationCalendarTests` | local ↔ UTC conversion, DST gap/overlap, local-time recurrence, IANA id validation — pure unit tests |
| `TimezoneSchedulingTests` | Europe/Zagreb organization: local absences, working hours, holidays, available slots, DST, recurrence, group generation, timezone setting |
| `CompanyTimeZoneTests` | Company timezone override: inheritance/override/clearing via the Company API, validation, org change vs overridden companies, Company-local hours, absences, holidays, slots, recurring appointments/breaks and group occurrences across DST, dashboard day boundaries |
| `BookingParticipationLifecycleTests` | D3B1 — lifecycle authoritative on `BookingSegmentParticipation`: creation seam, lifecycle writes, a Booking always has participations, read model, dropped booking columns, the "untouched = deletable" rule for RemoveParticipation and same-day Delete |
| `BookingParticipationLifecycleCutoverMigrationTests` (project root) | D3B1 migration on a throw-away database: backfill, guards, rollback |
| `BookingParticipationPricingTests` | D3B2 — price authoritative on `BookingSegmentParticipation`: every creation path, truthful resolution snapshot (BaseAmount/BaseAmountSource), manual override, repricing (participation price command, completion settlement amount, group un-check-in reset), pricing is not lifecycle history, checkout reads the participation price |
| `BookingParticipationPricingCutoverMigrationTests` (project root) | D3B2 migration on a throw-away database: exact price copy, no fabricated history, guards, identical rollback |
| `PackageConsumptionLedgerTests` | D3B3A — PackageConsumption ledger: eligibility (service, client, exhausted, expired), service-performance-date validity incl. company-local date (F-08), once-only/idempotent/concurrent consumption, reversal history (never twice, consume again), timing setting, history rule, schema |
| `PackageValidityCalendarTests` | D3B3A.1 — ValidUntilDate as a date, inclusive boundary, Zagreb/New York company boundaries, two companies per organization, sale-company business date, ledger uses the same rule, unlimited expiry, /eligible company context, clock-free reversal status |
| `TargetCommandTests` | M1H — CompleteNow matrix (staffing/pricing source, several clients, package + money, per-employee commission, room/resource capacity, overlap, past service, atomic rollback), `ChangeNote`, `SetParticipationPrice`, no company reassignment |
| `ParticipationSettlementTests` | D3B3B — participation is the settlement boundary: service item -> participation (tenant FK), derived settled/outstanding (partial, split, voided, prepaid, completed with debt), no over-settlement across checkouts or concurrently, check-in pays only the remainder, package is not money (single exclusivity policy), F-09 history rule, dashboard |

## Phase M1H — compatibility surface removed; target API and domain

The development database has no production data, so M1H removes the single-segment / single-employee compatibility surface
instead of keeping it alongside the target model. These are **intentional breaking API changes**.

### Removed endpoints and contracts

| Removed | Target replacement |
|---|---|
| `PUT /api/appointments/{id}` (`AppointmentUpdateRequest`, flat Update) | segment commands (`PATCH /api/segments/{id}/` + `time`, `service`, `employees`, `pricing-source`, `room`; `PUT .../resources`), `POST /{id}/clients`, `DELETE /api/participations/{id}`, `PATCH /{id}/note`, `PATCH /api/participations/{id}/price` |
| `PATCH /api/appointments/{id}/move` (`AppointmentMoveRequest`) | `PATCH /api/segments/{id}/time` (+ `/employees`, `/room`) |
| `PATCH /api/appointments/{id}/complete` (CompleteExisting) | `PATCH /api/participations/{id}/status` per participation (settlement in `BookingSetStatusRequest`) |
| `POST /api/appointments/schedule` (`AppointmentSingleSegmentRequest`) | `POST /api/appointments` (`AppointmentCreateRequest.Segments`) |
| old flat body of `POST /api/appointments/complete` (`AppointmentCompleteRequest`, `AppointmentClientSettlement`) | `AppointmentCompleteNowRequest` (one explicit `Segment` + `Clients`) on the same route |
| `PATCH /api/appointments/{id}/bookings/{clientId}/no-show` and `/confirm` | `PATCH /api/participations/{id}/no-show` / `/confirm` / `/status` |
| `POST /api/appointments/{id}/bookings` for an individual appointment, and without `SegmentId` | group guest only, `SegmentId` required; individual: `POST /{id}/clients` |
| singular projections `StartsAt`, `DurationMinutes`, `ServiceId/Name/ColorHex`, `EmployeeId/Name`, `RoomId/Name` on `AppointmentDto`, `AppointmentScheduleCellDto`, `ClientAppointmentHistoryDto`, `DashboardScheduleOccurrenceDto` | `PlannedStart`/`PlannedEnd` + `Segments` |
| `GroupDto.ServiceId/ServiceName/Capacity/DefaultTrainerId/Name/DefaultRoomId/Name`; flat `ServiceId/Capacity/DefaultRoomId/DefaultTrainerId` on `GroupCreateRequest`/`GroupUpdateRequest` | `SegmentTemplates` (create), template commands (`POST/PUT/DELETE /api/groups/{id}/segment-templates`); `GroupUpdateRequest` = name, company, note |
| `ClientGroupMembershipDto.ServiceName` | `ServiceNames` (services of the client's selected templates) |
| implicit selectors (one-template inference) | required `GroupMemberAddRequest.SegmentTemplateIds`, `WaitlistJoinRequest.SegmentId`, waitlist cancel `segmentId`, `BookingCreateRequest.SegmentId`, `SetGroupAttendanceRequest.SegmentId` |
| occurrence-level `GroupAttendanceListDto.Expected/Recorded` | `GroupAttendanceListDto.Segments[].Expected/Recorded` |
| `CheckoutAddBookingItemRequest.BookingId` | `ParticipationId` (required) |
| error codes `SEGMENT_SELECTION_REQUIRED`, `EMPLOYEE_SET_COMMAND_REQUIRED`, `BOOKING_PARTICIPATION_AMBIGUOUS`, `PARTICIPATION_BOOKING_MISMATCH`; warning `GROUP_COMMISSION_NOT_SUPPORTED_FOR_MULTI_SEGMENT` | a missing selector is now a 400 validation error; the ambiguous paths no longer exist |

Removed internals: `LegacySingleSegment`, `LegacySingleEmployee`, `SingleSegmentProjection`, the `GroupOccurrenceSegments`
inference (only `Require` remains), `BookingParticipations.GetSingleParticipation`, `AppointmentHandler.UpdateWithBookings`,
`GroupHandler.GetTemplateById`, the non-transactional `IAppointmentHandler.GetBookingById`, `BookingFactory.CreateCompletedAtCreation`
and the `initialStatus` parameter of `AppointmentFactory.CreateIndividual`, the legacy-trainer projections in `GroupService`.
**Database:** no change — the schema had no compatibility-only column left (fresh-database migration verified).

### New narrow target commands

* **`PATCH /api/appointments/{id}/note`** (`AppointmentNoteChangeRequest { Note }`) — the appointment note only; same ownership rule
  as other appointment edits (own scope must be assigned to every segment); never touches segments, pricing, schedule or lifecycle.
* **`PATCH /api/participations/{id}/price`** (`ParticipationPriceChangeRequest { Amount }`, null = back to the suggested price) —
  addressed by ParticipationId only; individual + `Confirmed` only (terminal → `ALREADY_COMPLETED`, group → 400); ownership follows
  the segment; the resolution snapshot (Suggested/Base amount and source, pricing mode/employee) is kept, the manual marker is
  `Amount != SuggestedAmount`; a price below the money already settled → `PAYMENT_EXCEEDS_OUTSTANDING_AMOUNT`; audited
  (`Amount`, with BookingId + ParticipationId); settlement derives from the new price; package/payment exclusivity unchanged.
* **Company reassignment:** Appointment company reassignment was a legacy capability and is intentionally not exposed by the
  target API. If the product needs cross-company appointment movement later, implement it as a dedicated
  MoveAppointmentToCompany use case with explicit business rules.

### CompleteNow (POST /api/appointments/complete) — kept as an explicit atomic command

The POS "record work already done" workflow stays ONE command (never "create + several PATCH calls"). The request carries exactly
ONE explicit segment (`ServiceId`, `PlannedStart`, `PlannedEnd?`, `EmployeeIds`, `PricingMode?`, `PricingEmployeeId?`, `RoomId?`,
`Resources`) and `Clients[]` (`ClientId`, `Amount?`, `PaymentMethod?`, `ClientPackageId?`, `IsPaid`). It validates the segment
exactly like create (M1G staffing, pricing source, eligibility, room people, resources), allows past and present starts
(workforce availability is checked only for a future start), creates one Booking + one Participation per client (Confirmed) and,
in the SAME transaction, completes each participation through the participation lifecycle core (`IParticipationLifecycleService`,
the code path of `PATCH /api/participations/{id}/status`): StatusVersion, audit, per-employee commission, package consumption and
check-in payment. Hard overlaps and capacity are checked under the existing scheduling locks first; any failure (e.g. an
ineligible package of the second client) rolls back everything — no Appointment, Segment, Booking, Participation, Payment,
Checkout, PackageConsumption or CommissionEntry survives. Behaviour change vs the old CompleteNew: `StatusVersion 1` (was 0), a
`BookingStatus` audit row per participation, commission `SourceVersion 1`.

### Target API (appointments and groups)

* Appointments: `POST /api/appointments` (multi-segment create), `POST /api/appointments/complete` (CompleteNow),
  `POST /api/appointments/recurring` (recurrence keeps its flat one-service series request — recurrence feature, not compatibility),
  `GET /api/appointments/{id}`, `/schedule`, `/by-client/{id}`, `/by-employee/{id}`, `/available-slots`,
  `POST /{id}/segments`, `POST /{id}/clients`, `PATCH /{id}/note`, `POST /{id}/cancel`, `POST /{id}/no-show`, `DELETE /{id}`.
* Segments: `PATCH /api/segments/{id}/time|service|employees|pricing-source|room`, `PUT /api/segments/{id}/resources`,
  `DELETE /api/segments/{id}`.
* Participations: `PATCH /api/participations/{id}/status|cancel|no-show|confirm|price`, `DELETE /api/participations/{id}`.
* Bookings (whole-client operations): `GET /{id}/bookings`, `PATCH /{id}/bookings/{clientId}/cancel` (every active
  participation), `GET /{id}/bookings/{clientId}/payments`; group guest `POST /{id}/bookings` (explicit `SegmentId`).
* Waitlist: `GET/POST /api/appointments/{id}/waitlist`, `DELETE .../waitlist/{clientId}?segmentId=` (segment-specific).
* Groups: `POST /api/groups` (≥1 `SegmentTemplates`), `PUT /api/groups/{id}` (name/company/note), template, slot and member
  commands (explicit `SegmentTemplateIds`, `PUT .../members/{memberId}/segment-templates`, `OverrideCapacity` with
  `groups.capacity.override`), `POST /api/groups/generate-appointments`, attendance
  `POST /api/groups/appointments/{id}/attendance` (explicit `SegmentId`; guest check-in), close-out
  `PATCH /api/groups/appointments/{id}/complete`.

### Target domain summary

* **Appointment** — the aggregate: company, form (Individual / Group occurrence), note, explicit cancellation/close-out facts
  and a status *derived* from its participations. It has no service, employee, room or time of its own: `PlannedStart/End` are
  derived from its segments.
* **AppointmentSegment** — one execution unit: service, `PlannedStart/End`, 0..N equal employees, pricing source
  (`Standard` / `Employee` + pricing employee), room, resources; group segments link to their `GroupSegmentTemplate`.
* **Booking** — one client inside one appointment (identity + package view); it has no status or price of its own.
* **BookingSegmentParticipation** — a booking's participation in one segment: lifecycle (Confirmed / Completed / Cancelled /
  NoShow, StatusVersion), price (amount, suggested amount, resolution snapshot, manual marker), settlement (checkout items →
  allocations → payments) and package consumption. All per-client operations address it by ParticipationId.
* **Group** — name, company, note, slots and ≥1 segment templates (service, offset, duration, capacity, room, resources, staff,
  pricing source); members explicitly select templates; generation copies the templates into occurrence segments.
* **Commission** — individual: per (participation, employee) from the participation's final price at completion; group: fixed
  session commission per (segment, employee) at close-out.

### Remaining NON-compatibility debt (documented, intentionally not changed in M1H)

* Pricing employee ∈ segment employees is enforced only in the service layer (no DB constraint).
* Close-out authorization for differently staffed templates (own scope must own every segment of the occurrence).
* Over-locking of pricing-source changes.
* Group percentage commission is not supported (`GROUP_COMMISSION_RULE_NOT_SUPPORTED`).
* Template edits do not propagate to already generated occurrences.
* Checkout item snapshot (description/amount fixed at item creation).
* Discount feature (no adjustment layer; `AdjustmentAmount` is never written).
* Group cancellation → reactivation provenance.
* Occurrence identity is not DB-enforced (slot advisory lock only).
* Break scheduling.
* Notification grouping (per participation).
* Unauthenticated requests answer 401 without the error envelope.
* Catalog deactivation policy.

## Current behaviour findings

> **M1H note.** Findings F-01, F-02, F-03, F-05 and F-06 describe the removed flat commands (`Update`, `Move`,
> `CompleteExisting`); those commands and their tests are gone, so the findings are historical only. F-04 now applies to the
> segment commands (ownership = assignment to the addressed segment). F-07 is **resolved**: CompleteNow runs the participation
> lifecycle core, so every completion path is `StatusVersion 1`, writes the `BookingStatus` audit row and the
> `BookingPackageCoverageApplied` row, and commission `SourceVersion 1`. See "Phase M1H" below.

Legend — **Test**: the characterization test(s) that pin it. **Later**: whether it must be decided during target-model work.

### Defect-like

* **F-01 `Update` does not persist a changed Service or Employee.**
  `AppointmentService.Update` validates the *requested* service/employee, derives duration and re-priced amounts from the
  requested service and writes an `EmployeeId` audit row — but `Appointment.ServiceId` / `EmployeeId` keep the old values.
  Result: old service, new service's duration, new service's price, an audit row claiming a change.
  Cause (observed): `AppointmentHandler.GetWithBookingsForMutation` loads the `Service`/`Employee` navigations, and
  `context.Appointments.Update(graph)` in `UpdateWithBookingsCore` lets the stale navigation win over the changed FK. `Move` and
  `CompleteExisting` (no such navigations) persist the same change correctly.
  **D3A:** the frame now lives on the appointment's single `AppointmentSegment`, so the stale-navigation mechanism no longer
  exists; the pinned behaviour is kept *explicitly* in `AppointmentService.Update` (the segment keeps the current service and
  employee; time, duration and room follow the request). Not fixed — still pending the new edit path.
  **M1E: FIXED (intentionally).** `Update` now persists the requested service and employee on the (single) segment, together
  with the derived duration, room and price; segment-native edits use `PATCH api/segments/{id}/service|employees|time|room` and
  `PUT .../resources`. On a multi-segment appointment the flat `Update`/`Move`/`CompleteExisting` answer
  `SEGMENT_SELECTION_REQUIRED`.
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
* **F-08 Package clock and link inconsistencies — FIXED in D3B3A.** Was: eligibility judged at the *appointment date*,
  deduction at *now*; and a returned coverage left `Booking.ClientPackageId` set so a cash re-completion failed with
  `PAYMENT_NOT_ALLOWED`. Now package usage is a `PackageConsumption` ledger on the participation; eligibility and
  consumption are both judged on the service-performance date as a company-local date (`PackageValidity`), and the payment
  guard asks for an ACTIVE consumption. Test: `Individual_APackageValidOnAPastAppointmentDateButExpiredToday_CoversThatAppointment`,
  `Xor_AfterTheCoverageWasReturned_ACashReCompletionIsAllowed_*`, `PackageConsumptionLedgerTests`.
  D3B3A.1: the expiry is a calendar date (`ClientPackage.ValidUntilDate`, PostgreSQL `date`) computed at sale from the
  sale Company's business date; validity is `service local date (appointment Company zone) <= ValidUntilDate`; `/eligible`
  requires the Company; a reversal restores `Active` regardless of the clock. Test: `PackageValidityCalendarTests`.
  D3B3A.2: `Package.ValidityFixedDate` is a calendar date (PostgreSQL `date`, used as-is at sale); `/eligible` requires
  the service date (no fallback to "now"). Test: `PackageCatalogDateTests`.
* **F-09 Delete leaks a persistence error — FIXED in D3B3B.** Was: same-day delete of an appointment whose booking sits on a
  checkout item failed with a raw `DbUpdateException` (FK). Now settlement history (any checkout item on the participation, hence
  any payment/allocation) makes the participation non-untouched (`ParticipationHistory`), and the delete / client removal fails
  with `REFERENCED_CANNOT_DELETE`. Test: `Delete_OfAnAppointmentWhoseBookingWasAddedToACheckout_IsRefusedWithADomainError`,
  `ParticipationSettlementTests.SettlementHistory_MakesAParticipationNonUntouched_WithADomainError`.

* **F-20 Appointment-wide cancel did not classify lateness — FIXED in M1E.1 (intentional).** Old behaviour: cancelling the whole
  appointment left `IsLateCancellation` empty on every cancelled participation ("a business cancellation is never late"), while a
  participation or Booking-wide cancel classified it. Target behaviour: every participation cancelled by an appointment-wide
  cancel is classified independently from **its own segment's `PlannedStart`** with the organization cutoff
  (`BookingCancellationPolicy.IsLateCancellation(ParticipationExecutionContext, …)` — the single formula for all three scopes),
  so one appointment cancel can yield late A and on-time B. Never the appointment start or range start.
  Test: `AppointmentCancel_CancelsEveryConfirmedBooking_AndClassifiesLatenessPerParticipation`,
  `MultiSegmentAppointmentTests.AppointmentCancel_*`.

* **F-21 Group RemoveMember did not classify lateness — FIXED in M1F (intentional).** Old behaviour: participations cancelled by
  removing a group member kept `IsLateCancellation` empty. Target: every cancelled participation is classified from its own
  segment's `PlannedStart` through the central `BookingCancellationPolicy` (one removal can yield late A and on-time C).
  Test: `GroupCapacityCharacterizationTests.RemoveMember_*`, `MultiSegmentGroupTests.RemoveMember_*`.
* **F-20/F-21 superseded by P1 (ADR-0016, D2).** Lateness is classified ONLY for a CLIENT cancellation (participation or
  Booking-wide, per participation segment, against the resolved policy version's window — `CancellationPolicyRules`).
  An appointment-wide cancel is always Business and a member removal / template deselection is System: neither is
  classified nor produces a policy consequence. Tests: `MultiSegmentAppointmentTests.BookingWideClientCancel_*`,
  `ClientCancel_ManyBookingsAndSegments_*`, `MultiSegmentGroupTests.RemoveMember_*_AsSystem_*`.
* **P1 — the 17 intentional changes of the policy engine (docs/p1/P1_DECISION_RECORD.md).** Pinned in
  `CancellationPolicyEngineTests` and updated in place (each changed assertion is marked `CHANGED in P1`): one transition
  matrix for Individual and Group, same status is a true no-op, manual payments no longer block a correction, Group
  un-check-in keeps the price, NoShow only from segment start (`ATTENDANCE_BEFORE_START`) and Client cancel only before it
  (`CANCELLATION_AFTER_START`), structured cancellation / no-show metadata that always matches the status, status-aware Due
  with unclamped Outstanding + `SurplusAmount`, and `NO_ACTIVE_PARTICIPATIONS` for an empty Booking-wide cancel. Tests that
  need a started segment use `SchedulingWorld.MoveToPast`; seeded Cancelled/NoShow rows carry status-consistent metadata.
* **F-22 Group capacity — CHANGED in M1F (intentional).** Old: one hard `Group.Capacity` for the whole occurrence; generation
  refused to run when the roster exceeded it. Target: capacity is a SOFT business seat limit per `GroupSegmentTemplate`
  (seat = Confirmed participation on that segment; membership = active members selecting the template); exceeding it needs an
  explicit `OverrideCapacity` plus the raw grant `groups.capacity.override` (403 without it). Room/Resource/Employee/Client
  rules stay hard and are never overridden. Generation reproduces existing membership (no member is dropped).
  Test: `GroupOccurrenceGenerationCharacterizationTests.Generate_WhenTheGroupHasMoreActiveMembersThanItsCapacity_ReproducesTheMembership`,
  `MultiSegmentGroupTests.SoftCapacity_*`, `HardRoomCapacity_*`.
* **F-23 Group generation vs membership changes — FIXED in M1F.1 (intentional).** Old: generation read members/selections
  before its transaction and never re-validated them; a concurrent AddMember / selection expansion could not see the
  not-yet-committed occurrence, so the new member was permanently missing from it. Target: every membership mutation
  (AddMember, ChangeMemberSegmentTemplates, RemoveMember) bumps `groups.membership_version` under the group row lock (after
  its subject locks); generation takes `FOR SHARE` on the candidate groups (ordered by id, after its subject locks) and
  verifies the version equals its snapshot, retrying up to 3× (`CONCURRENCY_CONFLICT` after that). Either generation commits
  first and the change propagates into the new occurrence, or the change commits first and generation sees it. Per-group
  row locks only; no org-wide or advisory group locks.
  Test: `MultiSegmentGroupConsistencyTests.Race_*`.
* **F-24 RemoveMember / selection removal use the central untouched rule — CHANGED in M1F.1 (intentional).** Untouched
  future participations (`ParticipationHistory.IsUntouched`) are hard-deleted (empty Bookings removed, `ParticipationRemoved`
  audit, no cancelled event); history-bearing ones are cancelled with per-segment lateness (F-21). Re-adding after an
  untouched removal creates a fresh participation. Re-adding after a history-bearing cancellation does NOT reactivate it
  (no reliable provenance; no reason-text heuristics) — pinned as open ambiguity.
  Test: `MultiSegmentGroupConsistencyTests.ReAdd_*`, `GroupCapacityCharacterizationTests.RemoveMember_*`.
* **F-25 Group attendance is per segment — CHANGED in M1F.1 (intentional).** `Segments[]` reports Recorded (concrete
  participations on that segment, authoritative for history) and Expected (active members selecting the segment's template
  without a participation, only for future, non-cancelled segments). Guests appear only as Recorded (`IsMember = false`).
  M1H: the occurrence-level `Expected`/`Recorded` lists are removed — attendance is per segment only.
  Test: `MultiSegmentGroupConsistencyTests.Attendance_*`.
* **F-26 Multi-employee segments — ENABLED in M1G (intentional).** `MULTI_EMPLOYEE_NOT_SUPPORTED` is gone. A segment has
  1..N equal employees (individual) or 0..N (group template / trainerless session). Pricing source per segment
  (`SegmentPricingMode`): 0 employees → Standard; 1 → automatic Employee/that employee; 2+ → explicit Standard or
  Employee + an assigned employee (`PRICING_SOURCE_REQUIRED` / `INVALID_PRICING_SOURCE`), re-required on every employee-set
  change resulting in 2+. Price precedence: Employee mode = employee+company → employee → company → organization →
  default; Standard skips the employee tiers. Participations snapshot `pricing_mode`/`pricing_employee_id`. (M1H: the flat
  Update/Move/CompleteExisting and `EMPLOYEE_SET_COMMAND_REQUIRED` are removed.) Employee-set changes on a segment with a
  Completed participation or a closed-out session → `SEGMENT_EXECUTION_HISTORY_LOCKED`. Own scope may not add/remove
  coworkers. Test: `MultiEmployeeSegmentTests`, `MultiEmployeeHttpContractTests`.
* **F-27 Commission per employee — CHANGED in M1G (intentional).** Individual: every segment employee earns independently
  from the participation's final price (percentage) or the fixed amount; unique per (participation, employee, source
  version); a correction reverses every employee's entry. Group: fixed session commission per (segment, employee) at
  close-out (no per-client multiplication, trainerless segment earns nothing); `GROUP_COMMISSION_NOT_SUPPORTED_FOR_MULTI_SEGMENT`
  is no longer emitted; a non-Fixed rule is not evaluated and yields `GROUP_COMMISSION_RULE_NOT_SUPPORTED`.
  Test: `MultiEmployeeSegmentTests.Commission_*`, `MultiEmployeeGroupTests.CloseOut_*`.
* **F-28 Group staffing per template — CHANGED in M1G (intentional).** `groups.default_trainer_id` is dropped; staff and
  pricing source live on `GroupSegmentTemplate` and are copied into generated segments (template edits affect future
  generation only). M1H: the `DefaultTrainerId` projection/input is removed — staff is only
  ever set per template (`EmployeeIds`). Test: `MultiEmployeeGroupTests`.
* **F-29 Employee/service eligibility — FIXED in M1G (intentional).** Old: no assignments = no services. Target: no
  assignments = every service; any assignment restricts (create, segment employees, templates, generation, available slots).
* **DB enforcement audit (M1G).** "Pricing employee ∈ segment/template employees" would need a circular (deferred) FK from
  the segment to its own employee rows — enforced in the domain (`SegmentPricingSource`); the DB only checks mode/employee
  consistency (CHECK) and the FK to employees.
* **DB enforcement audit (M1F.1).** Occurrence identity (`group_slot_id`, first segment start) spans appointments and
  segments and cannot be a unique constraint without denormalization; "segment template belongs to the appointment's group"
  needs `group_id` on `appointment_segments`. Both stay application-enforced (slot advisory lock / generation validation).

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

### Scheduling rules vs the business-rules document (old `docs/poslovna-logika-pregled.md`, removed 2026-10-06 as obsolete)

* **F-15 The document is stale in places.** Schedule breaks and working hours are **hard blocks** unless a full-scope caller sets
  `OverrideAvailability` (doc §4.4 says warning); there is no `NoShow` appointment status (doc §"AppointmentStatus"); group
  deactivation does **not** delete future occurrences (doc says it does); the error code and warning code for working hours differ
  (`OUTSIDE_WORKING_HOURS` vs `OUTSIDE_WORKING_HOURS_WARNING`); `ValidatePackageSelections` is now `ValidateSettlements`.
  Test: `AppointmentWorkforceAvailabilityCharacterizationTests`, `GeneratedOccurrence_SurvivesGroupDeactivationAndSlotRemoval`.
* **F-16 Workforce checks differ per flow.** Create and the segment commands always check; `CompleteNow` checks only for a
  future start; completing an existing participation never checks (it schedules nothing); recurring/group generation report aggregated `RECURRING_CONFLICT` (and skip holidays silently for
  groups); the recurring **client** overlap still surfaces as `APPOINTMENT_OVERLAP`. A missing working-hours template means "never
  available". Absence with `DateTo = null` is open-ended.

### Persistence and races

* **F-17 Overlap protection is application-level only** (employee, room, client): Create/Update/Move are not transactional and take no
  lock, so two concurrent requests can both pass the check. The database has no exclusion constraint (a double-booked employee/room
  can be written directly); it does enforce `ux_bookings_appointment_client`. Group-occurrence uniqueness (slot, start) was the
  unique index `ux_appointments_group_slot_startsat` until D3A; that index went away with `appointments.starts_at` and the guard
  is now `GroupHandler.AddAppointments` (per-slot transaction advisory lock + re-check before insert — an application-level
  guard, not a database constraint: a direct SQL insert is no longer refused).
  Test: `Database_HasNoExclusionConstraint_*`, `Database_DoesEnforce_*`, `DuplicateOccurrence_IsAlsoStoppedAtWriteTime_UnderTheSlotLock`,
  `AppointmentSegmentCutoverTests.ConcurrentGenerationOfTheSameRange_PersistsEachOccurrenceOnce`.
  The race itself is **not** asserted (non-deterministic); only the absence of a database guard is.
* **D3B1 hard-delete rule (locked).** A Booking and its Participation are hard-deleted only while the participation is
  *untouched* (Confirmed, `StatusVersion` 0, no arrival, no cancellation reason, no late classification) — one definition,
  `ParticipationHistory`. Removing a participation with history (`DELETE /participations/{id}`) → `REFERENCED_CANNOT_DELETE`
  (terminal participations included); same-day Delete is refused if any participation has history.
  Participations are deleted explicitly before their bookings — never by cascade. F-09 is unchanged for untouched bookings
  that sit on a checkout (a Completed-then-corrected booking is now stopped earlier by the history rule).
  Test: `BookingParticipationLifecycleTests` (deletion region).
* **F-18 Small hazards.** Appointment-wide `Cancel` twice silently overwrites the appointment's `CancellationReason`; adding a client to
  an Individual appointment has no capacity concept beyond the room's people limit; available-slot search ignores rooms and clients and lists absent employees
  with empty slot lists; read models join Service/Employee/Room names live (no name snapshot).

### Host environment

* **F-19 Scheduling depends on the host timezone.** **FIXED** by the Timezone Foundation slice (see *Resolution* below).
  Root cause:
  * Date → `DateTimeOffset` conversion uses the **machine-local offset** in the absence queries:
    `AppointmentService.EnsureWorkforceAvailability` passes `startsAt.Date` (an unspecified-kind `DateTime`) to
    `RosterEntryHandler.GetForPeriod`, and `GetAvailableSlots` does `DateTimeOffset requestedDay = query.Date.Date`. On a CET host
    2031-03-03 becomes `2031-03-03 00:00+01:00` (= `2031-03-02 23:00Z`), so `DateFrom <= periodTo` misses a roster entry stored at
    UTC midnight (the shape `RosterEntryService` writes) — the **first day of every absence** is dropped (the last day west of UTC).
  * Legacy Npgsql timestamp behaviour (`Npgsql.EnableLegacyTimestampBehavior`, set in `DatabaseContext.GenerateContext`) returns
    stored `timestamptz` values in the **host-local offset**.
  * `GetAvailableSlots` builds busy intervals from `StartsAt.TimeOfDay` of those values, so appointments and breaks are shifted by
    the host offset on non-UTC hosts.

  Affected tests (all valid characterization tests; they pin the intended behaviour and must stay unchanged):

  | Test class | Test |
  |---|---|
  | `AppointmentReadModelCharacterizationTests` | `AvailableSlots_DoNotConsiderRoomsOrClients_OnlyTheEmployee` |
  | `AppointmentReadModelCharacterizationTests` | `AvailableSlots_ExcludeSlotsThatOverlapTheEmployeesAppointmentsAndBreaks` |
  | `AppointmentReadModelCharacterizationTests` | `AvailableSlots_OmitEmployeesWhoCannotPerformTheServiceOrAreInactive_AndEmployeesOnAbsence` |
  | `AppointmentWorkforceAvailabilityCharacterizationTests` | `Absence_EmployeeAbsentOnTheDay_IsRejected` |
  | `AppointmentWorkforceAvailabilityCharacterizationTests` | `Absence_MultiDayRange_CoversEveryDayInclusive` |
  | `AppointmentWorkforceAvailabilityCharacterizationTests` | `Absence_WithFullScopeOverride_SucceedsWithAWarning` |
  | `AppointmentWorkforceAvailabilityCharacterizationTests` | `Override_IgnoredWithoutFullScope_EvenWhenRequested` |
  | `AppointmentWorkforceAvailabilityCharacterizationTests` | `Override_OnlyOneWarningIsProducedPerCall` |
  | `AppointmentWorkforceAvailabilityCharacterizationTests` | `Priority_AbsenceIsReportedBeforeABreak` |
  | `AppointmentWorkforceAvailabilityCharacterizationTests` | `Priority_AnAbsenceHidesTheOutsideHoursViolation` |

  Baselines (suite as of the characterization commits, 528 tests):
  * **UTC host** (e.g. `TZ=UTC dotnet test` on Linux/macOS): **528 passed, 0 failed.**
  * **Windows / CET host** (and Linux with `TZ=Europe/Zagreb`): **518 passed, 10 failed** — exactly the 10 tests above, nothing else.

  After S1 (occupancy read seam, +25 tests, 553 in total): **UTC 553/553**, **CET 543 passed / 10 failed** (the same 10).
  After S2 (execution-context seam, +14 tests, 567 in total): **UTC 567/567**, **CET 557 passed / 10 failed** (the same 10).
  After S3 (write seams + ownership, +25 tests, 592 in total): **UTC 592/592**, **CET 582 passed / 10 failed** (the same 10).

  **Resolution (Timezone Foundation).** Scheduling no longer reads the host timezone anywhere:
  * `Organization.TimeZone` (IANA id, default `Europe/Zagreb`) is the organization's business timezone;
    `Company.TimeZone` (nullable IANA id) overrides it per Company — NULL inherits, the Organization value is never copied
    into Company rows. **Effective Company zone = `Company.TimeZone ?? Organization.TimeZone`**
    (`OrganizationTimeZones.Effective`), exposed as `CompanyDto.TimeZone` / `CompanyDto.EffectiveTimeZone`.
  * `OrganizationCalendar` / `IOrganizationCalendarService` (`GetCalendar`, `GetCompanyCalendar`, `GetCompanyCalendars`) are
    the only local ↔ UTC conversion (never `TimeZoneInfo.Local`).
  * Absence dates (`roster_entries.date_from/date_to`), holiday dates (`company_holidays.date`) and the working-hours cycle
    anchor are PostgreSQL `date` / `DateOnly` — compared with the appointment's **Company-local** date.
  * Working hours, available slots, break busy-windows, recurrence (appointments, breaks), group generation, the dashboard
    day and the same-day delete rule use the effective **Company** zone; the roster "today" uses the employee's primary
    Company zone (Organization zone without one). Overlaps stay UTC-instant comparisons.
  * `Npgsql.EnableLegacyTimestampBehavior` is removed; every `DateTimeOffset` is written and read as a UTC instant.
  * The characterization fixture (`SchedulingWorld`) configures its Organization as **UTC** — the suite's constants are UTC
    wall-clock values — so the 10 tests above pass unchanged; Europe/Zagreb and DST behaviour is covered by
    `TimezoneSchedulingTests`, `CompanyTimeZoneTests` and `OrganizationCalendarTests`.
  * Verified host-independent: the full suite passes with `TZ=UTC`, `Europe/Zagreb`, `America/New_York` and `Asia/Tokyo`.

  Any refactor must keep these two baselines (plus its own new tests) — a failure outside this list on a CET host, or any failure on a
  UTC host, is a real regression.

## Known limits of the suite

* **Race conditions on Create and segment commands** cannot be asserted deterministically without changing production code (see F-17).
* **OutboxProcessorService** (claim/lock/retry) is not exercised — handlers are invoked directly, so the outbox → Notification step
  is covered but delivery mechanics are not.
* **HTTP layer / `RequireGrant` attributes** are not exercised; services take `hasFullScope` directly, which is the boundary the
  controllers use.
* **Time zones**: the characterization tests run their Organization in the UTC business timezone (all constants are UTC
  wall-clock values). Organization-local scheduling (Europe/Zagreb, DST) is covered by `TimezoneSchedulingTests`, Company
  overrides (inheritance, New York/Tokyo, DST) by `CompanyTimeZoneTests`; results
  do not depend on the host timezone (F-19 fixed).
* **Calendar constants** (2031 appointments, 2035 package expiry) are "far future" today; they must be moved forward if the real
  clock approaches them.
* Group slot management (`AddSlot/UpdateSlot`), the operational dashboard and the future-activity providers are outside this suite.
* **Notification grouping debt (M1E).** Notifications stay per participation (unchanged): a multi-segment Booking whose
  participations are cancelled together (Booking-wide cancel) emits one cancellation notification per participation, not one
  grouped message per Booking/Appointment. Grouping is deferred to the notification-delivery redesign.
