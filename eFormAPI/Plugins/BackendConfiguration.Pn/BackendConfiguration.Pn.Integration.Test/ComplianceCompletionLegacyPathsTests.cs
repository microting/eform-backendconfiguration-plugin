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

using System.Text;
using System.Text.Json;
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
/// Mostly CHARACTERIZATION tests — they pin what the three legacy compliance-completion
/// entry points do <b>today</b>, so the upcoming change to completion semantics
/// cannot alter them silently. Most of sections 1-3 is not a statement about what the
/// code <i>should</i> do; several assertions deliberately pin behaviour that is
/// arguably wrong, and each of those says so on the individual test.
///
/// <para>Two groups are the exception and ARE regression tests. Section 4 covers the
/// occurrence lookup (#1156 / PR #1158) and the ownership guard beneath it (#1218).
/// Separately, every <c>*_MutatesNothing</c> test — they sit in sections 1, 2 and 4 —
/// covers #1157: <c>Update</c>/<c>UpdateFromCalendar</c> now resolve the Compliance, the
/// SDK case and the <c>PlanningCaseSite</c> BEFORE the first irreversible write, so a
/// rejected completion leaves no partial write behind. Several of those tests previously
/// pinned the opposite and were rewritten deliberately, not incidentally.</para>
///
/// <para>The three paths, all previously untested:</para>
/// <list type="number">
///   <item><description><see cref="BackendConfigurationCompliancesService.Update"/>
///     (<c>PUT api/backend-configuration-pn/compliances/cases</c>) — since #1157
///     validates FIRST (Compliance still live, SDK case present, PlanningCaseSite found
///     and owned by the compliance's planning), then soft-deletes the Compliance, then
///     completes the SDK Case (<c>Status = 100</c>, DoneAt with time-of-day preserved),
///     then the matching <c>PlanningCaseSite</c>/<c>PlanningCase</c>, then recomputes
///     <c>Property.ComplianceStatus</c>, then retracts the device case SYNCHRONOUSLY via
///     <c>core.CaseDelete</c>.</description></item>
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
    ///
    /// <para><paramref name="deadline"/> must be supplied whenever a test seeds more
    /// than one Compliance on the SAME planning: <c>Compliances</c> is UNIQUE on
    /// <c>(PlanningId, Deadline)</c>, so two same-Deadline siblings blow up at seed
    /// time with a <c>DbUpdateException</c> rather than at assert time.
    /// <c>StartDate</c> is deliberately NOT parameterised — every back-filled
    /// sibling really does share <c>UtcNow.Date</c>, and that collapse is the shape
    /// the occurrence-lookup regression tests depend on.</para>
    /// </summary>
    private async Task<Compliance> SeedComplianceAsync(Scenario s, PlanningCase planningCase, Case sdkCase,
        DateTime? deadline = null)
    {
        var compliance = new Compliance
        {
            PlanningId = s.Planning.Id,
            PropertyId = s.Property.Id,
            AreaId = 0,
            Deadline = deadline ?? DateTime.UtcNow.Date.AddDays(-1),
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

    /// <summary>
    /// Soft-deletes a seeded Compliance through the SAME call the production paths use
    /// (<c>PnBase.Delete</c> — WorkflowState to <c>removed</c>, Version bumped, a
    /// ComplianceVersion row written), so the row left behind is byte-identical to the one
    /// a completed — or a partially-failed pre-#1157 — completion leaves. Used by the
    /// <c>*_RetryAgainstSoftDeletedCompliance_*</c> tests.
    /// </summary>
    private async Task SoftDeleteComplianceAsync(Compliance compliance) =>
        await compliance.Delete(BackendConfigurationPnDbContext!);

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

    /// <summary>
    /// <paramref name="siteId"/> overrides the scenario's own SDK site — only the
    /// <c>*_UnknownSiteId_*</c> tests pass it, to reach the pre-flight's site check with
    /// an id no <c>Sites</c> row carries.
    /// </summary>
    private static ReplyRequest MakeReply(Scenario s, int complianceId, int caseId, DateTime doneAt,
        int? siteId = null) =>
        new()
        {
            Id = caseId,
            Label = "legacy-completion",
            DoneAt = doneAt,
            IsDoneAtEditable = true,
            ExtraId = complianceId,
            SiteId = siteId ?? s.Site.Id,
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
    /// (:472-476), so the worker's device can keep a form the backend believes is
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
    /// #1157: a completion whose SDK case cannot be found must mutate NOTHING.
    ///
    /// <para><b>This test used to pin the exact opposite</b>, under the name
    /// <c>Update_CaseNotFound_StillSoftDeletesCompliance</c>, and its own doc comment
    /// called that "the arguably-wrong behaviour the fixture exists to freeze: it is
    /// pinned, not fixed". <c>compliance.Delete()</c> ran before the SDK case was even
    /// looked up, with no transaction and no compensation, so the caller got
    /// <c>CaseNotFound</c> over an occurrence that had ALREADY been soft-deleted — gone
    /// from the calendar and the compliance list, permanently, nothing completed, and a
    /// retry could not repair it because the Compliance lookup was not filtered on
    /// WorkflowState. #1157 hoists the whole validation above the first write, which
    /// necessarily inverts this outcome. <b>That is a deliberate product decision: a
    /// failed operation must not destroy data.</b></para>
    ///
    /// <para>What discriminates: <c>reloadedCompliance.WorkflowState</c> is
    /// <c>created</c>; on pre-fix code it is <c>removed</c>. The corroborating
    /// assertions — PlanningCaseSite/PlanningCase still 66 and
    /// <c>Property.ComplianceStatus</c> still 2 — are unchanged and still hold: nothing
    /// downstream ever ran.</para>
    /// </summary>
    [Test]
    public async Task Update_CaseNotFound_MutatesNothing()
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

            // #1157: the occurrence survives a failed completion.
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "#1157: the SDK-case check now runs in the pre-flight, above compliance.Delete(), "
                + "so a rejected completion leaves the occurrence intact (pre-fix: removed)");

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
    /// (BackendConfigurationCompliancesService.cs:723-739) and returns immediately.
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

    /// <summary>
    /// #1157 twin of <see cref="Update_CaseNotFound_MutatesNothing"/> against
    /// <c>UpdateFromCalendar</c>. The two production methods are verbatim copy-paste of
    /// each other — including the whole pre-flight — so a fix applied to only one of them
    /// is a live hole that a single test cannot see. The calendar variant is also the
    /// reachable one: the calendar completes occurrences through <c>UpdateFromCalendar</c>.
    ///
    /// <para>What discriminates: <c>reloadedCompliance.WorkflowState</c> is
    /// <c>created</c>. On pre-fix code it is <c>removed</c>.</para>
    /// </summary>
    [Test]
    public async Task UpdateFromCalendar_CaseNotFound_MutatesNothing()
    {
        var s = await SeedScenarioAsync("calendar-case-missing");
        var sdkCase = await SeedSdkCaseAsync(s, 970_013);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        // A case id that cannot exist — Cases.Id is an int identity starting at 1.
        var result = await MakeCompliancesService(s)
            .UpdateFromCalendar(MakeReply(s, compliance.Id, int.MaxValue, new DateTime(2026, 3, 17, 14, 35, 0)));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("CaseNotFound"));

            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "#1157: UpdateFromCalendar carries its own copy of the pre-flight and must leave "
                + "the occurrence intact too (pre-fix: removed)");

            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus));
            Assert.That(reloadedCase.DoneAt, Is.Null);
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus));
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

    // ==================================================================
    // 4. Regression cover for the occurrence lookup: #1156 / PR #1158, and the
    //    ownership guard added underneath it for #1218.
    //
    // PR #1158 shipped with no test that changes outcome on revert; the rest of this
    // fixture passes either way, because no other fixture seeds two PlanningCaseSites
    // for one planning sharing a CreatedAt day. These tests close that gap, and they
    // pin three DIFFERENT things:
    //
    //   * the two *_TwoBackfilledOccurrencesOnOneDay_* tests pin the PREDICATE — revert
    //     it from  x.MicrotingSdkCaseId == foundCase.Id  back to
    //     x.CreatedAt.Date == compliance.StartDate.Date && x.PlanningId == compliance.PlanningId
    //     and they fail on the second occurrence;
    //   * the two *_NoPlanningCaseSiteForSdkCase_* tests pin the rejection the old code
    //     had no branch for at all (it fell through and returned success). They do NOT
    //     discriminate on the predicate: with no PlanningCaseSite at all, both predicates
    //     find nothing. Their second half is #1157 cover — see below.
    //   * the two *_MismatchedCaseAndCompliance_* tests pin the #1218 guard that sits
    //     AFTER the lookup — the PlanningId cross-check that #1158 dropped along with
    //     the old predicate, re-added without touching the selector.
    //
    // Every *_MutatesNothing test in this section additionally pins #1157: since the
    // lookup and the guard moved into the pre-flight, above the first irreversible write,
    // a rejected request leaves neither a soft-deleted Compliance nor a completed SDK
    // case. Those assertions were inverted from what they pinned before; the two
    // *_RetryAgainstSoftDeletedCompliance_* tests at the end of the section cover the
    // WorkflowState filter that makes a retry fail fast instead of re-running.
    //
    // The bug is not hypothetical: it is the "all occurrences green in the calendar,
    // only one row in Logbøger" report. The calendar reads sdkCase.Status, which every
    // completion writes, while Logbøger filters PlanningCases.Status == 100, which only
    // the first completion ever reached.
    // ==================================================================

    /// <summary>
    /// THE regression test for issue #1156. Two occurrences of ONE planning whose
    /// <c>PlanningCaseSite.CreatedAt</c> and <c>Compliance.StartDate</c> all collapse
    /// onto today — exactly what a back-filled past series looks like, because every
    /// row of it is inserted on the day the backfill runs. The old date heuristic has
    /// no per-occurrence key in that shape: it matched BOTH siblings for BOTH
    /// completions and <c>FirstOrDefaultAsync</c> handed back the same row twice, after
    /// which <c>if (planningCase.Status != 100)</c> silently no-op'd everything from the
    /// second completion onward.
    ///
    /// <para>What discriminates: the assertions on the SECOND occurrence. Revert the
    /// predicate and <c>planningCaseSiteB.Status</c> / <c>planningCaseB.Status</c> stay at
    /// 66 and <c>planningCaseSiteB.MicrotingSdkCaseDoneAt</c> stays null, because the
    /// second call re-resolved to sibling A. The first occurrence is asserted too, and
    /// with its OWN DoneAt, so that neither "always take the last row" nor an
    /// implementation that lets completion #2 overwrite sibling A can pass.</para>
    ///
    /// <para>The two SDK cases carry distinct MicrotingUids purely so each keeps its
    /// own decoy pair (see the class remarks) and both retractions still fail offline.</para>
    /// </summary>
    [Test]
    public async Task Update_TwoBackfilledOccurrencesOnOneDay_PromotesBothPlanningCases()
    {
        var s = await SeedScenarioAsync("update-backfill-pair");

        // Two occurrences of the SAME planning, each with its own SDK case.
        var sdkCaseA = await SeedSdkCaseAsync(s, 970_006);
        var sdkCaseB = await SeedSdkCaseAsync(s, 970_007);
        var planningCaseA = await SeedPlanningCaseAsync(s);
        var planningCaseB = await SeedPlanningCaseAsync(s);

        // Both PlanningCaseSites keep the helper's default CreatedAt = UtcNow — the
        // backfill collapse. Each points at its OWN SDK case, which is the only field
        // that tells the two occurrences apart.
        var planningCaseSiteA = await SeedPlanningCaseSiteAsync(s, planningCaseA, sdkCaseA);
        var planningCaseSiteB = await SeedPlanningCaseSiteAsync(s, planningCaseB, sdkCaseB);

        // Same StartDate (what the backfill writes), different Deadline (forced by the
        // UNIQUE index on (PlanningId, Deadline)).
        var complianceA = await SeedComplianceAsync(s, planningCaseA, sdkCaseA,
            DateTime.UtcNow.Date.AddDays(-2));
        var complianceB = await SeedComplianceAsync(s, planningCaseB, sdkCaseB,
            DateTime.UtcNow.Date.AddDays(-1));

        // Distinct times of day, so an overwrite of sibling A by completion B is visible.
        var doneAtA = new DateTime(2026, 3, 17, 8, 5, 0, DateTimeKind.Unspecified);
        var doneAtB = new DateTime(2026, 3, 18, 16, 45, 0, DateTimeKind.Unspecified);

        var service = MakeCompliancesService(s);

        var resultA = await service.Update(MakeReply(s, complianceA.Id, sdkCaseA.Id, doneAtA));
        var resultB = await service.Update(MakeReply(s, complianceB.Id, sdkCaseB.Id, doneAtB));

        var reloadedSiteA = await ReadPlanningCaseSiteAsync(planningCaseSiteA.Id);
        var reloadedSiteB = await ReadPlanningCaseSiteAsync(planningCaseSiteB.Id);
        var reloadedCaseA = await ReadPlanningCaseAsync(planningCaseA.Id);
        var reloadedCaseB = await ReadPlanningCaseAsync(planningCaseB.Id);

        Assert.Multiple(() =>
        {
            Assert.That(resultA.Success, Is.True, resultA.Message);
            Assert.That(resultB.Success, Is.True, resultB.Message);

            // The second completion — this is what regressed.
            Assert.That(reloadedCaseB.Status, Is.EqualTo(CompletedStatus),
                "occurrence B's PlanningCase must reach 100; the date heuristic resolved to sibling A, "
                + "whose PlanningCase was already 100, so the `planningCase.Status != 100` guard skipped it "
                + "and Logbøger never saw the second completion");
            Assert.That(reloadedSiteB.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedSiteB.MicrotingSdkCaseDoneAt, Is.EqualTo(doneAtB));
            Assert.That(reloadedSiteB.MicrotingSdkCaseDoneAt, Is.Not.Null,
                "a completed occurrence must carry a DoneAt");
            Assert.That(reloadedSiteB.MicrotingSdkCaseId, Is.EqualTo(sdkCaseB.Id));
            Assert.That(reloadedCaseB.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Processed));

            // The first completion is still intact and was NOT overwritten by the
            // second — this half is what stops "always take the last row" passing.
            Assert.That(reloadedSiteA.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedSiteA.MicrotingSdkCaseId, Is.EqualTo(sdkCaseA.Id),
                "completion B must not repoint occurrence A at case B");
            Assert.That(reloadedSiteA.MicrotingSdkCaseDoneAt, Is.EqualTo(doneAtA),
                "occurrence A keeps its own DoneAt");
            Assert.That(reloadedCaseA.Status, Is.EqualTo(CompletedStatus));
        });
    }

    /// <summary>
    /// Byte-identical twin of <see cref="Update_TwoBackfilledOccurrencesOnOneDay_PromotesBothPlanningCases"/>
    /// against <c>UpdateFromCalendar</c>. The two production methods are copy-paste of
    /// each other — the occurrence lookup is duplicated verbatim — so a fix (or a
    /// revert) applied to only one of them is a live regression that a single test
    /// cannot catch. Pinned separately on purpose.
    ///
    /// <para>The calendar variant is also the path the customer actually hit: the
    /// calendar completes occurrences through <c>UpdateFromCalendar</c>, then renders
    /// them from <c>sdkCase.Status</c> (all green) while Logbøger reads
    /// <c>PlanningCases.Status</c> (one row).</para>
    /// </summary>
    [Test]
    public async Task UpdateFromCalendar_TwoBackfilledOccurrencesOnOneDay_PromotesBothPlanningCases()
    {
        var s = await SeedScenarioAsync("calendar-backfill-pair");

        var sdkCaseA = await SeedSdkCaseAsync(s, 970_008);
        var sdkCaseB = await SeedSdkCaseAsync(s, 970_009);
        var planningCaseA = await SeedPlanningCaseAsync(s);
        var planningCaseB = await SeedPlanningCaseAsync(s);

        var planningCaseSiteA = await SeedPlanningCaseSiteAsync(s, planningCaseA, sdkCaseA);
        var planningCaseSiteB = await SeedPlanningCaseSiteAsync(s, planningCaseB, sdkCaseB);

        var complianceA = await SeedComplianceAsync(s, planningCaseA, sdkCaseA,
            DateTime.UtcNow.Date.AddDays(-2));
        var complianceB = await SeedComplianceAsync(s, planningCaseB, sdkCaseB,
            DateTime.UtcNow.Date.AddDays(-1));

        var doneAtA = new DateTime(2026, 3, 17, 8, 5, 0, DateTimeKind.Unspecified);
        var doneAtB = new DateTime(2026, 3, 18, 16, 45, 0, DateTimeKind.Unspecified);

        var service = MakeCompliancesService(s);

        var resultA = await service.UpdateFromCalendar(MakeReply(s, complianceA.Id, sdkCaseA.Id, doneAtA));
        var resultB = await service.UpdateFromCalendar(MakeReply(s, complianceB.Id, sdkCaseB.Id, doneAtB));

        var reloadedSiteA = await ReadPlanningCaseSiteAsync(planningCaseSiteA.Id);
        var reloadedSiteB = await ReadPlanningCaseSiteAsync(planningCaseSiteB.Id);
        var reloadedCaseA = await ReadPlanningCaseAsync(planningCaseA.Id);
        var reloadedCaseB = await ReadPlanningCaseAsync(planningCaseB.Id);

        Assert.Multiple(() =>
        {
            Assert.That(resultA.Success, Is.True, resultA.Message);
            Assert.That(resultB.Success, Is.True, resultB.Message);

            Assert.That(reloadedCaseB.Status, Is.EqualTo(CompletedStatus),
                "UpdateFromCalendar carries its own copy of the occurrence lookup and must be "
                + "pinned independently of Update");
            Assert.That(reloadedSiteB.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedSiteB.MicrotingSdkCaseDoneAt, Is.EqualTo(doneAtB));
            Assert.That(reloadedSiteB.MicrotingSdkCaseDoneAt, Is.Not.Null);
            Assert.That(reloadedSiteB.MicrotingSdkCaseId, Is.EqualTo(sdkCaseB.Id));
            Assert.That(reloadedCaseB.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Processed));

            Assert.That(reloadedSiteA.Status, Is.EqualTo(CompletedStatus));
            Assert.That(reloadedSiteA.MicrotingSdkCaseId, Is.EqualTo(sdkCaseA.Id));
            Assert.That(reloadedSiteA.MicrotingSdkCaseDoneAt, Is.EqualTo(doneAtA));
            Assert.That(reloadedCaseA.Status, Is.EqualTo(CompletedStatus));
        });
    }

    /// <summary>
    /// Covers the rejection PR #1158 added underneath the new lookup (an <c>else</c>
    /// branch then; the pre-flight's <c>planningCaseSite == null</c> early return since
    /// #1157): when the SDK case EXISTS but no <c>PlanningCaseSite</c> references it, the
    /// caller gets <c>OperationResult(false, "CaseNotFound")</c>. The old code had neither
    /// — it fell through silently, recomputed the property and returned
    /// <c>CaseHasBeenUpdated</c> over an occurrence it had never completed.
    ///
    /// <para>Distinct from <see cref="Update_CaseNotFound_MutatesNothing"/>, which trips
    /// the OTHER <c>CaseNotFound</c> — the one where the SDK case itself is missing. Here
    /// the case is real; only the items-planning half is missing.</para>
    ///
    /// <para><b>#1157 inverted the second half of this test.</b> It used to pin a genuine
    /// partial write, "as-is rather than as it ought to be": <c>compliance.Delete()</c>
    /// and the SDK-case completion both ran long before this lookup, so the user got a
    /// failure toast over an occurrence already gone from the calendar AND a case already
    /// at 100. The lookup now runs in the pre-flight, above the first write, so the
    /// rejection is clean.</para>
    ///
    /// <para>What discriminates: <c>reloadedCompliance.WorkflowState</c> is <c>created</c>
    /// and <c>reloadedCase.Status</c> is still 33. Pre-fix they are <c>removed</c> and
    /// 100.</para>
    /// </summary>
    [Test]
    public async Task Update_NoPlanningCaseSiteForSdkCase_ReturnsCaseNotFoundAndMutatesNothing()
    {
        var s = await SeedScenarioAsync("update-no-pcs");
        var sdkCase = await SeedSdkCaseAsync(s, 970_010);
        var planningCase = await SeedPlanningCaseAsync(s);
        // Deliberately NO SeedPlanningCaseSiteAsync — nothing references this SDK case.
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        var result = await MakeCompliancesService(s)
            .Update(MakeReply(s, compliance.Id, sdkCase.Id, new DateTime(2026, 3, 17, 11, 20, 0)));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "an unresolvable occurrence must not be reported as completed");
            Assert.That(result.Message, Is.EqualTo("CaseNotFound"));

            // #1157: the rejection is clean — no partial write is left behind.
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "#1157: the PlanningCaseSite lookup now runs in the pre-flight, so the rejection "
                + "happens before compliance.Delete() (pre-fix: removed)");
            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus),
                "#1157: a request that is then rejected must not complete the SDK case "
                + "(pre-fix: 100)");
            Assert.That(reloadedCase.DoneAt, Is.Null);

            // Everything after the early return did not run.
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus),
                "the Property recompute sits after the early return");
        });
    }

    /// <summary>
    /// Twin of
    /// <see cref="Update_NoPlanningCaseSiteForSdkCase_ReturnsCaseNotFoundAndMutatesNothing"/>
    /// against <c>UpdateFromCalendar</c>, and the branch's FIRST coverage on that method
    /// — #1157 noted it had none, only the missing-SDK-case one. The two production
    /// methods are verbatim copy-paste of each other, including this lookup and the
    /// pre-flight it now sits in, so a fix applied to only one of them is a live hole a
    /// single test cannot see. The calendar variant is also the reachable one.
    ///
    /// <para>What discriminates: <c>reloadedCompliance.WorkflowState</c> is <c>created</c>
    /// and <c>reloadedCase.Status</c> is still 33. Pre-fix they are <c>removed</c> and
    /// 100. Reverting only <c>Update</c>'s pre-flight leaves this test green and its
    /// sibling red, which is the point of pinning both.</para>
    /// </summary>
    [Test]
    public async Task UpdateFromCalendar_NoPlanningCaseSiteForSdkCase_ReturnsCaseNotFoundAndMutatesNothing()
    {
        var s = await SeedScenarioAsync("calendar-no-pcs");
        var sdkCase = await SeedSdkCaseAsync(s, 970_014);
        var planningCase = await SeedPlanningCaseAsync(s);
        // Deliberately NO SeedPlanningCaseSiteAsync — nothing references this SDK case.
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        var result = await MakeCompliancesService(s)
            .UpdateFromCalendar(MakeReply(s, compliance.Id, sdkCase.Id, new DateTime(2026, 3, 17, 11, 20, 0)));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "an unresolvable occurrence must not be reported as completed");
            Assert.That(result.Message, Is.EqualTo("CaseNotFound"));

            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "#1157: UpdateFromCalendar carries its own copy of the pre-flight (pre-fix: removed)");
            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus),
                "#1157: a request that is then rejected must not complete the SDK case (pre-fix: 100)");
            Assert.That(reloadedCase.DoneAt, Is.Null);

            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus),
                "the Property recompute sits after the early return");
        });
    }

    /// <summary>
    /// A SECOND property + planning, sharing the owner scenario's core, SDK site,
    /// checklist and language. Built inline rather than through
    /// <see cref="SeedScenarioAsync"/> on purpose: that helper calls <c>GetCore()</c>,
    /// and this fixture must start exactly one eFormCore. Everything the seed helpers
    /// read off a <see cref="Scenario"/> is PlanningId/PropertyId, and the mismatch
    /// under test is compliance-vs-case, not site-vs-site.
    /// </summary>
    private async Task<Scenario> SeedStrangerScenarioAsync(Scenario owner, string tag)
    {
        var strangerProperty = new Property
        {
            Name = $"ComplianceLegacy-{tag}-stranger-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            ComplianceStatus = OverdueComplianceStatus,
            ComplianceStatusThirty = OverdueComplianceStatus,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(strangerProperty);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var strangerPlanning = new Planning
        {
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = DateTime.UtcNow.Date.AddDays(-14),
            RelatedEFormId = owner.CheckListId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(strangerPlanning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return new Scenario
        {
            CoreHelper = owner.CoreHelper,
            Property = strangerProperty,
            Planning = strangerPlanning,
            Language = owner.Language,
            Site = owner.Site,
            CheckListId = owner.CheckListId
        };
    }

    /// <summary>
    /// Regression test for issue #1218 — was CHARACTERISATION until the guard landed,
    /// and is now inverted, exactly as its previous doc comment said it should be.
    ///
    /// <para>PR #1158 rewrote the occurrence lookup and dropped the
    /// <c>PlanningId == compliance.PlanningId</c> term that used to travel with it.
    /// <c>foundCase</c> comes from <c>model.Id</c>, <c>compliance</c> from
    /// <c>model.ExtraId</c>; both are client-supplied and nothing else pairs them, so a
    /// caller quoting one property's Compliance id together with ANOTHER property's SDK
    /// case id got the unrelated property's <c>PlanningCaseSite</c>/<c>PlanningCase</c>
    /// promoted to 100 — and a success result. The #1218 guard sits AFTER the lookup, so
    /// <c>MicrotingSdkCaseId</c> remains its sole selector (the #1158 fix is untouched),
    /// and rejects the pair with <c>CaseDoesNotBelongToCompliance</c>.</para>
    ///
    /// <para><b>The rejection is now clean, and this test pins that too.</b> When the
    /// guard shipped, <c>compliance.Delete()</c> and the SDK-case completion both ran
    /// BEFORE the lookup it follows, so a rejected request still left the quoted
    /// Compliance soft-deleted and its SDK case at Status 100 — and this test asserted
    /// exactly that, labelled <c>#1157</c>. #1157 has since moved the lookup and the guard
    /// into the pre-flight, above the first irreversible write, so those two assertions
    /// are inverted here. The guard's own purpose is unchanged: no items-planning row of
    /// an unrelated planning is promoted, and no property's counters are recomputed.</para>
    ///
    /// <para>What discriminates for #1157: <c>reloadedStrangerCompliance.WorkflowState</c>
    /// is <c>created</c> and <c>reloadedSdkCase.Status</c> is still 33. Pre-#1157 they are
    /// <c>removed</c> and 100. What discriminates for #1218 is unchanged: remove the
    /// PlanningId cross-check and the owner planning's rows get promoted.</para>
    /// </summary>
    [Test]
    public async Task Update_MismatchedCaseAndCompliance_IsRejectedAndMutatesNothing()
    {
        // Property/planning 1 owns the SDK case and its occurrence...
        var owner = await SeedScenarioAsync("mismatch-owner");
        var sdkCase = await SeedSdkCaseAsync(owner, 970_011);
        var ownerPlanningCase = await SeedPlanningCaseAsync(owner);
        var ownerPlanningCaseSite = await SeedPlanningCaseSiteAsync(owner, ownerPlanningCase, sdkCase);

        // ...a SECOND property/planning owns the Compliance the caller quotes.
        var stranger = await SeedStrangerScenarioAsync(owner, "mismatch");
        var strangerPlanningCase = await SeedPlanningCaseAsync(stranger);
        var strangerCompliance = await SeedComplianceAsync(stranger, strangerPlanningCase, sdkCase);

        var doneAt = new DateTime(2026, 3, 19, 13, 0, 0, DateTimeKind.Unspecified);

        // ExtraId (compliance) and Id (SDK case) belong to different properties.
        var result = await MakeCompliancesService(owner)
            .Update(MakeReply(stranger, strangerCompliance.Id, sdkCase.Id, doneAt));

        var reloadedOwnerSite = await ReadPlanningCaseSiteAsync(ownerPlanningCaseSite.Id);
        var reloadedOwnerCase = await ReadPlanningCaseAsync(ownerPlanningCase.Id);
        var reloadedStrangerCase = await ReadPlanningCaseAsync(strangerPlanningCase.Id);
        var reloadedStrangerCompliance = await ReadComplianceAsync(strangerCompliance.Id);
        var reloadedStrangerProperty = await ReadPropertyAsync(stranger.Property.Id);
        var reloadedSdkCase = await ReadCaseAsync(sdkCase.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "a case id paired with a compliance id from a different planning must be rejected");
            Assert.That(result.Message, Is.EqualTo("CaseDoesNotBelongToCompliance"));

            // The point of the guard: the unrelated planning's occurrence is untouched.
            Assert.That(reloadedOwnerSite.Status, Is.EqualTo(OpenPlanningStatus),
                "the owner planning's PlanningCaseSite must NOT be promoted by a completion "
                + "that quoted another planning's compliance");
            Assert.That(reloadedOwnerSite.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(reloadedOwnerSite.DoneByUserId, Is.EqualTo(0));
            Assert.That(reloadedOwnerCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedOwnerCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

            // The quoted compliance's own occurrence is not completed either — the
            // request is rejected outright, it is not re-routed.
            Assert.That(reloadedStrangerCase.Status, Is.EqualTo(OpenPlanningStatus));

            // #1157: the rejection leaves no partial write behind — both of these used to
            // run before the lookup the guard follows, and now run after it.
            Assert.That(reloadedStrangerCompliance.WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Created),
                "#1157: the guard now runs in the pre-flight, above compliance.Delete() "
                + "(pre-fix: removed)");
            Assert.That(reloadedSdkCase.Status, Is.EqualTo(OpenCaseStatus),
                "#1157: a rejected pair must not complete the SDK case (pre-fix: 100)");
            Assert.That(reloadedSdkCase.DoneAt, Is.Null);

            // Everything after the early return did not run.
            Assert.That(reloadedStrangerProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus),
                "the Property recompute sits after the early return");
            Assert.That(reloadedStrangerProperty.ComplianceStatusThirty, Is.EqualTo(OverdueComplianceStatus));
        });
    }

    /// <summary>
    /// Twin of <see cref="Update_MismatchedCaseAndCompliance_IsRejectedAndMutatesNothing"/>
    /// against <c>UpdateFromCalendar</c>. The two production methods are verbatim
    /// copy-paste of each other — including this lookup and the #1218 guard beneath it —
    /// so a guard added to only one of them is a live hole that a single test cannot
    /// see. Pinned separately on purpose, same as the two
    /// <c>*_TwoBackfilledOccurrencesOnOneDay_*</c> tests above.
    ///
    /// <para>The calendar variant is also the reachable one: the calendar completes
    /// occurrences through <c>UpdateFromCalendar</c>.</para>
    /// </summary>
    [Test]
    public async Task UpdateFromCalendar_MismatchedCaseAndCompliance_IsRejectedAndMutatesNothing()
    {
        var owner = await SeedScenarioAsync("calendar-mismatch-owner");
        var sdkCase = await SeedSdkCaseAsync(owner, 970_012);
        var ownerPlanningCase = await SeedPlanningCaseAsync(owner);
        var ownerPlanningCaseSite = await SeedPlanningCaseSiteAsync(owner, ownerPlanningCase, sdkCase);

        var stranger = await SeedStrangerScenarioAsync(owner, "calendar-mismatch");
        var strangerPlanningCase = await SeedPlanningCaseAsync(stranger);
        var strangerCompliance = await SeedComplianceAsync(stranger, strangerPlanningCase, sdkCase);

        var doneAt = new DateTime(2026, 3, 19, 13, 0, 0, DateTimeKind.Unspecified);

        var result = await MakeCompliancesService(owner)
            .UpdateFromCalendar(MakeReply(stranger, strangerCompliance.Id, sdkCase.Id, doneAt));

        var reloadedOwnerSite = await ReadPlanningCaseSiteAsync(ownerPlanningCaseSite.Id);
        var reloadedOwnerCase = await ReadPlanningCaseAsync(ownerPlanningCase.Id);
        var reloadedStrangerCase = await ReadPlanningCaseAsync(strangerPlanningCase.Id);
        var reloadedStrangerCompliance = await ReadComplianceAsync(strangerCompliance.Id);
        var reloadedStrangerProperty = await ReadPropertyAsync(stranger.Property.Id);
        var reloadedSdkCase = await ReadCaseAsync(sdkCase.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "UpdateFromCalendar carries its own copy of the guard and must reject the pair too");
            Assert.That(result.Message, Is.EqualTo("CaseDoesNotBelongToCompliance"));

            Assert.That(reloadedOwnerSite.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedOwnerSite.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(reloadedOwnerSite.DoneByUserId, Is.EqualTo(0));
            Assert.That(reloadedOwnerCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedOwnerCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

            Assert.That(reloadedStrangerCase.Status, Is.EqualTo(OpenPlanningStatus));

            // Same clean rejection (#1157) as the Update twin — pre-fix these were
            // removed / 100.
            Assert.That(reloadedStrangerCompliance.WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Created),
                "#1157: UpdateFromCalendar carries its own copy of the pre-flight");
            Assert.That(reloadedSdkCase.Status, Is.EqualTo(OpenCaseStatus));
            Assert.That(reloadedSdkCase.DoneAt, Is.Null);

            Assert.That(reloadedStrangerProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus));
            Assert.That(reloadedStrangerProperty.ComplianceStatusThirty, Is.EqualTo(OverdueComplianceStatus));
        });
    }

    /// <summary>
    /// #1157 option 3, the retry path. Before the fix the Compliance lookup was
    /// <c>SingleOrDefaultAsync(x =&gt; x.Id == model.ExtraId)</c> with NO WorkflowState
    /// filter, so a retry after an earlier partially-failed completion re-found the
    /// already-soft-deleted row, re-ran the entire cascade against it — promoting the
    /// items-planning rows and completing the SDK case for an occurrence that no longer
    /// exists — and reported <c>CaseHasBeenUpdated</c>. There was no path back to a
    /// consistent state from the UI. The filter makes the retry fail fast and
    /// distinguishably instead.
    ///
    /// <para>The already-removed row is produced with the same <c>PnBase.Delete</c> the
    /// production paths call, so this is the state a real earlier completion leaves, not a
    /// hand-forged one.</para>
    ///
    /// <para>What discriminates: <c>result.Success</c> is false and
    /// <c>reloadedPlanningCaseSite.Status</c> is still 66. On pre-fix code the call
    /// succeeds and promotes both items-planning rows to 100.</para>
    /// </summary>
    [Test]
    public async Task Update_RetryAgainstSoftDeletedCompliance_IsRejectedAndMutatesNothing()
    {
        var s = await SeedScenarioAsync("update-retry-removed");
        var sdkCase = await SeedSdkCaseAsync(s, 970_015);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);
        await SoftDeleteComplianceAsync(compliance);

        var result = await MakeCompliancesService(s)
            .Update(MakeReply(s, compliance.Id, sdkCase.Id, new DateTime(2026, 3, 20, 10, 0, 0)));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "#1157: a completion quoting an already-soft-deleted Compliance must be rejected, "
                + "not re-run (pre-fix: success)");
            Assert.That(result.Message, Is.EqualTo("CaseCouldNotBeUpdated"),
                "the compliance-missing exit reuses CaseCouldNotBeUpdated, which is what "
                + "distinguishes it from the CaseNotFound exits");

            // The row stays exactly as the earlier completion left it.
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

            // Nothing else was touched.
            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus),
                "#1157: the retry must not complete the SDK case (pre-fix: 100)");
            Assert.That(reloadedCase.DoneAt, Is.Null);
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(OpenPlanningStatus),
                "#1157: the retry must not promote the occurrence (pre-fix: 100)");
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus));
        });
    }

    /// <summary>
    /// Twin of
    /// <see cref="Update_RetryAgainstSoftDeletedCompliance_IsRejectedAndMutatesNothing"/>
    /// against <c>UpdateFromCalendar</c> — the WorkflowState filter is duplicated verbatim
    /// in the calendar copy, so it needs its own pin. Same discriminator.
    /// </summary>
    [Test]
    public async Task UpdateFromCalendar_RetryAgainstSoftDeletedCompliance_IsRejectedAndMutatesNothing()
    {
        var s = await SeedScenarioAsync("calendar-retry-removed");
        var sdkCase = await SeedSdkCaseAsync(s, 970_016);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);
        await SoftDeleteComplianceAsync(compliance);

        var result = await MakeCompliancesService(s)
            .UpdateFromCalendar(MakeReply(s, compliance.Id, sdkCase.Id, new DateTime(2026, 3, 20, 10, 0, 0)));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "#1157: UpdateFromCalendar carries its own copy of the WorkflowState filter "
                + "(pre-fix: success)");
            Assert.That(result.Message, Is.EqualTo("CaseCouldNotBeUpdated"));

            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus));
            Assert.That(reloadedCase.DoneAt, Is.Null);
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus));
        });
    }

    /// <summary>
    /// #1157, the last routine partial write: a completion whose <c>SiteId</c> matches no
    /// live SDK <c>Sites</c> row must mutate NOTHING.
    ///
    /// <para><c>model.SiteId</c> is client-supplied and nothing else validates it. The
    /// site used to be resolved INSIDE the mutation block and dereferenced without a null
    /// guard at <c>planningCaseSite.DoneByUserName = site.Name</c> — i.e. after
    /// <c>compliance.Delete</c>, <c>core.CaseUpdate</c> and <c>foundCase.Update</c> had
    /// all committed. It is now resolved in the pre-flight, above the first write.</para>
    ///
    /// <para><b>What fails pre-fix.</b> Not an escaping exception: the
    /// <c>NullReferenceException</c> is caught by the method's own outer
    /// <c>catch (Exception)</c>, which returns
    /// <c>OperationResult(false, "CaseCouldNotBeUpdated Exception: ...")</c>. So pre-fix
    /// this test fails on ASSERTIONS — <c>result.Message</c> (which is that concatenated
    /// exception string, not <c>SiteNotFound</c>), <c>reloadedCompliance.WorkflowState</c>
    /// (<c>removed</c>) and <c>reloadedCase.Status</c> (100). The <c>Success</c>
    /// assertion is the one thing that passes either way, which is exactly why the
    /// partial write went unnoticed.</para>
    /// </summary>
    [Test]
    public async Task Update_UnknownSiteId_MutatesNothing()
    {
        // Sites.Id is auto-increment from 1, so int.MaxValue is unreachable for this
        // database. Deliberately NOT "max(Id) + n": other fixtures insert sites
        // concurrently (ParallelScope.Fixtures, and TestBaseSetup does not reset the
        // database per test), which would make that racy.
        const int nonExistentSiteId = int.MaxValue;

        var s = await SeedScenarioAsync("update-unknown-site");
        var sdkCase = await SeedSdkCaseAsync(s, 970_017);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        var result = await MakeCompliancesService(s)
            .Update(MakeReply(s, compliance.Id, sdkCase.Id, new DateTime(2026, 3, 24, 9, 15, 0),
                nonExistentSiteId));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "a completion naming a site that does not exist must not be reported as completed");
            Assert.That(result.Message, Is.EqualTo("SiteNotFound"),
                "#1157: the site is now resolved in the pre-flight and rejected by its own message "
                + "(pre-fix: \"CaseCouldNotBeUpdated Exception: <NullReferenceException>\", produced "
                + "by the outer catch AFTER the writes had committed)");

            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "#1157: the rejection happens before compliance.Delete() (pre-fix: removed)");
            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus),
                "#1157: a rejected request must not complete the SDK case (pre-fix: 100)");
            Assert.That(reloadedCase.DoneAt, Is.Null);
            Assert.That(reloadedCase.SiteId, Is.EqualTo(s.Site.Id),
                "the SDK case must keep its own site, not the unknown one from the request");

            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus),
                "the Property recompute sits after the early return");
        });
    }

    /// <summary>
    /// Twin of <see cref="Update_UnknownSiteId_MutatesNothing"/> against
    /// <c>UpdateFromCalendar</c> — the site lookup and its guard are duplicated verbatim
    /// in the calendar copy, and the calendar path is the reachable one, so reverting the
    /// hoist in only one method must leave exactly one of these two red. Same
    /// discriminators, same reason they are assertion failures rather than a thrown
    /// exception (the outer catch swallows the NRE either way).
    /// </summary>
    [Test]
    public async Task UpdateFromCalendar_UnknownSiteId_MutatesNothing()
    {
        const int nonExistentSiteId = int.MaxValue;

        var s = await SeedScenarioAsync("calendar-unknown-site");
        var sdkCase = await SeedSdkCaseAsync(s, 970_018);
        var planningCase = await SeedPlanningCaseAsync(s);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, sdkCase);
        var compliance = await SeedComplianceAsync(s, planningCase, sdkCase);

        var result = await MakeCompliancesService(s)
            .UpdateFromCalendar(MakeReply(s, compliance.Id, sdkCase.Id,
                new DateTime(2026, 3, 24, 9, 15, 0), nonExistentSiteId));

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedCase = await ReadCaseAsync(sdkCase.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var reloadedProperty = await ReadPropertyAsync(s.Property.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "a completion naming a site that does not exist must not be reported as completed");
            Assert.That(result.Message, Is.EqualTo("SiteNotFound"),
                "#1157: UpdateFromCalendar carries its own copy of the hoisted site lookup "
                + "(pre-fix: \"CaseCouldNotBeUpdated Exception: <NullReferenceException>\")");

            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "#1157: the rejection happens before compliance.Delete() (pre-fix: removed)");
            Assert.That(reloadedCase.Status, Is.EqualTo(OpenCaseStatus),
                "#1157: a rejected request must not complete the SDK case (pre-fix: 100)");
            Assert.That(reloadedCase.DoneAt, Is.Null);
            Assert.That(reloadedCase.SiteId, Is.EqualTo(s.Site.Id));

            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(OpenPlanningStatus));
            Assert.That(reloadedProperty.ComplianceStatus, Is.EqualTo(OverdueComplianceStatus),
                "the Property recompute sits after the early return");
        });
    }
}

