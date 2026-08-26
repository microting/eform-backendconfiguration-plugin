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
/// The "first completer wins" race on a SHARED <c>Compliance</c> row — the one
/// behaviour the merged <c>EventsGrpcCompleteEventTests</c> characterization
/// fixture explicitly flagged as "the test that will change".
///
/// <para>
/// Domain rule (authoritative). There is exactly ONE <c>Compliance</c> row per
/// event-occurrence, SHARED by every assigned worker; the <c>UNIQUE
/// (PlanningId, Deadline)</c> index encodes that. Deployment is per-worker —
/// each assignee gets their own SDK <c>Case</c> — but only the single tracking
/// row decides whether the occurrence is done. Whoever completes FIRST wins.
/// Every later submitter must have their payload DISCARDED and be told
/// "already completed by &lt;person&gt; on &lt;day&gt;" as a NORMAL outcome.
/// </para>
///
/// <para>
/// What this fixture was written against, and what it now holds in place.
/// <c>EventsGrpcService.CompleteEvent</c> resolves the compliance by PK with a
/// <c>WorkflowState != Removed</c> filter (EventsGrpcService.cs:~1250-1257), and
/// worker A's completion flips exactly that column to <c>'removed'</c>, so
/// worker B's request resolves NOTHING. That used to end in
/// <c>RpcException(FailedPrecondition, "Event {id} has no pending compliance —
/// there is no SDK case to complete.")</c>. The flutter-eform outbox classifies
/// <c>FailedPrecondition</c> as a PERMANENT failure and raises a conflict dialog
/// whose only options are Discard or Retry-forever — so losing a legitimate race
/// was presented to the worker as a sync error, with no hint of who actually did
/// the job or when. The gate replaced that anonymous throw with a normal response
/// naming the winner.
/// </para>
///
/// <para>
/// <c>Compliance</c> (backend-configuration-base) carries no completed-by /
/// completed-at column and no concurrency token, so the loser's answer cannot be
/// read off the shared row — it has to be reconstructed from the winning side of
/// the cascade, i.e. the winner's SDK <c>Case</c>, which supplies both the site
/// name and the completion time. The claim is the only concurrency control in the
/// file: a <c>SELECT ... FOR UPDATE</c> inside a short-lived transaction
/// (<c>EventsGrpcService.TryClaimOccurrenceAsync</c>).
/// </para>
///
/// <para>
/// Harness notes, inherited from <c>EventsGrpcCompleteEventTests</c>. The SUT
/// runs against a REAL <c>eFormCore</c> (<c>coreHelper.GetCore().Returns(core)</c>)
/// so the SDK <c>Case</c> / <c>FieldValues</c> writes go through production
/// code; every other collaborator is an NSubstitute stub. The six SQL dumps
/// replay ONCE PER FIXTURE, so every seed row carries a <see cref="System.Guid"/>
/// suffix and every assertion is scoped to the ids this test created — never a
/// whole-table count. Seeded cases have <c>MicrotingUid == null</c> and no
/// matching <c>CheckListSites</c> row, because <c>Core.CaseDelete</c> performs a
/// live HTTP round-trip to the Microting cloud that the harness has no
/// credentials for (<c>skipCloudDeploy</c> only stubs the OUTBOUND
/// <c>SendXml</c>) — so retraction is deliberately not exercised here.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventsGrpcCompleteEventRaceTests : TestBaseSetup
{
    /// <summary>
    /// A minimal real eForm with a single NON-mandatory Comment field. Copied
    /// verbatim from <c>EventsGrpcCompleteEventTests.CommentTemplateXml</c> /
    /// <c>EventDeployServiceTest</c>.
    /// <para>
    /// The single Comment field is what both workers write through
    /// <c>CompleteEventRequest.field_values</c>, and it is deliberately NOT a
    /// Number / NumberStepper, so CompleteEvent's "empty-fill" pre-pass
    /// (EventsGrpcService.cs:~1447-1490) finds zero targets and cannot muddy
    /// the "whose value survived?" assertion with a write of its own.
    /// </para>
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

    /// <summary>Worker A taps Complete at 2026-06-15T14:37:11Z — this is the WINNING tap.</summary>
    private static readonly DateTimeOffset WinnerTapInstant =
        new(2026, 6, 15, 14, 37, 11, TimeSpan.Zero);

    /// <summary>
    /// Worker B taps Complete 85 minutes later, at 2026-06-15T16:02:44Z. The
    /// distinct wall-clock time is what makes the <c>UpdatedAt</c> assertion
    /// meaningful: a response that echoed B's OWN tap time would produce
    /// 16:02:44 and fail, so only the winner's timestamp can pass.
    /// </summary>
    private static readonly DateTimeOffset LoserTapInstant =
        new(2026, 6, 15, 16, 2, 44, TimeSpan.Zero);

    /// <summary>
    /// Minimal <see cref="GrpcCore.ServerCallContext"/> shim — the service only
    /// reads <c>CancellationToken</c>. Same shape as the one in
    /// <c>EventsGrpcCompleteEventTests</c>; duplicated rather than shared
    /// because that one is a private nested type.
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
    /// Everything the race needs, scoped to this test's own ids so no assertion
    /// ever touches an unfiltered table.
    /// <para>
    /// Two <c>EventsGrpcService</c> instances are built over the SAME DbContexts
    /// and differ only in their <c>IGrpcSiteResolver</c> — that is exactly how
    /// two phones differ on the wire, since the caller's site is resolved from
    /// the authenticated connection and never from the request body.
    /// </para>
    /// </summary>
    private sealed record Race(
        EventsGrpcService ServiceA,
        EventsGrpcService ServiceB,
        int ArpId,
        int ComplianceId,
        int SharedFieldId,
        string SiteAName,
        int CaseAId,
        int CaseBId,
        DateTime Deadline);

    /// <summary>
    /// Created once per fixture — the SQL dumps also replay only once per
    /// fixture, so the CheckList/Field tree survives between tests, and
    /// <c>TemplateCreate</c> is by far the most expensive seed step.
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
    /// Seeds ONE event-occurrence assigned to TWO workers.
    /// <list type="bullet">
    ///   <item><description>Two SDK <see cref="Site"/>s (A and B) and two SDK
    ///     <see cref="SdkCase"/>s — one deployed per worker, which is the
    ///     production shape: deployment is per-worker.</description></item>
    ///   <item><description>Two <c>BcPlanningSite</c> rows on the one
    ///     <c>AreaRulePlanning</c>, i.e. both workers really are assigned to
    ///     this event.</description></item>
    ///   <item><description>Exactly ONE <see cref="Compliance"/> row, owned by
    ///     A's case (<c>MicrotingSdkCaseId = caseA.Id</c>) — the shared tracking
    ///     row. A second row would violate <c>UNIQUE (PlanningId, Deadline)</c>
    ///     and would not be a race at all.</description></item>
    ///   <item><description>A <c>PlanningCase</c> + <c>PlanningCaseSite</c> pair
    ///     per worker, because the service locates them by
    ///     <c>MicrotingSdkCaseId == foundCase.Id</c>.</description></item>
    /// </list>
    /// </summary>
    private async Task<Race> SeedSharedOccurrenceAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var templateId = await GetOrCreateTemplateAsync(core);

        // The one Comment field of the template — the field both workers write.
        // Located through the CheckList tree (the DataElement is a CHILD of the
        // main CheckList) rather than by the XML's <Id>, which is a source id
        // that TemplateCreate does not preserve.
        var sharedFieldId = await MicrotingDbContext.Fields
            .Where(f => f.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(f => f.CheckListId == templateId
                        || MicrotingDbContext.CheckLists
                            .Any(cl => cl.Id == f.CheckListId && cl.ParentId == templateId))
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .FirstAsync();

        var siteA = new Site
        {
            Name = $"race-winner-A-{suffix}",
            MicrotingUid = Random.Shared.Next(500000, 999999),
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        var siteB = new Site
        {
            Name = $"race-loser-B-{suffix}",
            MicrotingUid = Random.Shared.Next(500000, 999999),
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddRangeAsync(siteA, siteB);
        await MicrotingDbContext.SaveChangesAsync();

        // MicrotingUid stays null on both: see the fixture doc comment — it is
        // what keeps core.CaseDelete (a live cloud call) out of the harness.
        var caseA = new SdkCase
        {
            SiteId = siteA.Id,
            CheckListId = templateId,
            Status = 66,
            MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        var caseB = new SdkCase
        {
            SiteId = siteB.Id,
            CheckListId = templateId,
            Status = 66,
            MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddRangeAsync(caseA, caseB);
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

        var property = new BcProperty
        {
            Name = $"race-{suffix}",
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

        // BOTH workers assigned to the same event.
        await BackendConfigurationPnDbContext.PlanningSites.AddRangeAsync(
            new BcPlanningSite
            {
                AreaRulePlanningsId = arp.Id,
                SiteId = siteA.Id,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1,
                UpdatedByUserId = 1
            },
            new BcPlanningSite
            {
                AreaRulePlanningsId = arp.Id,
                SiteId = siteB.Id,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1,
                UpdatedByUserId = 1
            });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // THE shared row — one per occurrence, owned by A's case.
        var compliance = new Compliance
        {
            PlanningId = planning.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            Deadline = deadline,
            StartDate = deadline.AddDays(-7),
            MicrotingSdkCaseId = caseA.Id,
            MicrotingSdkeFormId = templateId,
            Version = 1,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        foreach (var (sdkCase, site) in new[] { (caseA, siteA), (caseB, siteB) })
        {
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

            await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(new PlanningCaseSite
            {
                PlanningCaseId = planningCase.Id,
                PlanningId = planning.Id,
                Status = 66,
                MicrotingSdkCaseId = sdkCase.Id,
                MicrotingSdkSiteId = (int)site.MicrotingUid!,
                MicrotingCheckListSitId = templateId,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1,
                UpdatedByUserId = 1
            });
            await ItemsPlanningPnDbContext.SaveChangesAsync();
        }

        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(Arg.Any<int>(), property.Id).Returns(Task.FromResult(true));

        // The post-write echo read. An empty list drives CompleteEvent down its
        // "synthesize a minimal completed Event" branch (EventsGrpcService.cs:~1873)
        // — no further writes, so the DB assertions observe exactly the cascade.
        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(
                true, new List<CalendarTaskResponseModel>()));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(core);

        EventsGrpcService BuildServiceFor(int sdkSiteId)
        {
            var siteResolver = Substitute.For<IGrpcSiteResolver>();
            siteResolver.GetSdkSiteIdAsync().Returns(Task.FromResult(sdkSiteId));
            siteResolver.GetSiteLanguageIdAsync(Arg.Any<int?>())
                .Returns(Task.FromResult<int?>(language.Id));

            return new EventsGrpcService(
                calendar,
                Substitute.For<IBackendConfigurationPropertiesService>(),
                access,
                siteResolver,
                coreHelper,
                BackendConfigurationPnDbContext,
                ItemsPlanningPnDbContext,
                Substitute.For<IEventDeployService>(),
                NullLogger<EventsGrpcService>.Instance);
        }

        return new Race(
            BuildServiceFor(siteA.Id),
            BuildServiceFor(siteB.Id),
            arp.Id, compliance.Id, sharedFieldId,
            siteA.Name, caseA.Id, caseB.Id,
            deadline);
    }

    private static CompleteEventRequest MakeRequest(
        Race race, int microtingSdkCaseId, DateTimeOffset tap,
        string completedBy, string fieldValue, string comment) =>
        new()
        {
            EventId = race.ArpId.ToString(CultureInfo.InvariantCulture),
            Completed = true,
            CompletedBy = completedBy,
            ClientTsUnix = tap.ToUnixTimeSeconds(),
            // Both phones echo the SAME shared compliance id — that is the whole
            // point of the shared row, and it is what the client received from
            // ListEvents. Only microting_sdk_case_id differs, because deployment
            // is per-worker.
            ComplianceId = race.ComplianceId,
            MicrotingSdkCaseId = microtingSdkCaseId,
            Comment = comment,
            DoneAtUserModifiable = string.Empty,
            FieldValues = { new FieldValueWrite { FieldId = race.SharedFieldId, Value = fieldValue } }
        };

    /// <summary>
    /// The DoneAt a completion produces: DATE from <c>compliance.Deadline</c>,
    /// wall TIME from <c>ClientTsUnix</c> (EventsGrpcService.cs:~1318-1340).
    /// </summary>
    private static DateTime ExpectedDoneAt(Race race, DateTimeOffset tap) => new(
        race.Deadline.Year, race.Deadline.Month, race.Deadline.Day,
        tap.UtcDateTime.Hour, tap.UtcDateTime.Minute, tap.UtcDateTime.Second,
        DateTimeKind.Utc);

    /// <summary>
    /// Worker B completes an occurrence worker A already closed. B must get a
    /// NORMAL <c>CompleteEventResponse</c> naming A as the completer, and B's
    /// payload must be thrown away.
    ///
    /// <para>
    /// Three things are asserted, in the order they matter:
    /// </para>
    /// <list type="number">
    ///   <item><description>B's call does NOT throw. It used to throw
    ///     <c>RpcException(FailedPrecondition, "…has no pending compliance…")</c>,
    ///     which flutter's outbox treats as a permanent sync error and surfaces
    ///     as a Discard/Retry conflict dialog — the wrong shape for a legitimate
    ///     race that B simply lost.</description></item>
    ///   <item><description>The returned <c>Event</c> identifies the WINNER:
    ///     <c>Completed == true</c>, <c>CompletedBy</c> == A's SITE NAME (proto
    ///     field 9) and <c>UpdatedAt</c> == A's completion timestamp (proto field
    ///     11). Every OTHER assignment in the service leaves them uninformative:
    ///     <c>CompletedBy</c> is either <c>string.Empty</c> or a verbatim echo of
    ///     the CALLER's <c>request.CompletedBy</c>, and <c>UpdatedAt</c> is set
    ///     nowhere else in the file at all. B deliberately sends
    ///     <c>completed_by</c> = its own device label
    ///     and taps 85 minutes after A, so neither an echo of B's label nor B's
    ///     own tap time can pass.</description></item>
    ///   <item><description>A's recorded work SURVIVES: the shared field value
    ///     and the comment on A's SDK case are still A's, and B's payload appears
    ///     nowhere. "First completer wins" is meaningless if the loser's write
    ///     still lands on top.</description></item>
    /// </list>
    ///
    /// <para>
    /// Before the gate landed this failed at (1) — B never reached a response at all.
    /// </para>
    /// </summary>
    [Test]
    public async Task CompleteEvent_SecondWorkerAfterFirstCompleted_ReturnsAlreadyCompletedByWinner()
    {
        var race = await SeedSharedOccurrenceAsync();
        var runSuffix = Guid.NewGuid().ToString("N")[..6];
        var aFieldValue = $"A-FIELD-{runSuffix}";
        var aComment = $"A-COMMENT-{runSuffix}";
        var bFieldValue = $"B-FIELD-{runSuffix}";
        var bComment = $"B-COMMENT-{runSuffix}";
        var winningDoneAt = ExpectedDoneAt(race, WinnerTapInstant);

        // ---- Worker A wins the race -------------------------------------
        await race.ServiceA.CompleteEvent(
            MakeRequest(race, race.CaseAId, WinnerTapInstant,
                completedBy: "phone-of-worker-A", fieldValue: aFieldValue, comment: aComment),
            new TestServerCallContext());

        // Preconditions — prove the win really landed, so the assertions below
        // are about the race gate and not about a no-op first call.
        var complianceAfterA = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == race.ComplianceId);
        var caseAfterA = await MicrotingDbContext!.Cases
            .AsNoTracking().FirstAsync(x => x.Id == race.CaseAId);
        var fieldValueAfterA = await MicrotingDbContext.FieldValues
            .AsNoTracking()
            .FirstAsync(fv => fv.CaseId == race.CaseAId && fv.FieldId == race.SharedFieldId);

        Assert.Multiple(() =>
        {
            Assert.That(complianceAfterA.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "precondition: A's completion soft-deleted the shared compliance row");
            Assert.That(caseAfterA.Status, Is.EqualTo(100),
                "precondition: A's SDK case is closed");
            Assert.That(caseAfterA.DoneAt, Is.EqualTo(winningDoneAt),
                "precondition: the winning completion is stamped deadline DATE + A's tap TIME");
            Assert.That(fieldValueAfterA.Value, Is.EqualTo(aFieldValue),
                "precondition: A's bundled field value persisted");
            Assert.That(caseAfterA.Custom, Does.Contain(aComment),
                "precondition: A's bundled comment persisted into the Cases.Custom envelope");
        });

        // ---- Worker B loses the race ------------------------------------
        // Must be a NORMAL return. Assert.DoesNotThrowAsync would swallow the
        // response, so capture it and let any RpcException surface as the
        // test's failure — the absence of a throw IS half the contract.
        var response = await race.ServiceB.CompleteEvent(
            MakeRequest(race, race.CaseBId, LoserTapInstant,
                completedBy: "phone-of-worker-B", fieldValue: bFieldValue, comment: bComment),
            new TestServerCallContext());

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Event, Is.Not.Null, "the loser still gets an Event to reconcile against");

        Assert.Multiple(() =>
        {
            Assert.That(response.Event.Completed, Is.True,
                "the occurrence IS completed — just not by B");
            Assert.That(response.Event.CompletedBy, Is.EqualTo(race.SiteAName),
                "CompletedBy (proto field 9) must name the WINNING worker's site; "
                + "an echo of the caller's own completed_by (\"phone-of-worker-B\") "
                + "tells B nothing about who won");
            Assert.That(response.Event.UpdatedAt, Is.Not.Null,
                "UpdatedAt (proto field 11) must carry the winning completion's timestamp");
            Assert.That(response.Event.UpdatedAt.ToDateTime(), Is.EqualTo(winningDoneAt),
                "UpdatedAt must be A's completion time, not B's tap time");
        });

        // ---- B's payload was DISCARDED ----------------------------------
        var caseAfterB = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == race.CaseAId);
        var fieldValueAfterB = await MicrotingDbContext.FieldValues
            .AsNoTracking()
            .FirstAsync(fv => fv.CaseId == race.CaseAId && fv.FieldId == race.SharedFieldId);

        Assert.Multiple(() =>
        {
            Assert.That(fieldValueAfterB.Value, Is.EqualTo(aFieldValue),
                "the winner's field value survives; the loser's submission is discarded");
            Assert.That(caseAfterB.Custom, Does.Contain(aComment),
                "the winner's comment survives");
            Assert.That(caseAfterB.Custom, Does.Not.Contain(bComment),
                "the loser's comment must never overwrite the winner's");
            Assert.That(caseAfterB.DoneAt, Is.EqualTo(winningDoneAt),
                "the completion stays dated to the winning tap");
        });
    }
}
