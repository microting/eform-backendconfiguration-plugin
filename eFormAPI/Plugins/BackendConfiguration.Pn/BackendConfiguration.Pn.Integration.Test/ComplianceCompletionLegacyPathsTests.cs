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

using BackendConfiguration.Pn.Grpc;
using BackendConfiguration.Pn.Services.BackendConfigurationCompliancesService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
// Fully qualified to avoid ambiguity with the generated
// BackendConfiguration.Pn.Grpc.* namespace imported above.
using GrpcCore = global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// CHARACTERIZATION tests — they pin what the three legacy compliance-completion
/// entry points do <b>today</b>, so the upcoming change to completion semantics
/// cannot alter them silently. Nothing here is a statement about what the code
/// <i>should</i> do; several assertions deliberately pin behaviour that is
/// arguably wrong, and each of those says so on the individual test.
///
/// <para>The three paths, all previously untested:</para>
/// <list type="number">
///   <item><description><see cref="BackendConfigurationCompliancesService.Update"/>
///     (<c>PUT api/backend-configuration-pn/compliances/cases</c>) — soft-deletes the
///     Compliance FIRST, then completes the SDK Case (<c>Status = 100</c>, DoneAt with
///     time-of-day preserved), then the matching <c>PlanningCaseSite</c>/<c>PlanningCase</c>,
///     then recomputes <c>Property.ComplianceStatus</c>, then retracts the device case
///     SYNCHRONOUSLY via <c>core.CaseDelete</c>.</description></item>
///   <item><description><see cref="BackendConfigurationCompliancesService.UpdateFromCalendar"/>
///     — byte-for-byte the same writes, except the retraction is fire-and-forget
///     (<c>_ = Task.Run(...)</c>).</description></item>
///   <item><description><see cref="CompliancesGrpcService.UpdateComplianceCase"/> — the
///     flutter-eform path. Soft-deletes the Compliance and completes the SDK Case with
///     DoneAt TRUNCATED TO MIDNIGHT; writes no items-planning rows and never retracts.</description></item>
/// </list>
///
/// <para><b>Why every SDK case here carries a duplicate MicrotingUid.</b>
/// <c>Core.CaseDelete(int microtingUId)</c> is a real HTTP call to the Microting
/// platform (<c>skipCloudDeploy</c> only short-circuits <c>SendXml</c>, never
/// <c>Delete</c>), which would make these tests hang on a DNS/HTTP timeout in CI.
/// Seeding a second Case row that shares the MicrotingUid makes
/// <c>SqlController.CaseReadByMUId</c> take its <c>Count(...) == 1</c> false branch,
/// whose <c>CheckListSites.FirstAsync</c> throws — all of it BEFORE the communicator is
/// reached. Same offline trick as
/// <c>EventDeployServiceEformRepairTests.Repair_CaseWithMicrotingUid_CloudDeleteThrows_LocalRowIsStillRemoved</c>.
/// The upshot is that retraction fails on both variants here, so these tests pin the
/// row state each path leaves behind, not the network round-trip itself.</para>
///
/// <para>The six SQL dumps replay once per fixture, so every row is seeded with a
/// <c>Guid.NewGuid()</c>-suffixed name and every assertion is scoped to ids this
/// fixture created — no absolute whole-table counts.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceCompletionLegacyPathsTests : TestBaseSetup
{
    /// <summary>SDK <c>Case.Status</c> for a live, unanswered case (SqlController.CaseCreate writes 33).</summary>
    private const int OpenCaseStatus = 33;

    /// <summary><c>PlanningCase.Status</c> / <c>PlanningCaseSite.Status</c> for an open occurrence.</summary>
    private const int OpenPlanningStatus = 66;

    /// <summary>The "done" status every path under test writes onto the SDK case.</summary>
    private const int CompletedStatus = 100;

    /// <summary><c>Property.ComplianceStatus</c> seeded as "overdue" so the recompute is observable.</summary>
    private const int OverdueComplianceStatus = 2;

    private sealed class Scenario
    {
        public IEFormCoreService CoreHelper = null!;
        public Property Property = null!;
        public Planning Planning = null!;
        public Language Language = null!;
        public Site Site = null!;
        public int CheckListId;
    }

    // ------------------------------------------------------------------
    // Seeding. Adapted from EventDeployServiceEformRepairTests — trimmed to
    // what the completion paths actually read (no AreaRule/AreaRulePlanning:
    // none of the three services touch them).
    // ------------------------------------------------------------------

    /// <summary>
    /// Seeds Property + Planning + a bare SDK CheckList (so <c>Case.CheckListId</c>
    /// satisfies its FK without paying for a full TemplateCreate) and a Site, then
    /// hands back a real <c>eFormCore.Core</c> wrapped in a substituted
    /// <see cref="IEFormCoreService"/> — <c>Core</c> is a concrete class and cannot
    /// be substituted, so the SDK work runs for real against the testcontainer.
    /// </summary>
    private async Task<Scenario> SeedScenarioAsync(string tag)
    {
        var core = await GetCore();

        // GetCore() seeds the SDK default languages; reuse one rather than
        // inserting a duplicate.
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var checkList = new CheckList
        {
            Label = $"compliance-legacy-{tag}-{Guid.NewGuid()}",
            ParentId = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();

        var site = new Site
        {
            Name = $"compliance-legacy-{tag}-{Guid.NewGuid()}",
            MicrotingUid = Random.Shared.Next(700_000, 799_999),
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"ComplianceLegacy-{tag}-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            // Seeded "overdue" so a recompute back to 0 is observable, and so the
            // absence of a recompute is equally observable.
            ComplianceStatus = OverdueComplianceStatus,
            ComplianceStatusThirty = OverdueComplianceStatus,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = DateTime.UtcNow.Date.AddDays(-14),
            RelatedEFormId = checkList.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        return new Scenario
        {
            CoreHelper = coreHelper,
            Property = property,
            Planning = planning,
            Language = language,
            Site = site,
            CheckListId = checkList.Id
        };
    }

    /// <summary>
    /// One live SDK case for the scenario's site, plus (see the class remarks) a
    /// decoy row sharing its MicrotingUid so any <c>Core.CaseDelete</c> throws
    /// locally instead of dialling the platform. The decoy is referenced by no
    /// Compliance and no PlanningCaseSite, so nothing under test reads it.
    /// </summary>
    private async Task<Case> SeedSdkCaseAsync(Scenario s, int microtingUid)
    {
        var sdkCase = new Case
        {
            SiteId = s.Site.Id,
            CheckListId = s.CheckListId,
            Status = OpenCaseStatus,
            MicrotingUid = microtingUid,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);

        var decoy = new Case
        {
            SiteId = s.Site.Id,
            CheckListId = s.CheckListId,
            Status = OpenCaseStatus,
            MicrotingUid = microtingUid,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(decoy);
        await MicrotingDbContext.SaveChangesAsync();

        return sdkCase;
    }

    private async Task<PlanningCase> SeedPlanningCaseAsync(Scenario s)
    {
        var planningCase = new PlanningCase
        {
            PlanningId = s.Planning.Id,
            Status = OpenPlanningStatus,
            MicrotingSdkeFormId = s.CheckListId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return planningCase;
    }

    /// <summary>
    /// <c>Update</c>/<c>UpdateFromCalendar</c> locate the occurrence's
    /// PlanningCaseSite by <c>MicrotingSdkCaseId == foundCase.Id</c>, so
    /// <c>MicrotingSdkCaseId</c> is the field that must be seeded from the SDK
    /// case. (It used to be a <c>CreatedAt.Date == Compliance.StartDate.Date</c>
    /// heuristic, which collapsed to one row per planning for back-filled past
    /// series — see issue #1156.)
    /// </summary>
    private async Task<PlanningCaseSite> SeedPlanningCaseSiteAsync(Scenario s, PlanningCase planningCase, Case sdkCase)
    {
        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = s.Planning.Id,
            PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = s.Site.Id,
            MicrotingSdkeFormId = s.CheckListId,
            MicrotingSdkCaseId = sdkCase.Id,
            Status = OpenPlanningStatus,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return planningCaseSite;
    }

    /// <summary>
    /// Compliance row for one occurrence. Deadline is in the past so the
    /// <c>Property.ComplianceStatus</c> recompute has something to flip.
    /// </summary>
    private async Task<Compliance> SeedComplianceAsync(Scenario s, PlanningCase planningCase, Case sdkCase)
    {
        var compliance = new Compliance
        {
            PlanningId = s.Planning.Id,
            PropertyId = s.Property.Id,
            AreaId = 0,
            Deadline = DateTime.UtcNow.Date.AddDays(-1),
            StartDate = DateTime.UtcNow.Date,
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = s.CheckListId,
            PlanningCaseSiteId = planningCase.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance;
    }

    private BackendConfigurationCompliancesService MakeCompliancesService(Scenario s)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(s.Language));

        return new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext!,
            BackendConfigurationPnDbContext!,
            userService,
            // Test-project stub (BackendConfigurationAssignmentWorkerServiceHelperTest.cs)
            // that echoes the resource key, which is what the Message assertions read.
            new BackendConfigurationLocalizationService(),
            s.CoreHelper,
            TimePlanningPnDbContext!);
    }

    private CompliancesGrpcService MakeGrpcService(Scenario s, bool hasAccess)
    {
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(s.Site.Id, s.Property.Id).Returns(Task.FromResult(hasAccess));

        var siteResolver = Substitute.For<IGrpcSiteResolver>();
        siteResolver.GetSdkSiteIdAsync().Returns(Task.FromResult(s.Site.Id));

        return new CompliancesGrpcService(
            s.CoreHelper, access, siteResolver, BackendConfigurationPnDbContext!);
    }

    private static ReplyRequest MakeReply(Scenario s, int complianceId, int caseId, DateTime doneAt) =>
        new()
        {
            Id = caseId,
            Label = "legacy-completion",
            DoneAt = doneAt,
            IsDoneAtEditable = true,
            ExtraId = complianceId,
            SiteId = s.Site.Id,
            ElementList = []
        };

    private async Task<Case> ReadCaseAsync(int caseId) =>
        await MicrotingDbContext!.Cases.AsNoTracking().FirstAsync(x => x.Id == caseId);

    private async Task<Compliance> ReadComplianceAsync(int complianceId) =>
        await BackendConfigurationPnDbContext!.Compliances.AsNoTracking().FirstAsync(x => x.Id == complianceId);

    private async Task<PlanningCaseSite> ReadPlanningCaseSiteAsync(int id) =>
        await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking().FirstAsync(x => x.Id == id);

    private async Task<PlanningCase> ReadPlanningCaseAsync(int id) =>
        await ItemsPlanningPnDbContext!.PlanningCases.AsNoTracking().FirstAsync(x => x.Id == id);

    private async Task<Property> ReadPropertyAsync(int id) =>
        await BackendConfigurationPnDbContext!.Properties.AsNoTracking().FirstAsync(x => x.Id == id);

    // ==================================================================
    // 1. BackendConfigurationCompliancesService.Update — the full cascade.
    // ==================================================================

    /// <summary>
    /// Pins the whole write cascade of the HTTP completion path in one shot:
    /// Compliance soft-deleted, SDK Case at <c>Status = 100</c> / <c>WorkflowState = created</c>
    /// / <c>SiteId = model.SiteId</c>, DoneAt and DoneAtUserModifiable carrying the
    /// caller's TIME-OF-DAY (not midnight — that is the documented
    /// <c>SpecifyKind(model.DoneAt, Utc)</c> behaviour and the single sharpest
    /// difference from the gRPC path), the matching PlanningCaseSite and PlanningCase
    /// at 100 with <c>DoneByUserName</c> taken from the SDK Site, and
    /// <c>Property.ComplianceStatus</c>/<c>ComplianceStatusThirty</c> recomputed to 0.
    ///
    /// <para>Arguably wrong, pinned anyway: <c>Message</c> is still
    /// <c>CaseHasBeenUpdated</c> even though the synchronous <c>core.CaseDelete</c>
    /// threw (see the class remarks — the duplicate MicrotingUid makes it throw
    /// offline). The retraction is best-effort and its failure is swallowed
    /// (:397-401), so the worker's device can keep a form the backend believes is
    /// retracted and the caller is told everything succeeded.</para>
    /// </summary>
    [Test]
    public async Task Update_SingleSite_CompletesCaseAndPlanningRows()
    {
        var s = await SeedScenarioAsync("update-cascade");
        var sdkCase = await SeedSdkCaseAsync(s, 970_001);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        // Deliberately not midnight: the time-of-day must survive the round-trip.
        var doneAt = new DateTime(2026, 3, 17, 14, 35, 0, DateTimeKind.Unspecified);

        var result = await MakeCompliancesService(s)
            .Update(MakeReply(s, compliance.Id, sdkCase.Id, doneAt));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Message, Is.EqualTo("CaseHasBeenUpdated"));

            // 1. Compliance occurrence is gone.
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

            // 2. SDK case completed, still "created" (NOT retracted locally).
            Assert.That(reloadedCase.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(reloadedCase.SiteId, Is.EqualTo(s.Site.Id));
            Assert.That(reloadedCase.DoneAt, Is.EqualTo(doneAt),
                "Update must preserve the caller's time-of-day (SpecifyKind, not truncate)");
            Assert.That(reloadedCase.DoneAtUserModifiable, Is.EqualTo(doneAt));

            // 3. Items-planning rows follow.
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId, Is.EqualTo(sdkCase.Id));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.EqualTo(doneAt));
            Assert.That(reloadedPlanningCaseSite.DoneByUserId, Is.EqualTo(s.Site.Id));
            Assert.That(reloadedPlanningCaseSite.DoneByUserName, Is.EqualTo(s.Site.Name),
                "DoneByUserName comes from the SDK Site, not the signed-in user");

            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedPlanningCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Processed));
            Assert.That(reloadedPlanningCase.MicrotingSdkCaseId, Is.EqualTo(sdkCase.Id));
            Assert.That(reloadedPlanningCase.MicrotingSdkCaseDoneAt, Is.EqualTo(doneAt));
            Assert.That(reloadedPlanningCase.DoneByUserId, Is.EqualTo(s.Site.Id));
            Assert.That(reloadedPlanningCase.DoneByUserName, Is.EqualTo(s.Site.Name));

            // 4. Property compliance recomputed — the property has no live overdue
            //    Compliance left, so both counters drop to 0.
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(0));
            Assert.That(reloadedProperty.ComplianceStatusThirty, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Pins the ordering hazard at BackendConfigurationCompliancesService.cs:212-217:
    /// <c>compliance.Delete()</c> runs BEFORE the SDK case is even looked up, and the
    /// method has no transaction and no compensation. So when the case cannot be found
    /// the caller gets <c>CaseNotFound</c> while the occurrence has ALREADY been soft-deleted
    /// — the task vanishes from the calendar and the compliance list, permanently, and
    /// nothing was ever completed.
    ///
    /// <para>This is the arguably-wrong behaviour the fixture exists to freeze: it is
    /// pinned, not fixed. The corroborating half of the assertion is that everything
    /// downstream is untouched — PlanningCaseSite/PlanningCase still 66 and
    /// <c>Property.ComplianceStatus</c> still 2 — proving the early return happened
    /// after the delete and before any completion work.</para>
    /// </summary>
    [Test]
    public async Task Update_CaseNotFound_StillSoftDeletesCompliance()
    {
        var s = await SeedScenarioAsync("update-case-missing");
        var sdkCase = await SeedSdkCaseAsync(s, 970_002);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        // A case id that cannot exist — Cases.Id is an int identity starting at 1.
        var result = await MakeCompliancesService(s)
            .Update(MakeReply(s, compliance.Id, int.MaxValue, new DateTime(2026, 3, 17, 14, 35, 0)));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("CaseNotFound"));

            // The occurrence is gone even though nothing was completed.
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "Compliance.Delete() runs before the case lookup and is never rolled back");

            // ...and nothing downstream ran.
            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus));
            Assert.That(reloadedCase.DoneAt, Is.Null);
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus),
                "the Property recompute sits after the early return, so it never happened");
        });
    }

    // ==================================================================
    // 2. UpdateFromCalendar — same writes, fire-and-forget retraction.
    // ==================================================================

    /// <summary>
    /// Pins that <c>UpdateFromCalendar</c> resolves the MicrotingUid to retract but
    /// hands the actual <c>core.CaseDelete</c> to <c>_ = Task.Run(...)</c>
    /// (BackendConfigurationCompliancesService.cs:576-588) and returns immediately.
    /// The observable consequence, asserted with NO delay and NO polling: the moment
    /// the call returns the SDK case is still <c>WorkflowState = created</c> with its
    /// <c>MicrotingUid</c> intact, i.e. from the caller's point of view the device form
    /// has not been withdrawn.
    ///
    /// <para>The rest of the cascade is asserted too, to pin that the calendar variant
    /// is otherwise identical to <c>Update</c> — same DoneAt time-of-day, same
    /// PlanningCaseSite/PlanningCase promotion to 100.</para>
    ///
    /// <para>Arguably wrong, pinned anyway: nothing awaits, observes or retries that
    /// task, so a failing retraction is invisible — the backend reports the occurrence
    /// complete while the worker's device may still hold the form.</para>
    ///
    /// <para>NOT proven here, deliberately: that the retraction is scheduled rather
    /// than awaited. This fixture's offline decoy makes <c>CaseDelete</c> throw before
    /// it mutates anything, and both the synchronous and the fire-and-forget variant
    /// swallow that exception — so the persisted state read below is identical either
    /// way. An earlier version of this test claimed to pin the scheduling and could
    /// not have failed if someone made it synchronous. Proving that needs a core
    /// substitute whose CaseDelete blocks on a gate the test releases; until then this
    /// pins the completion cascade only, which it does discriminate.</para>
    /// </summary>
    [Test]
    public async Task UpdateFromCalendar_WritesTheSameCompletionCascadeAsUpdate()
    {
        var s = await SeedScenarioAsync("calendar-retract");
        const int microtingUid = 970_003;
        var sdkCase = await SeedSdkCaseAsync(s, microtingUid);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        var doneAt = new DateTime(2026, 3, 17, 9, 15, 0, DateTimeKind.Unspecified);

        var result = await MakeCompliancesService(s)
            .UpdateFromCalendar(MakeReply(s, compliance.Id, sdkCase.Id, doneAt));

        // Read back with no delay and no poll: these assertions cover the
        // completion cascade, not the retraction scheduling (see remarks).
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Message, Is.EqualTo("CaseHasBeenUpdated"));

            // The retraction has NOT been applied by the time the call returns.
            Assert.That(reloadedCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "the retraction is fire-and-forget, so the case is still live on return");
            Assert.That(reloadedCase.MicrotingUid, Is.EqualTo(microtingUid),
                "the MicrotingUid is left in place — nothing synchronous cleared it");

            // Everything else matches Update exactly.
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(reloadedCase.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedCase.DoneAt, Is.EqualTo(doneAt));
            Assert.That(reloadedCase.DoneAtUserModifiable, Is.EqualTo(doneAt));
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.EqualTo(doneAt));
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedPlanningCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Processed));
        });
    }

    // ==================================================================
    // 3. CompliancesGrpcService.UpdateComplianceCase — the minimal path.
    // ==================================================================

    /// <summary>
    /// Pins how little the flutter-eform gRPC completion actually does
    /// (CompliancesGrpcService.cs:150-172): it soft-deletes the Compliance and
    /// completes the SDK Case, and that is all.
    ///
    /// <para>Arguably wrong, pinned anyway — two divergences from the HTTP paths:</para>
    /// <list type="bullet">
    ///   <item><description>It writes NO <c>PlanningCaseSite</c> / <c>PlanningCase</c> rows,
    ///     so both stay at 66. Every items-planning consumer (reports, task-tracker,
    ///     "completed last 30 days" stats) therefore never sees a mobile completion.</description></item>
    ///   <item><description>It TRUNCATES DoneAt to midnight
    ///     (<c>new DateTime(y, m, d, 0, 0, 0, Utc)</c>) where <c>Update</c> preserves the
    ///     time-of-day — the same occurrence gets a different DoneAt depending on which
    ///     client completed it.</description></item>
    /// </list>
    /// <para>It also never calls <c>CaseDelete</c>, so the MicrotingUid is untouched and
    /// no retraction is even attempted.</para>
    /// </summary>
    [Test]
    public async Task UpdateComplianceCase_Grpc_CompletesOnlySdkCase()
    {
        var s = await SeedScenarioAsync("grpc-complete");
        const int microtingUid = 970_004;
        var sdkCase = await SeedSdkCaseAsync(s, microtingUid);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        var request = new UpdateComplianceCaseRequest
        {
            Id = sdkCase.Id,
            Label = "grpc-legacy-completion",
            // Midday so the parsed day is stable regardless of the runner's timezone.
            DoneAt = "2026-03-17T12:00:00Z",
            IsDoneAtEditable = true,
            ExtraId = compliance.Id,
            SiteId = s.Site.Id
        };

        var response = await MakeGrpcService(s, hasAccess: true)
            .UpdateComplianceCase(request, Substitute.For<GrpcCore.ServerCallContext>());

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True, response.Message);
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

            // The SDK case IS completed...
            Assert.That(reloadedCase.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(reloadedCase.SiteId, Is.EqualTo(s.Site.Id));

            // ...with DoneAt truncated to midnight, unlike Update.
            Assert.That(reloadedCase.DoneAt, Is.EqualTo(new DateTime(2026, 3, 17, 0, 0, 0)),
                "the gRPC path zeroes the time-of-day");
            Assert.That(reloadedCase.DoneAtUserModifiable, Is.EqualTo(new DateTime(2026, 3, 17, 0, 0, 0)));

            // ...but the items-planning rows are NOT touched.
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(OpenPlanningStatus),
                "the gRPC path writes no PlanningCaseSite completion");
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus),
                "the gRPC path writes no PlanningCase completion");
            Assert.That(reloadedPlanningCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

            // ...no retraction was attempted.
            Assert.That(reloadedCase.MicrotingUid, Is.EqualTo(microtingUid),
                "the gRPC path never calls CaseDelete");

            // ...and Property.ComplianceStatus is never recomputed either.
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus));
            Assert.That(reloadedProperty.ComplianceStatusThirty, Is.EqualTo(OverdueComplianceStatus));
        });
    }

    /// <summary>
    /// Pins that the gRPC path's PropertyWorker-access gate sits BEFORE the
    /// destructive <c>compliance.Delete()</c> (CompliancesGrpcService.cs:117-121 vs
    /// :152): a caller with no access to the compliance's property gets an
    /// <c>RpcException</c> with <see cref="GrpcCore.StatusCode.PermissionDenied"/> and
    /// the Compliance row is left untouched at <c>created</c>.
    ///
    /// <para>Note that the exception escapes rather than being caught — the
    /// <c>try</c>/<c>catch</c> that turns failures into
    /// <c>UpdateComplianceCaseResponse{Success=false}</c> starts after the gate, so
    /// this one surfaces as a gRPC status rather than an in-band error.</para>
    /// </summary>
    [Test]
    public async Task UpdateComplianceCase_NoPropertyAccess_ThrowsPermissionDenied()
    {
        var s = await SeedScenarioAsync("grpc-denied");
        var sdkCase = await SeedSdkCaseAsync(s, 970_005);
        var planningCase = await SeedPlanningCaseAsync(s);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        var request = new UpdateComplianceCaseRequest
        {
            Id = sdkCase.Id,
            Label = "grpc-denied",
            DoneAt = "2026-03-17T12:00:00Z",
            IsDoneAtEditable = true,
            ExtraId = compliance.Id,
            SiteId = s.Site.Id
        };

        var service = MakeGrpcService(s, hasAccess: false);

        var ex = Assert.ThrowsAsync<GrpcCore.RpcException>(async () =>
            await service.UpdateComplianceCase(request, Substitute.For<GrpcCore.ServerCallContext>()));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.PermissionDenied));

            // The gate is upstream of every write.
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "the access check must run before compliance.Delete()");
            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus));
            Assert.That(reloadedCase.DoneAt, Is.Null);
        });
    }
}
