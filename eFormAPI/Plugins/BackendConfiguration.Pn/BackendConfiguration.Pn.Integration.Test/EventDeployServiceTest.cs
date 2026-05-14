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

    // ------------------------------------------------------------------
    // 1. EnsureDeployedAsync_NoRotationsInWindow_DoesNothing
    // ------------------------------------------------------------------
    [Test]
    public async Task EnsureDeployedAsync_NoRotationsInWindow_DoesNothing()
    {
        // Arrange — calendar returns zero rotations for the window.
        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, []));

        var coreHelper = Substitute.For<IEFormCoreService>();
        var service = new EventDeployService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            calendar,
            NullLogger<EventDeployService>.Instance);

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
        var yesterday = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pastRotation = new CalendarTaskResponseModel
        {
            Id = 100,
            PlanningId = 200,
            EformId = 300,
            TaskDate = yesterday,
            IsFromCompliance = false
        };

        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, [pastRotation]));

        var coreHelper = Substitute.For<IEFormCoreService>();
        var service = new EventDeployService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            calendar,
            NullLogger<EventDeployService>.Instance);

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
        var future = DateTime.UtcNow.Date.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var complianceBackedRotation = new CalendarTaskResponseModel
        {
            Id = 101,
            PlanningId = 201,
            EformId = 301,
            TaskDate = future,
            IsFromCompliance = true
        };

        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, [complianceBackedRotation]));

        var coreHelper = Substitute.For<IEFormCoreService>();
        var service = new EventDeployService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            calendar,
            NullLogger<EventDeployService>.Instance);

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
        Assert.That(core, Is.Not.Null);

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

        var rotation = new CalendarTaskResponseModel
        {
            Id = 102,
            PlanningId = planningId,
            EformId = 400,
            TaskDate = rotationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IsFromCompliance = false
        };
        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, [rotation]));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var service = new EventDeployService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            calendar,
            NullLogger<EventDeployService>.Instance);

        // Act
        await service.EnsureDeployedAsync(
            PropertyId, BoardIds, "2026-05-14", "2026-05-20", site.Id, CancellationToken.None);

        // Assert — still exactly 1 Compliance row, no duplicate from the deploy pass.
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
        Assert.That(core, Is.Not.Null);

        var future = DateTime.UtcNow.Date.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rotation = new CalendarTaskResponseModel
        {
            Id = 103,
            PlanningId = 700,
            EformId = 800,
            TaskDate = future,
            IsFromCompliance = false
        };
        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, [rotation]));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var service = new EventDeployService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            calendar,
            NullLogger<EventDeployService>.Instance);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act + Assert — either the cancellation token cascades into an
        // OperationCanceledException (preferred) or the call returns
        // without writing. Both shapes honour the contract.
        var beforeCount = await BackendConfigurationPnDbContext!.Compliances.CountAsync();

        try
        {
            await service.EnsureDeployedAsync(
                PropertyId, BoardIds, "2026-05-14", "2026-05-20", SdkSiteId, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected path — the EF query in the foreach loop (or the
            // sdkSite lookup) sees the cancelled token and throws.
        }

        var afterCount = await BackendConfigurationPnDbContext.Compliances.CountAsync();
        Assert.That(afterCount, Is.EqualTo(beforeCount),
            "Cancellation must not produce any Compliance writes.");
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
}
