# DuneLight P1 — Cancellation / Late Cancellation / NoShow Policy Engine — Decision Record

> Oct 5, 2026 · @Silvio
>
> Imported verbatim (structure and wording) from `Cancellation - Late Cancellation - Policy engine.docx` on 2026-10-06.
> From now on this file is the source of truth for P1; the .docx is historical. Summary ADRs:
> [ADR-0015](../decisions/0015-p1-policy-profili-i-resolver.md), [ADR-0016](../decisions/0016-p1-initiator-i-vremenska-pravila.md),
> [ADR-0017](../decisions/0017-p1-posljedice-ledger-i-settlement.md), [ADR-0018](../decisions/0018-p1-korekcije-waiver-i-grupe.md).
> Decisions and answers given during implementation are appended to the [implementation decision log](#implementation-decision-log) at the end.

## Status and baseline

P1 decisions D1–D13 are locked and the P1 scope is closed; this record is submitted for review before any implementation prompt is written.

Code baseline: branch `development-claude`, HEAD `16b2a59` (M1H), clean tree, 171 migrations (latest `20261022000000`), 1089/1089 tests passing (UTC run).

Evidence: the read-only P1 audit (`p1-audit/P1_AUDIT_REPORT.md`, verdicts C1–C10) and the 114 copied source files. Code references below use the audit's abbreviations (I/ Infrastructure, C/ Core, A/ API).
*(Note on import: the audit report is not in this repository; line references below are as of `16b2a59`.)*

Precedence: the M1H handoff is authoritative for the foundation; this record is authoritative for P1. Target Architecture v1 and Decision Log v1 remain valid only where they do not conflict. Decision Log items #15, #27, #36 and #38 are closed by M1A/M1G/M1F and should be updated in that log.

Reopening rule: D1–D13 are reopened only if code evidence shows a locked rule is impossible or unsafe to implement.

Development DB policy applies: clean target schema, no compatibility columns, no dual-write, no backfills; obsolete contracts are removed.

Terminology: Participation = BookingSegmentParticipation; event time = the single server timestamp of the lifecycle event; consequence = ParticipationPolicyConsequence.

---

## D1 — Policy scope and resolution

Policies are named, versioned profiles resolved per Participation with precedence Company+Service → Service → Company → Organization default.

**Locked rule**
- `CancellationPolicy` is a named profile (Id, OrganizationId, Name, IsActive). `CancellationPolicyVersion` holds the immutable rule fields (D3, D4, D6).
- `CancellationPolicyAssignment` (OrganizationId, CompanyId?, ServiceId?, CancellationPolicyId) supports exactly three scopes: Company+Service, Service only, Company only.
- The Organization default is represented either on OrganizationSettings or as an explicit default assignment, whichever is cleanest in the code (implementation choice, not a business rule).
- One central resolver: `ResolveCancellationPolicy(organizationId, companyId, serviceId)` with the exact precedence above. Inputs are `Appointment.CompanyId` and the Participation's `Segment.ServiceId`.
- Published versions are immutable; when a policy applies, the exact PolicyId + Version is snapshotted (D3, D5).

**Model / API consequence**
- New tables for profiles, versions and assignments, with uniqueness on assignment scope (one assignment per Company+Service, per Service, per Company).
- Removed: `organization_settings.cancellation_cutoff_minutes`, `PUT /api/organization/settings/cancellation-cutoff`, `OrganizationSettingsService.UpdateCancellationCutoff` and its default constant.
- New policy-management endpoints for profiles, versions, assignments and the Organization default.
- Behavior change from current code: the window was a single per-Organization setting (default 1440 min); it becomes per-scope and versioned.
- Authorization: policy management uses `catalog.cancellation-policies.view` / `.manage` (D10), not `organization.settings.manage`.
- Non-goals: Membership and Client Tags are not resolution dimensions; Package context is not a dimension.

## D2 — Cancellation initiator

Every cancellation records one of three initiators, and the policy is evaluated only for Client.

**Locked rule**

| Initiator | Who sets it | Policy evaluated | Late classification | Snapshot / consequence |
|---|---|---|---|---|
| Client | caller, explicitly | yes | on-time or late | yes when late (D5) |
| Business | caller, explicitly | no | null | none |
| System | internal code only | no | null | none |

- The initiator is separate from the acting user: Initiator = Client + CancelledBy = receptionist means the client asked and staff recorded it.
- No cancellation command has a default initiator.
- Participation cancel and Booking-wide cancel accept Client | Business (required). Appointment-wide cancel is Business only; Client is rejected there.
- System is never accepted from any public or manual request; internal code uses it for Group member removal and template deselection.
- NoShow has no initiator: a valid NoShow is itself client non-attendance. A business failure to deliver is a Business cancellation, not a NoShow.
- The `booking.cancelled.v1` outbox event carries CancellationInitiator. Notification wording and grouping are not changed in P1.
- Behavior change: Group withdrawals (`GroupService.cs:740-742, 795-796, 1023-1024`) currently set IsLateCancellation and look identical to client cancellations; they become System with no classification.
- Authorization: see D10 — Business at Participation/Booking level requires `appointments.write.all` and a reason.
- Ledger/history: provides structured provenance that handoff debt H lacked. Automatic reactivation is not solved in P1.
- Queued debt: a Booking-wide Client cancellation of the last client leaves the Appointment Scheduled and its slot reserved (§10, §11); freeing it needs a separate Business Appointment cancel. An atomic "release the slot" option is future work.

## D3 — Lateness, timing guards and metadata

A Client cancellation is late when `PlannedStart − CancelledAt < CancellationWindow`, measured per Participation Segment with one server timestamp.

**Locked rule**
- One window per policy version: `CancellationWindowMinutes >= 0`. Binary classification only (on-time / late); no tiers.
- Reference instant: the Participation's own `Segment.PlannedStart`; Appointment-wide operations classify each Participation separately (existing M1E.1 rule).
- One server-generated event timestamp is used for both classification and persistence; requests cannot supply it.
- Boundary: exactly at the cutoff is on-time. Absolute elapsed duration between UTC instants; no local wall-clock or DST arithmetic.
- Window 0 is valid: every valid Client cancellation before start is on-time.
- Client cancellation requires `CancelledAt < Segment.PlannedStart`; otherwise `CANCELLATION_AFTER_START` (new), and the request is not classified. Business and System cancellations remain allowed after start.
- NoShow requires `now >= Segment.PlannedStart`; otherwise `ATTENDANCE_BEFORE_START` on every path (Individual, Group member, guest, Booking-wide, Appointment-wide).
- Appointment-wide no-show is atomic: if any affected Confirmed Participation's Segment has not started, the whole command is rejected with `ATTENDANCE_BEFORE_START`.

**Current-state metadata on Participation**

| Field | Client cancel | Business / System cancel | NoShow |
|---|---|---|---|
| CancellationInitiator | Client | Business / System | null |
| CancelledAt, CancelledBy | set | set | null |
| CancellationReason | optional | required for Business; set by code for System | null |
| IsLateCancellation | true / false | null | null |
| CancellationPolicyId, CancellationPolicyVersion | set | null | null |
| AppliedCancellationWindowMinutes | set | null | null |
| NoShowAt, NoShowBy, NoShowReason | null | null | set (reason optional) |

- CancelledAt is the exact timestamp used for classification. History stays in AppointmentAuditLog.
- Behavior changes: cancellation after start was allowed and always late; NoShow before start was allowed for existing Participations; NoShow reused CancellationReason; Participation had no CancelledAt/By.
- Non-goals: tiers, backdated or client-supplied timestamps, local-time windows.

## D4 — Consequence shape

A policy version configures exactly two consequence events, LateCancellation and NoShow, each with an independent fee rule capped at FinalPrice.

**Locked rule**
- LateCancellation applies only to a Client cancellation classified late. NoShow applies to every valid NoShow.
- On-time Client cancellation has no consequence and is not configurable; no "on-time fee" exists.
- Each event has FeeType: `None | Fixed (amount ≥ 0) | Percentage (0–100)`. PackageAction is a separate dimension (D6).
- Percentage base: `Participation.Amount` (FinalPrice) at event time. Not BaseAmount, SuggestedAmount, paid amount, Outstanding or package value. Package coverage does not zero the base.

```
base         = Participation.Amount
rawFee       = 0                  (None)
             = value              (Fixed)
             = base × pct / 100   (Percentage)
finalFee     = Round(min(rawFee, base), 2, AwayFromZero)
WasFeeCapped = rawFee > base
```

- Decimal arithmetic only. Commission rounding is not changed (`CommissionService.cs:535` has no rounding; separate debt).
- Money-precision evidence: `Participation.Amount` is `numeric(10,2)` (`Migration_2026_10_08_BookingSegmentParticipations.cs:43`), so the base never carries fractional cents at evaluation. The Fixed fee value is stored as `numeric(10,2)` and validated to ≤ 2 decimals; the percentage to ≤ 2 decimals. Therefore `finalFee ≤ base` always holds; pin with a test.
- Snapshot (immutable): Event, FeeType, ConfiguredFeeValue, FeeBaseAmount, CalculatedFeeAmount, WasFeeCapped, PolicyId, PolicyVersion — physically on the consequence record (D5).
- Non-goals: allowances, tag exceptions, tiers, fees for on-time cancellation.

## D5 — Consequence ledger and settlement Due

Every policy-applied LateCancellation and NoShow creates an immutable ParticipationPolicyConsequence, and ParticipationSettlement becomes the single status-aware Due derivation.

**Locked rule — the record**
- Created for every policy-applied event, including FeeType = None and fee 0 ("evaluated, no fee" differs from "did not apply").
- Not created for on-time Client, Business or System cancellations.
- Fields: ParticipationId, SourceVersion (= Participation StatusVersion of the event), Event, FeeType, ConfiguredFeeValue, FeeBaseAmount, CalculatedFeeAmount, WasFeeCapped, PolicyId, PolicyVersion, PackageAction, PackageUnitConsumed, ClientPackageId?, Status (`Active | Waived | Reversed`, D10), CreatedAt/By, ReversedAt/By, ReversalReason, WaivedAt/By, WaiverReason.
- DB uniqueness on (ParticipationId, SourceVersion). Never deleted or rewritten; follows the existing PackageConsumption / CommissionEntry source-version pattern.
- `Participation.Amount` is never replaced by a fee.

**Locked rule — settlement**

| Participation state | MonetaryDue |
|---|---|
| Confirmed / Completed | existing service Due (Amount, or 0 when completion package coverage is active) |
| Cancelled / NoShow with an Active consequence that has an active linked policy PackageConsumption | 0 |
| Cancelled / NoShow with an Active consequence and no active policy consumption | CalculatedFeeAmount |
| Cancelled / NoShow without an Active consequence (on-time, Business, System, Waived, Reversed) | 0 |

- Settlement does not re-run classification; it reads the active consequence only.
- `Outstanding = MonetaryDue − Settled`, not clamped. SurplusAmount is defined in D7.
- Every consumer uses this derivation: dashboard outstanding (the local `Status != Cancelled` filter in `OperationalDashboardService.cs:176-192` is removed), BookingCommercialSummary, appointment read models.
- Revenue stays payment-based; a Due fee is revenue only once paid.
- Checkout eligibility becomes financial: a Participation with positive payable Outstanding may be settled regardless of status (replaces `CHECKOUT_ITEM_NOT_ELIGIBLE` for Cancelled at `CheckoutService.cs:165-166`). Package-vs-money exclusivity per D6.
- Reversal: correction back to Confirmed marks the Active consequence Reversed (never deleted), the service Due returns, and settled money stays attached as prepayment. A later event creates a fresh consequence with the new SourceVersion.
- Behavior change: Cancelled and NoShow kept MonetaryDue = Amount (`ParticipationSettlement.cs:37-46` ignores status); the dashboard, read models and checkout each used a different implicit rule (C5, C10).

## D6 — PackageAction

Each event may consume one counted package unit instead of charging the fee; a unit and a fee are alternatives, never added together.

**Locked rule**
- PackageAction: `None | ConsumeUnit`, configured per event, independent of FeeType.
- A successfully consumed counted unit makes the policy MonetaryDue 0; otherwise the fee applies. "Unit + fee" is not supported in P1 (would change SettlementExclusivityPolicy).
- Unlimited packages are never a penalty source; a client with only unlimited eligible packages falls back to the fee.

Package selection (only when a consequence exists, its PackageAction is ConsumeUnit, and exclusivity permits a package):
1. Active monetary settlement already exists → no package is selected or consumed; fee fallback. `PACKAGE_SELECTION_REQUIRED` is never thrown in this case.
2. Explicit ClientPackageId → used if eligible for Service, Company and service date; otherwise `PACKAGE_NOT_ELIGIBLE`.
3. No explicit package, exactly one eligible counted package → selected automatically (same as the existing Group check-in rule, `BookingService.cs:966-983`).
4. No explicit package, several eligible counted packages → `PACKAGE_SELECTION_REQUIRED` (new).
5. No eligible counted package → nothing consumed; fee fallback.

- The optional ClientPackageId on cancel / no-show / attendance commands is ignored unless steps 2–4 apply. Callers never need to predict lateness.

**Schema**
- `PackageConsumption.Trigger`: `ServiceCompletion | PolicyConsequence`.
- `PackageConsumption.ParticipationPolicyConsequenceId?`: required for PolicyConsequence, null for ServiceCompletion (DB CHECK).
- `UNIQUE (ParticipationPolicyConsequenceId) WHERE ParticipationPolicyConsequenceId IS NOT NULL`.
- The consequence snapshots PackageAction, PackageUnitConsumed and the ClientPackageId actually used. Current coverage is derived from the active linked consumption, never from that boolean.
- Reversal: reversing the consequence reverses its active linked consumption and returns the unit in the same transaction; both records are kept.
- Validity and locking: eligibility uses Segment Service, Appointment Company and the Segment service date. Existing ClientPackage versioning and lock order are unchanged (ClientPackage after Participation locks).
- Behavior change: no path consumed a package on Cancelled or NoShow (C2). Normal ServiceCompletion selection is unchanged.

## D7 — Surplus; no money movement

P1 never moves money because of a cancellation, no-show, waiver or correction; it only exposes the surplus.

**Locked rule**
- Never automatically: void a Payment, refund, create Client Credit, move a PaymentAllocation, or transfer settlement to another Participation.
- In the single settlement derivation: `Outstanding = MonetaryDue − Settled` (not clamped) and `SurplusAmount = max(Settled − MonetaryDue, 0)`.
- Examples: Due 10, Settled 50 → Outstanding −40, Surplus 40. Due 0, Settled 50 → Outstanding −50, Surplus 50.
- SurplusAmount is informational only: it is not Client Credit, a refundable balance, a wallet or available funds.
- Exposed wherever Due/Settled/Outstanding are already exposed.
- The existing overpayment guard is unchanged: Outstanding ≤ 0 cannot receive more money.
- `CheckoutService.VoidPayment` is unchanged and is not the P1 answer to a surplus (one Payment can be allocated across several CheckoutItems).
- Revenue stays payment-based; Due changes do not alter historical revenue.
- Correction example: Cancelled with fee Due 10 and Settled 50 (Surplus 40) → corrected to Confirmed with service Due 100 → Outstanding 50, Surplus 0.
- Non-goals (P3): partial/full refunds, Client Credit, allocation reversal/reallocation, credit as a settlement source, surplus listings.

## D8 — Commission

Policy events never generate commission in P1, and Group session commission is unchanged.

**Locked rule**
- LateCancellation and NoShow create no CommissionEntry for any Segment Employee, whatever the outcome: FeeType None, Fixed, Percentage, consumed unit or fee fallback.
- Service commission is never calculated from CalculatedFeeAmount, fee settlement or penalty consumption.
- ParticipationPolicyConsequence has no commission FK, field or side effect.
- A restored Participation later completed earns normal M1G service commission with the completion SourceVersion.
- Group session commission stays Fixed, per AppointmentSegment + Employee, at close-out, and is not generated, reduced or reversed by member consequences.
- Evidence: individual commission only on Confirmed → Completed (`BookingService.cs:605-609`); Group close-out commission ignores attendance (`CommissionService.cs:379-428`).
- Debt: commission on policy fees (a future, separate rule source); whether an empty or all-NoShow Group session earns session commission.

## D9 — Group rules

Group behavior follows D1–D8 per Participation, with no automatic NoShow, no refill waiver and no special case for auto-promoted clients.

**Locked rule**
- **9A Close-out:** unresolved Confirmed Participations produce only `GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS` and stay unchanged. No automatic NoShow. A NoShow recorded after close-out evaluates the normal NoShow policy.
- **9B Waitlist refill:** a LateCancellation consequence is never waived or reversed because the freed seat was refilled. No cross-client financial coupling; staff use the D10 waiver.
- **9C Auto-promotion:** a Participation activated by FIFO promotion follows the same policy (e.g. 24 h window, promoted at T−3 h, cancels at T−1 h → late, normal consequence).

**Derived rules (confirmed)**
- Member removal and template deselection → System cancellation → no consequence.
- Explicit whole-occurrence cancellation → Business → no consequence.
- Resolution per Participation from `Appointment.CompanyId` + `Segment.ServiceId`; lateness from each Segment's own PlannedStart.
- Attendance `Attended = false` → NoShow policy; `SetGroupAttendanceRequest.ClientPackageId` feeds D6 selection; `ATTENDANCE_BEFORE_START` applies.
- Cancellation frees the seat and may promote FIFO (existing); NoShow does not promote.
- Close-out does not lock later Participation corrections.
- Debt: automatic "seat refilled → waive"; grace or exemption for auto-promoted clients.

## D10 — Waiver and initiator authorization

An authorised user may waive a whole consequence (never part of it), and choosing Business requires `appointments.write.all` so the initiator cannot be used to bypass the policy.

**Locked rule — waiver**
- A waiver removes the entire consequence; classification never changes. No partial waiver, replacement fee, fee-only or unit-only waiver, and no consequence the policy did not produce.
- Consequence status Waived (with WaivedAt, WaivedBy, mandatory WaiverReason) is distinct from Reversed. A waiver cannot be undone in P1; a later correction to Confirmed leaves a Waived record Waived.
- Only Active consequences contribute Due or policy package coverage.
- At event time: Participation cancel, Booking-wide cancel, no-show and attendance commands accept `WaivePolicyConsequence` + `WaiverReason`. The policy is evaluated and snapshotted normally; the consequence is created directly as Waived; no package selection or consumption. Participations without a consequence (on-time) get no record. Booking-wide may therefore mix "no consequence" and "Waived".
- After the event: `POST /api/participations/{id}/policy-consequence/waive` (reason required) targets the Active consequence; in one transaction it reverses any active linked policy consumption (unit returned), marks the record Waived and writes audit PolicyConsequenceWaived. No money moves; no client-facing outbox event.
- Business cancellation and Client cancellation + waiver remain separate facts and are never represented as each other.

**Authorization matrix**

| Action | Required |
|---|---|
| Participation / Booking-wide cancel, Client | `appointments.write.own` (own Segment) or `.all` |
| Participation / Booking-wide cancel, Business | `appointments.write.all` + reason |
| Appointment-wide cancel (Business only) | `appointments.write.all` + reason |
| Waiver (either path) | normal access to the Participation + `appointments.policy.override` + reason |
| Group attendance waiver | `groups.attendance.own` / `.all` + `appointments.policy.override` + reason |
| Policy configuration read / write | `catalog.cancellation-policies.view` / `.manage` |

- `appointments.policy.override` never widens ownership scope.
- Seed the three new grants into the Admin template only; Reception and Trainer do not get `appointments.policy.override` by default.
- `organization.settings.manage` stays for unrelated settings (e.g. PackageConsumptionTiming).
- Audit: PolicyConsequenceWaived with Participation, consequence/source version, actor, reason, timestamp.

## D11 — Version selection and lifecycle

The policy is resolved once, at event time, from the current execution context; creating a version publishes it immediately.

**Locked rule**
- Resolve at the moment the cancellation or no-show is applied, using the D3 event timestamp, from Organization, `Appointment.CompanyId` and `Segment.ServiceId` with the D1 precedence, then take that profile's latest version.
- Resolve exactly once inside the lifecycle transaction and use that result for the whole event; snapshot PolicyId + Version.
- No pinning at Participation creation and no grandfathering: Service or Company changes before the event naturally change the resolved policy.
- Creating a version publishes it; there is no Draft, EffectiveFrom or scheduled publication. A change creates Version + 1; older versions are never modified or deleted. Assignments point to the profile, not a version.
- A policy that is the Organization default or has any assignment cannot be deactivated: `CANCELLATION_POLICY_IN_USE`. An inactive policy cannot be assigned or made default. No silent fall-through to another scope.
- Every Organization always has a valid default, created at registration and by the P1 cutover for existing development Organizations: window 1440 min; LateCancellation and NoShow both FeeType None + PackageAction None. A missing default is an integrity failure.
- Behavior change (correction accepted): the neutral default is not financially identical to the legacy code. Legacy settlement kept full Due on Cancelled and NoShow; under P1 a NoShow with None/None has Due 0. This is an intentional move from implicit to explicit policy-driven behavior.
- Debt: grandfathering / EffectiveFrom; scheduled publication.

## D12 — Corrections and transition matrix

Individual and Group share one transition matrix in which every different-status transition is allowed, subject only to the target event's guards and authorization.

**Matrix (Participation-addressed commands)**

| From \ To | Confirmed | Completed | Cancelled | NoShow |
|---|---|---|---|---|
| Confirmed | no-op | allowed | allowed (D3 guard by initiator) | allowed if started |
| Completed | allowed | no-op | allowed (D3 guard by initiator) | allowed |
| Cancelled | allowed | allowed | no-op | allowed if started |
| NoShow | allowed | allowed | Business only (NoShow implies started) | no-op |

Target guards: Client cancel needs `eventAt < PlannedStart` (`CANCELLATION_AFTER_START`); NoShow needs `eventAt >= PlannedStart` (`ATTENDANCE_BEFORE_START`); Business cancel may follow start with D10 authorization. No backdating.

**Locked rules**
- Same-status is a true no-op for both forms: no StatusVersion increment, no re-stamping, no consequence, package, commission, audit or outbox effect.
- Terminal → terminal is one atomic correction: (1) reverse all Active effects of the previous state; (2) clear previous current-state metadata; (3) increment StatusVersion once; (4) apply the new event completely; (5) create new effects with the new SourceVersion.
- Previous-state reversal covers: Active consequence and its policy PackageConsumption, completion CommissionEntries, completion PackageConsumption, completion- or check-in-generated payments (existing rules).
- Manual monetary settlement is never moved or voided by a correction and no longer blocks one: `BOOKING_HAS_NON_REVERSIBLE_PAYMENT` is removed as a correction block (`BookingService.cs:890-899`) and its characterization tests are updated.
- Reactivation into an occupying status (Confirmed or Completed from Cancelled/NoShow) re-runs Employee overlap, Client overlap, Room and Resource capacity, plus Group soft capacity with `groups.capacity.override` for future Segments, before commit.
- Override on correction: reversing an Active consequence with a real effect (CalculatedFeeAmount > 0 or an active linked policy consumption) requires normal access + `appointments.policy.override` + a correction reason. Zero-effect consequences need only normal access. Waived and Reversed records are never changed.
- Metadata always matches status: target Confirmed or Completed clears cancellation and no-show fields; target Cancelled clears no-show fields and sets cancellation fields; target NoShow clears cancellation fields and sets no-show fields. History lives in the audit log, consequences, consumptions, commissions and payments.
- Group price preserved: Group Completed → non-Completed no longer zeroes Amount, SuggestedAmount, BaseAmount or the pricing snapshot (`BookingService.cs:741-746`, `BookingParticipations.cs:139-147`); completion consumption, commission and check-in payments are still reversed.
- Cascades: Booking-wide and Appointment-wide cancel/no-show transition only Confirmed Participations; other statuses in scope are skipped. With nothing to transition both return `NO_ACTIVE_PARTICIPATIONS` (replaces Booking-wide `ALREADY_COMPLETED`).
- Unchanged: repeated Appointment-level explicit cancellation still re-stamps CancelledAt/By and writes another audit row (debt).

## D13 — Scope closure

Allowances, Client Tag exceptions and a configurable Client-reason requirement are out of P1, and the P1 scope is closed.

**Locked rule**
- Allowances (first late cancellation free, N per month, rolling periods, per-client counters) are not implemented; they need decisions on period, timezone, counted statuses, counter locking and reset. The D10 waiver is the P1 exception mechanism.
- Client Tags are not used in resolution. The resolver stays exactly Company+Service → Service → Company → Organization default.
- Reason requirements: an ordinary Client cancellation reason stays optional, with no configuration flag. A reason is mandatory for Business cancellation, waivers, and corrections that reverse an Active consequence with a real effect.
- Cascade clarifications (D12) and the unchanged Appointment-level re-cancel behavior are part of this closure.

---

## Known intentional behavior changes

These 17 changes from `16b2a59` are deliberate; characterization tests pinning the old behavior are updated, not treated as regressions.

| # | Area | Current behavior (evidence) | P1 behavior | Decision |
|---|---|---|---|---|
| 1 | Corrections | Individual Cancelled → Confirmed rejected with 400 (`BookingService.cs:378-381`) | Supported | D12 |
| 2 | Corrections | Individual: only Completed/NoShow → Confirmed; Group: any → any, unguarded | One matrix for both, with target-event guards | D12 |
| 3 | Idempotency | Repeat individual cancel/no-show → `ALREADY_COMPLETED`; Confirmed → Confirmed may throw | Every same-status transition is a true no-op | D12 |
| 4 | Corrections | Manual payment blocks individual Completed → Confirmed (`BOOKING_HAS_NON_REVERSIBLE_PAYMENT`) | Never blocks; manual money stays as settlement | D7, D12 |
| 5 | Group pricing | Un-check-in sets Amount, Suggested and Base to 0 | Price snapshot preserved | D12 |
| 6 | Settlement | Cancelled/NoShow MonetaryDue = Amount | Due from the Active consequence (fee, or 0) | D5, D11 |
| 7 | Read models | Dashboard excludes only Cancelled; summaries include both | One shared derivation everywhere | D5 |
| 8 | Checkout | Cancelled refused, NoShow accepted, by status | Eligibility from positive payable Outstanding | D5 |
| 9 | Timing | Client cancel after start allowed and marked late | Rejected with `CANCELLATION_AFTER_START` | D3 |
| 10 | Timing | NoShow before start allowed except for new guests | Rejected with `ATTENDANCE_BEFORE_START` on every path | D3 |
| 11 | Metadata | Free-text reason only; NoShow reuses it; no CancelledAt/By; stale fields survive corrections | Structured initiator, cancellation and no-show fields, always matching status | D2, D3, D12 |
| 12 | Classification | Group withdrawals and Appointment cancels get IsLateCancellation | Business/System get null and no consequence | D2 |
| 13 | Packages | No consumption on Cancelled/NoShow | Policy may consume one counted unit | D6 |
| 14 | API | `ReturnPackageEntry` / `ReturnEntryForClientIds` accepted (dead) | Removed | D6 |
| 15 | Settings | `organization_settings.cancellation_cutoff_minutes` + PUT endpoint | Removed; replaced by policy profiles | D1, D11 |
| 16 | Cascades | Booking-wide with nothing active → `ALREADY_COMPLETED` | `NO_ACTIVE_PARTICIPATIONS` | D13 |
| 17 | Authorization | Any write.own user could record any cancellation type | Business needs write.all; waivers and real-effect corrections need `appointments.policy.override` | D10, D12 |

## Schema, API, grant and error-code summary

P1 adds three policy tables and a consequence ledger, extends Participation and PackageConsumption, and removes the cutoff setting and dead package-return inputs.

**Schema**

| Object | Change |
|---|---|
| `cancellation_policies` | new: profile (Name, IsActive) |
| `cancellation_policy_versions` | new, immutable: Version, CancellationWindowMinutes, Late/NoShow FeeType + value (`numeric(10,2)` / percent ≤ 2 dp), Late/NoShow PackageAction; unique (policy, version) |
| `cancellation_policy_assignments` | new: CompanyId?, ServiceId?, PolicyId; at least one scope column set; unique per scope |
| Organization default | on organization_settings or a default assignment (implementation choice) |
| `participation_policy_consequences` | new ledger (D5 fields); unique (participation, source_version) |
| `booking_segment_participations` | add CancellationInitiator, CancelledAt/By, CancellationPolicyId, CancellationPolicyVersion, AppliedCancellationWindowMinutes, NoShowAt/By, NoShowReason |
| `package_consumptions` | add Trigger, ParticipationPolicyConsequenceId? + CHECK + partial unique |
| `organization_settings` | drop cancellation_cutoff_minutes |

**API**
- New: policy profile, version, assignment and default endpoints; `POST /api/participations/{id}/policy-consequence/waive`.
- Changed: Participation / Booking-wide / Appointment-wide cancel requests gain a required CancellationInitiator (Appointment-wide: Business only); cancel, no-show and attendance requests gain optional ClientPackageId, WaivePolicyConsequence, WaiverReason; status/correction requests gain a correction reason when D12 requires it; settlement DTOs gain SurplusAmount; Participation DTOs expose the new metadata.
- Removed: `PUT /api/organization/settings/cancellation-cutoff`; ReturnPackageEntry; ReturnEntryForClientIds.

**Grants (Admin template only):** `catalog.cancellation-policies.view`, `catalog.cancellation-policies.manage`, `appointments.policy.override`.

**Error codes:** new `CANCELLATION_AFTER_START`, `PACKAGE_SELECTION_REQUIRED`, `CANCELLATION_POLICY_IN_USE`; reused `ATTENDANCE_BEFORE_START` (all paths), `NO_ACTIVE_PARTICIPATIONS` (both cascades), `PACKAGE_NOT_ELIGIBLE`; `BOOKING_HAS_NON_REVERSIBLE_PAYMENT` no longer used as a correction block.

**Audit change types:** existing BookingStatus lifecycle audit plus new PolicyConsequenceWaived only. Creation and reversal of consequences are not separate audit rows; the immutable ParticipationPolicyConsequence ledger is authoritative for them.

**Outbox:** `booking.cancelled.v1` payload gains CancellationInitiator; no new event types.

## Debt and future extensions

These items are explicitly out of P1 and must not be implemented silently.

**Policy extensions**
- Allowances (first late cancellation free, N per period, counters).
- Client Tag policy exceptions and their precedence.
- Multi-tier windows and fees.
- Grandfathering / EffectiveFrom / scheduled publication.
- Automatic waiver when a waitlist replacement fills the seat.
- Grace or exemption for automatic waitlist promotion.
- Partial consequence waiver.
- Configurable requirement for an ordinary Client cancellation reason.
- Historical/backdated Client cancellation correction (a privileged feature).

**Operational**
- Atomic "release the slot" option for a Booking-wide Client cancellation of the last client.
- Appointment-level repeated cancel re-stamps CancelledAt/By and writes another audit row.
- Appointment-wide NoShow expires the Group waitlist with reason AppointmentCancelled (`AppointmentService.cs:984-986`).
- ArrivedAt/ArrivedBy columns exist but are never written (check-in feature).

**Financial (P3)**
- Surplus resolution: refunds, Client Credit, allocation reversal/reallocation, credit as a settlement source.
- Unit + fee together, mixed/partial package settlement (Decision Log #32, handoff debt O).

**Commission**
- Commission on policy fees (new rule source).
- Empty/all-NoShow Group session commission.
- Percentage commission has no rounding (`CommissionService.cs:535`).

**Housekeeping**
- Stale doc comments (`Appointment.cs:43`, `AppointmentService.cs:845-847`, `GroupAppointmentsController.cs:24-26`, `BookingSetStatusRequest.ClientPackageId`) are fixed only in files P1 touches.
- Decision Log v1 items #15, #27, #36 and #38 should be marked closed.

---

## Implementation decision log

Decisions, clarifications and answers given during P1 implementation, newest last. Format: date — question/context — answer — effect.

*(No entries yet — P1 implementation has not started.)*
