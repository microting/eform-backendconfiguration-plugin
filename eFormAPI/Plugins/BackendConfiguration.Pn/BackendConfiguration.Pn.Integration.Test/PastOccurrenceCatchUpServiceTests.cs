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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.PastOccurrenceCatchUp;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// Integration tests for <see cref="PastOccurrenceCatchUpService"/> — the service
/// that materialises missed backend-configuration occurrences since 2026-09-17
/// when skipCloudDeploy is enabled.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class PastOccurrenceCatchUpServiceTests : TestBaseSetup
{
    private DateTime _today;

    private DateTime Today => _today;

    [SetUp]
    public async Task ClearAccumulatingTables()
    {
        _today = DateTime.UtcNow.Date;

        // Each test gets a fresh property ID to avoid throttle collisions
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");

        // Clear the throttle between tests
        PastOccurrenceCatchUpService.ResetThrottleForTests();
    }

    private sealed record Seeded(AreaRulePlanning Arp, Planning Planning);

    private async Task<Seeded> SeedEvent(
        DateTime startDate,
        int repeatType,
        int repeatEvery = 1,
        bool complianceEnabled = true,
        int? dayOfMonth = null,
        int? repeatEndMode = null,
        DateTime? repeatUntilDate = null,
        int? repeatOccurrences = null,
        string? repeatWeekdaysCsv = null,
        int? repeatOrdinalWeek = null)
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
            Name = $"CatchUpTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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
            Enabled = true,
            RepeatEvery = repeatEvery,
            RepeatType = (RepeatType)repeatType,
            StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            DayOfWeek = startDate.DayOfWeek,
            DayOfMonth = dayOfMonth,
            RepeatOrdinalWeek = repeatOrdinalWeek,
            RelatedEFormId = 0,
            IsLocked = true,
            IsEditable = false,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            Status = true,
            RepeatType = repeatType, RepeatEvery = repeatEvery,
            DayOfWeek = (int)startDate.DayOfWeek,
            DayOfMonth = dayOfMonth ?? 0,
            RepeatEndMode = repeatEndMode,
            RepeatUntilDate = repeatUntilDate,
            RepeatOccurrences = repeatOccurrences,
            RepeatWeekdaysCsv = repeatWeekdaysCsv,
            RepeatOrdinalWeek = repeatOrdinalWeek,
            ComplianceEnabled = complianceEnabled,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Seeded(arp, planning);
    }

    private async Task<int> AddSite(int arpId, int siteId)
    {
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arpId, SiteId = siteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return siteId;
    }

    private async Task<PastOccurrenceCatchUpService> BuildService(
        ICalendarPastSeriesBackfillService? planner = null,
        IEventDeployService? deploy = null,
        IEFormCoreService? coreHelper = null)
    {
        coreHelper ??= Substitute.For<IEFormCoreService>();
        planner ??= Substitute.For<ICalendarPastSeriesBackfillService>();
        deploy ??= Substitute.For<IEventDeployService>();

        var service = new PastOccurrenceCatchUpService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            planner,
            deploy,
            NullLogger<PastOccurrenceCatchUpService>.Instance);

        return service;
    }

    [Test]
    public async Task CatchUp_OnlyDeploysOccurrencesFromGapStartToYesterday()
    {
        // Seed a series starting 5 weeks back (well before the gap start of 2026-09-17)
        var seeded = await SeedEvent(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), repeatType: (int)RepeatType.Week);
        var siteId = await AddSite(seeded.Arp.Id, 7);

        // Plan: occurrences on 2026-09-10 (before gap), 2026-09-17 (gap start), 2026-09-24,
        // yesterday, today, and beyond
        var planOccurrences = new List<DateTime>
        {
            new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc),
            Today.AddDays(-1),
            Today,
            Today.AddDays(1)
        };

        var plan = new PastSeriesBackfillPlan(
            seeded.Arp.StartDate ?? DateTime.UnixEpoch,
            AnchorIsInThePast: true,
            planOccurrences,
            [siteId],
            Today.AddDays(1),
            ComplianceEnabled: true);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        planner.PlanPastSeriesBackfillAsync(
                Arg.Is<AreaRulePlanning>(a => a.Id == seeded.Arp.Id),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(plan);

        var deploy = Substitute.For<IEventDeployService>();
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 })!);

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        // Act
        var result = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        // Assert: only 2026-09-17, 2026-09-24, and yesterday should be deployed
        await deploy.Received(3).EnsureComplianceForOccurrenceAsync(
            Arg.Is<AreaRulePlanning>(a => a.Id == seeded.Arp.Id),
            Arg.Any<DateTime>(),
            Arg.Is(siteId),
            Arg.Any<CancellationToken>());

        var calls = deploy.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEventDeployService.EnsureComplianceForOccurrenceAsync))
            .Select(c => ((DateTime)c.GetArguments()[1]!).Date)
            .OrderBy(d => d)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(calls, Has.Count.EqualTo(3));
            Assert.That(calls[0], Is.EqualTo(new DateTime(2026, 9, 17).Date));
            Assert.That(calls[1], Is.EqualTo(new DateTime(2026, 9, 24).Date));
            Assert.That(calls[2], Is.EqualTo(Today.AddDays(-1).Date));
        });
    }

    [Test]
    public async Task CatchUp_DeploysEverySiteOfEveryOccurrence()
    {
        var seeded = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site7 = await AddSite(seeded.Arp.Id, 7);
        var site8 = await AddSite(seeded.Arp.Id, 8);

        var planOccurrences = new List<DateTime>
        {
            Today.AddDays(-14),
            Today.AddDays(-7)
        };

        var plan = new PastSeriesBackfillPlan(
            seeded.Arp.StartDate ?? DateTime.UnixEpoch,
            AnchorIsInThePast: true,
            planOccurrences,
            [site7, site8],
            Today,
            ComplianceEnabled: true);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        planner.PlanPastSeriesBackfillAsync(Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        var deploy = Substitute.For<IEventDeployService>();
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 })!);

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        var result = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        Assert.That(result.Created, Is.EqualTo(4), "2 occurrences × 2 sites");
    }

    [Test]
    public async Task CatchUp_SkipCloudDeployOff_DoesNothing()
    {
        var seeded = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site7 = await AddSite(seeded.Arp.Id, 7);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        var deploy = Substitute.For<IEventDeployService>();

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "false");

        var service = await BuildService(planner, deploy, coreHelper);

        var result = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        // Should return zeros and never call planner or deploy
        Assert.Multiple(() =>
        {
            Assert.That(result.Created, Is.EqualTo(0));
            Assert.That(result.AlreadyPresent, Is.EqualTo(0));
            Assert.That(result.Failed, Is.EqualTo(0));
            Assert.That(result.PropertiesProcessed, Is.EqualTo(0));
        });

        await planner.DidNotReceive().PlanPastSeriesBackfillAsync(
            Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [TestCase("status")]
    [TestCase("compliance")]
    [TestCase("removedArp")]
    [TestCase("disabledPlanning")]
    [TestCase("editablePlanning")]
    [TestCase("removedPlanning")]
    public async Task CatchUp_IneligibleSeries_IsSkipped(string variant)
    {
        Seeded seeded = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);

        if (variant == "status")
        {
            seeded.Arp.Status = false;
            await BackendConfigurationPnDbContext!.SaveChangesAsync();
        }
        else if (variant == "compliance")
        {
            seeded.Arp.ComplianceEnabled = false;
            await BackendConfigurationPnDbContext!.SaveChangesAsync();
        }
        else if (variant == "removedArp")
        {
            seeded.Arp.WorkflowState = Constants.WorkflowStates.Removed;
            await BackendConfigurationPnDbContext!.SaveChangesAsync();
        }
        else if (variant == "disabledPlanning")
        {
            seeded.Planning.Enabled = false;
            await ItemsPlanningPnDbContext!.SaveChangesAsync();
        }
        else if (variant == "editablePlanning")
        {
            seeded.Planning.IsEditable = true;
            await ItemsPlanningPnDbContext!.SaveChangesAsync();
        }
        else if (variant == "removedPlanning")
        {
            seeded.Planning.WorkflowState = Constants.WorkflowStates.Removed;
            await ItemsPlanningPnDbContext!.SaveChangesAsync();
        }

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        var deploy = Substitute.For<IEventDeployService>();

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        var result = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        await planner.DidNotReceive().PlanPastSeriesBackfillAsync(
            Arg.Is<AreaRulePlanning>(a => a.Id == seeded.Arp.Id),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CatchUp_OneFailingPair_DoesNotStopTheRest()
    {
        var seeded = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site7 = await AddSite(seeded.Arp.Id, 7);
        var site8 = await AddSite(seeded.Arp.Id, 8);

        var planOccurrences = new List<DateTime>
        {
            Today.AddDays(-14),
            Today.AddDays(-7)
        };
        var plan = new PastSeriesBackfillPlan(
            seeded.Arp.StartDate ?? DateTime.UnixEpoch,
            AnchorIsInThePast: true,
            planOccurrences,
            [site7, site8],
            Today,
            ComplianceEnabled: true);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        planner.PlanPastSeriesBackfillAsync(Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        var deploy = Substitute.For<IEventDeployService>();
        // Throw for site 7 only
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Is(site7),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EnsureComplianceResult>(new InvalidOperationException("Deploy failed for site 7")));
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Is(site8),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 })!);

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        var result = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        // 2 occurrences, site7 fails on both (2 failures), site8 succeeds on both (2 created)
        Assert.Multiple(() =>
        {
            Assert.That(result.Created, Is.EqualTo(2));
            Assert.That(result.Failed, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task CatchUp_NullResult_CountsAsFailed()
    {
        var seeded = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site7 = await AddSite(seeded.Arp.Id, 7);

        var plan = new PastSeriesBackfillPlan(
            seeded.Arp.StartDate ?? DateTime.UnixEpoch,
            AnchorIsInThePast: true,
            [Today.AddDays(-14)],
            [site7],
            Today,
            ComplianceEnabled: true);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        planner.PlanPastSeriesBackfillAsync(Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        var deploy = Substitute.For<IEventDeployService>();
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((EnsureComplianceResult?)null));

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        var result = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        Assert.That(result.Failed, Is.EqualTo(1));
    }

    [Test]
    public async Task CatchUp_ExistingRow_CountsAsAlreadyPresent()
    {
        var seeded = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site7 = await AddSite(seeded.Arp.Id, 7);

        var plan = new PastSeriesBackfillPlan(
            seeded.Arp.StartDate ?? DateTime.UnixEpoch,
            AnchorIsInThePast: true,
            [Today.AddDays(-14)],
            [site7],
            Today,
            ComplianceEnabled: true);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        planner.PlanPastSeriesBackfillAsync(Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        var deploy = Substitute.For<IEventDeployService>();
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EnsureComplianceResult { Created = false, ComplianceId = 1, SdkCaseId = 1 })!);

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        var result = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        Assert.That(result.AlreadyPresent, Is.EqualTo(1));
    }

    // A day that already has a Compliance row in ANY state, or a live deleted-occurrence
    // exception, is left alone: redeploying a completed or user-deleted occurrence would
    // create an SDK case that revives the deleted task or is orphaned, on every run.
    [TestCase("removedRow", false)]
    [TestCase("liveRow", false)]
    [TestCase("deletedException", false)]
    [TestCase("removedDeletedException", true)]
    [TestCase("movedException", true)]
    public async Task CatchUp_DayWithRowOrDeletedException_IsNotDeployed(string variant, bool expectDeployed)
    {
        var seeded = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site7 = await AddSite(seeded.Arp.Id, 7);
        var day = Today.AddDays(-7);

        switch (variant)
        {
            case "removedRow":
            case "liveRow":
                await BackendConfigurationPnDbContext!.Compliances.AddAsync(new Compliance
                {
                    Deadline = day, PlanningId = seeded.Planning.Id, PropertyId = seeded.Arp.PropertyId,
                    StartDate = day.AddDays(-7), MicrotingSdkCaseId = 1,
                    WorkflowState = variant == "removedRow"
                        ? Constants.WorkflowStates.Removed
                        : Constants.WorkflowStates.Created
                });
                break;
            default:
                await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(new CalendarOccurrenceException
                {
                    AreaRulePlanningId = seeded.Arp.Id,
                    OriginalDate = DateTime.SpecifyKind(day, DateTimeKind.Utc),
                    IsDeleted = variant != "movedException",
                    NewDate = variant == "movedException" ? DateTime.SpecifyKind(day.AddDays(1), DateTimeKind.Utc) : null,
                    WorkflowState = variant == "removedDeletedException"
                        ? Constants.WorkflowStates.Removed
                        : Constants.WorkflowStates.Created,
                    CreatedByUserId = 1, UpdatedByUserId = 1
                });
                break;
        }
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var plan = new PastSeriesBackfillPlan(
            seeded.Arp.StartDate ?? DateTime.UnixEpoch,
            AnchorIsInThePast: true,
            [Today.AddDays(-14), day],
            [site7],
            Today,
            ComplianceEnabled: true);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        planner.PlanPastSeriesBackfillAsync(Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        var deploy = Substitute.For<IEventDeployService>();
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 })!);

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        await deploy.Received(1).EnsureComplianceForOccurrenceAsync(
            Arg.Any<AreaRulePlanning>(), Arg.Is<DateTime>(d => d.Date == Today.AddDays(-14)), site7,
            Arg.Any<CancellationToken>());
        await deploy.Received(expectDeployed ? 1 : 0).EnsureComplianceForOccurrenceAsync(
            Arg.Any<AreaRulePlanning>(), Arg.Is<DateTime>(d => d.Date == day), site7,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CatchUp_SecondCallWithin10Minutes_IsThrottled_UnlessBypassed()
    {
        var seeded = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site7 = await AddSite(seeded.Arp.Id, 7);

        var plan = new PastSeriesBackfillPlan(
            seeded.Arp.StartDate ?? DateTime.UnixEpoch,
            AnchorIsInThePast: true,
            [Today.AddDays(-14)],
            [site7],
            Today,
            ComplianceEnabled: true);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        planner.PlanPastSeriesBackfillAsync(Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        var deploy = Substitute.For<IEventDeployService>();
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 })!);

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        // First call without throttle bypass
        var result1 = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: false, CancellationToken.None);
        Assert.That(result1.Created, Is.EqualTo(1));

        // Second call within 10 minutes, no bypass → should be throttled
        var result2 = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: false, CancellationToken.None);
        Assert.That(result2.PropertiesProcessed, Is.EqualTo(0), "Should skip the property due to throttle");

        // Third call with bypass → should run again
        var result3 = await service.CatchUpAsync([seeded.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);
        Assert.That(result3.Created, Is.EqualTo(1), "Should run again with bypass");
    }

    [Test]
    public async Task CatchUp_PropertyFilter_OnlyTouchesThoseProperties()
    {
        var seeded1 = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site1 = await AddSite(seeded1.Arp.Id, 7);

        var seeded2 = await SeedEvent(Today.AddDays(-14), repeatType: (int)RepeatType.Week);
        var site2 = await AddSite(seeded2.Arp.Id, 8);

        var plan = new PastSeriesBackfillPlan(
            new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
            AnchorIsInThePast: true,
            [Today.AddDays(-14)],
            [site1],
            Today,
            ComplianceEnabled: true);

        var planner = Substitute.For<ICalendarPastSeriesBackfillService>();
        planner.PlanPastSeriesBackfillAsync(Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(plan);

        var deploy = Substitute.For<IEventDeployService>();
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 })!);

        var coreHelper = Substitute.For<IEFormCoreService>();
        var core = await GetCore();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        var service = await BuildService(planner, deploy, coreHelper);

        // Call with only property1
        await service.CatchUpAsync([seeded1.Arp.PropertyId], bypassThrottle: true, CancellationToken.None);

        // Planner should only be called for seeded1
        await planner.Received(1).PlanPastSeriesBackfillAsync(
            Arg.Is<AreaRulePlanning>(a => a.PropertyId == seeded1.Arp.PropertyId),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());

        await planner.DidNotReceive().PlanPastSeriesBackfillAsync(
            Arg.Is<AreaRulePlanning>(a => a.PropertyId == seeded2.Arp.PropertyId),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());
    }
}
