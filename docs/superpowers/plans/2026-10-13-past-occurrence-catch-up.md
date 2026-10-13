# Past-Occurrence Catch-Up Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On `skipCloudDeploy` tenants, materialise every missed backend-configuration occurrence since 2026-09-17 so it shows as overdue in the app, the web task tracker and the compliance report, and can be completed from flutter-eform.

**Architecture:** A new `PastOccurrenceCatchUpService` reuses the #1122 read-only planner (`ICalendarPastSeriesBackfillService.PlanPastSeriesBackfillAsync`) to enumerate past occurrences and effective sites per series, and the existing `IEventDeployService.EnsureComplianceForOccurrenceAsync` to create each (occurrence, site) idempotently. It is called before every list read (gRPC `ListTaskTracker`, web task tracker, compliance report, compliances list) with a 10-minute per-property throttle, and nightly by a `BackgroundService`.

**Tech Stack:** C# / .NET, EF Core (MySQL), NUnit + NSubstitute, Testcontainers-based integration tests (`BackendConfiguration.Pn.Integration.Test`, run in CI only).

**Spec:** `docs/superpowers/specs/2026-10-13-past-occurrence-materialiser-design.md`

All paths below are relative to `eFormAPI/Plugins/BackendConfiguration.Pn/` unless they start with `docs/`.

## Global Constraints

- Lower bound: only occurrences with date **>= 2026-09-17** (UTC date) and **< today** (UTC date) are materialised.
- Only when the SDK setting `skipCloudDeploy` equals `"true"` (read with `core.GetSdkSetting(Settings.skipCloudDeploy)`); any other value or a read error → do nothing.
- Eligible series: `AreaRulePlanning.WorkflowState != Removed`, `Status == true`, `ComplianceEnabled == true`, `ItemPlanningId > 0`; its `Planning` (items-planning DB) `WorkflowState != Removed`, `Enabled == true`, `IsLocked == true`, `IsEditable == false`.
- Never write `Planning.NextExecutionTime` / `LastExecutedTime` (do NOT call `BackfillPastSeriesAsync`).
- Every (occurrence, site) call is individually try/caught; a failure is counted + logged, never aborts.
- A catch-up failure must never fail a list read: callers wrap it, log, and continue.
- Throttle: per property, 10 minutes, in-process; the nightly run passes `bypassThrottle: true`.
- No real customer names/emails in code, tests or commits. Tests run in CI only (`dotnet build` locally).
- Never `git add .`; `git add` and `git commit` in separate Bash calls (a hook enforces it).

## Review Focus

- A series whose planner returns occurrences both before and after 2026-09-17 → only the ones on/after it are deployed (Task 1 test `CatchUp_OnlyDeploysOccurrencesFromGapStartToYesterday`).
- An occurrence for today or later → never deployed by the catch-up (EnsureDeployedAsync owns it) (same test).
- Deploy call throws for one site → the other sites/occurrences still run and the failure is counted (Task 1 test `CatchUp_OneFailingPair_DoesNotStopTheRest`).
- `skipCloudDeploy` not `"true"` → no planner or deploy call at all (Task 1 test `CatchUp_SkipCloudDeployOff_DoesNothing`).
- Catch-up throws inside `ListTaskTracker` → the overdue list is still returned (Task 2 test `ListTaskTracker_CatchUpThrows_StillReturnsList`).

---

### Task 1: `PastOccurrenceCatchUpService`

**Model:** sonnet (new service with eligibility queries, throttling and tests against existing patterns).

**Files:**
- Create: `BackendConfiguration.Pn/Services/PastOccurrenceCatchUp/IPastOccurrenceCatchUpService.cs`
- Create: `BackendConfiguration.Pn/Services/PastOccurrenceCatchUp/PastOccurrenceCatchUpService.cs`
- Modify: `BackendConfiguration.Pn/EformBackendConfigurationPlugin.cs` (register in `ConfigureServices`, next to the `ICalendarPastSeriesBackfillService` registration ~line 174)
- Test: `BackendConfiguration.Pn.Integration.Test/PastOccurrenceCatchUpServiceTests.cs`

