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

using eFormCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Infrastructure.Models.Report;
using Services.BackendConfigurationReportService;
using Services.ExcelService;
using Services.WordService;

/// <summary>
/// Regression coverage for <see cref="BackendConfigurationReportService"/>'s
/// per-planning-case resolution of property name and reporting worker.
///
/// Both <c>GenerateReport</c> and <c>GenerateReportV2</c> wrap their entire body
/// in a single try/catch, so any exception thrown while building ONE row aborts
/// the WHOLE report and the caller gets <c>Success = false</c>. That makes every
/// unguarded <c>.First(...)</c> / null dereference in the per-case loop a
/// report-wide outage, not a single bad cell. These tests pin the two fixes:
///
/// * A planning case whose property cannot be resolved (the
///   <c>AreaRulesPlanningVersions</c> fallback rows carry <c>PropertyId = 0</c>,
///   a 2022 migration default that matches no <c>Properties</c> row) must still
///   be reported, with a blank <c>PropertyName</c>. Both property lookups are
///   covered per report method: the live <c>AreaRulePlannings</c> row (the
///   common production path) and the version-row fallback.
/// * In <c>GenerateReportV2</c>, a missing SDK case must be skipped (not NRE), a
///   site with no active <c>SiteWorkers</c> row must yield a blank
///   <c>EmployeeNo</c> (not throw), and the <c>WorkflowState != "removed"</c>
///   filter must stop a soft-deleted site worker from winning over the active
///   one. The <c>OrderBy(x =&gt; x.Id)</c> that shipped with that filter is a
///   determinism guard only and is deliberately NOT pinned here - see
///   <see cref="GenerateReportV2_WhenSiteWorkerIsRemoved_UsesActiveSiteWorker"/>.
///
/// Every test asserts on the produced items, never on <c>Success</c> alone:
/// <c>GenerateReport</c> unconditionally appends a trailing model and therefore
/// reports success even when zero rows were built, so a <c>Success</c>-only
/// assertion passes vacuously whenever the seed is wrong.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationReportServiceRegressionTests : TestBaseSetup
{
    // "da" is seeded as Id 1 by SQL/420_SDK.sql and is what the stubbed
    // IUserService.GetCurrentUserLocale() returns, so every translation row the
    // service looks up must be written against this language.
    private const int DanishLanguageId = 1;

    private const int MissingSdkCaseId = 987654;

    private BackendConfigurationReportService CreateSut(Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        // Both stubs are load-bearing: an unstubbed GetCurrentUserTimeZoneInfo()
        // returns null and blows up in TimeZoneInfo.ConvertTimeFromUtc, and an
        // unstubbed GetCurrentUserLocale() makes
        // Languages.Single(x => x.LanguageCode == null) throw. Either failure is
        // caught by the method-wide try/catch and would masquerade as the bug
        // under test.
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserTimeZoneInfo().Returns(Task.FromResult(TimeZoneInfo.Utc));
        userService.GetCurrentUserLocale().Returns(Task.FromResult("da"));

        return new BackendConfigurationReportService(
            // The test-namespace stub (declared alongside
            // BackendConfigurationAssignmentWorkerServiceHelperTest), not the real
            // service: it echoes translation keys straight back, which is what
            // makes result.Message a readable diagnostic on failure.
            new BackendConfigurationLocalizationService(),
            Substitute.For<ILogger<BackendConfigurationReportService>>(),
            coreHelper,
            Substitute.For<IWordService>(),
            Substitute.For<IExcelService>(),
            Substitute.For<ICasePostBaseService>(),
            ItemsPlanningPnDbContext!,
            userService,
            BackendConfigurationPnDbContext!);
    }

    private static GenerateReportModel ReportModelCovering(DateTime doneAt) => new()
    {
        DateFrom = doneAt.Date.AddDays(-7),
        DateTo = doneAt.Date.AddDays(7),
        Type = "docx"
    };

    private static List<ReportEformItemModel> ItemsOf(
        OperationDataResult<List<OldReportEformModel>> result) =>
        result.Model.SelectMany(x => x.Items).ToList();

    private static List<ReportEformItemModel> ItemsOf(
        OperationDataResult<List<ReportEformModel>> result) =>
        result.Model.SelectMany(x => x.GroupEform).SelectMany(x => x.Items).ToList();

    /// <summary>
    /// A top-level (<c>ParentId = null</c>), childless checklist plus its Danish
    /// translation. Childlessness matters: <c>Core.Advanced_TemplateFieldReadAll</c>
    /// swallows its own failures and returns <b>null</b> (not an empty list) when
    /// reading the eForm fails, which NREs downstream. A bare top-level checklist
    /// reads back with an empty field list, so no Fields/FieldTranslations need
    /// seeding at all.
    /// </summary>
    private async Task<CheckList> SeedCheckList(string label)
    {
        var checkList = new CheckList
        {
            Label = label,
            Description = "",
            ParentId = null,
            Repeated = 0,
            DisplayIndex = 0,
            CaseType = "",
            FolderName = "",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
        await MicrotingDbContext!.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();

        await MicrotingDbContext.CheckListTranslations.AddAsync(new CheckListTranslation
        {
            CheckListId = checkList.Id,
            LanguageId = DanishLanguageId,
            Text = label,
            Description = "",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        });
        await MicrotingDbContext.SaveChangesAsync();

        return checkList;
    }

    /// <summary>
    /// The SDK case a planning case points at. <c>CreatedAt</c> is set explicitly
    /// because the SDK has no SaveChanges override to populate it and the service
    /// hard-casts <c>(DateTime)dbCase.CreatedAt</c> - leaving it null produces an
    /// NRE that looks exactly like the bug under test.
    /// </summary>
    private async Task<Case> SeedSdkCase(int checkListId, int? siteId)
    {
        var sdkCase = new Case
        {
            CheckListId = checkListId,
            SiteId = siteId,
            Status = 100,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase;
    }

    private async Task<Site> SeedSite(string name)
    {
        var site = new Site
        {
            Name = name,
            MicrotingUid = Random.Shared.Next(100000, 999999),
            LanguageId = DanishLanguageId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
        await MicrotingDbContext!.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site;
    }

    private async Task<Worker> SeedWorker(string employeeNo)
    {
        var worker = new Worker
        {
            FirstName = "Regression",
            LastName = employeeNo,
            Email = $"{Guid.NewGuid():N}@example.com",
            EmployeeNo = employeeNo,
            MicrotingUid = Random.Shared.Next(100000, 999999),
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
        await MicrotingDbContext!.Workers.AddAsync(worker);
        await MicrotingDbContext.SaveChangesAsync();
        return worker;
    }

    private async Task SeedSiteWorker(int siteId, int workerId, string workflowState)
    {
        await MicrotingDbContext!.SiteWorkers.AddAsync(new SiteWorker
        {
            SiteId = siteId,
            WorkerId = workerId,
            MicrotingUid = Random.Shared.Next(100000, 999999),
            WorkflowState = workflowState,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        });
        await MicrotingDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// A planning plus its Danish name translation. The translation is mandatory:
    /// both report methods skip a planning case outright when
    /// <c>PlanningNameTranslation</c> is missing, so without it no item is built
    /// and every assertion on items would be vacuous.
    /// </summary>
    private async Task<Planning> SeedPlanning(string name, int? reportGroupPlanningTagId)
    {
        var planning = new Planning
        {
            Description = $"{name} description",
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Day,
            RelatedEFormId = 0,
            RelatedEFormName = name,
            ReportGroupPlanningTagId = reportGroupPlanningTagId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext.PlanningNameTranslation.AddAsync(new PlanningNameTranslation
        {
            PlanningId = planning.Id,
            LanguageId = DanishLanguageId,
            Name = name,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return planning;
    }

    /// <summary>
    /// GenerateReportV2 groups by <c>Planning.ReportGroupPlanningTagId</c> and
    /// drops every group whose tag does not resolve, so a V2 test that skips this
    /// never reaches the code under test.
    /// </summary>
    private async Task<PlanningTag> SeedPlanningTag(string name)
    {
        var tag = new PlanningTag
        {
            Name = name,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(tag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return tag;
    }

    private async Task<PlanningCase> SeedPlanningCase(
        int planningId, int checkListId, int sdkCaseId, DateTime doneAt)
    {
        var planningCase = new PlanningCase
        {
            PlanningId = planningId,
            Status = 100,
            MicrotingSdkeFormId = checkListId,
            MicrotingSdkCaseId = sdkCaseId,
            MicrotingSdkCaseDoneAt = doneAt,
            DoneByUserName = "Regression Tester",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return planningCase;
    }

    /// <summary>
    /// The historic-data shape that triggers bug A: the planning has no live
    /// <c>AreaRulePlannings</c> row (unplanning zeroes <c>ItemPlanningId</c> in
    /// place), so the service falls back to the newest
    /// <c>AreaRulesPlanningVersions</c> row. Version rows are NOT written
    /// automatically, hence the plain AddAsync.
    /// </summary>
    private async Task SeedAreaRulePlanningVersion(int itemPlanningId, int propertyId)
    {
        await BackendConfigurationPnDbContext!.AreaRulesPlanningVersions.AddAsync(new AreaRulePlanningVersion
        {
            AreaRulePlanningId = 0,
            AreaRuleId = 0,
            AreaId = 0,
            FolderId = 0,
            ItemPlanningId = itemPlanningId,
            PropertyId = propertyId,
            Status = true,
            Version = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// The live-planning shape: an <c>AreaRulePlannings</c> row still pointing at
    /// the planning, which is the branch every not-yet-unplanned production
    /// planning takes. The service reads the property off THIS row and never
    /// consults <c>AreaRulesPlanningVersions</c>, so no version row is written
    /// here. The lookup has no <c>WorkflowState</c> filter, hence a plain
    /// <c>created</c> row is what production sees.
    ///
    /// <c>AreaRuleId</c> is a real FK
    /// (<c>FK_AreaRulePlannings_AreaRules_AreaRuleId</c>) and an <c>AreaRule</c>
    /// is in turn FK'd to an <c>Areas</c> and a <c>Properties</c> row, so that
    /// whole chain has to exist. None of it is read by the report - only
    /// <paramref name="propertyId"/>, set on the planning row itself, is.
    /// </summary>
    private async Task SeedAreaRulePlanning(int itemPlanningId, int propertyId)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var areaRuleOwner = await SeedProperty($"Area rule owner {Guid.NewGuid():N}");
        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            PropertyId = areaRuleOwner.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        await BackendConfigurationPnDbContext!.AreaRulePlannings.AddAsync(new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            AreaId = area.Id,
            FolderId = 0,
            ItemPlanningId = itemPlanningId,
            PropertyId = propertyId,
            Status = true,
            Version = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task<Property> SeedProperty(string name)
    {
        var property = new Property
        {
            Name = name,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return property;
    }

    /// <summary>
    /// Bug A, V1, version-fallback branch. The planning resolves only through an
    /// <c>AreaRulesPlanningVersions</c> row carrying <c>PropertyId = 0</c>, which
    /// matches no <c>Properties</c> row. The old <c>Properties.First(...)</c>
    /// threw "Sequence contains no elements" and, because the whole method body
    /// sits in one try/catch, killed the entire report. The row must instead come
    /// back with an empty property cell.
    /// </summary>
    [Test]
    public async Task GenerateReport_WhenPropertyIsUnresolvable_ReturnsItemWithBlankPropertyName()
    {
        var core = await GetCore();
        var doneAt = DateTime.UtcNow.AddDays(-1);

        var checkList = await SeedCheckList("Unresolvable property V1");
        var site = await SeedSite("report-v1-site");
        var sdkCase = await SeedSdkCase(checkList.Id, site.Id);
        var planning = await SeedPlanning("V1 orphan planning", reportGroupPlanningTagId: null);
        var planningCase = await SeedPlanningCase(planning.Id, checkList.Id, sdkCase.Id, doneAt);
        await SeedAreaRulePlanningVersion(planning.Id, propertyId: 0);

        var result = await CreateSut(core).GenerateReport(ReportModelCovering(doneAt), true);

        Assert.That(result.Success, Is.True, result.Message);
        var items = ItemsOf(result);
        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Id, Is.EqualTo(planningCase.Id));
        Assert.That(items[0].PropertyName, Is.EqualTo(""));
    }

    /// <summary>
    /// Bug A, V1, live-planning branch. Same unresolvable <c>PropertyId = 0</c>,
    /// but carried by a live <c>AreaRulePlannings</c> row instead of a version
    /// row, so the service never reaches the fallback. This is the branch the
    /// vast majority of production plannings take, and it had its own
    /// <c>Properties.First(...)</c> call site with the same
    /// "Sequence contains no elements" outage.
    /// </summary>
    [Test]
    public async Task GenerateReport_WhenLivePlanningPropertyIsUnresolvable_ReturnsItemWithBlankPropertyName()
    {
        var core = await GetCore();
        var doneAt = DateTime.UtcNow.AddDays(-1);

        var checkList = await SeedCheckList("Unresolvable live property V1");
        var site = await SeedSite("report-v1-live-site");
        var sdkCase = await SeedSdkCase(checkList.Id, site.Id);
        var planning = await SeedPlanning("V1 live planning", reportGroupPlanningTagId: null);
        var planningCase = await SeedPlanningCase(planning.Id, checkList.Id, sdkCase.Id, doneAt);
        // Live row, no version row: pins the areaRulePlanning != null branch.
        await SeedAreaRulePlanning(planning.Id, propertyId: 0);

        var result = await CreateSut(core).GenerateReport(ReportModelCovering(doneAt), true);

        Assert.That(result.Success, Is.True, result.Message);
        var items = ItemsOf(result);
        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Id, Is.EqualTo(planningCase.Id));
        Assert.That(items[0].PropertyName, Is.EqualTo(""));
    }

    /// <summary>
    /// Bug A, V2. Same unresolvable <c>PropertyId = 0</c> version row, exercised
    /// through <c>GenerateReportV2</c>, which had an identical
    /// <c>Properties.First(...)</c> call site.
    /// </summary>
    [Test]
    public async Task GenerateReportV2_WhenPropertyIsUnresolvable_ReturnsItemWithBlankPropertyName()
    {
        var core = await GetCore();
        var doneAt = DateTime.UtcNow.AddDays(-1);

        var checkList = await SeedCheckList("Unresolvable property V2");
        var site = await SeedSite("report-v2-site");
        var sdkCase = await SeedSdkCase(checkList.Id, site.Id);
        var tag = await SeedPlanningTag("V2 report group");
        var planning = await SeedPlanning("V2 orphan planning", tag.Id);
        var planningCase = await SeedPlanningCase(planning.Id, checkList.Id, sdkCase.Id, doneAt);
        await SeedAreaRulePlanningVersion(planning.Id, propertyId: 0);

        var result = await CreateSut(core).GenerateReportV2(ReportModelCovering(doneAt), true);

        Assert.That(result.Success, Is.True, result.Message);
        var items = ItemsOf(result);
        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Id, Is.EqualTo(planningCase.Id));
        Assert.That(items[0].PropertyName, Is.EqualTo(""));
    }

    /// <summary>
    /// Bug A, V2, live-planning branch. The V2 counterpart of the V1 live-row
    /// test: <c>GenerateReportV2</c> carries its own copy of the
    /// <c>areaRulePlanning != null</c> lookup and needs pinning separately.
    /// </summary>
    [Test]
    public async Task GenerateReportV2_WhenLivePlanningPropertyIsUnresolvable_ReturnsItemWithBlankPropertyName()
    {
        var core = await GetCore();
        var doneAt = DateTime.UtcNow.AddDays(-1);

        var checkList = await SeedCheckList("Unresolvable live property V2");
        var site = await SeedSite("report-v2-live-site");
        var sdkCase = await SeedSdkCase(checkList.Id, site.Id);
        var tag = await SeedPlanningTag("V2 live report group");
        var planning = await SeedPlanning("V2 live planning", tag.Id);
        var planningCase = await SeedPlanningCase(planning.Id, checkList.Id, sdkCase.Id, doneAt);
        // Live row, no version row: pins the areaRulePlanning != null branch.
        await SeedAreaRulePlanning(planning.Id, propertyId: 0);

        var result = await CreateSut(core).GenerateReportV2(ReportModelCovering(doneAt), true);

        Assert.That(result.Success, Is.True, result.Message);
        var items = ItemsOf(result);
        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Id, Is.EqualTo(planningCase.Id));
        Assert.That(items[0].PropertyName, Is.EqualTo(""));
    }

    /// <summary>
    /// Positive control for bug A: when the version row's <c>PropertyId</c> DOES
    /// resolve, the real property name must still reach the report. Without this
    /// the blank-name tests above would also pass against a fix that blanked
    /// every property name.
    /// </summary>
    [Test]
    public async Task GenerateReport_WhenPropertyResolves_ReturnsItemWithPropertyName()
    {
        var core = await GetCore();
        var doneAt = DateTime.UtcNow.AddDays(-1);

        var property = await SeedProperty($"Resolvable property {Guid.NewGuid():N}");
        var checkList = await SeedCheckList("Resolvable property V1");
        var site = await SeedSite("report-v1-resolvable-site");
        var sdkCase = await SeedSdkCase(checkList.Id, site.Id);
        var planning = await SeedPlanning("V1 planning with property", reportGroupPlanningTagId: null);
        var planningCase = await SeedPlanningCase(planning.Id, checkList.Id, sdkCase.Id, doneAt);
        await SeedAreaRulePlanningVersion(planning.Id, property.Id);

        var result = await CreateSut(core).GenerateReport(ReportModelCovering(doneAt), true);

        Assert.That(result.Success, Is.True, result.Message);
        var items = ItemsOf(result);
        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Id, Is.EqualTo(planningCase.Id));
        Assert.That(items[0].PropertyName, Is.EqualTo(property.Name));
    }

    /// <summary>
    /// Bug B, worker lookup. The case's site has no <c>SiteWorkers</c> row at all.
    /// The old <c>SiteWorkers.First(x => x.SiteId == dbCase.SiteId)</c> threw
    /// "Sequence contains no elements" and aborted the whole report; the row must
    /// instead be reported with an empty <c>EmployeeNo</c>.
    ///
    /// This also doubles as the V2 positive control for bug A: it is the only V2
    /// test whose property resolves AND that asserts on <c>PropertyName</c>, so
    /// without that assertion a V2 fix that blanked every property name would go
    /// unnoticed (the V1 positive control above does not cover
    /// <c>GenerateReportV2</c>).
    /// </summary>
    [Test]
    public async Task GenerateReportV2_WhenSiteHasNoWorker_ReturnsItemWithBlankEmployeeNo()
    {
        var core = await GetCore();
        var doneAt = DateTime.UtcNow.AddDays(-1);

        var checkList = await SeedCheckList("Worker-less site V2");
        // Deliberately no SiteWorker for this site (the dump's SiteWorkers 1 and 2
        // point at Sites 1 and 2, never at a freshly created site).
        var site = await SeedSite("report-v2-site-without-worker");
        var sdkCase = await SeedSdkCase(checkList.Id, site.Id);
        var tag = await SeedPlanningTag("V2 worker-less group");
        var planning = await SeedPlanning("V2 worker-less planning", tag.Id);
        var planningCase = await SeedPlanningCase(planning.Id, checkList.Id, sdkCase.Id, doneAt);
        // Resolvable property: keeps this test failing only for the worker defect
        // rather than tripping the (separate) unresolvable-property bug first.
        var property = await SeedProperty($"Worker-less property {Guid.NewGuid():N}");
        await SeedAreaRulePlanningVersion(planning.Id, property.Id);

        var result = await CreateSut(core).GenerateReportV2(ReportModelCovering(doneAt), true);

        Assert.That(result.Success, Is.True, result.Message);
        var items = ItemsOf(result);
        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Id, Is.EqualTo(planningCase.Id));
        Assert.That(items[0].EmployeeNo, Is.EqualTo(""));
        Assert.That(items[0].PropertyName, Is.EqualTo(property.Name));
    }

    /// <summary>
    /// Bug B, missing SDK case. <c>dbCase.SiteId</c> used to be dereferenced
    /// BEFORE the <c>dbCase == null</c> guard, so a planning case pointing at a
    /// deleted SDK case NREd out of the whole report instead of hitting the
    /// intended <c>continue</c>. A second, healthy planning case in the same group
    /// proves the loop continues rather than aborting: the good row must still be
    /// returned and the dangling one silently dropped.
    /// </summary>
    [Test]
    public async Task GenerateReportV2_WhenCaseIsMissing_SkipsCaseAndReturnsOtherItems()
    {
        var core = await GetCore();
        var goodDoneAt = DateTime.UtcNow.AddDays(-1);
        // Ordered first inside the group (the loop sorts by MicrotingSdkCaseDoneAt),
        // so the bad row is hit before the good one.
        var danglingDoneAt = goodDoneAt.AddHours(-1);

        var checkList = await SeedCheckList("Missing case V2");
        var site = await SeedSite("report-v2-missing-case-site");
        var sdkCase = await SeedSdkCase(checkList.Id, site.Id);
        var tag = await SeedPlanningTag("V2 missing-case group");
        var planning = await SeedPlanning("V2 missing-case planning", tag.Id);
        // Resolvable property: keeps this test failing only for the missing-case
        // defect rather than tripping the (separate) unresolvable-property bug first.
        var property = await SeedProperty($"Missing-case property {Guid.NewGuid():N}");
        await SeedAreaRulePlanningVersion(planning.Id, property.Id);

        var danglingPlanningCase =
            await SeedPlanningCase(planning.Id, checkList.Id, MissingSdkCaseId, danglingDoneAt);
        var goodPlanningCase =
            await SeedPlanningCase(planning.Id, checkList.Id, sdkCase.Id, goodDoneAt);

        var result = await CreateSut(core).GenerateReportV2(ReportModelCovering(goodDoneAt), true);

        Assert.That(result.Success, Is.True, result.Message);
        var items = ItemsOf(result);
        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Id, Is.EqualTo(goodPlanningCase.Id));
        Assert.That(items.Select(x => x.Id), Does.Not.Contain(danglingPlanningCase.Id));
    }

    /// <summary>
    /// Bug B, site-worker selection. One site with two <c>SiteWorkers</c> rows: an
    /// older soft-deleted one and a newer active one. The old
    /// <c>SiteWorkers.First(...)</c> had no <c>WorkflowState</c> filter, so it
    /// reported the removed worker's employee number. The active row must win.
    ///
    /// The filter is all this pins. The fix also added <c>OrderBy(x => x.Id)</c>,
    /// but with the filter in place exactly one row survives, so dropping the
    /// ordering still yields <c>EMP-ACTIVE</c> - and an unordered MySQL
    /// primary-key scan returns the lowest Id anyway. The <c>OrderBy</c> is a
    /// determinism guard that no seed here can meaningfully distinguish.
    /// </summary>
    [Test]
    public async Task GenerateReportV2_WhenSiteWorkerIsRemoved_UsesActiveSiteWorker()
    {
        var core = await GetCore();
        var doneAt = DateTime.UtcNow.AddDays(-1);

        var checkList = await SeedCheckList("Removed site worker V2");
        var site = await SeedSite("report-v2-removed-site-worker");
        var removedWorker = await SeedWorker("EMP-REMOVED");
        var activeWorker = await SeedWorker("EMP-ACTIVE");
        // Inserted first, so it also has the lower Id and wins any lookup that
        // ignores WorkflowState.
        await SeedSiteWorker(site.Id, removedWorker.Id, Constants.WorkflowStates.Removed);
        await SeedSiteWorker(site.Id, activeWorker.Id, Constants.WorkflowStates.Created);

        var sdkCase = await SeedSdkCase(checkList.Id, site.Id);
        var tag = await SeedPlanningTag("V2 removed-site-worker group");
        var planning = await SeedPlanning("V2 removed-site-worker planning", tag.Id);
        var planningCase = await SeedPlanningCase(planning.Id, checkList.Id, sdkCase.Id, doneAt);
        // Resolvable property: keeps this test failing only for the site-worker
        // defect rather than tripping the (separate) unresolvable-property bug first.
        var property = await SeedProperty($"Removed-site-worker property {Guid.NewGuid():N}");
        await SeedAreaRulePlanningVersion(planning.Id, property.Id);

        var result = await CreateSut(core).GenerateReportV2(ReportModelCovering(doneAt), true);

        Assert.That(result.Success, Is.True, result.Message);
        var items = ItemsOf(result);
        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Id, Is.EqualTo(planningCase.Id));
        Assert.That(items[0].EmployeeNo, Is.EqualTo("EMP-ACTIVE"));
    }
}
