/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System.Globalization;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using NSubstitute;

/// <summary>
/// Integration tests for <see cref="EventDeployService"/> — the inline-deploy
/// pipeline that backs <c>EventsGrpcService.ListEvents</c>. Each test pins one
/// invariant from <c>EventDeployService.cs:42-50</c> so a future regression in
/// the eager-deploy contract surfaces in CI before reaching master.
///
/// All tests use a real <see cref="BackendConfigurationPnDbContext"/> +
/// <see cref="ItemsPlanningPnDbContext"/> from <see cref="TestBaseSetup"/> and
/// mock <see cref="IBackendConfigurationCalendarService"/> so we can pin the
/// rotation stream the deploy pipeline iterates over without standing up the
/// full calendar service. <see cref="IEFormCoreService"/> is also mocked
/// because the no-op fast paths (tests 1-3) return before
/// <c>coreHelper.GetCore()</c> is reached; tests that DO reach the SDK path
/// (4, 7) seed a real SDK site via <see cref="TestBaseSetup.GetCore"/>.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventDeployServiceTest : TestBaseSetup
{
    private const string PropertyId = "1";
    private const int SdkSiteId = 1;
    private static readonly IReadOnlyCollection<string> BoardIds = ["10"];

    /// <summary>
    /// Build the two mocks every test needs: <see cref="IBackendConfigurationCalendarService"/>
    /// pinned to return <paramref name="rotations"/>, plus
    /// <see cref="IEFormCoreService"/>. When <paramref name="core"/> is supplied
    /// the mock's <c>GetCore()</c> is wired; otherwise it's left unstubbed so the
    /// no-op fast-path tests can assert via <c>DidNotReceive().GetCore()</c>.
    /// </summary>
    private static (IBackendConfigurationCalendarService Calendar, IEFormCoreService CoreHelper)
        MakeMocks(IEnumerable<CalendarTaskResponseModel> rotations, eFormCore.Core? core = null)
    {
        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(
                true, rotations.ToList()));

        var coreHelper = Substitute.For<IEFormCoreService>();
        if (core != null)
        {
            coreHelper.GetCore().Returns(Task.FromResult(core));
        }
        return (calendar, coreHelper);
    }

    /// <summary>
    /// Build the SUT against the contexts inherited from <see cref="TestBaseSetup"/>.
    /// Pass a custom <paramref name="logger"/> when the test needs to inspect log
    /// output (idempotence-guard test); otherwise defaults to
    /// <see cref="NullLogger{T}.Instance"/>.
    /// </summary>
    private EventDeployService MakeService(
        IBackendConfigurationCalendarService calendar,
        IEFormCoreService coreHelper,
        ILogger<EventDeployService>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(calendar);
        var sp = services.BuildServiceProvider();
        return new EventDeployService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            sp,
            logger ?? NullLogger<EventDeployService>.Instance);
    }

    /// <summary>
    /// Factory for the rotation DTO that every test arranges. Defaults match the
    /// shape the existing tests used pre-refactor (planningId=200, eformId=300);
    /// callers override per case.
    /// </summary>
    private static CalendarTaskResponseModel MakeRotation(
        int id,
        DateTime date,
        bool isFromCompliance = false,
        int planningId = 200,
        int eformId = 300)
        => new()
        {
            Id = id,
            PlanningId = planningId,
            EformId = eformId,
            TaskDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IsFromCompliance = isFromCompliance
        };

    // ------------------------------------------------------------------
    // 1. EnsureDeployedAsync_NoRotationsInWindow_DoesNothing
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_NoRotationsInWindow_DoesNothing()
    {
        // Arrange — calendar returns zero rotations for the window.
        var (calendar, coreHelper) = MakeMocks([]);
        var service = MakeService(calendar, coreHelper);

        // Act
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-14", "2026-05-20", SdkSiteId, CancellationToken.None);

        // Assert — nothing was written, and the SDK Core was never even fetched.
        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == 200);
        Assert.That(complianceCount, Is.EqualTo(0));
        await coreHelper.DidNotReceive().GetCore();
    }

    // ------------------------------------------------------------------
    // 2. EnsureDeployedAsync_RotationInPast_SkippedNotDeployed
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_RotationInPast_SkippedNotDeployed()
    {
        // Arrange — yesterday's rotation. EventDeployService should refuse
        // to back-deploy historical rows (the scheduler owns those).
        var pastRotation = MakeRotation(id: 100, date: DateTime.UtcNow.Date.AddDays(-1));
        var (calendar, coreHelper) = MakeMocks([pastRotation]);
        var service = MakeService(calendar, coreHelper);

        // Act
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-01", "2026-05-31", SdkSiteId, CancellationToken.None);

        // Assert — past rotation skipped before reaching the SDK path.
        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == 200);
        Assert.That(complianceCount, Is.EqualTo(0));
        await coreHelper.DidNotReceive().GetCore();
    }

    // ------------------------------------------------------------------
    // 3. EnsureDeployedAsync_FullyDeployedComplianceRotation_SkippedNotDeployed
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_FullyDeployedComplianceRotation_SkippedNotDeployed()
    {
        // Arrange — a future rotation that is already fully backed by a
        // Compliance row AND an SDK Case (IsFromCompliance=true, SdkCaseId>0).
        //
        // NEW CONTRACT (PR #829): the candidate-enumeration filter at
        // EventDeployService.cs:119-120 drops Compliance-backed rotations
        // ONLY when SdkCaseId > 0 — i.e. fully deployed. Compliance-backed
        // rotations with SdkCaseId == 0 are the "stuck" shape and MUST fall
        // through to the revive path (covered by test #9). This test pins
        // the fully-deployed branch of the filter.
        var fullyDeployedRotation = MakeRotation(
            id: 101,
            date: DateTime.UtcNow.Date.AddDays(2),
            isFromCompliance: true,
            planningId: 201,
            eformId: 301);
        fullyDeployedRotation.SdkCaseId = 999_999;
        var (calendar, coreHelper) = MakeMocks([fullyDeployedRotation]);
        var service = MakeService(calendar, coreHelper);

        // Act
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-14", "2026-05-20", SdkSiteId, CancellationToken.None);

        // Assert — no Compliance row created, SDK Core never fetched
        // (filter dropped the rotation before the SDK path).
        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == 201);
        Assert.That(complianceCount, Is.EqualTo(0));
        await coreHelper.DidNotReceive().GetCore();
    }

    // ------------------------------------------------------------------
    // 4. EnsureDeployedAsync_DeployedComplianceExists_SkipsRotation
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_DeployedComplianceExists_SkipsRotation()
    {
        // Arrange — a fully-deployed Compliance row already exists for
        // (planningId, rotationDate) with MicrotingSdkCaseId > 0. The
        // pipeline's idempotence guard (EventDeployService.cs:194-206) must
        // short-circuit before any PlanningCase / CaseCreate is attempted.
        //
        // NEW CONTRACT (PR #829): the guard short-circuits only when the
        // existing slot is either soft-Removed OR fully deployed
        // (MicrotingSdkCaseId > 0). A Created row with MicrotingSdkCaseId == 0
        // is the genuine "stuck" shape (see test #9) and must fall through
        // to the revive path, so this test pins the fully-deployed branch.

        // Boot a real Core so the SDK schema is materialised against the
        // testcontainer; we'll seed a Site/Language directly.
        var core = await GetCore();

        // GetCore() seeds the SDK's default languages (Language.AddDefaultLanguages);
        // grab any one of them rather than inserting a duplicate.
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var site = new Site
        {
            Name = "test-site",
            MicrotingUid = 42,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        const int planningId = 500;
        const int existingSdkCaseId = 12_345;
        var rotationDate = DateTime.UtcNow.Date.AddDays(3);
        var existingCompliance = new Compliance
        {
            PlanningId = planningId,
            PropertyId = 1,
            AreaId = 1,
            Deadline = rotationDate,
            StartDate = rotationDate.AddDays(-7),
            MicrotingSdkCaseId = existingSdkCaseId,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(existingCompliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var rotation = MakeRotation(
            id: 102,
            date: rotationDate,
            planningId: planningId,
            eformId: 400);

        // Use a capturing TestLogger so we can prove the idempotence guard
        // (EventDeployService.cs:194-206) short-circuited. Asserting only
        // Count==1 is tautological: if the guard silently broke,
        // execution would fall through to the planning lookup at line 211,
        // find nothing for planningId=500, log a "planning ... not found"
        // warning at line 219, hit `continue` at line 222, and STILL leave
        // Compliance count at 1. By asserting that NEITHER the
        // planning-not-found NOR areaRulePlanning-not-found warnings were
        // emitted, we pin that the guard fired before either downstream
        // null-fallthrough.
        var logger = new TestLogger<EventDeployService>();
        var (calendar, coreHelper) = MakeMocks([rotation], core);
        var service = MakeService(calendar, coreHelper, logger);

        // Act
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-14", "2026-05-20", site.Id, CancellationToken.None);

        // Assert — guard fired (no downstream null-fallthrough warnings)
        // AND no duplicate Compliance row was created.
        Assert.Multiple(() =>
        {
            Assert.That(
                logger.Entries.Any(e => e.Message.Contains("planning") && e.Message.Contains("not found")),
                Is.False,
                "Guard should have short-circuited before the planning lookup at EventDeployService.cs:211-223.");
            Assert.That(
                logger.Entries.Any(e => e.Message.Contains("areaRulePlanning") && e.Message.Contains("not found")),
                Is.False,
                "Guard should have short-circuited before the areaRulePlanning lookup at EventDeployService.cs:225-238.");
        });

        var complianceCount = await BackendConfigurationPnDbContext.Compliances
            .CountAsync(c => c.PlanningId == planningId);
        Assert.That(complianceCount, Is.EqualTo(1));
    }

    // ------------------------------------------------------------------
    // 5. EnsureDeployedAsync_PlanningStateRemainsUntouched
    // SKIPPED — to drive the pipeline PAST the idempotence guard we'd need
    // to seed the full Planning + PlanningNameTranslation + AreaRulePlanning
    // + Area + AreaRule + Property graph AND make the real Core's CaseCreate
    // succeed against a real eForm template (or stub the Core, which is not
    // an interface). The Planning-state invariant is enforced structurally:
    // EventDeployService.cs:168-358 reads `planning.LastExecutedTime` only as
    // a fallback for `Compliance.StartDate` and otherwise never writes to
    // LastExecutedTime / DoneInPeriod / NextExecutionTime / PushMessageSent.
    // The dual-subagent code review on PR #813 already pinned this; covering
    // it as an integration test would cost more setup than the regression
    // surface justifies. Re-evaluate if the deploy path grows a Planning
    // write.
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // 6. EnsureDeployedAsync_RotationFailureDoesNotAbortOthers
    // SKIPPED — same blocker as test 5. To make CaseCreate throw on the
    // first rotation and succeed on the second we'd need to stub
    // <c>eFormCore.Core</c>, which is a concrete class (no interface). The
    // per-rotation try/catch on EventDeployService.cs:351-357 is structural
    // and was pinned by the PR #813 review. Re-evaluate if we extract an
    // ICoreFacade or similar that lets us inject failures at the SDK seam.
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // 7. EnsureDeployedAsync_CancellationRequested_HonoursToken
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_CancellationRequested_HonoursToken()
    {
        // Arrange — pre-cancelled token + a future-day rotation, so the
        // pipeline gets past the candidate filter and into the SDK Sites
        // EF query, which honours the token.
        var core = await GetCore();

        var rotation = MakeRotation(
            id: 103,
            date: DateTime.UtcNow.Date.AddDays(2),
            planningId: 705,
            eformId: 800);
        var (calendar, coreHelper) = MakeMocks([rotation], core);
        var service = MakeService(calendar, coreHelper);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act + Assert — the future-day rotation passes the candidate
        // filter, so the pipeline reaches the SDK Sites EF query at
        // EventDeployService.cs:145 which is passed the cancelled token
        // and is GUARANTEED to throw OperationCanceledException (or its
        // TaskCanceledException subclass). A previous swallowing try/catch
        // would have masked a regression that accidentally drops the token
        // (e.g. passes CancellationToken.None to the EF call).
        Assert.ThrowsAsync<OperationCanceledException>(
            () => service.EnsureDeployedAsync(
                PropertyId, BoardIds, "2026-05-14", "2026-05-20", SdkSiteId, cts.Token));

        var afterCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == 705);
        Assert.That(afterCount, Is.EqualTo(0),
            "Cancellation must not produce any Compliance writes.");
    }

    // ------------------------------------------------------------------
    // 7b. EnsureDeployedAsync_TodayRotation_AdmittedByCandidateFilter
    //
    // Pins that the candidate filter at EventDeployService.cs:131
    // (`rotationDate >= todayUtc`) admits today's rotation. A refactor that
    // narrows this to `> todayUtc` would silently exclude today's rotations
    // from eager deploy.
    //
    // This is NOT a regression test for the EndDate end-of-day fix from this
    // commit. The assertion below fires at step 2 (planning lookup) of the
    // deploy pipeline; the EndDate guard lives at step 7. Pre-fix and
    // post-fix code emit the same planning-not-found warning for this
    // fixture. Direct regression coverage of the EndDate guard requires a
    // full Planning + AreaRulePlanning + Area + Property + eForm template
    // graph (already deferred in the SKIPPED comments for tests #5/#6/#8
    // below).
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_TodayRotation_AdmittedByCandidateFilter()
    {
        // Arrange — a today rotation. We seed a Planning + PlanningSite
        // pair so the site-aware narrowing filter at
        // EventDeployService.cs:150-165 (defect A from commit 5c543341)
        // admits the candidate. We deliberately stop short of seeding the
        // AreaRulePlanning so the pipeline emits the
        // "areaRulePlanning for planning ... not found" warning. That
        // warning contains both "planning" and "not found", which is the
        // observable canary the assertion below pins.
        var core = await GetCore();

        // GetCore() seeds the SDK's default languages; reuse one.
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var site = new Site
        {
            Name = "test-site-today",
            MicrotingUid = 43,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;

        // Seed a Planning (auto-id) so the PlanningSite FK is satisfied,
        // then seed the PlanningSite that the site-aware narrowing filter
        // at EventDeployService.cs:150-165 requires. The rotation's
        // PlanningId is wired to the seeded Planning.Id so the candidate
        // survives the narrowing.
        var planning = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning
        {
            WorkflowState = Constants.WorkflowStates.Created,
            StartDate = today,
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month,
            NextExecutionTime = today.AddMonths(1),
            DayOfMonth = today.Day,
            RepeatUntil = today.AddMonths(6),
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.PlanningSites.Add(new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
        {
            PlanningId = planning.Id,
            SiteId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var rotation = MakeRotation(
            id: 104,
            date: today,
            planningId: planning.Id,
            eformId: 901);

        var logger = new TestLogger<EventDeployService>();
        var (calendar, coreHelper) = MakeMocks([rotation], core);
        var service = MakeService(calendar, coreHelper, logger);

        var todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Act — drive the deploy pipeline with a today-keyed range. If the
        // candidate filter at line ~131 admits today, control reaches the
        // planning lookup (step 2). If a future refactor narrows the filter
        // to `> todayUtc`, the rotation is dropped before step 2 fires.
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, todayKey, todayKey, site.Id, CancellationToken.None);

        // Assert — the today rotation reached the planning lookup at
        // EventDeployService.cs:200-212, proving the candidate filter
        // admitted it. The "planning ... not found" warning is the
        // observable canary. If the test fails, today rotations are being
        // silently filtered out before the deploy work begins.
        Assert.That(
            logger.Entries.Any(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("planning")
                && e.Message.Contains("not found")),
            Is.True,
            "Today rotation must reach the planning-lookup step at "
            + "EventDeployService.cs:200-212. If this fails, today's "
            + "rotations are being silently excluded from the deploy path.");

        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == planning.Id);
        Assert.That(complianceCount, Is.EqualTo(0),
            "Missing-planning path must not leave any Compliance rows behind.");
    }

    // ------------------------------------------------------------------
    // 9. EnsureDeployedAsync_StuckCompliance_FallsThroughToDeploy
    //
    // Pins the NEW contract introduced in PR #829: a Compliance row at
    // (PlanningId, Deadline) with WorkflowState=Created AND
    // MicrotingSdkCaseId == 0 is a STUCK row (earlier deploy persisted the
    // Compliance but failed CaseCreate — see the SDK 10.0.27 EndDate bug).
    // The idempotence guard at EventDeployService.cs:194-206 must NOT
    // short-circuit on this shape; the rotation must fall through to the
    // deploy / revive path so EnsureComplianceRowAsync can attach the
    // missing MicrotingSdkCaseId.
    //
    // Driving the pipeline all the way through CaseCreate requires a full
    // Planning + AreaRulePlanning + Area + Property + eForm template graph
    // (already deferred at tests #5/#6/#8). What we CAN pin here is the
    // observable canary: the stuck row reaches the planning lookup at
    // EventDeployService.cs:211-223 and emits the "planning ... not found"
    // warning — proof that the guard did NOT short-circuit. A regression
    // that re-tightens the guard to "any Compliance row exists → skip"
    // would suppress that warning and this test would fail.
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_StuckCompliance_FallsThroughToDeploy()
    {
        // Arrange — boot a real Core so the SDK schema is materialised
        // against the testcontainer; seed Site/Language directly.
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var site = new Site
        {
            Name = "test-site-stuck",
            MicrotingUid = 44,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var rotationDate = DateTime.UtcNow.Date.AddDays(4);

        // Seed a Planning (auto-id) so the PlanningSite FK is satisfied,
        // then seed the PlanningSite that the site-aware narrowing filter
        // at EventDeployService.cs:150-165 (defect A from commit 5c543341)
        // requires. Without the PlanningSite the rotation is dropped before
        // reaching the idempotence guard and the stuck-row fall-through
        // behaviour we're pinning never gets exercised.
        var planning = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning
        {
            WorkflowState = Constants.WorkflowStates.Created,
            StartDate = rotationDate.AddDays(-7),
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month,
            NextExecutionTime = rotationDate,
            DayOfMonth = rotationDate.Day,
            RepeatUntil = rotationDate.AddMonths(6),
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        var planningId = planning.Id;

        ItemsPlanningPnDbContext.PlanningSites.Add(new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
        {
            PlanningId = planningId,
            SiteId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // The stuck shape: Created + MicrotingSdkCaseId == 0. Default int
        // is 0 so the property is left at its default to make the intent
        // explicit (also asserted below before the act).
        var stuckCompliance = new Compliance
        {
            PlanningId = planningId,
            PropertyId = 1,
            AreaId = 1,
            Deadline = rotationDate,
            StartDate = rotationDate.AddDays(-7),
            MicrotingSdkCaseId = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(stuckCompliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var rotation = MakeRotation(
            id: 105,
            date: rotationDate,
            planningId: planningId,
            eformId: 601);

        var logger = new TestLogger<EventDeployService>();
        var (calendar, coreHelper) = MakeMocks([rotation], core);
        var service = MakeService(calendar, coreHelper, logger);

        // Act
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-14", "2026-05-20", site.Id, CancellationToken.None);

        // Assert — the stuck row fell through to the planning-lookup step.
        // The "planning ... not found" warning is the observable canary
        // that the guard did NOT short-circuit. A regression that
        // re-tightens the guard to "any Compliance row exists → skip"
        // would suppress this warning and this test would fail.
        Assert.That(
            logger.Entries.Any(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("planning")
                && e.Message.Contains("not found")),
            Is.True,
            "Stuck Compliance (Created + MicrotingSdkCaseId==0) must fall "
            + "through to the deploy/revive path at "
            + "EventDeployService.cs:211-223. If this fails, the guard "
            + "is silently treating stuck rows as fully deployed.");

        // The stuck row is still present (we did not reach the revive
        // step because no Planning was seeded; that's the unavoidable
        // limit of integration coverage without the full entity graph,
        // already documented at tests #5/#6/#8). No new Compliance row
        // was INSERTed because the pipeline `continue`d at line 222
        // before reaching EnsureComplianceRowAsync.
        var complianceCount = await BackendConfigurationPnDbContext.Compliances
            .CountAsync(c => c.PlanningId == planningId);
        Assert.That(complianceCount, Is.EqualTo(1),
            "Falling through to the missing-planning path must not "
            + "INSERT a duplicate Compliance row.");
    }

    // ------------------------------------------------------------------
    // 10. EnsureDeployedAsync_RemovedCompliance_SkipsRotation
    //
    // Pins the NEW contract introduced in PR #829: a Compliance row at
    // (PlanningId, Deadline) with WorkflowState=Removed represents a
    // user-completed event whose Compliance was retracted (the SDK Case
    // still exists). The idempotence guard at
    // EventDeployService.cs:194-206 MUST short-circuit on this shape so
    // we do not phantom-uncomplete that event by re-deploying. Mirrors
    // the structure of test #4 (fully-deployed branch).
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_RemovedCompliance_SkipsRotation()
    {
        // Arrange — boot a real Core so the SDK schema exists; seed
        // Site/Language directly.
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var site = new Site
        {
            Name = "test-site-removed",
            MicrotingUid = 45,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        const int planningId = 700;
        var rotationDate = DateTime.UtcNow.Date.AddDays(5);

        // The completed-then-retracted shape: WorkflowState=Removed.
        // MicrotingSdkCaseId is left as 0 here ON PURPOSE — if a future
        // refactor accidentally narrows the guard to only "Removed AND
        // MicrotingSdkCaseId > 0", this test would still pass with a
        // non-zero MicrotingSdkCaseId and silently mask the regression.
        // By keeping MicrotingSdkCaseId == 0 we pin that the Removed
        // branch alone short-circuits.
        var removedCompliance = new Compliance
        {
            PlanningId = planningId,
            PropertyId = 1,
            AreaId = 1,
            Deadline = rotationDate,
            StartDate = rotationDate.AddDays(-7),
            MicrotingSdkCaseId = 0,
            WorkflowState = Constants.WorkflowStates.Removed
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(removedCompliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var rotation = MakeRotation(
            id: 106,
            date: rotationDate,
            planningId: planningId,
            eformId: 701);

        var logger = new TestLogger<EventDeployService>();
        var (calendar, coreHelper) = MakeMocks([rotation], core);
        var service = MakeService(calendar, coreHelper, logger);

        // Act
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-14", "2026-05-20", site.Id, CancellationToken.None);

        // Assert — guard short-circuited on the Removed branch BEFORE
        // either of the downstream null-fallthrough warnings could fire.
        Assert.Multiple(() =>
        {
            Assert.That(
                logger.Entries.Any(e => e.Message.Contains("planning") && e.Message.Contains("not found")),
                Is.False,
                "Removed Compliance must short-circuit before the "
                + "planning lookup at EventDeployService.cs:211-223.");
            Assert.That(
                logger.Entries.Any(e => e.Message.Contains("areaRulePlanning") && e.Message.Contains("not found")),
                Is.False,
                "Removed Compliance must short-circuit before the "
                + "areaRulePlanning lookup at EventDeployService.cs:225-238.");
        });

        // No duplicate Compliance row was INSERTed and the existing
        // Removed row was not flipped back to Created (which would
        // phantom-uncomplete the underlying event).
        var complianceCount = await BackendConfigurationPnDbContext.Compliances
            .CountAsync(c => c.PlanningId == planningId);
        Assert.That(complianceCount, Is.EqualTo(1));

        var preservedRow = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking()
            .SingleAsync(c => c.PlanningId == planningId);
        Assert.That(preservedRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
            "Removed Compliance must NOT be revived — flipping it back to "
            + "Created would phantom-uncomplete a user-completed event.");
        Assert.That(preservedRow.MicrotingSdkCaseId, Is.EqualTo(0),
            "Removed Compliance must NOT have its MicrotingSdkCaseId mutated.");
    }

    // ------------------------------------------------------------------
    // 8. EnsureDeployedAsync_HappyPath_CreatesPlanningCaseSiteAndCompliance
    // SKIPPED (deferred). The end-to-end deploy needs:
    //   - real Planning + PlanningNameTranslation
    //   - real AreaRule + AreaRulePlanning + Area + Property
    //   - real eForm template in the SDK so ReadeForm / CaseCreate succeed
    //   - real SDK Site/Worker/Unit chain
    // That's ~10 entities + a parsed XML template. The closest precedent is
    // BackendConfigurationTaskTrackerServiceHelperTest which does set this
    // up; replicating it here is high-value but beyond the budget for this
    // first batch. Track as a follow-up: copy the TaskTracker bootstrap and
    // assert that one call to EnsureDeployedAsync creates exactly one
    // PlanningCase + PlanningCaseSite + Compliance with the documented
    // column-name quirk (PlanningCaseSiteId = PlanningCase.Id; see
    // EventDeployService.cs:392-395).
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // 11. EnsureDeployedAsync_PlanningAssignedToOtherSite_NotDeployedForCallingSite
    //
    // Regression for #932 (deploy-side cross-worker leak — the deploy
    // companion of #931's read-side leak). A planning assigned ONLY to site A
    // must never be deployed for a different site B that merely shares
    // property access. The site-aware candidate narrowing in
    // EnsureDeployedAsync (EventDeployService.cs:150-165, #935 defect A) is
    // what prevents this: the planning has no PlanningSite for the calling
    // site B, so the candidate is dropped before the SDK deploy path runs.
    //
    // Observable canary: with narrowing, the candidate list is empty after
    // the filter and EnsureDeployedAsync returns BEFORE coreHelper.GetCore()
    // (EventDeployService.cs:167-175). Remove the narrowing and the same
    // fixture reaches GetCore + the planning/ARP lookups, so
    // DidNotReceive().GetCore() precisely distinguishes "narrowing dropped the
    // leak" from "deploy ran". Test #7b is the positive control: the SAME
    // fixture shape with calling site == assigned site DOES reach the deploy
    // path, so this pair pins both directions of the narrowing.
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_PlanningAssignedToOtherSite_NotDeployedForCallingSite()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // The assigned worker's site (A) and the non-assigned caller's site (B).
        var assignedSite = new Site
        {
            Name = "niels-site",
            MicrotingUid = 50,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        var callingSite = new Site
        {
            Name = "soren-site",
            MicrotingUid = 51,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddRangeAsync(assignedSite, callingSite);
        await MicrotingDbContext.SaveChangesAsync();

        var rotationDate = DateTime.UtcNow.Date.AddDays(2);
        var planning = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning
        {
            WorkflowState = Constants.WorkflowStates.Created,
            StartDate = rotationDate.AddDays(-7),
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month,
            NextExecutionTime = rotationDate,
            DayOfMonth = rotationDate.Day,
            RepeatUntil = rotationDate.AddMonths(6),
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Planning is assigned ONLY to site A.
        ItemsPlanningPnDbContext.PlanningSites.Add(new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
        {
            PlanningId = planning.Id,
            SiteId = assignedSite.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var rotation = MakeRotation(id: 932, date: rotationDate, planningId: planning.Id, eformId: 9320);
        var (calendar, coreHelper) = MakeMocks([rotation], core);
        var service = MakeService(calendar, coreHelper);

        // Act — site B polls/deploys for a planning it is NOT assigned to.
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-14", "2026-05-20", callingSite.Id, CancellationToken.None);

        // Assert — narrowing dropped the candidate before the SDK path: GetCore
        // is never reached, and no deploy artefacts were written for site B.
        await coreHelper.DidNotReceive().GetCore();

        var planningCaseSiteCount = await ItemsPlanningPnDbContext.PlanningCaseSites
            .CountAsync(pcs => pcs.PlanningId == planning.Id);
        Assert.That(planningCaseSiteCount, Is.EqualTo(0),
            "A planning assigned only to another site must not get a PlanningCaseSite "
            + "for the calling site (#932).");

        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == planning.Id);
        Assert.That(complianceCount, Is.EqualTo(0));
    }

    // ------------------------------------------------------------------
    // 12. EnsureComplianceForOccurrence_SiteNotInPlanningSites_ThrowsAndDoesNotDeploy
    //
    // Pins the new defense-in-depth guard at the top of the single
    // PlanningCaseSite write site (DeployForRotationAsync). The on-demand
    // calendar materialisation path (EnsureComplianceForOccurrenceAsync ->
    // DeployForRotationAsync) does NOT site-narrow its caller the way
    // EnsureDeployedAsync does; its only protection is that
    // BackendConfigurationCalendarService passes an assigned site. If that
    // invariant ever regresses, the guard must throw rather than silently leak
    // a case to a non-assigned worker (#932/#1377).
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureComplianceForOccurrence_SiteNotInPlanningSites_ThrowsAndDoesNotDeploy()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // Two real, separately-inserted sites: the planning is assigned to A,
        // the caller is B. Capturing both generated Ids (rather than hardcoding
        // a magic id) keeps the test robust against the fixture's shared
        // testcontainer auto-increment carrying across tests.
        var assignedSite = new Site
        {
            Name = "niels-site-ondemand",
            MicrotingUid = 53,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        var callingSite = new Site
        {
            Name = "soren-site-ondemand",
            MicrotingUid = 52,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddRangeAsync(assignedSite, callingSite);
        await MicrotingDbContext.SaveChangesAsync();

        var deadline = DateTime.UtcNow.Date.AddDays(2);

        var planning = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning
        {
            WorkflowState = Constants.WorkflowStates.Created,
            StartDate = deadline.AddDays(-7),
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month,
            NextExecutionTime = deadline,
            DayOfMonth = deadline.Day,
            RepeatUntil = deadline.AddMonths(6),
            // > 0 so EnsureComplianceForOccurrenceAsync resolves an EformId and
            // reaches DeployForRotationAsync (where the guard lives) rather than
            // bailing early on a missing eform.
            RelatedEFormId = 9321,
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Planning is assigned ONLY to assignedSite — NOT the calling site.
        ItemsPlanningPnDbContext.PlanningSites.Add(new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
        {
            PlanningId = planning.Id,
            SiteId = assignedSite.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Intentionally an in-memory AreaRulePlanning, NOT persisted:
        // EnsureComplianceForOccurrenceAsync takes it as a parameter and only
        // reads .ItemPlanningId off it (the Planning itself is looked up from
        // the DB), so this is enough to drive the call through to
        // DeployForRotationAsync where the guard lives. If that method is ever
        // refactored to re-fetch the ARP from the DB, persist this entity.
        var areaRulePlanning = new AreaRulePlanning
        {
            ItemPlanningId = planning.Id,
            PropertyId = 1,
            AreaId = 1,
        };

        var (calendar, coreHelper) = MakeMocks([], core);
        var service = MakeService(calendar, coreHelper);

        // Act + Assert — the guard refuses to deploy to a non-assigned site.
        Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureComplianceForOccurrenceAsync(
                areaRulePlanning, deadline, callingSite.Id, CancellationToken.None));

        var planningCaseSiteCount = await ItemsPlanningPnDbContext.PlanningCaseSites
            .CountAsync(pcs => pcs.PlanningId == planning.Id);
        Assert.That(planningCaseSiteCount, Is.EqualTo(0),
            "No PlanningCaseSite may be written for a site outside the planning's "
            + "PlanningSites (#932).");
    }

    // ------------------------------------------------------------------
    // 12b. EnsureComplianceForOccurrence_SiteIsActivePropertyWorkerNotInPlanningSites_Deploys
    //
    // Positive counterpart to the pin test above, locking in the #932/#1377
    // guard RELAXATION. The on-demand calendar materialisation now lets a user
    // complete a future/on-demand occurrence on behalf of ANY active worker of
    // the event's PROPERTY (the worker pickers list every property worker, same
    // source as GetLinkedSites). Such a worker is legitimately tied to the event
    // even though it is NOT in the planning's PlanningSites, so the guard's
    // second branch (PropertyWorkers probe) must accept it and DEPLOY rather
    // than throw. Setup mirrors the pin test (target site absent from
    // PlanningSites) but adds a real eForm template + the full BC graph so the
    // deploy path runs to completion (ReadeForm + CaseCreateLocalOnly + the
    // PlanningCaseSite/Compliance writes), plus the one extra row the relaxation
    // turns on: an active PropertyWorker for (property, target site).
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureComplianceForOccurrence_SiteIsActivePropertyWorkerNotInPlanningSites_Deploys()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // The planning is assigned to A; the target site B is NOT in
        // PlanningSites but IS an active worker of the planning's property.
        var assignedSite = new Site
        {
            Name = "niels-site-propertyworker",
            MicrotingUid = 63,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        var targetSite = new Site
        {
            Name = "soren-site-propertyworker",
            MicrotingUid = 62,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddRangeAsync(assignedSite, targetSite);
        await MicrotingDbContext.SaveChangesAsync();

        // Real eForm template so DeployForRotationAsync's ReadeForm +
        // CaseCreateLocalOnly succeed and a real PlanningCaseSite + Compliance
        // row are written once the guard lets the deploy through.
        var template = await core.TemplateFromXml(CommentTemplateXml);
        var templateId = await core.TemplateCreate(template);

        // The guard's PropertyWorkers probe keys on areaRulePlanning.PropertyId,
        // so the ARP must point at a real Property and the seeded PropertyWorker
        // must reference that same PropertyId. Seed the minimal BC graph
        // (Area → Property → AreaRule) the deploy path reads.
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"PropertyWorkerProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // The relaxation: target site B is an ACTIVE PropertyWorker of the
        // planning's property even though it is absent from PlanningSites. This
        // single row is what flips the guard from throw to deploy.
        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = property.Id,
            WorkerId = targetSite.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var deadline = DateTime.UtcNow.Date.AddDays(2);

        var planning = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning
        {
            WorkflowState = Constants.WorkflowStates.Created,
            StartDate = deadline.AddDays(-7),
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month,
            NextExecutionTime = deadline,
            DayOfMonth = deadline.Day,
            RepeatUntil = deadline.AddMonths(6),
            RelatedEFormId = templateId,
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Planning is assigned ONLY to assignedSite — NOT the target site.
        ItemsPlanningPnDbContext.PlanningSites.Add(new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
        {
            PlanningId = planning.Id,
            SiteId = assignedSite.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // In-memory ARP (as in the pin test): the call only reads
        // .ItemPlanningId and .PropertyId off it; the Planning itself is looked
        // up from the DB. PropertyId MUST match the seeded PropertyWorker.
        var areaRulePlanning = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            ItemPlanningId = planning.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
        };

        var (calendar, coreHelper) = MakeMocks([], core);
        var service = MakeService(calendar, coreHelper);

        // Act — target site B (an active property worker, absent from
        // PlanningSites) materialises the occurrence. The guard must NOT throw.
        EnsureComplianceResult? result = null;
        Assert.DoesNotThrowAsync(async () =>
            result = await service.EnsureComplianceForOccurrenceAsync(
                areaRulePlanning, deadline, targetSite.Id, CancellationToken.None));

        // Assert — it DEPLOYED: a fresh Compliance with a real SDK case, and a
        // PlanningCaseSite written for the target site (inverse of the pin test).
        Assert.That(result, Is.Not.Null,
            "An active property worker must get a deploy result, not a null skip.");
        Assert.That(result!.Created, Is.True,
            "The occurrence must be newly materialised for the property worker.");
        Assert.That(result.ComplianceId, Is.GreaterThan(0),
            "A valid (non-zero) ComplianceId must be returned on deploy.");
        Assert.That(result.SdkCaseId, Is.GreaterThan(0),
            "A real SDK case must back the deploy (MicrotingSdkCaseId > 0).");

        var planningCaseSiteCount = await ItemsPlanningPnDbContext.PlanningCaseSites
            .CountAsync(pcs => pcs.PlanningId == planning.Id
                               && pcs.MicrotingSdkSiteId == targetSite.Id
                               && pcs.WorkflowState != Constants.WorkflowStates.Removed);
        Assert.That(planningCaseSiteCount, Is.EqualTo(1),
            "A PlanningCaseSite must be written for an active property worker "
            + "even when the site is absent from PlanningSites (#932/#1377 relaxation).");
    }

    // ------------------------------------------------------------------
    // 13. EnsureDeployedAsync_ConcurrentPassesSameRotation_CreatesExactlyOnePlanningCaseSite
    //
    // Regression for #934. The forensic snapshot showed four identical
    // PlanningCaseSites rows for the same (planning, site), three of them
    // created in the same second — the 5s StreamEventChanges poll, ListEvents
    // one-shots and the several window/board calls a single client fires per
    // sync, all racing through the deploy path before any of them writes the
    // Compliance row the idempotence guard keys on. This test fires N truly
    // concurrent EnsureDeployedAsync passes (each on its OWN DbContexts, like
    // separate gRPC requests on one pod) for the same rotation and asserts that
    // exactly ONE PlanningCaseSite is created. Without the per-(planning, site)
    // serialization in EventDeployService the passes each create a duplicate.
    //
    // Unlike the deferred happy-path test #8, this one drives the FULL deploy
    // (real eForm template + CaseCreateLocalOnly), so it also covers the
    // create → SDK case → Compliance write order end to end.
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_ConcurrentPassesSameRotation_CreatesExactlyOnePlanningCaseSite()
    {
        const int passes = 4; // mirrors the 4-row forensic snapshot in #934
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // SDK site whose MicrotingUid CaseCreateLocalOnly resolves against.
        const int siteMicrotingUid = 4242;
        var site = new Site
        {
            Name = "concurrent-deploy-site",
            MicrotingUid = siteMicrotingUid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        // Real eForm template so DeployForRotationAsync's ReadeForm +
        // CaseCreateLocalOnly succeed (and the Compliance guard becomes
        // effective for the second pass onward).
        var template = await core.TemplateFromXml(CommentTemplateXml);
        var templateId = await core.TemplateCreate(template);

        // Full BC graph the deploy path reads: Area → Property → AreaRule →
        // AreaRulePlanning(ItemPlanningId), plus the ItemsPlanning Planning +
        // PlanningSite (the #935 site-narrowing + #932 guard require it).
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"ConcurrentProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var rotationDate = DateTime.UtcNow.Date.AddDays(2);

        var planning = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning
        {
            WorkflowState = Constants.WorkflowStates.Created,
            StartDate = rotationDate.AddDays(-7),
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Day,
            NextExecutionTime = rotationDate,
            DayOfMonth = rotationDate.Day,
            RepeatUntil = rotationDate.AddMonths(6),
            RelatedEFormId = templateId,
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.PlanningSites.Add(new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
        {
            PlanningId = planning.Id,
            SiteId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = rotationDate.AddDays(-7), Status = true,
            RepeatType = 1, RepeatEvery = 1, DayOfWeek = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Shared calendar + core mocks (read-only across passes); each pass gets
        // its OWN DbContexts, mirroring separate gRPC requests' scoped contexts.
        var rotation = MakeRotation(id: 934, date: rotationDate, planningId: planning.Id, eformId: templateId);
        var (calendar, coreHelper) = MakeMocks([rotation], core);
        var sp = new ServiceCollection().AddSingleton(calendar).BuildServiceProvider();

        var todayKey = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toKey = rotationDate.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Warm the cached ServerVersion on the main thread so the concurrent
        // context builds below don't race to detect it / open extra connections.
        _ = CachedServerVersion();

        // Act — N concurrent deploy passes for the same (planning, site,
        // rotation). A Barrier holds every task at the gate until all are built,
        // then releases them together so they genuinely overlap in the
        // check-then-deploy window — without this the thread pool could run them
        // sequentially on a constrained runner and the race would not reproduce.
        using var startGate = new Barrier(passes);
        var tasks = Enumerable.Range(0, passes).Select(_ => Task.Run(async () =>
        {
            var bc = NewBackendContext();
            var ip = NewItemsPlanningContext();
            try
            {
                var service = new EventDeployService(
                    bc, ip, coreHelper, sp, NullLogger<EventDeployService>.Instance);
                // Release all passes simultaneously (timeout guards against a hang
                // if a sibling task faulted before reaching the gate).
                startGate.SignalAndWait(TimeSpan.FromSeconds(60));
                await service.EnsureDeployedAsync(
                    PropertyId, BoardIds, todayKey, toKey, site.Id, CancellationToken.None);
            }
            finally
            {
                await bc.DisposeAsync();
                await ip.DisposeAsync();
            }
        })).ToArray();
        await Task.WhenAll(tasks);

        // Assert — exactly one active PlanningCaseSite for (planning, site),
        // read through a fresh context so we see all committed rows.
        await using var verifyIp = NewItemsPlanningContext();
        var planningCaseSiteCount = await verifyIp.PlanningCaseSites
            .CountAsync(pcs => pcs.PlanningId == planning.Id
                               && pcs.MicrotingSdkSiteId == site.Id
                               && pcs.WorkflowState != Constants.WorkflowStates.Removed);
        Assert.That(planningCaseSiteCount, Is.EqualTo(1),
            $"Expected exactly one PlanningCaseSite for (planning {planning.Id}, site {site.Id}) "
            + $"after {passes} concurrent deploy passes, but found {planningCaseSiteCount} (#934).");
    }

    /// <summary>
    /// Builds a fresh <see cref="BackendConfigurationPnDbContext"/> against the
    /// same test database as the inherited one, so concurrent passes use their
    /// own context like separate gRPC requests (EF DbContext is not thread-safe).
    /// </summary>
    // Cached so the concurrent context builds in test #13 don't each open a
    // version-detection connection against the shared testcontainer. Same DB
    // server for both contexts, so one detection serves both. Warmed once
    // (on the main thread) before any parallel use to avoid a detect race.
    private ServerVersion? _serverVersion;

    private ServerVersion CachedServerVersion()
        => _serverVersion ??= ServerVersion.AutoDetect(
            BackendConfigurationPnDbContext!.Database.GetConnectionString());

    private BackendConfigurationPnDbContext NewBackendContext()
    {
        var cs = BackendConfigurationPnDbContext!.Database.GetConnectionString();
        var ob = new DbContextOptionsBuilder<BackendConfigurationPnDbContext>();
        ob.UseMySql(cs, CachedServerVersion(), b => b.EnableRetryOnFailure());
        return new BackendConfigurationPnDbContext(ob.Options);
    }

    private ItemsPlanningPnDbContext NewItemsPlanningContext()
    {
        var cs = ItemsPlanningPnDbContext!.Database.GetConnectionString();
        var ob = new DbContextOptionsBuilder<ItemsPlanningPnDbContext>();
        ob.UseMySql(cs, CachedServerVersion(), b => b.EnableRetryOnFailure());
        return new ItemsPlanningPnDbContext(ob.Options);
    }

    /// <summary>
    /// A minimal real eForm (single Comment field) used to make the full deploy
    /// path succeed. Copied from the SDK's CoreTesteFormCreateInDB test.
    /// </summary>
    private const string CommentTemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9060</Id>
    <Repeated>0</Repeated>
    <Label>CommentMain</Label>
    <StartDate>2017-07-07</StartDate>
    <EndDate>2027-07-07</EndDate>
    <Language>da</Language>
    <MultiApproval>false</MultiApproval>
    <FastNavigation>false</FastNavigation>
    <Review>false</Review>
    <Summary>false</Summary>
    <DisplayOrder>0</DisplayOrder>
    <ElementList>
        <Element type='DataElement'>
            <Id>9060</Id>
            <Label>CommentDataElement</Label>
            <Description><![CDATA[CommentDataElementDescription]]></Description>
            <DisplayOrder>0</DisplayOrder>
            <ReviewEnabled>false</ReviewEnabled>
            <ManualSync>false</ManualSync>
            <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
            <DoneButtonDisabled>false</DoneButtonDisabled>
            <ApprovalEnabled>false</ApprovalEnabled>
            <DataItemList>
                <DataItem type='Comment'>
                    <Id>73660</Id>
                    <Label>CommentField</Label>
                    <Description><![CDATA[CommentFieldDescription]]></Description>
                    <DisplayOrder>0</DisplayOrder>
                    <Multi>1</Multi>
                    <GeolocationEnabled>false</GeolocationEnabled>
                    <Split>false</Split>
                    <Value />
                    <ReadOnly>false</ReadOnly>
                    <Mandatory>false</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
            </DataItemList>
        </Element>
    </ElementList>
</Main>";

    /// <summary>
    /// Captures every <see cref="ILogger.Log"/> invocation into an in-memory
    /// list so tests can pin "this log line was (or wasn't) emitted". Used
    /// by the idempotence-guard test to distinguish "guard fired" from
    /// "guard missed but a downstream null-fallthrough produced the same
    /// observable Compliance count". Asserting on
    /// <see cref="ILogger"/> via NSubstitute against the underlying
    /// <c>Log(LogLevel, EventId, TState, Exception?, Func&lt;TState,Exception?,string&gt;)</c>
    /// overload is brittle because <c>TState</c> is the runtime
    /// <c>FormattedLogValues</c> struct; a plain capture is more robust.
    /// </summary>
    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