**Interfaces:**
- Consumes: `ICalendarPastSeriesBackfillService.PlanPastSeriesBackfillAsync(AreaRulePlanning arp, DateTime? prospectiveStartDate = null, CancellationToken ct = default)` → `PastSeriesBackfillPlan(DateTime Anchor, bool AnchorIsInThePast, IReadOnlyList<DateTime> PastOccurrences, IReadOnlyList<int> SiteIds, DateTime? FirstFutureOccurrence, bool ComplianceEnabled, int AlreadyCovered = 0)`; `IEventDeployService.EnsureComplianceForOccurrenceAsync(AreaRulePlanning, DateTime deadline, int sdkSiteId, CancellationToken)` → `Task<EnsureComplianceResult?>` (`Created` bool, `ComplianceId`, `SdkCaseId`; `null` = could not materialise).
- Produces:
  ```csharp
  public interface IPastOccurrenceCatchUpService
  {
      Task<PastOccurrenceCatchUpResult> CatchUpAsync(
          IReadOnlyCollection<int>? propertyIds, bool bypassThrottle, CancellationToken ct);
  }
  public sealed record PastOccurrenceCatchUpResult(int Created, int AlreadyPresent, int Failed, int PropertiesProcessed);
  ```

- [ ] **Step 1: Write the failing tests** in `PastOccurrenceCatchUpServiceTests.cs` (class `PastOccurrenceCatchUpServiceTests : TestBaseSetup`, `[Parallelizable(ParallelScope.Fixtures)]`, `[TestFixture]`). Seed series the way `CalendarPastSeriesBackfillTests.SeedEvent` does (Area → Property → AreaRule → items-planning `Planning` → `AreaRulePlanning`), but set `planning.IsLocked = true`, `planning.IsEditable = false`. Substitute `ICalendarPastSeriesBackfillService` (return a fixed plan) and `IEventDeployService` (record calls; default returns `new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 }`). Use a real `GetCore()` core behind an `IEFormCoreService` substitute and set the setting with `await core.SetSdkSetting(Settings.skipCloudDeploy, "true")` (or `"false"`). Tests:

  ```csharp
  [Test] public async Task CatchUp_OnlyDeploysOccurrencesFromGapStartToYesterday()
  // plan.PastOccurrences = [2026-09-10, 2026-09-17, 2026-09-24, yesterday, today], SiteIds = [7]
  // expect EnsureComplianceForOccurrenceAsync called exactly for 2026-09-17, 2026-09-24, yesterday (site 7);
  // never for 2026-09-10 or today.

  [Test] public async Task CatchUp_DeploysEverySiteOfEveryOccurrence()
  // SiteIds = [7, 8], two eligible occurrences → 4 calls, result.Created == 4.

  [Test] public async Task CatchUp_SkipCloudDeployOff_DoesNothing()
  // setting "false" → planner and deploy never called; result == (0,0,0,0).

  [TestCase("status")] [TestCase("compliance")] [TestCase("removedArp")] [TestCase("disabledPlanning")]
  [TestCase("editablePlanning")] [TestCase("removedPlanning")]
  public async Task CatchUp_IneligibleSeries_IsSkipped(string variant)
  // one ineligibility per case (arp.Status=false; arp.ComplianceEnabled=false; arp removed;
  // planning.Enabled=false; planning.IsEditable=true; planning removed) → planner never called for it.

  [Test] public async Task CatchUp_OneFailingPair_DoesNotStopTheRest()
  // deploy throws for site 7 only → site 8 still called; result.Failed == n7, result.Created == n8.

  [Test] public async Task CatchUp_NullResult_CountsAsFailed()
  // deploy returns null → counted in Failed, not Created.

  [Test] public async Task CatchUp_ExistingRow_CountsAsAlreadyPresent()
  // deploy returns Created = false → AlreadyPresent incremented.

  [Test] public async Task CatchUp_SecondCallWithin10Minutes_IsThrottled_UnlessBypassed()
  // call twice for the same property → deploy calls only from the first; a third call with
  // bypassThrottle: true runs again.

  [Test] public async Task CatchUp_PropertyFilter_OnlyTouchesThoseProperties()
  // two properties; CatchUpAsync([propA]) → planner only called for propA's series;
  // CatchUpAsync(null) → both.
  ```
  Because the throttle is static per process and tests share one, give each test fresh property ids (seeded per test) — the throttle keys on property id.

- [ ] **Step 2: Build** — `dotnet build BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj` → fails (types missing).

