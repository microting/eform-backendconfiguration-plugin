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

using eFormCore;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
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

/// <summary>
/// #5467 — the completions of a "no repeat" (always available) task — an items-planning
/// <c>Planning</c> with <c>RepeatType == Day</c> and <c>RepeatEvery == 0</c> — have no
/// Compliance row (the service plugin never writes one), so the Compliance report used to
/// miss them. Detaljer and Rapport now read them from items-planning <c>PlanningCases</c>,
/// under the filters a compliance row gets, placed on their done date; Oversigt does not
/// count them, since an on-demand completion was never due.
///
/// <para>Every date is fixed and in the past. Every assertion is scoped to the property or
/// SDK case the test seeded (the database is shared and not reset). SDK cases carry
/// <c>MicrotingUid = null</c>, so no platform call is attempted.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceReportNoRepeatCompletionsTests : TestBaseSetup
{
    private const int CompletedStatus = 100;
    private const int OpenCaseStatus = 66;

    private Core? _core;
    private int _uidCounter = 975_000;

    private async Task<Core> SharedCore() => _core ??= await GetCore();

    // ------------------------------------------------------------------
    // Services
    // ------------------------------------------------------------------

    private async Task<BackendConfigurationComplianceReportService> ReportService()
    {
        var core = await SharedCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        userService.GetCurrentUserLocale().Returns(Task.FromResult("en-US"));
        userService.GetCurrentUserAsync().Returns(Task.FromResult(new EformUser { Id = 1 }));

        return new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, ItemsPlanningPnDbContext!,
            TestContextLogger<BackendConfigurationComplianceReportService>.Instance,
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext));
    }

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    private sealed class Series
    {
        public int PropertyId;
        public int AreaId;
        public int ArpId;
        public int PlanningId;
    }

    /// <summary>
    /// Area → Property (or <paramref name="onPropertyOf"/>'s) → AreaRule(+translation) →
    /// Planning → AreaRulePlanning with a report headline. <paramref name="noRepeat"/>
    /// gives the "no repeat" shape (Day / 0, mirrored on the ARP as 1 / 0); otherwise a
    /// weekly Monday series.
    /// </summary>
    private async Task<Series> SeedSeries(
        string title, bool noRepeat = true, Series? onPropertyOf = null, string? arpWorkflowState = "created")
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var propertyId = onPropertyOf?.PropertyId ?? 0;
        if (onPropertyOf == null)
        {
            var property = new Property
            {
                Name = $"Example Property {Guid.NewGuid()}", ItemPlanningTagId = 0,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext.Properties.AddAsync(property);
            await BackendConfigurationPnDbContext.SaveChangesAsync();
            propertyId = property.Id;
        }

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = propertyId, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var startDate = new DateTime(2024, 9, 2, 0, 0, 0, DateTimeKind.Utc); // a Monday
        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = noRepeat ? 0 : 1,
            RepeatType = noRepeat ? RepeatType.Day : RepeatType.Week,
            StartDate = startDate, DayOfWeek = DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var headline = new PlanningTag
        {
            Name = $"No repeat headline {Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningTags.AddAsync(headline);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            ItemPlanningTagId = headline.Id,
            ComplianceEnabled = true,
            RepeatType = noRepeat ? 1 : 2, RepeatEvery = noRepeat ? 0 : 1,
            RepeatWeekdaysCsv = noRepeat ? null : "1", DayOfWeek = 1,
            WorkflowState = arpWorkflowState, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Series { PropertyId = propertyId, AreaId = area.Id, ArpId = arp.Id, PlanningId = planning.Id };
    }

    /// <summary>
    /// One completion of the series, the shape a "no repeat" task leaves: an SDK case done
    /// at <paramref name="doneAt"/> (retracted from the device, as every completion is), a
    /// completed PlanningCase pointing at it — and NO Compliance row.
    /// </summary>
    private async Task<int> SeedCompletion(
        Series series, DateTime doneAt, int sdkCaseStatus = CompletedStatus, bool withSite = true,
        string? doneByUserName = null)
    {
        int? siteId = withSite ? await SeedSdkSite("Jane Doe") : null;
        var checkList = new CheckList
        {
            Label = $"no-repeat-{Guid.NewGuid()}", ParentId = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkCase = new Case
        {
            SiteId = siteId,
            Status = sdkCaseStatus,
            DoneAt = doneAt,
            DoneAtUserModifiable = doneAt,
            CheckListId = checkList.Id,
            MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Retracted
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = series.PlanningId,
            MicrotingSdkeFormId = checkList.Id,
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkCaseDoneAt = doneAt,
            Status = CompletedStatus,
            DoneByUserName = doneByUserName,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return sdkCase.Id;
    }

    private async Task<int> SeedSdkSite(string name)
    {
        var uid = ++_uidCounter;
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = name, MicrotingUid = uid, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    private async Task<int> SeedTag(int? onArpId)
    {
        var tag = new PlanningTag
        {
            Name = $"Example tag {Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(tag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        if (onArpId is { } arpId)
        {
            await BackendConfigurationPnDbContext!.AreaRulePlanningTags.AddAsync(new AreaRulePlanningTag
            {
                AreaRulePlanningId = arpId, ItemPlanningTagId = tag.Id,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
            await BackendConfigurationPnDbContext.SaveChangesAsync();
        }
        return tag.Id;
    }

    private static DateTime Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime MarchFrom = Utc(2025, 3, 1);
    private static readonly DateTime MarchTo = Utc(2025, 3, 31);

    // ------------------------------------------------------------------
    // Reading the views
    // ------------------------------------------------------------------

    private async Task<ComplianceReportPagedModel> DetaljerPage(
        string status, int? propertyId, DateTime from, DateTime to, List<int>? tagIds = null)
    {
        var result = await (await ReportService()).Index(new ComplianceReportRequestModel
        {
            DateFrom = from, DateTo = to, Status = status, PropertyId = propertyId,
            BoardIds = [], TagIds = tagIds ?? [], SiteIds = [], PageIndex = 0, PageSize = 0
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task<List<ComplianceReportRowModel>> Detaljer(
        string status, int? propertyId, DateTime from, DateTime to, List<int>? tagIds = null) =>
        (await DetaljerPage(status, propertyId, from, to, tagIds)).Entities;

    private async Task<List<ComplianceReportCaseModel>> Rapport(int propertyId, DateTime from, DateTime to)
    {
        var result = await (await ReportService()).EformColumns(new ComplianceReportRequestModel
        {
            DateFrom = from, DateTo = to, Status = "all", PropertyId = propertyId,
            BoardIds = [], TagIds = [], SiteIds = [], PageSize = 0
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model.SelectMany(g => g.Templates).SelectMany(t => t.Cases).ToList();
    }

    // ==================================================================
    // Detaljer
    // ==================================================================

    [Test]
    public async Task Detaljer_Done_ListsTheNoRepeatCompletion_OnItsDoneDate()
    {
        var series = await SeedSeries("Always available A");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 5, 10));

        var rows = await Detaljer("done", series.PropertyId, MarchFrom, MarchTo);

        var row = rows.SingleOrDefault(r => r.SdkCaseId == caseId);
        Assert.That(row, Is.Not.Null, "the completion is listed");
        Assert.Multiple(() =>
        {
            Assert.That(row!.Completed, Is.True);
            Assert.That(row.IsOnDemand, Is.True);
            Assert.That(row.IsProjected, Is.False);
            Assert.That(row.ComplianceId, Is.EqualTo(0), "no Compliance row backs it");
            Assert.That(row.TaskDate, Is.EqualTo("2025-03-05"));
            Assert.That(row.PlanningId, Is.EqualTo(series.PlanningId));
            Assert.That(row.AreaRulePlanningId, Is.EqualTo(series.ArpId));
            Assert.That(row.Title, Is.EqualTo("Always available A"));
            Assert.That(row.WorkerNames, Is.EqualTo(new List<string> { "Jane Doe" }), "the completer (#1333)");
        });
    }

    [Test]
    public async Task Detaljer_All_ListsIt_Open_DoesNot()
    {
        var series = await SeedSeries("Always available B");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 6, 10));

        var all = await Detaljer("all", series.PropertyId, MarchFrom, MarchTo);
        var open = await Detaljer("open", series.PropertyId, MarchFrom, MarchTo);

        Assert.Multiple(() =>
        {
            Assert.That(all.Count(r => r.SdkCaseId == caseId), Is.EqualTo(1), "status all");
            Assert.That(open.Where(r => r.SdkCaseId == caseId), Is.Empty, "status open");
            Assert.That(open.Where(r => r.PlanningId == series.PlanningId), Is.Empty, "no open row for it either");
        });
    }

    [Test]
    public async Task Detaljer_TagFilter_ListsItWithItsTag_NotWithAnUnrelatedOne()
    {
        var series = await SeedSeries("Always available C");
        var ownTag = await SeedTag(series.ArpId);
        var unrelatedTag = await SeedTag(onArpId: null);
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 7, 10));

        var withOwn = await Detaljer("done", null, MarchFrom, MarchTo, [ownTag]);
        var withUnrelated = await Detaljer("done", null, MarchFrom, MarchTo, [unrelatedTag]);

        Assert.Multiple(() =>
        {
            Assert.That(withOwn.Count(r => r.SdkCaseId == caseId), Is.EqualTo(1), "its own tag");
            Assert.That(withUnrelated.Where(r => r.SdkCaseId == caseId), Is.Empty, "an unrelated tag");
        });
    }

    [Test]
    public async Task Detaljer_PropertyFilterAndPeriod_AreApplied()
    {
        var series = await SeedSeries("Always available D");
        var otherProperty = await SeedSeries("Always available elsewhere");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 8, 10));

        var ownProperty = await Detaljer("done", series.PropertyId, MarchFrom, MarchTo);
        var wrongProperty = await Detaljer("done", otherProperty.PropertyId, MarchFrom, MarchTo);
        var april = await Detaljer("done", series.PropertyId, Utc(2025, 4, 1), Utc(2025, 4, 30));

        Assert.Multiple(() =>
        {
            Assert.That(ownProperty.Count(r => r.SdkCaseId == caseId), Is.EqualTo(1), "its own property");
            Assert.That(wrongProperty.Where(r => r.SdkCaseId == caseId), Is.Empty, "another property");
            Assert.That(april.Where(r => r.SdkCaseId == caseId), Is.Empty, "done outside the period");
        });
    }

    /// <summary>The done date is the Danish date: 23:30 UTC on 31 March is 1 April.</summary>
    [Test]
    public async Task Detaljer_DoneDate_IsTheCopenhagenDate()
    {
        var series = await SeedSeries("Always available E");
        var caseId = await SeedCompletion(series, doneAt: new DateTime(2025, 3, 31, 23, 30, 0, DateTimeKind.Utc));

        var march = await Detaljer("done", series.PropertyId, MarchFrom, MarchTo);
        var april = await Detaljer("done", series.PropertyId, Utc(2025, 4, 1), Utc(2025, 4, 30));

        Assert.Multiple(() =>
        {
            Assert.That(march.Where(r => r.SdkCaseId == caseId), Is.Empty, "not on the UTC date");
            Assert.That(april.SingleOrDefault(r => r.SdkCaseId == caseId)?.TaskDate, Is.EqualTo("2025-04-01"));
        });
    }

    /// <summary>
    /// An ARP whose WorkflowState was never set (NULL) is live — production holds such
    /// rows, and SQL's <c>NULL &lt;&gt; 'removed'</c> must not drop them.
    /// </summary>
    [Test]
    public async Task Detaljer_ArpWithNullWorkflowState_StillCounts()
    {
        var series = await SeedSeries("Always available F", arpWorkflowState: null);
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 9, 10));

        var rows = await Detaljer("done", series.PropertyId, MarchFrom, MarchTo);

        Assert.That(rows.SingleOrDefault(r => r.SdkCaseId == caseId)?.AreaRulePlanningId, Is.EqualTo(series.ArpId));
    }

    /// <summary>
    /// Production holds several PlanningCases pointing at one SDK case. The case is ONE
    /// row, and Total counts it once.
    /// </summary>
    [Test]
    public async Task Detaljer_SeveralPlanningCasesOnOneSdkCase_AreOneRow()
    {
        var series = await SeedSeries("Always available M");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 18, 10));
        for (var i = 0; i < 2; i++)
        {
            await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(new PlanningCase
            {
                PlanningId = series.PlanningId, MicrotingSdkeFormId = 0, MicrotingSdkCaseId = caseId,
                MicrotingSdkCaseDoneAt = Utc(2025, 3, 18, 10), Status = CompletedStatus,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        }
        await ItemsPlanningPnDbContext!.SaveChangesAsync();

        var page = await DetaljerPage("done", series.PropertyId, MarchFrom, MarchTo);

        Assert.Multiple(() =>
        {
            Assert.That(page.Entities.Count(r => r.SdkCaseId == caseId), Is.EqualTo(1), "one row");
            Assert.That(page.Total, Is.EqualTo(1), "Total counts the case once");
        });
    }

    /// <summary>Done-ness is the SDK case's: a case reopened there is not listed as done.</summary>
    [Test]
    public async Task Detaljer_SdkCaseNotDone_IsNotListed()
    {
        var series = await SeedSeries("Always available G");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 10, 10), sdkCaseStatus: OpenCaseStatus);

        var rows = await Detaljer("all", series.PropertyId, MarchFrom, MarchTo);

        Assert.That(rows.Where(r => r.SdkCaseId == caseId), Is.Empty);
    }

    /// <summary>A case a Compliance row carries is that row's, and is listed once.</summary>
    [Test]
    public async Task Detaljer_CaseAlsoCarriedByACompliance_IsListedOnce_AsTheComplianceRow()
    {
        var series = await SeedSeries("Always available H");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 11, 10));
        var compliance = new Compliance
        {
            ItemName = "Example item", PlanningId = series.PlanningId, PropertyId = series.PropertyId,
            AreaId = series.AreaId, Deadline = Utc(2025, 3, 11), StartDate = Utc(2025, 3, 4),
            MicrotingSdkCaseId = caseId, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Removed, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var rows = (await Detaljer("done", series.PropertyId, MarchFrom, MarchTo))
            .Where(r => r.SdkCaseId == caseId).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows.FirstOrDefault()?.ComplianceId, Is.EqualTo(compliance.Id));
            Assert.That(rows.FirstOrDefault()?.IsOnDemand, Is.False);
        });
    }

    /// <summary>A deleted-log marker (#1290) on (ARP, done date) hides the completion.</summary>
    [Test]
    public async Task Detaljer_DeletedMarkerOnTheDoneDate_HidesIt()
    {
        var series = await SeedSeries("Always available I");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 12, 10));
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(new CalendarOccurrenceException
        {
            AreaRulePlanningId = series.ArpId, OriginalDate = Utc(2025, 3, 12), IsDeleted = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var rows = await Detaljer("done", series.PropertyId, MarchFrom, MarchTo);

        Assert.That(rows.Where(r => r.SdkCaseId == caseId), Is.Empty);
    }

    /// <summary>With no SDK site, "Udført af" falls back to PlanningCase.DoneByUserName.</summary>
    [Test]
    public async Task Detaljer_CompleterName_FallsBackToThePlanningCase()
    {
        var series = await SeedSeries("Always available J");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 13, 10), withSite: false,
            doneByUserName: "John Example");

        var rows = await Detaljer("done", series.PropertyId, MarchFrom, MarchTo);

        Assert.That(rows.SingleOrDefault(r => r.SdkCaseId == caseId)?.WorkerNames,
            Is.EqualTo(new List<string> { "John Example" }));
    }

    /// <summary>
    /// Scope guard: only "no repeat" tasks take this path. A repeating task's completed
    /// PlanningCase with no Compliance row is not pulled in.
    /// </summary>
    [Test]
    public async Task Detaljer_RepeatingTaskPlanningCaseWithoutCompliance_IsNotListed()
    {
        var series = await SeedSeries("Weekly task", noRepeat: false);
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 14, 10));

        var rows = await Detaljer("all", series.PropertyId, MarchFrom, MarchTo);

        Assert.That(rows.Where(r => r.SdkCaseId == caseId), Is.Empty);
    }

    // ==================================================================
    // Rapport
    // ==================================================================

    [Test]
    public async Task Rapport_ListsTheNoRepeatCompletion()
    {
        var series = await SeedSeries("Always available K");
        var caseId = await SeedCompletion(series, doneAt: Utc(2025, 3, 15, 10));

        var cases = await Rapport(series.PropertyId, MarchFrom, MarchTo);

        var caseModel = cases.SingleOrDefault(c => c.SdkCaseId == caseId);
        Assert.That(caseModel, Is.Not.Null, "the completion is in Rapport");
        Assert.Multiple(() =>
        {
            Assert.That(caseModel!.IsOnDemand, Is.True);
            Assert.That(caseModel.ComplianceId, Is.EqualTo(0));
            Assert.That(caseModel.Completed, Is.True);
            Assert.That(caseModel.TaskDate, Is.EqualTo("2025-03-15"));
            Assert.That(caseModel.WorkerNames, Is.EqualTo(new List<string> { "Jane Doe" }));
        });
    }

    // ==================================================================
    // Oversigt
    // ==================================================================

    /// <summary>
    /// Oversigt does not count an on-demand completion: it was never due. The property's
    /// one overdue weekly occurrence alone makes its numbers — 1 total, 0 done, 0 %.
    /// </summary>
    [Test]
    public async Task Oversigt_IsUnchangedByTheNoRepeatCompletion()
    {
        var noRepeat = await SeedSeries("Always available L");
        await SeedCompletion(noRepeat, doneAt: Utc(2025, 3, 16, 10));

        var weekly = await SeedSeries("Weekly on the same property", noRepeat: false, onPropertyOf: noRepeat);
        var openCase = new Case
        {
            Status = OpenCaseStatus, MicrotingUid = null, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(openCase);
        await MicrotingDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(new Compliance
        {
            ItemName = "Example weekly item", PlanningId = weekly.PlanningId, PropertyId = weekly.PropertyId,
            AreaId = weekly.AreaId, Deadline = Utc(2025, 3, 17), StartDate = Utc(2025, 3, 10),
            MicrotingSdkCaseId = openCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await (await ReportService()).Overview(new ComplianceReportOverviewRequestModel
        {
            DateFrom = MarchFrom, DateTo = MarchTo, PropertyId = noRepeat.PropertyId,
            BoardIds = [], TagIds = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model.Rows.Single(r => r.PropertyId == noRepeat.PropertyId);

        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(1), "Total");
            Assert.That(row.Done, Is.EqualTo(0), "Done");
            Assert.That(row.DueTotal, Is.EqualTo(1), "DueTotal");
            Assert.That(row.Overdue, Is.EqualTo(1), "Overdue");
            Assert.That(row.CompliancePct, Is.EqualTo(0), "CompliancePct");
        });
    }
}
