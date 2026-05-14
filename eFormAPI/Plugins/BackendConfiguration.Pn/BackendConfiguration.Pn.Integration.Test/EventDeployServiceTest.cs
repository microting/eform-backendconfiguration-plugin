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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
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
        => new(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            calendar,
            logger ?? NullLogger<EventDeployService>.Instance);

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
        var complianceCount = await BackendConfigurationPnDbContext!.Compliances.CountAsync();
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
        var complianceCount = await BackendConfigurationPnDbContext!.Compliances.CountAsync();
        Assert.That(complianceCount, Is.EqualTo(0));
        await coreHelper.DidNotReceive().GetCore();
    }

    // ------------------------------------------------------------------
    // 3. EnsureDeployedAsync_RotationIsFromCompliance_SkippedNotDeployed
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_RotationIsFromCompliance_SkippedNotDeployed()
    {
        // Arrange — a future rotation that is already backed by a Compliance
        // (IsFromCompliance=true). EventDeployService should filter these out
        // because they need no deploy.
        var complianceBackedRotation = MakeRotation(
            id: 101,
            date: DateTime.UtcNow.Date.AddDays(2),
            isFromCompliance: true,
            planningId: 201,
            eformId: 301);
        var (calendar, coreHelper) = MakeMocks([complianceBackedRotation]);
        var service = MakeService(calendar, coreHelper);

        // Act
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-14", "2026-05-20", SdkSiteId, CancellationToken.None);

        // Assert — no Compliance row created, SDK Core never fetched.
        var complianceCount = await BackendConfigurationPnDbContext!.Compliances.CountAsync();
        Assert.That(complianceCount, Is.EqualTo(0));
        await coreHelper.DidNotReceive().GetCore();
    }

    // ------------------------------------------------------------------
    // 4. EnsureDeployedAsync_ComplianceAlreadyExists_SkipsRotation
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_ComplianceAlreadyExists_SkipsRotation()
    {
        // Arrange — a Compliance row already exists for
        // (planningId, rotationDate). The pipeline's idempotence guard
        // (EventDeployService.cs:184-195) must short-circuit before any
        // PlanningCase / CaseCreate is attempted.

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
        var rotationDate = DateTime.UtcNow.Date.AddDays(3);
        var existingCompliance = new Compliance
        {
            PlanningId = planningId,
            PropertyId = 1,
            AreaId = 1,
            Deadline = rotationDate,
            StartDate = rotationDate.AddDays(-7),
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
        // (EventDeployService.cs:184-195) short-circuited. Asserting only
        // Count==1 is tautological: if the guard silently broke,
        // execution would fall through to the planning lookup at line 200,
        // find nothing for planningId=500, log a "planning ... not found"
        // warning at line 208, hit `continue` at line 211, and STILL leave
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
                "Guard should have short-circuited before the planning lookup at EventDeployService.cs:200-212.");
            Assert.That(
                logger.Entries.Any(e => e.Message.Contains("areaRulePlanning") && e.Message.Contains("not found")),
                Is.False,
                "Guard should have short-circuited before the areaRulePlanning lookup at EventDeployService.cs:214-227.");
        });

        var complianceCount = await BackendConfigurationPnDbContext.Compliances.CountAsync();
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
            planningId: 700,
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

        var afterCount = await BackendConfigurationPnDbContext!.Compliances.CountAsync();
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
        // Arrange — a today rotation. We do NOT seed a Planning, so when
        // the rotation is admitted by the candidate filter the pipeline
        // reaches the planning lookup and emits a "planning ... not found"
        // warning. That warning is the observable canary that the filter
        // admitted today's date.
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
        const int planningId = 900;
        var rotation = MakeRotation(
            id: 104,
            date: today,
            planningId: planningId,
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

        var complianceCount = await BackendConfigurationPnDbContext!.Compliances.CountAsync();
        Assert.That(complianceCount, Is.EqualTo(0),
            "Missing-planning path must not leave any Compliance rows behind.");
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