- [ ] **Step 3: Implement.** `IPastOccurrenceCatchUpService.cs` holds the interface and the result record exactly as in *Produces*. `PastOccurrenceCatchUpService.cs`:

  ```csharp
  namespace BackendConfiguration.Pn.Services.PastOccurrenceCatchUp;

  public class PastOccurrenceCatchUpService(
      BackendConfigurationPnDbContext backendConfigurationPnDbContext,
      ItemsPlanningPnDbContext itemsPlanningPnDbContext,
      IEFormCoreService coreHelper,
      ICalendarPastSeriesBackfillService pastSeriesPlanner,
      IEventDeployService eventDeployService,
      ILogger<PastOccurrenceCatchUpService> logger) : IPastOccurrenceCatchUpService
  {
      /// The day items-planning #1520 started leaving backend-configuration plannings to us
      /// when skipCloudDeploy is on; earlier gaps are a separate problem.
      public static readonly DateTime SkipCloudDeployGapStart = new(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc);
      private static readonly TimeSpan Throttle = TimeSpan.FromMinutes(10);
      private static readonly ConcurrentDictionary<int, DateTime> LastRunUtc = new();

      public async Task<PastOccurrenceCatchUpResult> CatchUpAsync(
          IReadOnlyCollection<int>? propertyIds, bool bypassThrottle, CancellationToken ct)
      {
          if (!await IsSkipCloudDeployOnAsync().ConfigureAwait(false))
              return new PastOccurrenceCatchUpResult(0, 0, 0, 0);

          var now = DateTime.UtcNow;
          var today = now.Date;
          var arpQuery = backendConfigurationPnDbContext.AreaRulePlannings
              .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                          && x.Status && x.ComplianceEnabled && x.ItemPlanningId > 0);
          if (propertyIds is { Count: > 0 })
              arpQuery = arpQuery.Where(x => propertyIds.Contains(x.PropertyId));
          var arps = await arpQuery.ToListAsync(ct).ConfigureAwait(false);

          var planningIds = arps.Select(x => x.ItemPlanningId).Distinct().ToList();
          var ownedPlanningIds = (await itemsPlanningPnDbContext.Plannings.AsNoTracking()
                  .Where(p => planningIds.Contains(p.Id)
                              && p.WorkflowState != Constants.WorkflowStates.Removed
                              && p.Enabled && p.IsLocked && !p.IsEditable)
                  .Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false))
              .ToHashSet();

          int created = 0, present = 0, failed = 0, properties = 0;
          foreach (var group in arps.Where(a => ownedPlanningIds.Contains(a.ItemPlanningId))
                       .GroupBy(a => a.PropertyId))
          {
              if (!bypassThrottle && LastRunUtc.TryGetValue(group.Key, out var last) && now - last < Throttle)
                  continue;
              LastRunUtc[group.Key] = now;
              properties++;

              foreach (var arp in group)
              {
                  ct.ThrowIfCancellationRequested();
                  PastSeriesBackfillPlan plan;
                  try
                  {
                      plan = await pastSeriesPlanner.PlanPastSeriesBackfillAsync(arp, null, ct).ConfigureAwait(false);
                  }
                  catch (Exception e)
                  {
                      failed++;
                      logger.LogError(e, "Past-occurrence catch-up could not plan AreaRulePlanning {ArpId}", arp.Id);
                      continue;
                  }
                  if (!plan.ComplianceEnabled) continue;

                  foreach (var occurrence in plan.PastOccurrences
                               .Where(d => d.Date >= SkipCloudDeployGapStart && d.Date < today))
                  foreach (var sdkSiteId in plan.SiteIds)
                  {
                      try
                      {
                          var ensured = await eventDeployService
                              .EnsureComplianceForOccurrenceAsync(arp, occurrence, sdkSiteId, ct)
                              .ConfigureAwait(false);
                          if (ensured == null) failed++;
                          else if (ensured.Created) created++;
                          else present++;
                      }
                      catch (OperationCanceledException) { throw; }
                      catch (Exception e)
                      {
                          failed++;
                          logger.LogError(e,
                              "Past-occurrence catch-up failed for AreaRulePlanning {ArpId}, occurrence {Occurrence}, site {SiteId}",
                              arp.Id, occurrence.ToString("yyyy-MM-dd"), sdkSiteId);
                      }
                  }
              }
          }

          if (created + failed > 0)
              logger.LogInformation(
                  "Past-occurrence catch-up: {Created} created, {Present} already present, {Failed} failed over {Properties} propert(y/ies)",
                  created, present, failed, properties);
          return new PastOccurrenceCatchUpResult(created, present, failed, properties);
      }

      private async Task<bool> IsSkipCloudDeployOnAsync()
      {
          try
          {
              var core = await coreHelper.GetCore().ConfigureAwait(false);
              return await core.GetSdkSetting(Settings.skipCloudDeploy).ConfigureAwait(false) == "true";
          }
          catch (Exception e)
          {
              logger.LogWarning(e, "Past-occurrence catch-up could not read skipCloudDeploy; assuming off");
              return false;
          }
      }
  }
  ```
  Usings: `System.Collections.Concurrent`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Logging`, `Microting.eForm.Dto` (for `Settings`; verify the enum's namespace by building), `Microting.eForm.Infrastructure.Constants`, `Microting.eFormApi.BasePn.Abstractions`, the two DbContexts, `BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill`, `BackendConfiguration.Pn.Services.EventDeployService`. Match the file header/licence block and `#nullable` usage of `CalendarPastSeriesBackfillService.cs`.

  Register in `ConfigureServices`:
  ```csharp
  services.AddTransient<Services.PastOccurrenceCatchUp.IPastOccurrenceCatchUpService,
      Services.PastOccurrenceCatchUp.PastOccurrenceCatchUpService>();
  ```

  Add a test-only way to clear the throttle so fixtures are independent:
  ```csharp
  internal static void ResetThrottleForTests() => LastRunUtc.Clear();
  ```
  (check the plugin project already has `InternalsVisibleTo` for the integration test project; if not, make it `public` with an XML comment saying it is for tests).

