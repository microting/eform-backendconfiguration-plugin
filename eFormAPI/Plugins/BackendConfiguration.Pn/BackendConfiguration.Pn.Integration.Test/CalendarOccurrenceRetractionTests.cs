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
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.CalendarOccurrenceRetraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;

/// <summary>
/// #1122/#1123 — <see cref="CalendarOccurrenceRetractionService"/>.
///
/// WHY this exists: both "change a task's start date to a different recurrence
/// period" (#1122) and "deactivate a task" (#1123) have to clear a series'
/// OPEN deployed occurrences while leaving its ANSWERED ones alone. Getting the
/// second half wrong is silent and unrecoverable — soft-removing a completed
/// occurrence's Compliance row destroys the only DB link between the rotation
/// date and the SDK case that answered it, so DoneByName/DoneAt stop rendering
/// for that date and no later pass can rebuild the link. Hence invariant R2:
/// completed occurrences are immutable.
///
/// The completion gate under test is exactly
/// <c>MicrotingSdkCaseId &gt; 0 AND backing SDK Case.Status == 100</c>. Both
/// halves are covered independently below, because each has its own way of
/// going wrong: a row released back to <c>MicrotingSdkCaseId == 0</c> by the
/// reconciliation engine has NO backing case at all and must NOT be mistaken
/// for "completed", and a case parked one short of the completed status (99, or
/// the ordinary in-progress 66) must not be either. <c>MicrotingSdkCaseDoneAt</c>
/// is deliberately never consulted — it is written for other purposes and is not
/// the gate.
///
/// Every case below seeds <c>MicrotingUid = null</c> so the SDK
/// <c>core.CaseDelete</c> cloud call is skipped (there is no cloud in CI), while
/// all the local bookkeeping — PlanningCaseSite soft-delete, PlanningCase
/// retraction, Compliance soft-delete — still runs. That is the same trick
/// <c>WorkerTagAssignmentTest</c> uses.
///
/// NB: the fixture's container/DBs are shared across the tests in the fixture
/// (the base [SetUp] only EnsureCreated()s the schema, so rows accumulate), so
/// every test seeds its own entities and scopes every assertion to those ids.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarOccurrenceRetractionTests : TestBaseSetup
{
    private static readonly DateTime SeriesStart =
        new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public async Task CleanRetractionTables()
    {
        // FK-safe cleanup so accumulated rows from earlier tests in this fixture
        // cannot be picked up by a planning id that restarts low.
        BackendConfigurationPnDbContext!.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.PlanningCaseSites.RemoveRange(
            ItemsPlanningPnDbContext.PlanningCaseSites);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.PlanningCases.RemoveRange(
            ItemsPlanningPnDbContext.PlanningCases);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        MicrotingDbContext!.Cases.RemoveRange(MicrotingDbContext.Cases);
        await MicrotingDbContext.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Seeds Area/Property/AreaRule/Planning/AreaRulePlanning.</summary>
    private async Task<(AreaRulePlanning Arp, Planning Planning)> SeedEvent()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"RetractionTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = SeriesStart, DayOfWeek = DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = SeriesStart, Status = true,
            RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (arp, planning);
    }

    /// <summary>Creates an SDK Site and returns its generated id.</summary>
    private async Task<int> SeedSdkSite()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"retraction-site-{Guid.NewGuid()}", MicrotingUid = null,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>
    /// Seeds one deployed occurrence: SDK Case (MicrotingUid null so CaseDelete
    /// is skipped) + PlanningCase + PlanningCaseSite + Compliance.
    /// </summary>
    private async Task<(int SdkCaseId, int ComplianceId, int PlanningCaseId, int PlanningCaseSiteId)>
        SeedDeployedOccurrence(int planningId, DateTime deadline, int status)
    {
        var siteId = await SeedSdkSite();

        var sdkCase = new SdkCase
        {
            SiteId = siteId, Status = status, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = planningId, Status = status, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = planningId, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = siteId, MicrotingSdkeFormId = 0,
            MicrotingSdkCaseId = sdkCase.Id, Status = status,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var complianceId = await SeedCompliance(planningId, deadline, sdkCase.Id);

        return (sdkCase.Id, complianceId, planningCase.Id, planningCaseSite.Id);
    }

    /// <summary>
    /// Seeds a bare Compliance row at the given deadline. Deadlines must be
    /// distinct: Compliances carries a UNIQUE index on (PlanningId, Deadline),
    /// so two occurrences on the same DAY need different times of day.
    /// </summary>
    private async Task<int> SeedCompliance(int planningId, DateTime deadline, int sdkCaseId)
    {
        var compliance = new BcCompliance
        {
            PlanningId = planningId,
            Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCaseId, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private async Task<CalendarOccurrenceRetractionService> BuildService()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        return new CalendarOccurrenceRetractionService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            NullLogger<CalendarOccurrenceRetractionService>.Instance);
    }

    private async Task<string> ComplianceWorkflowState(int complianceId) =>
        (await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == complianceId)).WorkflowState;

    // ─────────────────────────────────────────────────────────────────────────
    // R2 — a COMPLETED occurrence is never touched
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task RetractNonCompleted_LeavesCompletedOccurrenceAndItsComplianceRowIntact()
    {
        var (arp, planning) = await SeedEvent();

        // Status 100 + MicrotingSdkCaseId > 0 == completed. Nothing about this
        // occurrence may change: the Compliance row is the only record tying the
        // rotation date to the answered case.
        var completed = await SeedDeployedOccurrence(
            planning.Id, SeriesStart.AddDays(7).AddHours(9), status: 100);

        var service = await BuildService();
        var result = await service.RetractNonCompletedOccurrencesAsync(arp);

        var pcs = await ItemsPlanningPnDbContext!.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == completed.PlanningCaseSiteId);
        var pc = await ItemsPlanningPnDbContext.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == completed.PlanningCaseId);

        var complianceState = await ComplianceWorkflowState(completed.ComplianceId);

        Assert.Multiple(() =>
        {
            Assert.That(complianceState,
                Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "a completed occurrence's Compliance row is the only link to its answered SDK case — it must survive");
            Assert.That(pcs.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "the completed occurrence's PlanningCaseSite must not be soft-deleted");
            Assert.That(pc.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted),
                "the completed occurrence's PlanningCase must not be retracted");
            Assert.That(result.CompletedPreserved, Is.EqualTo(1),
                "the completed occurrence must be counted as preserved, not retracted");
            Assert.That(result.Retracted, Is.EqualTo(0));
            Assert.That(result.Failed, Is.EqualTo(0));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A NON-completed occurrence is fully retracted
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task RetractNonCompleted_RetractsOpenOccurrenceAndItsPlanningCaseChain()
    {
        var (arp, planning) = await SeedEvent();

        // Status 66 — deployed, opened on a device, not answered.
        var open = await SeedDeployedOccurrence(
            planning.Id, SeriesStart.AddDays(7).AddHours(9), status: 66);

        var service = await BuildService();
        var result = await service.RetractNonCompletedOccurrencesAsync(arp);

        var pcs = await ItemsPlanningPnDbContext!.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == open.PlanningCaseSiteId);
        var pc = await ItemsPlanningPnDbContext.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == open.PlanningCaseId);

        var complianceState = await ComplianceWorkflowState(open.ComplianceId);

        Assert.Multiple(() =>
        {
            Assert.That(complianceState,
                Is.EqualTo(Constants.WorkflowStates.Removed),
                "an open occurrence's Compliance row must be soft-deleted");
            Assert.That(pcs.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the open occurrence's PlanningCaseSite must be soft-deleted");
            Assert.That(pc.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Retracted),
                "with no live sibling site left, the owning PlanningCase must be retracted");
            Assert.That(result.Retracted, Is.EqualTo(1));
            Assert.That(result.CompletedPreserved, Is.EqualTo(0));
            Assert.That(result.Failed, Is.EqualTo(0));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate half 1 — MicrotingSdkCaseId == 0 is NOT completed
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task RetractNonCompleted_TreatsZeroSdkCaseIdAsNotCompleted()
    {
        var (arp, planning) = await SeedEvent();

        // A row released back to 0 by the reconciliation engine (its owner was
        // unassigned, other workers remain). It has no backing case at all, so
        // it can never satisfy Status == 100 — it is an OPEN occurrence and must
        // be retracted, with no SDK work to do.
        var releasedComplianceId = await SeedCompliance(
            planning.Id, SeriesStart.AddDays(7).AddHours(9), sdkCaseId: 0);

        var service = await BuildService();
        var result = await service.RetractNonCompletedOccurrencesAsync(arp);

        var complianceState = await ComplianceWorkflowState(releasedComplianceId);

        Assert.Multiple(() =>
        {
            Assert.That(complianceState,
                Is.EqualTo(Constants.WorkflowStates.Removed),
                "MicrotingSdkCaseId == 0 is not-completed, so the row must be soft-deleted");
            Assert.That(result.Retracted, Is.EqualTo(1));
            Assert.That(result.CompletedPreserved, Is.EqualTo(0),
                "a row with no backing case must never be counted as completed");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate half 2 — Status != 100 is NOT completed, right up to the boundary
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task RetractNonCompleted_TreatsStatusJustBelowCompletedAsNotCompleted()
    {
        var (arp, planning) = await SeedEvent();

        // 99 is the tightest possible probe of the "== 100" gate: anything that
        // reached for ">= some threshold" or for MicrotingSdkCaseDoneAt instead
        // would wrongly preserve this row.
        var almost = await SeedDeployedOccurrence(
            planning.Id, SeriesStart.AddDays(7).AddHours(9), status: 99);

        var service = await BuildService();
        var result = await service.RetractNonCompletedOccurrencesAsync(arp);

        var complianceState = await ComplianceWorkflowState(almost.ComplianceId);

        Assert.Multiple(() =>
        {
            Assert.That(complianceState,
                Is.EqualTo(Constants.WorkflowStates.Removed),
                "Status 99 is not completed — the occurrence must be retracted");
            Assert.That(result.Retracted, Is.EqualTo(1));
            Assert.That(result.CompletedPreserved, Is.EqualTo(0));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // fromDate bound
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task RetractNonCompleted_RespectsFromDateBound_AndIncludesTheWholeBoundaryDay()
    {
        var (arp, planning) = await SeedEvent();

        var cutoff = SeriesStart.AddDays(14);

        // Strictly before the cutoff — history the caller asked to keep.
        var before = await SeedDeployedOccurrence(
            planning.Id, cutoff.AddDays(-7).AddHours(9), status: 66);

        // ON the cutoff day but at 09:00, i.e. after the day's midnight. A bound
        // applied at instant granularity instead of day granularity would skip
        // this one, silently leaving an open occurrence on the boundary date.
        var onBoundary = await SeedDeployedOccurrence(
            planning.Id, cutoff.AddHours(9), status: 66);

        // After the cutoff.
        var after = await SeedDeployedOccurrence(
            planning.Id, cutoff.AddDays(7).AddHours(9), status: 66);

        var service = await BuildService();
        var result = await service.RetractNonCompletedOccurrencesAsync(arp, cutoff);

        var beforeState = await ComplianceWorkflowState(before.ComplianceId);
        var onBoundaryState = await ComplianceWorkflowState(onBoundary.ComplianceId);
        var afterState = await ComplianceWorkflowState(after.ComplianceId);

        Assert.Multiple(() =>
        {
            Assert.That(beforeState,
                Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "an occurrence before fromDate is out of scope and must be untouched");
            Assert.That(onBoundaryState,
                Is.EqualTo(Constants.WorkflowStates.Removed),
                "fromDate is inclusive at DAY granularity — 09:00 on the boundary day is in scope");
            Assert.That(afterState,
                Is.EqualTo(Constants.WorkflowStates.Removed),
                "an occurrence after fromDate must be retracted");
            Assert.That(result.Retracted, Is.EqualTo(2),
                "exactly the two in-scope occurrences are retracted");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The completion lookup is ONE SDK query for the whole batch
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task RetractNonCompleted_ResolvesCompletionWithASingleSdkCasesQuery()
    {
        var (arp, planning) = await SeedEvent();

        // Three occurrences, three distinct SDK cases. A per-row completion read
        // would issue three SELECTs against `Cases`; the batched HashSet issues
        // one. On a long past range (#1122 backfills daily series over months)
        // that difference is the whole cost of the operation.
        await SeedDeployedOccurrence(planning.Id, SeriesStart.AddDays(7).AddHours(9), status: 66);
        await SeedDeployedOccurrence(planning.Id, SeriesStart.AddDays(14).AddHours(9), status: 100);
        await SeedDeployedOccurrence(planning.Id, SeriesStart.AddDays(21).AddHours(9), status: 66);

        var service = await BuildService();

        // Scope the counter to THIS fixture's container. Fixtures run in
        // parallel against separate MariaDB containers on separate host ports,
        // and the EF DiagnosticListener is process-wide, so the port is what
        // separates our commands from a neighbouring fixture's.
        var port = PortOf(MicrotingDbContext!.Database.GetConnectionString());
        Assert.That(port, Is.Not.Null, "could not read the test container port from the SDK connection string");

        using var counter = new SdkCasesQueryCounter(port!);

        // Self-check first: prove the listener/filter chain actually observes a
        // `Cases` SELECT on this connection, so a later count of 0 can only mean
        // the service skipped the query, never that the plumbing is broken.
        _ = await MicrotingDbContext.Cases.AsNoTracking().Select(c => c.Id).ToListAsync();
        Assert.That(counter.Count, Is.GreaterThanOrEqualTo(1),
            "self-check: the diagnostic listener must see a `Cases` SELECT on this container");

        counter.Reset();
        var result = await service.RetractNonCompletedOccurrencesAsync(arp);

        Assert.Multiple(() =>
        {
            Assert.That(counter.Count, Is.EqualTo(1),
                "completion must be resolved with ONE batched SELECT over `Cases`, not one per Compliance row");
            Assert.That(result.Retracted, Is.EqualTo(2));
            Assert.That(result.CompletedPreserved, Is.EqualTo(1));
        });
    }

    private static string? PortOf(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var match = Regex.Match(connectionString, @"Port\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Counts EF Core commands that SELECT from the SDK <c>Cases</c> table on one
    /// specific MariaDB container. EF Core publishes every command through the
    /// process-wide "Microsoft.EntityFrameworkCore" DiagnosticListener, which is
    /// the only seam available here — the service resolves its own SdkDbContext
    /// from <c>core.DbContextHelper</c>, so no interceptor can be injected into it.
    ///
    /// The <c>`Cases`</c> match is exact enough on purpose: <c>`PlanningCases`</c>
    /// and <c>`PlanningCaseSites`</c> do not contain a backtick immediately before
    /// "Cases", so the items-planning bookkeeping the service also performs is not
    /// counted.
    /// </summary>
    private sealed class SdkCasesQueryCounter
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string _port;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _allListeners;
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public SdkCasesQueryCounter(string port)
        {
            _port = port;
            _allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void Reset() => Volatile.Write(ref _count, 0);

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != "Microsoft.EntityFrameworkCore") return;
            lock (_subscriptions)
            {
                _subscriptions.Add(listener.Subscribe(this));
            }
        }

        public void OnNext(KeyValuePair<string, object?> evt)
        {
            if (evt.Key != RelationalEventId.CommandExecuting.Name) return;
            if (evt.Value is not CommandEventData data) return;

            var text = data.Command.CommandText ?? string.Empty;
            if (!text.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) return;
            if (!text.Contains("`Cases`", StringComparison.Ordinal)) return;

            var connectionString = data.Command.Connection?.ConnectionString ?? string.Empty;
            if (!connectionString.Contains(_port, StringComparison.Ordinal)) return;

            Interlocked.Increment(ref _count);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (_subscriptions)
            {
                foreach (var subscription in _subscriptions) subscription.Dispose();
                _subscriptions.Clear();
            }
            _allListeners.Dispose();
        }
    }
}
