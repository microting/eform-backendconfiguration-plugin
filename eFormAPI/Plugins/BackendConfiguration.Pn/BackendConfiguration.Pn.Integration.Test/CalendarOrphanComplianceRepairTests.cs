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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.CalendarOrphanComplianceRepair;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1383 — the orphaned off-pattern compliance repair
/// (<see cref="CalendarOrphanComplianceRepairService"/>).
///
/// The rule is "1st Wednesday, every month" from Wed 7 Jan 2026 — the shape the #1294
/// conversion left a legacy "on the 7th" task in. An orphan is a live compliance on the
/// legacy 7th whose SDK case is gone. "Today" is pinned (2026-09-29, Copenhagen) through
/// the service's clock seam; the months used are before it unless a test says otherwise.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarOrphanComplianceRepairTests : TestBaseSetup
{
    private static readonly DateTime Now = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>SDK Case.Status of a live, unanswered case (SqlController.CaseCreate).</summary>
    private const int OpenCaseStatus = 33;

    private CalendarOrphanComplianceRepairService _sut = null!;
    private BackendConfigurationCalendarService _calendar = null!;
    private Microting.eForm.Infrastructure.Data.Entities.Site _site = null!;

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0);

    [SetUp]
    public async Task SetUpRepair()
    {
        var ctx = BackendConfigurationPnDbContext!;
        ctx.Compliances.RemoveRange(ctx.Compliances);
        ctx.CalendarOccurrenceExceptionSites.RemoveRange(ctx.CalendarOccurrenceExceptionSites);
        await ctx.SaveChangesAsync();
        ctx.CalendarOccurrenceExceptions.RemoveRange(ctx.CalendarOccurrenceExceptions);
        ctx.CalendarConfigurations.RemoveRange(ctx.CalendarConfigurations);
        await ctx.SaveChangesAsync();
        ctx.CalendarBoards.RemoveRange(ctx.CalendarBoards);
        ctx.AreaRulePlannings.RemoveRange(ctx.AreaRulePlannings);
        await ctx.SaveChangesAsync();
        ctx.AreaRules.RemoveRange(ctx.AreaRules);
        await ctx.SaveChangesAsync();
        ctx.Areas.RemoveRange(ctx.Areas);
        ctx.Properties.RemoveRange(ctx.Properties);
        await ctx.SaveChangesAsync();

        var ip = ItemsPlanningPnDbContext!;
        ip.PlanningCaseSites.RemoveRange(ip.PlanningCaseSites);
        await ip.SaveChangesAsync();
        ip.PlanningCases.RemoveRange(ip.PlanningCases);
        await ip.SaveChangesAsync();
        ip.Plannings.RemoveRange(ip.Plannings);
        await ip.SaveChangesAsync();

        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var language = await MicrotingDbContext!.Languages.FirstAsync();
        _site = new Microting.eForm.Infrastructure.Data.Entities.Site
        {
            Name = "Device A", MicrotingUid = Random.Shared.Next(100_000, 900_000), LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(_site);
        await MicrotingDbContext.SaveChangesAsync();

        _sut = new CalendarOrphanComplianceRepairService(ctx, ip, coreHelper,
            TestContextLogger<CalendarOrphanComplianceRepairService>.Instance)
        {
            UtcNow = () => Now
        };

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage()
            .Returns(Task.FromResult(new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));
        _calendar = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService, ctx, coreHelper,
            Substitute.For<IEventDeployService>(), ip, Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(), Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(), Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, ctx));
    }

    // ── Seeding ─────────────────────────────────────────────────────────────

    private sealed record Rule(int ArpId, int PlanningId, int PropertyId);

    /// <summary>"1st Wednesday, every month" from Wed 7 Jan 2026 (legacy day-of-month 7).</summary>
    private async Task<Rule> SeedMonthlyRuleAsync()
    {
        var ctx = BackendConfigurationPnDbContext!;
        var property = new Property
        {
            Name = $"Example Property {Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(ctx);
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(ctx);
        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(ctx);

        var planning = new Planning
        {
            Enabled = true, RepeatType = RepeatType.Month, RepeatEvery = 1, StartDate = D(2026, 1, 7),
            DayOfMonth = 7, DayOfWeek = DayOfWeek.Wednesday, RepeatOrdinalWeek = 1,
            NextExecutionTime = D(2026, 10, 7), RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = D(2026, 1, 7), Status = true,
            RepeatType = 3, RepeatEvery = 1, RepeatOrdinalWeek = 1, DayOfWeek = (int)DayOfWeek.Wednesday,
            ComplianceEnabled = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(ctx);
        return new Rule(arp.Id, planning.Id, property.Id);
    }

    private async Task<Case> SeedCaseAsync(int status = OpenCaseStatus, DateTime? doneAt = null,
        string workflowState = Constants.WorkflowStates.Created)
    {
        var sdkCase = new Case
        {
            SiteId = _site.Id, Status = status, DoneAt = doneAt, MicrotingUid = null, WorkflowState = workflowState
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase;
    }

    private Task<Case> SeedRemovedCaseAsync() => SeedCaseAsync(workflowState: Constants.WorkflowStates.Removed);

    /// <summary>What Compliance.PlanningCaseSiteId holds.</summary>
    private enum StoredLink
    {
        /// <summary>The PlanningCase id (rows since late 2025).</summary>
        PlanningCaseId,
        /// <summary>The own PlanningCaseSite's id (rows from 2022 to late 2025).</summary>
        OwnPlanningCaseSiteId,
        /// <summary>0 — never linked.</summary>
        None
    }

    /// <summary>
    /// A compliance and, as production deploys it, a PlanningCase with a PlanningCaseSite for
    /// its own case plus one per <paramref name="otherSiteCaseIds"/>. <paramref name="link"/>
    /// says what Compliance.PlanningCaseSiteId stores; <paramref name="withPlanningCase"/>
    /// false seeds no PlanningCase at all.
    /// </summary>
    private async Task<Compliance> SeedComplianceAsync(Rule rule, DateTime deadline, int sdkCaseId,
        int[]? otherSiteCaseIds = null, StoredLink link = StoredLink.PlanningCaseId, bool withPlanningCase = true)
    {
        var compliance = new Compliance
        {
            ItemName = "Monthly check", PlanningId = rule.PlanningId, PropertyId = rule.PropertyId,
            Deadline = deadline, StartDate = deadline.AddMonths(-1), MicrotingSdkCaseId = sdkCaseId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        if (!withPlanningCase)
        {
            return compliance;
        }

        var planningCase = new PlanningCase
        {
            PlanningId = rule.PlanningId, Status = 66, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        var sites = (otherSiteCaseIds ?? []).Prepend(sdkCaseId)
            .Select(caseId => new PlanningCaseSite
            {
                PlanningId = rule.PlanningId, PlanningCaseId = planningCase.Id, MicrotingSdkSiteId = _site.Id,
                MicrotingSdkeFormId = 0, MicrotingSdkCaseId = caseId, Status = 66,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            })
            .ToList();
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddRangeAsync(sites);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        compliance.PlanningCaseSiteId = link switch
        {
            StoredLink.PlanningCaseId => planningCase.Id,
            StoredLink.OwnPlanningCaseSiteId => sites[0].Id,
            _ => 0
        };
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance;
    }

    private async Task<OrphanOffPatternComplianceRepairPlanModel> DryRunAsync()
    {
        var result = await _sut.DryRunAsync();
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task<OrphanOffPatternComplianceRepairRunResultModel> RunAsync()
    {
        var plan = await DryRunAsync();
        var result = await _sut.RunAsync(plan.PlanHash);
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task<Compliance> ComplianceAsync(int id)
        => await BackendConfigurationPnDbContext!.Compliances.AsNoTracking().SingleAsync(x => x.Id == id);

    /// <summary>The planning's tile dates GetTasksForWeek renders for every day of the month (admin view).</summary>
    private async Task<List<DateTime>> TilesInMonthAsync(Rule rule, int year, int month)
    {
        var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monday = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var dates = new List<DateTime>();
        for (; monday < first.AddMonths(1); monday = monday.AddDays(7))
        {
            var week = await _calendar.GetTasksForWeek(new CalendarTaskRequestModel
            {
                PropertyId = rule.PropertyId,
                WeekStart = monday.ToString("yyyy-MM-ddT00:00:00Z", CultureInfo.InvariantCulture),
                WeekEnd = monday.AddDays(6).ToString("yyyy-MM-ddT23:59:00Z", CultureInfo.InvariantCulture),
                ActionableOnly = false,
                BoardIds = [], TagNames = [], SiteIds = []
            });
            Assert.That(week.Success, Is.True, week.Message);
            dates.AddRange(week.Model.Where(t => t.PlanningId == rule.PlanningId)
                .Select(t => DateTime.ParseExact(t.TaskDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        return dates.Where(d => d.Year == year && d.Month == month).OrderBy(d => d).ToList();
    }

    // ── The issue's case ────────────────────────────────────────────────────

    /// <summary>
    /// June 2026: the rule's 1st Wednesday is the 3rd; the orphan sits on the legacy
    /// Sun 7 June with its SDK case removed. Both render — one month, two tiles.
    /// </summary>
    [Test]
    public async Task DryRun_ListsTheOrphan_AndWritesNothing()
    {
        var rule = await SeedMonthlyRuleAsync();
        var orphan = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);
        var versionBefore = (await ComplianceAsync(orphan.Id)).Version;

        var plan = await DryRunAsync();
        var replan = await DryRunAsync();

        var after = await ComplianceAsync(orphan.Id);
        Assert.Multiple(() =>
        {
            Assert.That(plan.PlanHash, Is.EqualTo(replan.PlanHash), "the plan is deterministic");
            Assert.That(plan.Closures, Has.Count.EqualTo(1));
            Assert.That(plan.Closures[0].ComplianceId, Is.EqualTo(orphan.Id));
            Assert.That(plan.Closures[0].AreaRulePlanningId, Is.EqualTo(rule.ArpId));
            Assert.That(plan.Closures[0].OnPatternDate, Is.EqualTo(D(2026, 6, 3)));
            Assert.That(plan.Closures[0].SdkCaseState, Is.EqualTo("Removed"));
            Assert.That(plan.Kept, Is.Empty);
            Assert.That(after.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created), "a dry run writes nothing");
            Assert.That(after.Version, Is.EqualTo(versionBefore));
        });
    }

    [Test]
    public async Task Run_ClosesTheOrphan_AndItsMonthRendersOneTile()
    {
        var rule = await SeedMonthlyRuleAsync();
        var orphan = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);
        Assert.That(await TilesInMonthAsync(rule, 2026, 6), Is.EqualTo(new[] { D(2026, 6, 3), D(2026, 6, 7) }),
            "precondition (#1383): the orphan renders next to the rule's own occurrence");

        var result = await RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ClosedCompliances, Is.EqualTo(1));
            Assert.That(result.Skipped, Is.Empty);
            Assert.That(result.Failures, Is.Empty);
        });
        Assert.That((await ComplianceAsync(orphan.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await TilesInMonthAsync(rule, 2026, 6), Is.EqualTo(new[] { D(2026, 6, 3) }),
            "one monthly period, one tile");
    }

    [Test]
    public async Task SecondRun_HasNothingToClose_AndIsRefused()
    {
        var rule = await SeedMonthlyRuleAsync();
        var orphan = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);
        await RunAsync();
        var versionAfterFirstRun = (await ComplianceAsync(orphan.Id)).Version;

        var again = await DryRunAsync();
        var second = await _sut.RunAsync(again.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(again.Closures, Is.Empty, "the closed row dropped out of the plan");
            Assert.That(second.Success, Is.False, "an empty plan is refused");
            Assert.That((await ComplianceAsync(orphan.Id)).Version, Is.EqualTo(versionAfterFirstRun));
        });
    }

    // ── Refusals ────────────────────────────────────────────────────────────

    [Test]
    public async Task Run_WithAStaleOrMissingHash_IsRefused_AndWritesNothing()
    {
        var rule = await SeedMonthlyRuleAsync();
        var june = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);
        var reviewed = await DryRunAsync();

        // The data changes after the review: another orphan appears.
        var may = await SeedComplianceAsync(rule, D(2026, 5, 7), (await SeedRemovedCaseAsync()).Id);

        var stale = await _sut.RunAsync(reviewed.PlanHash);
        var missing = await _sut.RunAsync("");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(stale.Success, Is.False, "the reviewed plan is no longer the current plan");
            Assert.That(missing.Success, Is.False, "a run without a reviewed hash is refused");
            Assert.That((await ComplianceAsync(june.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That((await ComplianceAsync(may.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        });
    }

    // ── What is never closed ────────────────────────────────────────────────

    /// <summary>
    /// Only the June orphan qualifies. An orphan ON the pattern (1 July) is that month's
    /// only tile, and an off-pattern row with a live open case (7 August) is the #1294
    /// repair's business — neither is even listed. A completed orphan (7 March) and a
    /// future one (7 November) are listed as kept.
    /// </summary>
    [Test]
    public async Task OnPatternAndLiveCaseCompliances_AreUntouched_AndCompletedOrFutureOrphansAreKept()
    {
        var rule = await SeedMonthlyRuleAsync();
        var june = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);
        var onPattern = await SeedComplianceAsync(rule, D(2026, 7, 1), (await SeedRemovedCaseAsync()).Id);
        var liveCase = await SeedComplianceAsync(rule, D(2026, 8, 7), (await SeedCaseAsync()).Id);
        var completed = await SeedComplianceAsync(rule, D(2026, 3, 7),
            (await SeedCaseAsync(status: 100, doneAt: D(2026, 3, 7))).Id);
        var future = await SeedComplianceAsync(rule, D(2026, 11, 7), (await SeedRemovedCaseAsync()).Id);
        var untouched = new[] { onPattern, liveCase, completed, future };
        var versionsBefore = new Dictionary<int, int>();
        foreach (var c in untouched)
        {
            versionsBefore[c.Id] = (await ComplianceAsync(c.Id)).Version;
        }

        var result = await RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Plan.Closures.Select(x => x.ComplianceId), Is.EqualTo(new[] { june.Id }));
            Assert.That(result.Plan.Kept.Select(x => (x.ComplianceId, string.Join(",", x.Reasons))),
                Is.EquivalentTo(new[]
                {
                    (completed.Id, CalendarOrphanComplianceRepairService.ReasonCaseCompleted),
                    (future.Id, CalendarOrphanComplianceRepairService.ReasonNotPast)
                }));
        });
        foreach (var c in untouched)
        {
            var row = await ComplianceAsync(c.Id);
            Assert.That(row.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created), $"compliance on {c.Deadline:d}");
            Assert.That(row.Version, Is.EqualTo(versionsBefore[c.Id]), $"compliance on {c.Deadline:d}");
        }
        Assert.That((await ComplianceAsync(june.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    /// <summary>
    /// Orphans whose occurrence is not provably dead, or whose month has no single
    /// on-pattern occurrence, stay where they are, with the reason.
    /// </summary>
    [Test]
    public async Task OrphansThatAreNotProvablyDead_AreKept_WithTheirReason()
    {
        var rule = await SeedMonthlyRuleAsync();

        // April: a "this" edit of the month's occurrence — a person touched the month.
        var april = await SeedComplianceAsync(rule, D(2026, 4, 7), (await SeedRemovedCaseAsync()).Id);
        await new CalendarOccurrenceException
        {
            AreaRulePlanningId = rule.ArpId, OriginalDate = D(2026, 4, 1), NewDate = D(2026, 4, 2),
            CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        // December 2025: before the series starts — the rule has no occurrence that month.
        var december = await SeedComplianceAsync(rule, D(2025, 12, 7), (await SeedRemovedCaseAsync()).Id);

        // May: the same occurrence is still open on another device.
        var mayOwn = await SeedRemovedCaseAsync();
        var may = await SeedComplianceAsync(rule, D(2026, 5, 7), mayOwn.Id, [(await SeedCaseAsync()).Id]);

        // September: the same occurrence was completed on another device.
        var septemberOwn = await SeedRemovedCaseAsync();
        var september = await SeedComplianceAsync(rule, D(2026, 9, 7), septemberOwn.Id,
            [(await SeedCaseAsync(status: 100, doneAt: D(2026, 9, 7))).Id]);

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(plan.Closures, Is.Empty);
            Assert.That(plan.Kept.Select(x => (x.ComplianceId, string.Join(",", x.Reasons))),
                Is.EquivalentTo(new[]
                {
                    (april.Id, CalendarOrphanComplianceRepairService.ReasonExceptionInMonth),
                    (december.Id, CalendarOrphanComplianceRepairService.ReasonNoOnPatternOccurrence),
                    (may.Id, CalendarOrphanComplianceRepairService.ReasonOpenElsewhere),
                    (september.Id, CalendarOrphanComplianceRepairService.ReasonCompletedElsewhere)
                }));
            Assert.That(run.Success, Is.False, "nothing to close");
            foreach (var c in new[] { april, december, may, september })
            {
                Assert.That((await ComplianceAsync(c.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            }
        });
    }

    /// <summary>
    /// A legacy row without Compliance.PlanningCaseSiteId is resolved through the
    /// PlanningCaseSite of its own case: dead on every site → closed; still open on another
    /// site → kept. A row that resolves to no PlanningCase at all cannot be proven dead.
    /// </summary>
    [Test]
    public async Task UnlinkedOrphans_AreResolvedThroughTheirOwnCase()
    {
        var rule = await SeedMonthlyRuleAsync();
        var deadEverywhere = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id,
            [(await SeedRemovedCaseAsync()).Id], link: StoredLink.None);
        var openOnAnotherSite = await SeedComplianceAsync(rule, D(2026, 7, 7), (await SeedRemovedCaseAsync()).Id,
            [(await SeedCaseAsync()).Id], link: StoredLink.None);
        var noPlanningCase = await SeedComplianceAsync(rule, D(2026, 5, 7), (await SeedRemovedCaseAsync()).Id,
            withPlanningCase: false);

        var result = await RunAsync();

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Plan.Closures.Select(x => x.ComplianceId), Is.EqualTo(new[] { deadEverywhere.Id }));
            Assert.That(result.Plan.Kept.Select(x => (x.ComplianceId, string.Join(",", x.Reasons))),
                Is.EquivalentTo(new[]
                {
                    (openOnAnotherSite.Id, CalendarOrphanComplianceRepairService.ReasonOpenElsewhere),
                    (noPlanningCase.Id, CalendarOrphanComplianceRepairService.ReasonNoPlanningCaseLink)
                }));
            Assert.That((await ComplianceAsync(deadEverywhere.Id)).WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That((await ComplianceAsync(openOnAnotherSite.Id)).WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That((await ComplianceAsync(noPlanningCase.Id)).WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Created));
        });
    }

    /// <summary>
    /// Rows from 2022 to late 2025 store their own PlanningCaseSite's id, not the
    /// PlanningCase id. The PlanningCase is resolved from the own SDK case either way, so an
    /// old row still sees the other site's open case (kept) and an old row dead on every
    /// site is closed. A stored value that matches neither meaning is a mismatch (kept).
    /// </summary>
    [Test]
    public async Task OldRowsStoringTheirPlanningCaseSiteId_AreResolvedThroughTheirOwnCase()
    {
        var rule = await SeedMonthlyRuleAsync();
        var openOnAnotherSite = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id,
            [(await SeedCaseAsync()).Id], StoredLink.OwnPlanningCaseSiteId);
        var deadEverywhere = await SeedComplianceAsync(rule, D(2026, 7, 7), (await SeedRemovedCaseAsync()).Id,
            [(await SeedRemovedCaseAsync()).Id], StoredLink.OwnPlanningCaseSiteId);
        var mismatch = await SeedComplianceAsync(rule, D(2026, 5, 7), (await SeedRemovedCaseAsync()).Id);
        await BackendConfigurationPnDbContext!.Compliances.Where(x => x.Id == mismatch.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlanningCaseSiteId, int.MaxValue));

        var plan = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Closures.Select(x => x.ComplianceId), Is.EqualTo(new[] { deadEverywhere.Id }));
            Assert.That(plan.Kept.Select(x => (x.ComplianceId, string.Join(",", x.Reasons))),
                Is.EquivalentTo(new[]
                {
                    (openOnAnotherSite.Id, CalendarOrphanComplianceRepairService.ReasonOpenElsewhere),
                    (mismatch.Id, CalendarOrphanComplianceRepairService.ReasonPlanningCaseLinkMismatch)
                }));
        });
    }

    /// <summary>
    /// The PlanningCaseSite of the row's own case points at a PlanningCase that does not
    /// exist: nothing proves the occurrence dead elsewhere, so the row is kept.
    /// </summary>
    [Test]
    public async Task LinkedRowWhosePlanningCaseDoesNotExist_IsKept()
    {
        var rule = await SeedMonthlyRuleAsync();
        var ownCase = await SeedRemovedCaseAsync();
        var june = await SeedComplianceAsync(rule, D(2026, 6, 7), ownCase.Id);
        const int missingPlanningCaseId = int.MaxValue;
        await ItemsPlanningPnDbContext!.PlanningCaseSites.Where(x => x.MicrotingSdkCaseId == ownCase.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlanningCaseId, missingPlanningCaseId));
        await BackendConfigurationPnDbContext!.Compliances.Where(x => x.Id == june.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlanningCaseSiteId, missingPlanningCaseId));

        var plan = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Closures, Is.Empty);
            Assert.That(plan.Kept.Single().ComplianceId, Is.EqualTo(june.Id));
            Assert.That(plan.Kept.Single().Reasons,
                Is.EqualTo(new[] { CalendarOrphanComplianceRepairService.ReasonNoPlanningCaseLink }));
        });
    }

    /// <summary>
    /// The closed orphan was the property's only overdue compliance: its traffic light goes
    /// back to green. On a property that still has an overdue row it stays red.
    /// </summary>
    [Test]
    public async Task Run_RecomputesThePropertyComplianceStatus()
    {
        var clean = await SeedMonthlyRuleAsync();
        var stillOverdue = await SeedMonthlyRuleAsync();
        await SeedComplianceAsync(clean, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);
        await SeedComplianceAsync(stillOverdue, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);
        await SeedComplianceAsync(stillOverdue, D(2026, 8, 7), (await SeedCaseAsync()).Id);
        await BackendConfigurationPnDbContext!.Properties
            .Where(x => x.Id == clean.PropertyId || x.Id == stillOverdue.PropertyId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ComplianceStatus, 2).SetProperty(x => x.ComplianceStatusThirty, 2));
        BackendConfigurationPnDbContext.ChangeTracker.Clear();

        var result = await RunAsync();

        var properties = await BackendConfigurationPnDbContext.Properties.AsNoTracking()
            .Where(x => x.Id == clean.PropertyId || x.Id == stillOverdue.PropertyId)
            .ToDictionaryAsync(x => x.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.ClosedCompliances, Is.EqualTo(2));
            Assert.That(result.Plan.Closures.Select(x => x.PropertyId),
                Is.EquivalentTo(new[] { clean.PropertyId, stillOverdue.PropertyId }));
            Assert.That(properties[clean.PropertyId].ComplianceStatus, Is.EqualTo(0));
            Assert.That(properties[clean.PropertyId].ComplianceStatusThirty, Is.EqualTo(0));
            Assert.That(properties[stillOverdue.PropertyId].ComplianceStatus, Is.EqualTo(2),
                "the open 7 August row is still overdue");
            Assert.That(properties[stillOverdue.PropertyId].ComplianceStatusThirty, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// The closed orphan was the property's only overdue compliance, but the on-pattern
    /// 7 October row is due within 30 days of the pinned today: the 30-day light is
    /// recomputed to 1 (due soon), not left at the stale 2 the orphan caused.
    /// </summary>
    [Test]
    public async Task Run_WithAComplianceDueWithinThirtyDays_SetsTheThirtyDayStatusToDueSoon()
    {
        var rule = await SeedMonthlyRuleAsync();
        await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);
        await SeedComplianceAsync(rule, D(2026, 10, 7), (await SeedCaseAsync()).Id);
        await BackendConfigurationPnDbContext!.Properties.Where(x => x.Id == rule.PropertyId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ComplianceStatus, 2).SetProperty(x => x.ComplianceStatusThirty, 2));
        BackendConfigurationPnDbContext.ChangeTracker.Clear();

        var result = await RunAsync();

        var property = await BackendConfigurationPnDbContext.Properties.AsNoTracking()
            .SingleAsync(x => x.Id == rule.PropertyId);
        Assert.Multiple(() =>
        {
            Assert.That(result.ClosedCompliances, Is.EqualTo(1));
            Assert.That(property.ComplianceStatus, Is.EqualTo(0), "no overdue compliance is left");
            Assert.That(property.ComplianceStatusThirty, Is.EqualTo(1), "the 7 October row is due within 30 days");
        });
    }

    /// <summary>
    /// "Delete this and following" from Wed 3 June sets Planning.RepeatUntil to 2 June, so
    /// the week view paints nothing on the pattern in June. The earlier Mon 1 June row is
    /// then the month's only tile and must stay — the month must not become empty.
    /// </summary>
    [Test]
    public async Task RepeatUntilBeforeTheOnPatternDate_KeepsTheEarlierCompliance()
    {
        var rule = await SeedMonthlyRuleAsync();
        await ItemsPlanningPnDbContext!.Plannings.Where(x => x.Id == rule.PlanningId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RepeatUntil, D(2026, 6, 2)));
        ItemsPlanningPnDbContext.ChangeTracker.Clear();
        var june = await SeedComplianceAsync(rule, D(2026, 6, 1), (await SeedRemovedCaseAsync()).Id);
        Assert.That(await TilesInMonthAsync(rule, 2026, 6), Is.EqualTo(new[] { D(2026, 6, 1) }),
            "precondition: the row is June's only tile");

        var plan = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Closures, Is.Empty);
            Assert.That(plan.Kept.Single().ComplianceId, Is.EqualTo(june.Id));
            Assert.That(plan.Kept.Single().Reasons,
                Is.EqualTo(new[] { CalendarOrphanComplianceRepairService.ReasonNoOnPatternOccurrence }));
        });
    }

    [Test]
    public async Task PlanningSharedBySeveralRules_IsKept()
    {
        var rule = await SeedMonthlyRuleAsync();
        var first = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking()
            .SingleAsync(x => x.Id == rule.ArpId);
        await new AreaRulePlanning
        {
            AreaRuleId = first.AreaRuleId, PropertyId = first.PropertyId, AreaId = first.AreaId,
            ItemPlanningId = rule.PlanningId, StartDate = first.StartDate, Status = true,
            RepeatType = 3, RepeatEvery = 1, RepeatOrdinalWeek = 1, DayOfWeek = (int)DayOfWeek.Wednesday,
            ComplianceEnabled = true, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext);
        var june = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);

        var plan = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Closures, Is.Empty);
            Assert.That(plan.Kept.Single().ComplianceId, Is.EqualTo(june.Id));
            Assert.That(plan.Kept.Single().Reasons, Does.Contain(CalendarOrphanComplianceRepairService.ReasonSeveralRules));
        });
    }

    [Test]
    public async Task RuleWhoseWeekdayListDisagreesWithItsWeekday_IsKept()
    {
        var rule = await SeedMonthlyRuleAsync();
        await BackendConfigurationPnDbContext!.AreaRulePlannings.Where(x => x.Id == rule.ArpId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RepeatWeekdaysCsv, "1"));
        BackendConfigurationPnDbContext.ChangeTracker.Clear();
        var june = await SeedComplianceAsync(rule, D(2026, 6, 7), (await SeedRemovedCaseAsync()).Id);

        var plan = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Closures, Is.Empty);
            Assert.That(plan.Kept.Single().ComplianceId, Is.EqualTo(june.Id));
            Assert.That(plan.Kept.Single().Reasons, Does.Contain(CalendarOrphanComplianceRepairService.ReasonCsvDisagrees));
        });
    }

    [Test]
    public async Task RetractedOwnCase_IsClosed_AsRetracted()
    {
        var rule = await SeedMonthlyRuleAsync();
        var june = await SeedComplianceAsync(rule, D(2026, 6, 7),
            (await SeedCaseAsync(workflowState: Constants.WorkflowStates.Retracted)).Id);

        var result = await RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Plan.Closures.Single().ComplianceId, Is.EqualTo(june.Id));
            Assert.That(result.Plan.Closures.Single().SdkCaseState, Is.EqualTo("Retracted"));
            Assert.That(result.ClosedCompliances, Is.EqualTo(1));
        });
        Assert.That((await ComplianceAsync(june.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    /// <summary>
    /// A row that stops qualifying between the hash check and its write — here its SDK
    /// case comes back to life — is skipped, not closed; the rest of the plan still runs.
    /// </summary>
    [Test]
    public async Task RowThatChangedBeforeItsWrite_IsSkipped_AndTheRestIsClosed()
    {
        var rule = await SeedMonthlyRuleAsync();
        var juneCase = await SeedRemovedCaseAsync();
        var june = await SeedComplianceAsync(rule, D(2026, 6, 7), juneCase.Id);
        var july = await SeedComplianceAsync(rule, D(2026, 7, 7), (await SeedRemovedCaseAsync()).Id);
        var plan = await DryRunAsync();
        Assert.That(plan.Closures.Select(x => x.ComplianceId), Is.EqualTo(new[] { june.Id, july.Id }));

        _sut.BeforePlanningRecheck = async _ =>
        {
            await MicrotingDbContext!.Cases.Where(x => x.Id == juneCase.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.WorkflowState, Constants.WorkflowStates.Created));
        };
        var result = await _sut.RunAsync(plan.PlanHash);

        Assert.That(result.Success, Is.True, result.Message);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Model.ClosedCompliances, Is.EqualTo(1));
            Assert.That(result.Model.Skipped, Has.Count.EqualTo(1));
            Assert.That(result.Model.Skipped[0], Does.StartWith($"compliance {june.Id}:"));
            Assert.That((await ComplianceAsync(june.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "its case is live again: not an orphan any more");
            Assert.That((await ComplianceAsync(july.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        });
    }
}
