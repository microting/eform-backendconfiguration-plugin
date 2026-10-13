# Past-occurrence materialiser — design

Date: 2026-10-13
Status: approved in conversation (design), spec under review

## Problem

On tenants where the SDK setting `skipCloudDeploy` is `true`, overdue calendar
tasks never reach the compliance views or the mobile app's overdue list.

- Since items-planning service #1520 (2026-09-17, `SearchListJob.IsLeftToBackendConfiguration`)
  the scheduler skips every backend-configuration-owned planning (`IsLocked && !IsEditable`)
  when `skipCloudDeploy` is on. Its comment assumes backend-configuration creates its own
  Compliance rows.
- Backend-configuration only does that for **today and later**:
  `EventDeployService.EnsureDeployedAsync` filters `RotationDate >= todayUtc`
  ("the scheduler microservice owns historical deploys"), and it is only called from
  `Events.ListEvents` / `StreamEventChanges`, whose window the app always sends as today..today+2.
- `Events.ListTaskTracker` — the app's only source of overdue tasks — and the web task tracker
  and compliance list/report read existing `Compliances` rows only (`GetTaskTrackerList`,
  `BackendConfigurationTaskTrackerHelper.Index`, compliance report service). A missed occurrence
  has no row, so it is invisible: 0 overdue, and by #1374 a 100 % compliance.
- The calendar projects occurrences from the schedule (`GetTasksForWeek`) and shows them red.

Measured on production: 22 tenants have `skipCloudDeploy` on; most have backend-configuration
plannings whose last occurrence row is from mid-September.

## Contract

When the mobile app (or the web) asks for a task/compliance list, the server fills in every
missing occurrence the calendar shows — including past ones — so the list matches the calendar,
and every overdue occurrence can be completed from the flutter-eform app.

## Goals

1. Every past occurrence the calendar shows for a backend-configuration planning gets the same
   deployment a future one gets today: `PlanningCase`, `PlanningCaseSite`, SDK `Case` and
   `Compliance` row, for each site the occurrence is assigned to. It then appears as overdue
   (`Deadline < now`, not completed) in `ListTaskTracker`, the web task tracker and the compliance
   report, and completes through the existing `CompleteEvent` path.
2. All tenants affected since 2026-09-17 are caught up after deploy, without anyone opening a list.
3. No duplicates: an occurrence that already has a row (live or removed/done) for a site is never
   deployed again.

Non-goals: changing the items-planning scheduler or #1520; occurrences of tenants with
`skipCloudDeploy` off (the scheduler still owns those); the calendar showing deactivated tasks
(`AreaRulePlanning.Status = 0`) as future tasks — separate fix.

## Design

### Conventions this relies on (verified in code)

- An occurrence's date is `Compliance.Deadline.Date`: the calendar maps a row to
  `TaskDate = compliance.Deadline.Date`, and every idempotence guard keys on
  (`PlanningId`, `Deadline.Date`). A scheduler-written row (StartDate = previous occurrence,
  Deadline = this occurrence) therefore already covers its Deadline day; the first missing
  occurrence is the next scheduled date after the latest Deadline.
- `CalendarPastSeriesBackfillService.PlanPastSeriesBackfillAsync(arp)` (#1122) is a read-only
  planner: every occurrence in [series anchor, today), the effective sites (explicit PlanningSites
  ∪ live worker-tag members) and `ComplianceEnabled`.
- `EventDeployService.EnsureComplianceForOccurrenceAsync(arp, deadline, sdkSiteId)` materialises
  one occurrence for one site (PlanningCase, PlanningCaseSite, SDK case, Compliance row),
  tolerates a past deadline and is idempotent per (planning, deadline, site).

### 1. `PastOccurrenceCatchUpService`

New service `Services/PastOccurrenceCatchUp/` with
`Task<PastOccurrenceCatchUpResult> CatchUpAsync(IReadOnlyCollection<int>? propertyIds, CancellationToken ct)`
(`null` or empty = every property):

1. Return immediately unless the SDK setting `skipCloudDeploy` is `"true"`
   (`core.GetSdkSetting(Settings.skipCloudDeploy)`), the same check the scheduler makes; otherwise
   the scheduler owns past occurrences.
2. Eligible series: `AreaRulePlanning` not removed, `Status = true`, `ComplianceEnabled = true`,
   `ItemPlanningId > 0`, whose items-planning `Planning` is not removed, `Enabled`, and
   backend-configuration-owned (`IsLocked && !IsEditable`).
3. Per series: take the planner's past occurrences, keep those on or after
   **2026-09-17** (`SkipCloudDeployGapStart`, the date #1520 started skipping) and before today, and
   for each (occurrence, effective site) call `EnsureComplianceForOccurrenceAsync`. Nothing writes
   scheduler state (`NextExecutionTime`/`LastExecutedTime`) — unlike `BackfillPastSeriesAsync`.
4. Each pair is try/caught; a failing pair is counted and logged, never aborts the run.
5. Throttle: a property caught up in the last 10 minutes is skipped (in-process, per property),
   so list pages do not pay the cost on every request. The nightly run bypasses the throttle.

Occurrences before 2026-09-17 are out of scope: the scan found older gaps (mostly daily plannings
on a few tenants, and plannings with an unknown repeat type) that predate #1520 and are a separate
problem.

### 2. Called before every list read

| Read path | Call |
|---|---|
| `Events.ListTaskTracker` (app overdue list) | `CatchUpAsync([propertyId])` before `GetTaskTrackerList` |
| Web task tracker `Index` and `GenerateExcelReport` | `CatchUpAsync(filters.PropertyIds)` |
| Compliance report `Index` and `Overview` | `CatchUpAsync(PropertyId is null ? null : [PropertyId])` |
| Compliances `Index`, `ComplianceStatus` | `CatchUpAsync([propertyId])`; `Stats` → `CatchUpAsync(null)` |

A failure in the catch-up is logged and never fails the read.

### 3. Nightly catch-up

`PastOccurrenceCatchUpHostedService : BackgroundService`, registered with `AddHostedService` in the
plugin's `ConfigureServices`: first run 5 minutes after start, then every 24 hours; each run creates a
DI scope and calls `CatchUpAsync(null)` without the throttle, logging per property how many
occurrences were created, already present and failed. Each tenant runs its own plugin process, so
deploying the plugin catches up every affected tenant.

## Testing

Integration tests (`BackendConfiguration.Pn.Integration.Test`, CI only):

- A weekly planning whose last Compliance row is three weeks back: the pass creates rows for the
  missing past weeks (not for today+), each with an SDK case and `Deadline < now`.
- A second pass deploys nothing (idempotent); an occurrence with a removed (done) row is not
  redeployed.
- `ListTaskTracker` returns the materialised rows with `TaskIsExpired = true`, and completing one
  through `CompleteEvent` marks it done.
- The compliance report counts them as overdue (compliance below 100 %).
- Ineligible plannings are skipped: `Status = 0`, disabled, not backend-configuration-owned,
  `skipCloudDeploy` off, site not assigned.
- The lower bound: occurrences before 2026-09-17 are not deployed.
- The throttle: a second call within 10 minutes does nothing; the nightly run bypasses it.
- Existing `EnsureDeployedAsync` and past-series backfill tests still pass unchanged.

## Rollout

1. Read-only scan per tenant (done 2026-10-13): 1,182 occurrences missing since 2026-09-17 on 378
   plannings across 20 tenants.
2. PR → CI → merge → deploy. The nightly pass catches up each tenant; the list paths keep it current.
3. Re-run the scan: the gap since 2026-09-17 must be 0 on every tenant.