- [ ] **Step 4: Build** — `dotnet build BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj` → succeeds. (Tests run in CI.)

- [ ] **Step 5: Commit**
  ```bash
  git add eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/PastOccurrenceCatchUp/IPastOccurrenceCatchUpService.cs eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/PastOccurrenceCatchUp/PastOccurrenceCatchUpService.cs eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/EformBackendConfigurationPlugin.cs eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/PastOccurrenceCatchUpServiceTests.cs
  ```
  then (separate call) `git commit -m "feat(compliance): catch up missed past occurrences on skipCloudDeploy tenants"` with the session trailers.

---

### Task 2: Call the catch-up before every list read

**Model:** haiku (mechanical: inject one service and add a guarded call in seven methods; complete code below).

**Files:**
- Modify: `BackendConfiguration.Pn/Services/GrpcServices/EventsGrpcService.cs` (`ListTaskTracker`, ~line 347–363; constructor parameters)
- Modify: `BackendConfiguration.Pn/Services/BackendConfigurationTaskTrackerService/BackendConfigurationTaskTrackerService.cs` (`Index` ~line 60, `GenerateExcelReport` ~line 143; primary constructor)
- Modify: `BackendConfiguration.Pn/Services/BackendConfigurationComplianceReportService/BackendConfigurationComplianceReportService.cs` (`Index` ~line 91, `Overview` ~line 1163; constructor)
- Modify: `BackendConfiguration.Pn/Services/BackendConfigurationCompliancesService/BackendConfigurationCompliancesService.cs` (`Index` ~line 134, `ComplianceStatus` ~line 254, `Stats` ~line 1181; constructor)
- Modify: every test fixture that constructs these four classes directly (grep `new EventsGrpcService(`, `new BackendConfigurationTaskTrackerService(`, `new BackendConfigurationComplianceReportService(`, `new BackendConfigurationCompliancesService(` in `BackendConfiguration.Pn.Integration.Test` and `BackendConfiguration.Pn.Test`) — pass `Substitute.For<IPastOccurrenceCatchUpService>()`.
- Test: `BackendConfiguration.Pn.Integration.Test/EventsGrpcServiceTest.cs` (add two tests)

**Interfaces:**
- Consumes: `IPastOccurrenceCatchUpService.CatchUpAsync(IReadOnlyCollection<int>? propertyIds, bool bypassThrottle, CancellationToken ct)` from Task 1.

- [ ] **Step 1: Write the failing tests** in `EventsGrpcServiceTest.cs`, following how the existing `ListTaskTracker` tests there build the service:
  ```csharp
  [Test] public async Task ListTaskTracker_CallsCatchUpForTheProperty()
  // after ListTaskTracker(property_id = P): catchUp.Received(1).CatchUpAsync(
  //   Arg.Is<IReadOnlyCollection<int>>(ids => ids.Single() == P), false, Arg.Any<CancellationToken>())
  [Test] public async Task ListTaskTracker_CatchUpThrows_StillReturnsList()
  // catchUp.CatchUpAsync(...).ThrowsAsync(new InvalidOperationException("boom")) → response still returned with the seeded rows.
  ```