/// <summary>
/// The DB-free half of the #1218 cover: it checks that
/// <c>CaseDoesNotBelongToCompliance</c> — the failure message the ownership guard in
/// <c>BackendConfigurationCompliancesService.Update</c> /
/// <c>UpdateFromCalendar</c> returns — actually EXISTS in the plugin's embedded
/// <c>Resources/localization.json</c>, in every locale the file ships.
///
/// <para>
/// <b>Why this exists.</b> The two sibling tests above assert
/// <c>result.Message == "CaseDoesNotBelongToCompliance"</c>, and that assertion cannot
/// fail for a missing translation: the integration project's
/// <c>BackendConfigurationLocalizationService</c> stub (in
/// <c>BackendConfigurationAssignmentWorkerServiceHelperTest.cs</c>) simply ECHOES the
/// key it is given. Production uses <c>JsonStringLocalizer</c>, which also echoes the
/// key when the entry is absent or its value for the current culture is blank — it does
/// not fall back to English and it does not throw. So the sibling assertions would pass
/// unchanged if the JSON entry had never been added, and every customer would be shown
/// the bare identifier <c>CaseDoesNotBelongToCompliance</c> in the failure toast. This
/// fixture reads the real file and is the only thing in CI that notices.
/// </para>
///
/// <para>
/// <b>No database and no container, hence a separate fixture.</b>
/// <see cref="ComplianceCompletionLegacyPathsTests"/> derives from <c>TestBaseSetup</c>,
/// which starts a MariaDB testcontainer and replays six SQL dumps per fixture; there is
/// nothing here to seed, and the check must stay runnable (and fast) independently of
/// all that. Same shape as <c>ExportLocalizationCompletenessTests</c>, the project's
/// other resource-file fixture: no base class, <c>ParallelScope.All</c>.
/// </para>
///
/// <para>
/// <b>Stated gap.</b> Presence is not correctness — a locale whose value is a copy of
/// the English one passes here, and nothing automated can tell those apart. The exact
/// translated strings are editorial and are deliberately NOT asserted.
/// </para>
///
/// <para>
/// <b>Also covers <c>SiteNotFound</c></b>, the message #1157's hoisted site lookup
/// returns. Same shape of exposure, same blind spot in the sibling tests, so it is
/// checked here rather than in a fixture of its own; the class name predates it.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ComplianceOwnershipGuardLocalizationTests
{
    /// <summary>
    /// The key both <c>GetString</c> call sites of the #1218 guard pass
    /// (BackendConfigurationCompliancesService.cs:293 and :599).
    /// </summary>
    private const string GuardKey = "CaseDoesNotBelongToCompliance";

    /// <summary>
    /// The message the hoisted site lookup returns when <c>model.SiteId</c> matches no
    /// live SDK <c>Sites</c> row (#1157 — BackendConfigurationCompliancesService.cs, both
    /// methods' pre-flight). It has exactly the same exposure as <see cref="GuardKey"/>:
    /// the <c>*_UnknownSiteId_MutatesNothing</c> tests assert the raw key against a stub
    /// that echoes keys, so nothing but this fixture notices a missing JSON entry.
    /// </summary>
    private const string SiteKey = "SiteNotFound";

    /// <summary>
    /// The entry whose locale set defines "every locale the plugin ships". Derived from
    /// a reference key rather than hard-coded as 26, so adding a language platform-wide
    /// stays a JSON-only change. The plugin's own display name is the safest reference
    /// available — <c>EformBackendConfigurationPlugin.GetNavigationMenu</c> reads it to
    /// label the plugin in the sidebar, so it is the one key that must exist in every
    /// locale for the plugin to be usable at all. Same reference
    /// <c>ExportLocalizationCompletenessTests</c> uses, on purpose: one definition of
    /// "the shipped locale set" across both resource fixtures.
    /// </summary>
    private const string ReferenceKey = "BackendConfiguration";

    /// <summary>
    /// <b>The assertion this fixture exists for.</b> <see cref="GuardKey"/> is present in
    /// <c>Resources/localization.json</c>, carries exactly the locale set
    /// <see cref="ReferenceKey"/> carries, and has a non-blank value in each of them —
    /// resolved the way <c>JsonStringLocalizer.GetString</c> resolves it (first entry
    /// carrying the culture, then the key match, then the empty check; the file contains
    /// duplicate keys, so those two rules are not the same rule).
    ///
    /// <para>The reference entry is sanity-checked first, so the test cannot pass
    /// vacuously if that entry is ever reduced to a single locale.</para>
    ///
    /// <para>Every failure message names the offending locale(s), so a regression is a
    /// one-line fix in the JSON rather than a debugging session.</para>
    /// </summary>
    [Test]
    public void GuardKeyResolvesInEveryShippedLocale() =>
        AssertKeyResolvesInEveryShippedLocale(GuardKey,
            "The #1218 guard returns _localizationService.GetString(\"" + GuardKey + "\"). The "
            + "sibling *_MismatchedCaseAndCompliance_* tests cannot catch a missing entry: the "
            + "test project's localisation stub echoes keys, so they pass either way.");

    /// <summary>
    /// The same assertion for <see cref="SiteKey"/> — the #1157 site pre-flight's message.
    /// A separate <c>[Test]</c> rather than a second key inside the one above, so a
    /// failure names which key regressed without the other masking it.
    /// </summary>
    [Test]
    public void SiteNotFoundKeyResolvesInEveryShippedLocale() =>
        AssertKeyResolvesInEveryShippedLocale(SiteKey,
            "The #1157 site pre-flight returns _localizationService.GetString(\"" + SiteKey + "\") "
            + "from both Update and UpdateFromCalendar. The sibling *_UnknownSiteId_MutatesNothing "
            + "tests cannot catch a missing entry: the test project's localisation stub echoes "
            + "keys, so they pass either way.");

    private static void AssertKeyResolvesInEveryShippedLocale(string key, string missingKeyRationale)
    {
        var entries = Entries();

        var reference = entries.FirstOrDefault(e => e.Key == ReferenceKey);
        Assert.That(reference.Key, Is.EqualTo(ReferenceKey),
            $"the reference key '{ReferenceKey}' is not in Resources/localization.json, so the "
            + "shipped locale set cannot be derived and this test would check nothing");

        var shippedLocales = reference.Values.Keys.ToList();
        Assert.That(shippedLocales, Has.Count.GreaterThanOrEqualTo(20),
            $"'{ReferenceKey}' resolved only {shippedLocales.Count} locales — the reference entry "
            + "is broken, so every assertion below would be vacuous");

        Assert.That(entries.Any(e => e.Key == key), Is.True,
            $"'{key}' is missing from Resources/localization.json, and JsonStringLocalizer hands "
            + "back the raw key when there is no entry — so the user sees the identifier itself in "
            + $"the failure toast. {missingKeyRationale}");

        var unresolved = shippedLocales
            .Where(locale => string.IsNullOrWhiteSpace(Resolve(entries, key, locale)))
            .ToList();

        var extra = entries.First(e => e.Key == key).Values.Keys
            .Where(locale => !shippedLocales.Contains(locale))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty,
                $"'{key}' has no usable value in {unresolved.Count} locale(s) — JsonStringLocalizer "
                + "returns the raw key for each of them. Offenders: " + string.Join(", ", unresolved));

            Assert.That(extra, Is.Empty,
                $"'{key}' carries locale(s) that '{ReferenceKey}' does not, which means the entry "
                + "was hand-edited against a different locale set than the rest of the file. "
                + "Offenders: " + string.Join(", ", extra));
        });
    }

    /// <summary>
    /// <c>JsonStringLocalizer.GetString</c>, reproduced: the entries carrying this
    /// culture, then the first of those whose key matches, then the empty check.
    /// Returns null where the localizer would hand the caller the key back.
    /// </summary>
    private static string? Resolve(
        List<(string Key, Dictionary<string, string> Values)> entries, string key, string locale)
    {
        var entry = entries
            .Where(e => e.Values.ContainsKey(locale))
            .FirstOrDefault(e => e.Key == key);

        if (entry.Key == null) return null;

        var value = entry.Values[locale];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// The embedded <c>Resources/localization.json</c>, addressed by the SAME name
    /// <c>JsonStringLocalizer</c> builds (<c>{assemblyName}.Resources.localization.json</c>)
    /// — so this fixture also fails if the resource is renamed or its
    /// <c>&lt;EmbeddedResource&gt;</c> entry is dropped from the csproj, which would
    /// otherwise only surface at runtime as a <c>NullReferenceException</c> on the first
    /// localised string.
    /// </summary>
    private static List<(string Key, Dictionary<string, string> Values)> Entries()
    {
        var assembly = typeof(EformBackendConfigurationPlugin).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Resources.localization.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.That(stream, Is.Not.Null, $"embedded resource '{resourceName}' was not found");

        using var reader = new StreamReader(stream!, Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());

        return json.RootElement.EnumerateArray()
            .Select(element => (
                Key: element.GetProperty("Key").GetString()!,
                Values: element.GetProperty("LocalizedValue").EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty)))
            .ToList();
    }
}
