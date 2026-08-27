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

using System.Threading;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using eFormCore;
// Fully qualify Grpc.Core to avoid ambiguity with the generated
// BackendConfiguration.Pn.Grpc.* namespace which shadows the short alias
// inside this test namespace.
using GrpcCore = global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
// `Property` also exists as a generated proto message in
// BackendConfiguration.Pn.Grpc.Events, and `PlanningSite` exists in BOTH the
// backend-configuration and items-planning base assemblies — alias both.
using BcProperty = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Property;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// CHARACTERIZATION tests for <see cref="EventsGrpcService.CompleteEvent"/> —
/// the mobile (flutter-eform) completion path.
/// <para>
/// These tests describe what the code does TODAY, not what it should do. The
/// domain rule the team is moving toward is "ONE <c>Compliance</c> row per
/// event-occurrence, shared by all assigned workers; first completer wins".
/// That gate does NOT exist yet: <c>EventsGrpcService.cs</c> contains zero
/// transactions, zero row locks and zero concurrency tokens across its ~3776
/// lines, so two workers tapping Complete on the same occurrence race freely.
/// The point of this fixture is that the upcoming concurrency gate cannot land
/// silently — every behaviour it changes is pinned here and will go red.
/// </para>
/// <para>
/// Where today's behaviour is arguably wrong it is pinned AS-IS and called out
/// in the individual test's doc comment (see
/// <see cref="CompleteEvent_RemovedCompliance_ThrowsFailedPrecondition"/> and
/// <see cref="CompleteEvent_DoesNotRecomputePropertyComplianceStatus"/>).
/// Nothing here is a fix.
/// </para>
/// <para>
/// Harness notes. The SUT is built with a REAL eFormCore
/// (<c>coreHelper.GetCore().Returns(await GetCore())</c>) so the SDK-side
/// <c>Case</c> / <c>CaseVersions</c> writes go through the production code
/// path; every other collaborator is an NSubstitute stub. The six SQL dumps
/// now replay once per fixture, so all seed rows carry a
/// <see cref="System.Guid"/> suffix and every assertion is scoped to the ids
/// this test created — no whole-table counts.
/// </para>
/// <para>
/// One production step is deliberately NOT exercised: the synchronous
/// <c>core.CaseDelete</c> at EventsGrpcService.cs:~1726. Seeded cases have
/// <c>MicrotingUid == null</c> and no matching <c>CheckListSites</c> row, so
/// both CaseDelete branches are skipped — <c>Core.CaseDelete(int microtingUId)</c>
/// calls <c>_communicator.Delete</c>, i.e. a live HTTP round-trip to the
/// Microting cloud, which the integration harness has no credentials for
/// (<c>skipCloudDeploy</c> only stubs the OUTBOUND SendXml, not Delete). The
/// consequence is visible in the assertions below: the SDK Case ends at
/// <c>WorkflowState = 'created'</c> here, whereas in production CaseDelete
/// immediately soft-deletes it to <c>'removed'</c>. That divergence is
/// harness-induced and is asserted as such, not as desired behaviour.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventsGrpcCompleteEventTests : TestBaseSetup
{
    /// <summary>
    /// A minimal real eForm with a single NON-mandatory Comment field.
    /// Copied verbatim from <c>EventDeployServiceEformRepairTests</c> /
    /// <c>EventDeployServiceTest.CommentTemplateXml</c>. Deliberately contains
    /// no Number / NumberStepper field, so CompleteEvent's "empty-fill"
    /// pre-pass (EventsGrpcService.cs:~1447-1490) finds zero targets and no
    /// <c>core.CaseUpdate</c> is issued — keeping the tests focused on the
    /// completion cascade rather than the field-value bundle.
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
    /// Fixed client tap instant: 2026-06-15T14:37:11Z. CompleteEvent takes the
    /// DATE from <c>compliance.Deadline</c> and only the wall TIME from this
    /// value, so the date component here must never show up in a DoneAt
    /// assertion — that is precisely what
    /// <see cref="CompleteEvent_HappyPath_WritesFullCascade"/> pins.
    /// </summary>
    private static readonly DateTimeOffset ClientTapInstant =
        new(2026, 6, 15, 14, 37, 11, TimeSpan.Zero);

    /// <summary>
    /// Minimal <see cref="GrpcCore.ServerCallContext"/> shim — the service only
    /// reads <c>CancellationToken</c>. Same shape as the one in
    /// <c>EventsGrpcServiceTest</c>; duplicated rather than shared because that
    /// one is a private nested type.
    /// </summary>
    private sealed class TestServerCallContext : GrpcCore.ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override GrpcCore.Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override GrpcCore.Metadata ResponseTrailersCore { get; } = new();
        protected override GrpcCore.Status StatusCore { get; set; }
        protected override GrpcCore.WriteOptions? WriteOptionsCore { get; set; }

        protected override GrpcCore.AuthContext AuthContextCore { get; } =
            new(string.Empty, new Dictionary<string, List<GrpcCore.AuthProperty>>());

        protected override GrpcCore.ContextPropagationToken CreatePropagationTokenCore(
            GrpcCore.ContextPropagationOptions? options) => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(GrpcCore.Metadata responseHeaders)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Every id the tests need to scope their assertions. Records the ids
    /// created by <see cref="SeedScenarioAsync"/> so no assertion ever has to
    /// query an unfiltered table.
    /// </summary>
    private sealed record Scenario(
        EventsGrpcService Service,
        int PropertyId,
        int ArpId,
        int ComplianceId,
        int SdkCaseId,
        int PlanningCaseId,
        int PlanningCaseSiteId,
        int CompletingSiteId,
        string CompletingSiteName,
        int OtherSiteId,
        DateTime Deadline);

    /// <summary>
    /// The template is created once per fixture (the SQL dumps also replay only
    /// once per fixture, so the row survives between tests) — <c>TemplateCreate</c>
    /// writes a full CheckList/Field tree and is the single most expensive step
    /// in the seed.
    /// </summary>
    private int _templateId;

    private async Task<int> GetOrCreateTemplateAsync(Core core)
    {
        if (_templateId != 0)
        {
            return _templateId;
        }

        var template = await core.TemplateFromXml(CommentTemplateXml);
        _templateId = await core.TemplateCreate(template);
        return _templateId;
    }

    /// <summary>
    /// Seeds the full graph CompleteEvent walks: SDK Site (x2) + Case,
    /// Area/Property/AreaRule/AreaRulePlanning, ItemsPlanning Planning +
    /// PlanningCase + PlanningCaseSite, and the single Compliance row that
    /// joins them.
    /// <para>
    /// The SDK Case is deliberately homed on a DIFFERENT site
    /// (<c>OtherSiteId</c>) than the completing worker, so asserting
    /// <c>Case.SiteId == sdkSiteId</c> after the call actually proves the write
    /// at EventsGrpcService.cs:~1497 rather than passing vacuously.
    /// </para>
    /// <para>
    /// <c>MicrotingUid</c> is left null on purpose — see the fixture doc
    /// comment for why core.CaseDelete must not be reachable from the harness.
    /// </para>
    /// </summary>
    private async Task<Scenario> SeedScenarioAsync(
        string label,
        int caseStatus = 66,
        string caseWorkflowState = Constants.WorkflowStates.Created,
        bool hasPropertyAccess = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var templateId = await GetOrCreateTemplateAsync(core);

        // Two distinct SDK sites: the worker who completes, and the site the
        // case currently belongs to.
        var completingSite = new Site
        {
            Name = $"{label}-completing-{suffix}",
            MicrotingUid = Random.Shared.Next(500000, 999999),
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        var otherSite = new Site
        {
            Name = $"{label}-other-{suffix}",
            MicrotingUid = Random.Shared.Next(500000, 999999),
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddRangeAsync(completingSite, otherSite);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkCase = new SdkCase
        {
            SiteId = otherSite.Id,
            CheckListId = templateId,
            Status = caseStatus,
            MicrotingUid = null,
            WorkflowState = caseWorkflowState
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Pre-seeded, deliberately "stale" compliance dots. CompleteEvent must
        // leave both untouched (self-documented gap, EventsGrpcService.cs:99-106).
        var property = new BcProperty
        {
            Name = $"{label}-{suffix}",
            ItemPlanningTagId = 0,
            ComplianceStatus = 1,
            ComplianceStatusThirty = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            PropertyId = property.Id,
            EformId = templateId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Deadline three days in the past, midnight UTC — the "missed rotation"
        // shape. The DATE of this value is what DoneAt must end up carrying.
        var deadline = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-3), DateTimeKind.Utc);

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = deadline,
            RelatedEFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = deadline,
            Status = true,
            RepeatType = 1,
            RepeatEvery = 1,
            DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id,
            SiteId = completingSite.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // THE shared row. One per occurrence — today nothing stops two workers
        // from both resolving it and both running the cascade.
        var compliance = new Compliance
        {
            PlanningId = planning.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            Deadline = deadline,
            StartDate = deadline.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = templateId,
            Version = 1,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = planning.Id,
            Status = 66,
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningCaseId = planningCase.Id,
            PlanningId = planning.Id,
            Status = 66,
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkSiteId = (int)completingSite.MicrotingUid!,
            MicrotingCheckListSitId = templateId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var siteResolver = Substitute.For<IGrpcSiteResolver>();
        siteResolver.GetSdkSiteIdAsync().Returns(Task.FromResult(completingSite.Id));
        siteResolver.GetSiteLanguageIdAsync(Arg.Any<int?>())
            .Returns(Task.FromResult<int?>(language.Id));

        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(completingSite.Id, property.Id)
            .Returns(Task.FromResult(hasPropertyAccess));

        // The post-write echo read. Returning an empty list drives CompleteEvent
        // down its "synthesize a minimal completed Event" branch
        // (EventsGrpcService.cs:~1873), which performs no further writes — so the
        // DB assertions in these tests observe exactly the cascade, nothing else.
        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(
                true, new List<CalendarTaskResponseModel>()));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(core);

        var service = new EventsGrpcService(
            calendar,
            Substitute.For<IBackendConfigurationPropertiesService>(),
            access,
            siteResolver,
            coreHelper,
            BackendConfigurationPnDbContext,
            ItemsPlanningPnDbContext,
            Substitute.For<IEventDeployService>(),
            NullLogger<EventsGrpcService>.Instance);

        return new Scenario(
            service, property.Id, arp.Id, compliance.Id, sdkCase.Id,
            planningCase.Id, planningCaseSite.Id, completingSite.Id, completingSite.Name,
            otherSite.Id, deadline);
    }

    private static CompleteEventRequest MakeRequest(Scenario s, bool completed = true) =>
        new()
        {
            EventId = s.ArpId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Completed = completed,
            CompletedBy = "characterization-worker",
            ClientTsUnix = ClientTapInstant.ToUnixTimeSeconds(),
            ComplianceId = s.ComplianceId,
            MicrotingSdkCaseId = s.SdkCaseId,
            Comment = string.Empty,
            DoneAtUserModifiable = string.Empty
        };

    /// <summary>
    /// The expected DoneAt for a scenario: the DATE from
    /// <c>compliance.Deadline</c> combined with the wall TIME from
    /// <c>ClientTsUnix</c> (EventsGrpcService.cs:~1318-1340).
    /// </summary>
    private static DateTime ExpectedDoneAt(Scenario s) => new(
        s.Deadline.Year, s.Deadline.Month, s.Deadline.Day,
        ClientTapInstant.UtcDateTime.Hour,
        ClientTapInstant.UtcDateTime.Minute,
        ClientTapInstant.UtcDateTime.Second,
        DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Tests
    // ------------------------------------------------------------------

    /// <summary>
    /// Pins the whole write cascade of a successful mobile completion, all of
    /// it performed WITHOUT a transaction:
    /// <list type="bullet">
    ///   <item><description>the shared <c>Compliance</c> row is soft-deleted
    ///     (<c>compliance.Delete</c>, EventsGrpcService.cs:~1389) and its
    ///     <c>Version</c> is bumped;</description></item>
    ///   <item><description>the SDK <c>Case</c> gets <c>Status = 100</c>,
    ///     <c>SiteId = sdkSiteId</c> (re-homed away from the site it was
    ///     deployed to) and <c>DoneAt</c> / <c>DoneAtUserModifiable</c> =
    ///     deadline DATE + client wall TIME — NOT the client's date, and NOT
    ///     "now";</description></item>
    ///   <item><description><c>PlanningCaseSite</c> (located by
    ///     <c>MicrotingSdkCaseId == foundCase.Id</c>) and its parent
    ///     <c>PlanningCase</c> both move to <c>Status = 100</c>, with
    ///     <c>DoneByUserId = sdkSiteId</c> / <c>DoneByUserName = siteName</c>
    ///     and <c>PlanningCase.WorkflowState = Processed</c>.</description></item>
    /// </list>
    /// <para>
    /// Fails when: the completion cascade is wrapped in a transaction/row-lock
    /// that changes which rows get written (or their order/values), when DoneAt
    /// stops being deadline-date + client-time, or when the PlanningCaseSite
    /// lookup key changes away from <c>MicrotingSdkCaseId</c>.
    /// </para>
    /// </summary>
    [Test]
    public async Task CompleteEvent_HappyPath_WritesFullCascade()
    {
        var s = await SeedScenarioAsync("cascade");
        var expectedDoneAt = ExpectedDoneAt(s);

        var response = await s.Service.CompleteEvent(MakeRequest(s), new TestServerCallContext());

        Assert.That(response.Event, Is.Not.Null);
        Assert.That(response.Event.Completed, Is.True);

        var compliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);
        var sdkCase = await MicrotingDbContext!.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var planningCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);
        var planningCaseSite = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseSiteId);

        Assert.Multiple(() =>
        {
            // Compliance — soft-deleted, so the occurrence drops out of every
            // outstanding list. This is also the ONLY thing that stops a second
            // worker today, and it is not atomic with anything else.
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "compliance must be soft-deleted by compliance.Delete()");
            Assert.That(compliance.Version, Is.GreaterThan(1),
                "compliance.Delete() bumps Version and writes a ComplianceVersions row");

            // SDK Case.
            Assert.That(sdkCase.Status, Is.EqualTo(100), "SDK case must be marked done");
            Assert.That(sdkCase.SiteId, Is.EqualTo(s.CompletingSiteId),
                "SDK case is re-homed to the completing worker's site (sdkSiteId)");
            Assert.That(sdkCase.DoneAt, Is.EqualTo(expectedDoneAt),
                "DoneAt = compliance.Deadline DATE + ClientTsUnix wall TIME");
            Assert.That(sdkCase.DoneAtUserModifiable, Is.EqualTo(expectedDoneAt),
                "DoneAtUserModifiable tracks DoneAt when no override is sent");
            // Harness-induced, see fixture doc: core.CaseDelete is unreachable
            // without a MicrotingUid, so the case is NOT soft-deleted here.
            // In production CaseDelete flips this to 'removed' right after.
            Assert.That(sdkCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "case is revived to 'created'; core.CaseDelete is a no-op without a MicrotingUid");

            // ItemsPlanning report rows.
            Assert.That(planningCaseSite.Status, Is.EqualTo(100));
            Assert.That(planningCaseSite.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedDoneAt));
            Assert.That(planningCaseSite.DoneByUserId, Is.EqualTo(s.CompletingSiteId),
                "DoneByUserId is the SDK site id, not an eForm user id");
            Assert.That(planningCaseSite.DoneByUserName, Is.EqualTo(s.CompletingSiteName));

            Assert.That(planningCase.Status, Is.EqualTo(100));
            Assert.That(planningCase.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedDoneAt));
            Assert.That(planningCase.DoneByUserId, Is.EqualTo(s.CompletingSiteId));
            Assert.That(planningCase.DoneByUserName, Is.EqualTo(s.CompletingSiteName));
            Assert.That(planningCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Processed));
        });
    }

    /// <summary>
    /// Pins the REVIVAL of a retracted missed-deadline case. A rotation whose
    /// deadline passed arrives here as <c>Case.Status = 77</c>,
    /// <c>WorkflowState = 'removed'</c>. CompleteEvent's SDK-case block does not
    /// filter on WorkflowState and assigns
    /// <c>foundCase.WorkflowState = Created</c> directly
    /// (EventsGrpcService.cs:~1502) — un-soft-deleting the row so the angular
    /// admin "filled cases" view picks it up.
    /// <para>
    /// This is intentional today (it is the only direct WorkflowState write in
    /// the service) but it is worth knowing that "complete" can resurrect a row
    /// an admin previously retracted.
    /// </para>
    /// <para>
    /// Fails when: a concurrency/state gate starts rejecting completion of
    /// cases that are not in a live state, or when revival is moved behind a
    /// flag.
    /// </para>
    /// </summary>
    [Test]
    public async Task CompleteEvent_MissedDeadline_RevivesRetractedCase()
    {
        var s = await SeedScenarioAsync(
            "revive", caseStatus: 77, caseWorkflowState: Constants.WorkflowStates.Removed);

        var before = await MicrotingDbContext!.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        Assert.Multiple(() =>
        {
            Assert.That(before.Status, Is.EqualTo(77), "precondition: retracted missed-deadline case");
            Assert.That(before.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        });

        await s.Service.CompleteEvent(MakeRequest(s), new TestServerCallContext());

        var after = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);

        Assert.Multiple(() =>
        {
            Assert.That(after.Status, Is.EqualTo(100));
            Assert.That(after.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "completing a retracted case REVIVES it (direct WorkflowState assignment)");
            Assert.That(after.DoneAt, Is.EqualTo(ExpectedDoneAt(s)),
                "a missed rotation is still dated to its scheduled deadline DATE");
        });
    }

    /// <summary>
    /// A soft-deleted Compliance row whose case was NEVER completed must not be
    /// reported as "already completed by X".
    /// </summary>
    /// <remarks>
    /// <c>compliance.Delete()</c> is not exclusive to completion. The same call
    /// runs when CalendarAssignmentReconciliationService unassigns the last
    /// worker from an occurrence, and when BackendConfigurationTaskWizardService
    /// deletes or deactivates a task - in both, the backing case was never
    /// completed and its site is whoever happened to hold it last.
    /// <para>
    /// Naming that site as the completer would invent a person and a moment that
    /// never existed. A worker told "already completed by a colleague" for work
    /// that was actually cancelled is worse off than one who saw the anonymous
    /// error, because the false answer is credible. So the service falls back to
    /// FailedPrecondition rather than guessing.
    /// </para>
    /// <para>
    /// Fails when: the completion check on the winning case is dropped and any
    /// soft-deleted row starts yielding an "already completed" answer.
    /// </para>
    /// </remarks>
    [Test]
    public async Task CompleteEvent_ComplianceRemovedWithoutCompletion_DoesNotInventACompleter()
    {
        var s = await SeedScenarioAsync("cancelled-not-completed");

        // The occurrence goes away WITHOUT anyone completing it - the case keeps
        // its seeded Status 66 and null DoneAt.
        var compliance = await BackendConfigurationPnDbContext!.Compliances
            .FirstAsync(x => x.Id == s.ComplianceId);
        await compliance.Delete(BackendConfigurationPnDbContext);

        var ex = Assert.ThrowsAsync<GrpcCore.RpcException>(async () =>
            await s.Service.CompleteEvent(MakeRequest(s), new TestServerCallContext()));

        var uninvolvedSiteName = await MicrotingDbContext!.Sites
            .AsNoTracking().Where(x => x.Id == s.OtherSiteId)
            .Select(x => x.Name).FirstAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.FailedPrecondition),
                "a cancelled occurrence is not a completed one");
            Assert.That(ex.Status.Detail, Does.Not.Contain(uninvolvedSiteName),
                "must not name a completer for work nobody completed");
        });
    }

    /// <summary>
    /// Pins the answer to "someone already completed this occurrence".
    /// The PK lookup at EventsGrpcService.cs:~1250-1257 filters
    /// <c>WorkflowState != Removed</c>, so once the first completer's
    /// <c>compliance.Delete()</c> has landed, the second worker's request
    /// resolves no compliance and falls into the already-completed branch.
    /// <para>
    /// UPDATED for the "first completer wins" gate. This test previously
    /// asserted a bare <c>FailedPrecondition: "Event {id} has no pending
    /// compliance — there is no SDK case to complete."</c>, and its remarks said
    /// to rewrite it rather than delete it when the gate landed - so the
    /// transition stays visible in history. The occurrence is shared, so losing
    /// the race is a normal outcome: the caller now gets a successful response
    /// naming the winner, and their own payload is discarded.
    /// </para>
    /// <para>
    /// Fails when: the gate is removed and the anonymous FailedPrecondition
    /// comes back, when the winner's identity stops being carried on
    /// <c>completed_by</c>, or when a losing caller starts mutating the winner's
    /// case. (The companion <c>EventsGrpcCompleteEventRaceTests</c> is what pins
    /// <c>updated_at</c>; this fixture only seeds a soft-deleted row, so there is
    /// no winning tap time to compare against here.)
    /// </para>
    /// </summary>
    [Test]
    public async Task CompleteEvent_RemovedCompliance_ReportsAlreadyCompletedByWinner()
    {
        var s = await SeedScenarioAsync("already-done");

        // Simulate "the first worker already completed it" properly: the winner's
        // case must actually BE complete. Soft-deleting the Compliance row alone
        // is not a completion - the same Delete() happens when an admin unassigns
        // the last worker or deletes the task, and the service must not claim a
        // completer in those cases (see the sibling test below).
        var winningCase = await MicrotingDbContext!.Cases
            .FirstAsync(x => x.Id == s.SdkCaseId);
        winningCase.Status = 100;
        winningCase.DoneAt = new DateTime(2026, 5, 4, 8, 30, 0, DateTimeKind.Utc);
        await winningCase.Update(MicrotingDbContext);

        var compliance = await BackendConfigurationPnDbContext!.Compliances
            .FirstAsync(x => x.Id == s.ComplianceId);
        await compliance.Delete(BackendConfigurationPnDbContext);

        var winnerName = await MicrotingDbContext!.Sites
            .AsNoTracking().Where(x => x.Id == s.OtherSiteId)
            .Select(x => x.Name).FirstAsync();

        var response = await s.Service.CompleteEvent(
            MakeRequest(s), new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(response.Event, Is.Not.Null, "losing the race is not an error");
            Assert.That(response.Event.Completed, Is.True,
                "the occurrence IS complete - somebody else finished it");
            Assert.That(response.Event.CompletedBy, Is.EqualTo(winnerName),
                "the caller must be told WHO completed it");
        });

        // The loser's payload is discarded rather than applied: the winner's case
        // still carries exactly what the arrange set, and the loser's cascade
        // (which would promote PlanningCase to 100) never ran.
        var sdkCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var planningCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);

        Assert.Multiple(() =>
        {
            Assert.That(sdkCase.Status, Is.EqualTo(100),
                "the winner's completion stands");
            Assert.That(sdkCase.DoneAt, Is.EqualTo(new DateTime(2026, 5, 4, 8, 30, 0)),
                "the loser must not overwrite the winner's completion time");
            Assert.That(sdkCase.SiteId, Is.EqualTo(s.OtherSiteId),
                "the case is not re-homed to the losing caller");
            Assert.That(planningCase.Status, Is.EqualTo(66),
                "the losing caller's completion cascade never ran");
        });
    }

    /// <summary>
    /// Pins that <c>completed = false</c> (the flutter UI sends <c>!o.completed</c>
    /// when a worker re-taps a row) routes to
    /// <c>BuildIdempotentCompleteEventResponse</c> (EventsGrpcService.cs:~1919)
    /// and is STRICTLY READ-ONLY: no un-completion, no compliance revival, no
    /// status change anywhere. Compliance <c>WorkflowState</c> AND <c>Version</c>
    /// (which would move on any <c>Update</c>/<c>Delete</c> call), the SDK case
    /// <c>Status</c>/<c>WorkflowState</c>/<c>SiteId</c>, and
    /// <c>PlanningCase.Status</c> are all compared byte-for-byte before/after.
    /// <para>
    /// Fails when: the idempotent branch starts writing anything — e.g. a
    /// concurrency gate that stamps a "seen" marker, or an un-complete
    /// implementation.
    /// </para>
    /// </summary>
    [Test]
    public async Task CompleteEvent_CompletedFalse_IsReadOnlyNoOp()
    {
        var s = await SeedScenarioAsync("readonly-retap");

        var complianceBefore = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);
        var caseBefore = await MicrotingDbContext!.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var planningCaseBefore = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);
        var planningCaseSiteBefore = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseSiteId);

        var response = await s.Service.CompleteEvent(
            MakeRequest(s, completed: false), new TestServerCallContext());

        Assert.That(response.Event, Is.Not.Null);

        var complianceAfter = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);
        var caseAfter = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var planningCaseAfter = await ItemsPlanningPnDbContext.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);
        var planningCaseSiteAfter = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseSiteId);

        Assert.Multiple(() =>
        {
            Assert.That(complianceAfter.WorkflowState, Is.EqualTo(complianceBefore.WorkflowState));
            Assert.That(complianceAfter.Version, Is.EqualTo(complianceBefore.Version),
                "any write through PnBase.Update/Delete would bump Version");
            Assert.That(complianceAfter.UpdatedAt, Is.EqualTo(complianceBefore.UpdatedAt));

            Assert.That(caseAfter.Status, Is.EqualTo(caseBefore.Status));
            Assert.That(caseAfter.WorkflowState, Is.EqualTo(caseBefore.WorkflowState));
            Assert.That(caseAfter.SiteId, Is.EqualTo(caseBefore.SiteId));
            Assert.That(caseAfter.DoneAt, Is.EqualTo(caseBefore.DoneAt));
            Assert.That(caseAfter.Version, Is.EqualTo(caseBefore.Version));

            Assert.That(planningCaseAfter.Status, Is.EqualTo(planningCaseBefore.Status));
            Assert.That(planningCaseAfter.WorkflowState, Is.EqualTo(planningCaseBefore.WorkflowState));
            Assert.That(planningCaseSiteAfter.Status, Is.EqualTo(planningCaseSiteBefore.Status));
            Assert.That(planningCaseSiteAfter.DoneByUserId, Is.EqualTo(planningCaseSiteBefore.DoneByUserId));
        });
    }

    /// <summary>
    /// Pins the KNOWN GAP the service documents on itself at
    /// EventsGrpcService.cs:99-106: unlike the angular admin path
    /// (<c>BackendConfigurationCompliancesService.Update</c> lines 344-371),
    /// CompleteEvent never recomputes <c>Property.ComplianceStatus</c> /
    /// <c>ComplianceStatusThirty</c>. The property is seeded "non-compliant"
    /// (both = 1) and stays that way even though its only outstanding
    /// compliance row was just cleared — so the property compliance "dot" in
    /// the web UI goes stale after every mobile completion.
    /// <para>
    /// This is arguably WRONG and is pinned as-is, not fixed. If the upcoming
    /// work factors out a shared completion helper, this test going red is the
    /// signal that the gap closed — flip the expectation to 0 at that point.
    /// </para>
    /// <para>
    /// Fails when: property compliance recomputation is added to the mobile
    /// completion path.
    /// </para>
    /// </summary>
    [Test]
    public async Task CompleteEvent_DoesNotRecomputePropertyComplianceStatus()
    {
        var s = await SeedScenarioAsync("stale-dot");

        await s.Service.CompleteEvent(MakeRequest(s), new TestServerCallContext());

        // Proves the completion really happened, so the assertion below is
        // about the missing recomputation and not about a no-op call.
        var compliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);
        Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
            "precondition: the completion cascade did run");

        var property = await BackendConfigurationPnDbContext.Properties
            .AsNoTracking().FirstAsync(x => x.Id == s.PropertyId);

        Assert.Multiple(() =>
        {
            Assert.That(property.ComplianceStatus, Is.EqualTo(1),
                "KNOWN GAP: ComplianceStatus is never recomputed on the mobile path");
            Assert.That(property.ComplianceStatusThirty, Is.EqualTo(1),
                "KNOWN GAP: ComplianceStatusThirty is never recomputed on the mobile path");
        });
    }

    /// <summary>
    /// Pins the authorization gate: when the caller's site has no
    /// PropertyWorker access to the event's property, CompleteEvent throws
    /// <c>PermissionDenied</c> BEFORE resolving the compliance — so not a
    /// single row is written. The check sits between the ARP lookup and the
    /// compliance lookup (EventsGrpcService.cs:~1223-1229), which is why the
    /// compliance survives untouched here.
    /// <para>
    /// Fails when: the access check moves after any write, or when the
    /// concurrency gate acquires/marks a row before authorizing.
    /// </para>
    /// </summary>
    [Test]
    public async Task CompleteEvent_NoPropertyAccess_ThrowsPermissionDenied_NoWrites()
    {
        var s = await SeedScenarioAsync("no-access", hasPropertyAccess: false);

        var ex = Assert.ThrowsAsync<GrpcCore.RpcException>(async () =>
            await s.Service.CompleteEvent(MakeRequest(s), new TestServerCallContext()));

        Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.PermissionDenied));

        var compliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);
        var sdkCase = await MicrotingDbContext!.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var planningCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);
        var planningCaseSite = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseSiteId);
        var property = await BackendConfigurationPnDbContext.Properties
            .AsNoTracking().FirstAsync(x => x.Id == s.PropertyId);

        Assert.Multiple(() =>
        {
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "compliance must survive an unauthorized attempt");
            Assert.That(compliance.Version, Is.EqualTo(1));

            Assert.That(sdkCase.Status, Is.EqualTo(66));
            Assert.That(sdkCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(sdkCase.SiteId, Is.EqualTo(s.OtherSiteId),
                "case must not be re-homed to an unauthorized caller's site");
            Assert.That(sdkCase.DoneAt, Is.Null);

            Assert.That(planningCase.Status, Is.EqualTo(66));
            Assert.That(planningCase.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(planningCaseSite.Status, Is.EqualTo(66));
            Assert.That(planningCaseSite.DoneByUserId, Is.EqualTo(0),
                "DoneByUserId is still the seeded default — the cascade never ran");

            Assert.That(property.ComplianceStatus, Is.EqualTo(1));
        });
    }
}