- [ ] **Step 2: Build** → fails (constructor/parameter missing).

- [ ] **Step 3: Implement.** Add `IPastOccurrenceCatchUpService pastOccurrenceCatchUpService` to each class's constructor (primary constructors: append the parameter; classic constructor in `BackendConfigurationCompliancesService`: add a `private readonly IPastOccurrenceCatchUpService _pastOccurrenceCatchUpService;` field and assign it). In each class add this private helper (adapt the field name and logger: use the class's existing `ILogger` if it has one, otherwise add `ILogger<ThatClass>`? — do NOT add a logger dependency; if the class has none, use `Console.WriteLine` the way neighbouring catch blocks in that file do):
  ```csharp
  private async Task CatchUpPastOccurrencesAsync(IReadOnlyCollection<int>? propertyIds)
  {
      try
      {
          await pastOccurrenceCatchUpService.CatchUpAsync(propertyIds, bypassThrottle: false, CancellationToken.None)
              .ConfigureAwait(false);
      }
      catch (Exception e)
      {
          // Never fail a list read because the catch-up failed; the list shows what exists.
          logger.LogError(e, "Past-occurrence catch-up failed before a list read");
      }
  }
  ```
  Call it as the first statement inside the existing `try` (or first statement of the method when there is no `try`):
  - `EventsGrpcService.ListTaskTracker`: `await CatchUpPastOccurrencesAsync([propertyId]);` right before the `GetTaskTrackerList` call (after `propertyId`/`sdkSiteId` are resolved).
  - `BackendConfigurationTaskTrackerService.Index` and `GenerateExcelReport`: `await CatchUpPastOccurrencesAsync(filtersModel.PropertyIds);`
  - `BackendConfigurationComplianceReportService.Index` and `Overview`: `await CatchUpPastOccurrencesAsync(requestModel.PropertyId is { } p ? [p] : null);`
  - `BackendConfigurationCompliancesService.Index`: `await CatchUpPastOccurrencesAsync([request.PropertyId]);` ; `ComplianceStatus(int propertyId)`: `await CatchUpPastOccurrencesAsync([propertyId]);` ; `Stats()`: `await CatchUpPastOccurrencesAsync(null);`

- [ ] **Step 4: Build both test projects** → succeed.

- [ ] **Step 5: Commit** the modified files by name, message `feat(compliance): catch up missed occurrences before task and compliance lists`.

---

### Task 3: Nightly catch-up hosted service

**Model:** haiku (one small class + registration + one test; complete code below).

**Files:**
- Create: `BackendConfiguration.Pn/Services/PastOccurrenceCatchUp/PastOccurrenceCatchUpHostedService.cs`
- Modify: `BackendConfiguration.Pn/EformBackendConfigurationPlugin.cs` (`ConfigureServices`, right after the Task 1 registration)
- Test: `BackendConfiguration.Pn.Integration.Test/PastOccurrenceCatchUpHostedServiceTests.cs`

**Interfaces:**
- Consumes: `IPastOccurrenceCatchUpService` (Task 1).
- Produces: `public Task RunOnceAsync(CancellationToken ct)` on the hosted service (tested directly).

- [ ] **Step 1: Write the failing test**
  ```csharp
  [Test] public async Task RunOnce_CatchesUpAllPropertiesBypassingTheThrottle()
  {
      var catchUp = Substitute.For<IPastOccurrenceCatchUpService>();
      catchUp.CatchUpAsync(null, true, Arg.Any<CancellationToken>()).Returns(new PastOccurrenceCatchUpResult(3, 1, 0, 2));
      var services = new ServiceCollection();
      services.AddSingleton(catchUp);
      var sp = services.BuildServiceProvider();
      var hosted = new PastOccurrenceCatchUpHostedService(sp.GetRequiredService<IServiceScopeFactory>(),
          NullLogger<PastOccurrenceCatchUpHostedService>.Instance);
      await hosted.RunOnceAsync(CancellationToken.None);
      await catchUp.Received(1).CatchUpAsync(null, true, Arg.Any<CancellationToken>());
  }
  [Test] public async Task RunOnce_CatchUpThrows_DoesNotThrow()
  // CatchUpAsync throws → RunOnceAsync completes without exception.
  ```

- [ ] **Step 2: Build** → fails.

- [ ] **Step 3: Implement**
  ```csharp
  namespace BackendConfiguration.Pn.Services.PastOccurrenceCatchUp;

  /// Nightly safety net: catches up every property, so a tenant is fixed even when nobody opens a list.
  public class PastOccurrenceCatchUpHostedService(
      IServiceScopeFactory scopeFactory,
      ILogger<PastOccurrenceCatchUpHostedService> logger) : BackgroundService
  {
      private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(5);
      private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
          try { await Task.Delay(FirstRunDelay, stoppingToken).ConfigureAwait(false); }
          catch (OperationCanceledException) { return; }

          using var timer = new PeriodicTimer(Interval);
          do
          {
              await RunOnceAsync(stoppingToken).ConfigureAwait(false);
          } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
      }

      public async Task RunOnceAsync(CancellationToken ct)
      {
          try
          {
              using var scope = scopeFactory.CreateScope();
              var catchUp = scope.ServiceProvider.GetRequiredService<IPastOccurrenceCatchUpService>();
              var result = await catchUp.CatchUpAsync(null, bypassThrottle: true, ct).ConfigureAwait(false);
              logger.LogInformation(
                  "Nightly past-occurrence catch-up: {Created} created, {Present} already present, {Failed} failed over {Properties} propert(y/ies)",
                  result.Created, result.AlreadyPresent, result.Failed, result.PropertiesProcessed);
          }
          catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
          catch (Exception e)
          {
              logger.LogError(e, "Nightly past-occurrence catch-up failed");
          }
      }
  }
  ```
  Register in `ConfigureServices`:
  ```csharp
  services.AddHostedService<Services.PastOccurrenceCatchUp.PastOccurrenceCatchUpHostedService>();
  ```
  Verify `Microsoft.Extensions.Hosting` is referenced by the plugin project (it is an ASP.NET plugin; `BackgroundService` lives in `Microsoft.Extensions.Hosting.Abstractions`); if the build fails, add the package reference matching the version family already used.

- [ ] **Step 4: Build both test projects** → succeed.

- [ ] **Step 5: Commit** by name, message `feat(compliance): nightly past-occurrence catch-up`.

---

### Task 4: End-to-end integration test with the real planner and deploy

**Model:** sonnet (wires the real planner + real `EventDeployService` + SDK core in a test; judgment on fixture setup).

**Files:**
- Test: `BackendConfiguration.Pn.Integration.Test/PastOccurrenceCatchUpEndToEndTests.cs`

**Interfaces:**
- Consumes: `PastOccurrenceCatchUpService` (Task 1), real `CalendarPastSeriesBackfillService` (built as in `CalendarPastSeriesBackfillTests.BuildService` but with a real `EventDeployService` built as in `EventDeployServiceTest.MakeService`), seeding as in `EventDeployServiceTest.SeedDeployableOccurrence` (real eForm template, PropertyWorker/PlanningSites so the site qualifies).

- [ ] **Step 1: Write the tests**
  ```csharp
  [Test] public async Task CatchUp_WeeklySeriesWithGap_CreatesOverdueRowsThatTheTaskTrackerShows()
  // weekly bc-owned planning (IsLocked=true, IsEditable=false) starting 2026-09-07, one site;
  // seed a done (removed) Compliance row with Deadline 2026-09-14 (covers 14/9);
  // skipCloudDeploy "true"; run CatchUpAsync([property], bypassThrottle: true).
  // Expect: live Compliance rows with Deadline = every weekly date from 2026-09-21 up to the last one < today,
  // each MicrotingSdkCaseId > 0; none for 2026-09-14 or today+.
  // Then calendarService.GetTaskTrackerList(property, site) returns those rows with TaskIsExpired == true
  // (build the calendar service the way EventsGrpcServiceTest does, or assert directly on Compliances if
  // the calendar service is too heavy to build — say which in the commit message).
  [Test] public async Task CatchUp_RunTwice_CreatesNoDuplicates()
  // second run: no new Compliance / PlanningCaseSite rows; result.Created == 0.
  ```

- [ ] **Step 2: Build** → succeeds (production code exists).

- [ ] **Step 3: Commit** by name, message `test(compliance): end-to-end past-occurrence catch-up`.

---

## After all tasks (controller)

- Whole-branch dual review (code review + simplifier), PR toward `stable` with the spec summary and the scan numbers, CI to a verdict (the integration tests run only there), Copilot review considered.
- After merge and deploy: re-run the read-only scan; the gap since 2026-09-17 must be 0 on all 20 tenants. Then tell the customer of card 992.
