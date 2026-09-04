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

using System.Globalization;
using eFormCore;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// DB-backed integration coverage for
/// <c>POST api/backend-configuration-pn/compliance-report/eform-columns</c>
/// (<see cref="BackendConfigurationComplianceReportService.EformColumns"/>) — issue #1166 §11.
///
/// <para>
/// The fixture seeds SDK templates by hand — a top <c>CheckList</c> with ZERO direct
/// fields plus a child <c>CheckList</c> the fields hang off — because that is both the
/// shape the SDK itself produces and the shape #1166 §4 identifies as the one a
/// non-recursive column derivation silently returns nothing for. Seeding the rows
/// directly (rather than through <c>TemplateFromXml</c>/<c>TemplateCreate</c>) is what
/// makes per-language label gaps, dirty stored values and individual field types
/// addressable one at a time.
/// </para>
///
/// <para>
/// Two SDK behaviours the seeding has to respect, learned the hard way:
/// <c>SqlController.GetElement</c> filters fields on <c>(Dummy == 1) != true</c> and on
/// <c>WorkflowState != removed</c>, and in SQL a NULL in either column makes the
/// predicate NULL — so both are always set explicitly. And there is no <c>Movie</c>
/// case in <c>SqlController.GetDataItem</c>: a <c>Movie</c> field makes the SDK's own
/// reader throw, so the excluded-types test seeds the other seven. <c>Movie</c> is
/// still in the service's exclusion list.
/// </para>
///
/// <para>
/// <c>Compliances</c> carries a UNIQUE index on <c>(PlanningId, Deadline)</c>, so every
/// row seeded against one planning gets its own deadline.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceReportEformColumnsTests : TestBaseSetup
{
    private int _uidCounter = 960_000;

    [SetUp]
    public async Task CleanTables()
    {
        // FK-safe cleanup, children before parents, so each test starts from an
        // empty compliance/template world and group counts can be asserted as
        // absolute numbers.
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptionSites.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        BackendConfigurationPnDbContext.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
        BackendConfigurationPnDbContext.AreaRulePlanningTags.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningTags);
        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarBoards.RemoveRange(
            BackendConfigurationPnDbContext.CalendarBoards);
        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRuleTranslations.RemoveRange(
            BackendConfigurationPnDbContext.AreaRuleTranslations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRules.RemoveRange(BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Areas.RemoveRange(BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.PlanningTags.RemoveRange(ItemsPlanningPnDbContext.PlanningTags);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // SDK side: the answers and the cases they hang off, children first.
        //
        // The TEMPLATE graph (CheckLists / Fields / their translations and options)
        // is deliberately LEFT IN PLACE. Groups are produced only from Compliance
        // rows, and those are all gone by this point, so a leftover template can
        // never leak into a result — while dropping the whole graph would have to
        // fight the SDK dump's own seeded checklists and everything referencing
        // them. Each test seeds its own template and asserts against its own ids.
        MicrotingDbContext!.FieldValues.RemoveRange(MicrotingDbContext.FieldValues);
        await MicrotingDbContext.SaveChangesAsync();

        MicrotingDbContext.UploadedDatas.RemoveRange(MicrotingDbContext.UploadedDatas);
        MicrotingDbContext.Cases.RemoveRange(MicrotingDbContext.Cases);
        await MicrotingDbContext.SaveChangesAsync();
    }

    // ==================================================================
    // Service construction
    // ==================================================================

    private BackendConfigurationComplianceReportService BuildService(Core core, Language language)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        userService.GetCurrentUserLocale().Returns(Task.FromResult(language.LanguageCode));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        return new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, ItemsPlanningPnDbContext!,
            NullLogger<BackendConfigurationComplianceReportService>.Instance);
    }

    private Task<Language> Danish() =>
        MicrotingDbContext!.Languages.FirstAsync(x => x.LanguageCode == "da");

    private Task<Language> German() =>
        MicrotingDbContext!.Languages.FirstAsync(x => x.LanguageCode == "de-DE");

    private static ComplianceReportRequestModel Request(DateTime from, DateTime to, string status = "all")
        => new()
        {
            DateFrom = from,
            DateTo = to,
            Status = status,
            BoardIds = [],
            TagIds = [],
            SiteIds = [],
            PageSize = 0
        };

    private async Task<List<ComplianceReportTagGroupModel>> Run(
        Core core, Language language, DateTime from, DateTime to, string status = "all")
    {
        var result = await BuildService(core, language).EformColumns(Request(from, to, status));
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private static ComplianceReportTemplateGroupModel OnlyTemplate(List<ComplianceReportTagGroupModel> groups)
    {
        Assert.That(groups, Has.Count.EqualTo(1), "expected exactly one tag group");
        Assert.That(groups[0].Templates, Has.Count.EqualTo(1), "expected exactly one template group");
        return groups[0].Templates[0];
    }

    // ==================================================================
    // SDK template seeding
    // ==================================================================

    /// <summary>
    /// Seeds one <c>CheckList</c> plus a translation per supplied language.
    /// <paramref name="parentId"/> null makes it a top-level template.
    /// </summary>
    private async Task<int> SeedCheckList(
        string label, int? parentId, params (int LanguageId, string Text)[] translations)
    {
        var checkList = new CheckList
        {
            Label = $"{label}-{Guid.NewGuid()}",
            ParentId = parentId,
            DisplayIndex = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();

        foreach (var (languageId, text) in translations)
        {
            await MicrotingDbContext.CheckListTranslations.AddAsync(new CheckListTranslation
            {
                CheckListId = checkList.Id,
                LanguageId = languageId,
                Text = text,
                Description = string.Empty,
                WorkflowState = Constants.WorkflowStates.Created
            });
        }

        await MicrotingDbContext.SaveChangesAsync();
        return checkList.Id;
    }

    /// <summary>
    /// The two-level template every test uses: a top-level template with ZERO direct
    /// fields and one child checklist that carries them. Returns
    /// (templateCheckListId, childCheckListId).
    /// </summary>
    private async Task<(int TemplateId, int ChildId)> SeedTwoLevelTemplate(
        string name, params (int LanguageId, string Text)[] translations)
    {
        var templateId = await SeedCheckList($"{name}-top", null, translations);
        var childId = await SeedCheckList($"{name}-child", templateId, translations);
        return (templateId, childId);
    }

    /// <summary>
    /// Seeds one field on a checklist.
    ///
    /// <para>
    /// <c>Dummy</c> and <c>WorkflowState</c> are ALWAYS set: the SDK's reader filters
    /// on both, and a NULL makes those SQL predicates NULL, which silently drops the
    /// field from the derived column set. <c>Date</c> needs parseable
    /// Min/MaxValue and <c>NumberStepper</c> a parseable translation
    /// <c>DefaultValue</c>, or <c>SqlController.GetDataItem</c> throws.
    /// </para>
    /// </summary>
    private async Task<int> SeedField(
        int checkListId, string fieldType, int displayIndex,
        (int LanguageId, string Text)[] labels, int? parentFieldId = null)
    {
        var fieldTypeRow = await MicrotingDbContext!.FieldTypes.FirstAsync(x => x.Type == fieldType);

        var field = new Field
        {
            CheckListId = checkListId,
            FieldTypeId = fieldTypeRow.Id,
            ParentFieldId = parentFieldId,
            Label = labels.Length > 0 ? labels[0].Text : fieldType,
            Description = string.Empty,
            Color = "e8eaf6",
            DisplayIndex = displayIndex,
            Dummy = 0,
            Mandatory = 0,
            ReadOnly = 0,
            Multi = 0,
            Selected = 0,
            Split = 0,
            GeolocationEnabled = 0,
            GeolocationForced = 0,
            GeolocationHidden = 0,
            StopOnSave = 0,
            IsNum = 0,
            BarcodeEnabled = 0,
            BarcodeType = string.Empty,
            QueryType = string.Empty,
            MaxLength = 0,
            DecimalCount = 0,
            EntityGroupId = 0,
            DefaultValue = "0",
            MinValue = fieldType == Constants.FieldTypes.Date ? "2000-01-01" : null,
            MaxValue = fieldType == Constants.FieldTypes.Date ? "2100-01-01" : null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Fields.AddAsync(field);
        await MicrotingDbContext.SaveChangesAsync();

        foreach (var (languageId, text) in labels)
        {
            await MicrotingDbContext.FieldTranslations.AddAsync(new FieldTranslation
            {
                FieldId = field.Id,
                LanguageId = languageId,
                Text = text,
                Description = string.Empty,
                // "0" satisfies every DefaultValue consumer in GetDataItem —
                // int.Parse for NumberStepper, Tools.Bool for CheckBox, a plain
                // string everywhere else.
                DefaultValue = "0",
                WorkflowState = Constants.WorkflowStates.Created
            });
        }

        await MicrotingDbContext.SaveChangesAsync();
        return field.Id;
    }

    /// <summary>Seeds a select option and its translation. Returns the option id.</summary>
    private async Task<int> SeedFieldOption(
        int fieldId, string key, params (int LanguageId, string Text)[] translations)
    {
        var option = new FieldOption
        {
            FieldId = fieldId,
            Key = key,
            Selected = false,
            DisplayOrder = "0",
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.FieldOptions.AddAsync(option);
        await MicrotingDbContext.SaveChangesAsync();

        foreach (var (languageId, text) in translations)
        {
            await MicrotingDbContext.FieldOptionTranslations.AddAsync(new FieldOptionTranslation
            {
                FieldOptionId = option.Id,
                LanguageId = languageId,
                Text = text,
                WorkflowState = Constants.WorkflowStates.Created
            });
        }

        await MicrotingDbContext.SaveChangesAsync();
        return option.Id;
    }

    private async Task<int> SeedFieldValue(
        int caseId, int fieldId, int checkListId, string value,
        int? uploadedDataId = null, string latitude = null, string longitude = null)
    {
        var fieldValue = new FieldValue
        {
            CaseId = caseId,
            FieldId = fieldId,
            CheckListId = checkListId,
            UploadedDataId = uploadedDataId,
            Value = value,
            Latitude = latitude,
            Longitude = longitude,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.FieldValues.AddAsync(fieldValue);
        await MicrotingDbContext.SaveChangesAsync();
        return fieldValue.Id;
    }

    private async Task<int> SeedUploadedData(
        string fileName = "photo.jpg", string checksum = "abc123", string extension = ".jpg",
        bool removed = false)
    {
        var uploadedData = new Microting.eForm.Infrastructure.Data.Entities.UploadedData
        {
            FileName = fileName,
            Checksum = checksum,
            Extension = extension,
            FileLocation = "/tmp/",
            WorkflowState = removed ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.UploadedDatas.AddAsync(uploadedData);
        await MicrotingDbContext.SaveChangesAsync();
        return uploadedData.Id;
    }

    private async Task<int> SeedEntityItem(string name)
    {
        var entityItem = new EntityItem
        {
            EntityGroupId = 1,
            EntityItemUid = Guid.NewGuid().ToString()[..8],
            MicrotingUid = Guid.NewGuid().ToString()[..8],
            Name = name,
            Description = string.Empty,
            DisplayIndex = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.EntityItems.AddAsync(entityItem);
        await MicrotingDbContext.SaveChangesAsync();
        return entityItem.Id;
    }

    // ==================================================================
    // Compliance-side seeding (same shape as ComplianceReportIndexTests)
    // ==================================================================

    private async Task<int> SeedSdkSite(string name)
    {
        var uid = ++_uidCounter;
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = $"{name}-{uid}",
            MicrotingUid = uid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkSite.Id;
    }

    private async Task<int> SeedSdkCase(int? checkListId, int status = 100, DateTime? doneAt = null)
    {
        var siteId = await SeedSdkSite("eform-columns-site");
        var sdkCase = new Case
        {
            SiteId = siteId,
            Status = status,
            DoneAt = doneAt,
            CheckListId = checkListId,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase.Id;
    }

    private async Task<(int AreaId, int PropertyId)> SeedAreaAndProperty(string propertyName)
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
            Name = $"{propertyName}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (area.Id, property.Id);
    }

    private async Task<(int ArpId, int PropertyId, int PlanningId, int AreaId, int AreaRuleId)> SeedSeries(
        string propertyName, string title, DateTime startDate, int? eformId = 0)
    {
        var (areaId, propertyId) = await SeedAreaAndProperty(propertyName);

        var areaRule = new AreaRule
        {
            AreaId = areaId, PropertyId = propertyId, EformId = eformId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            DayOfWeek = DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId, AreaId = areaId,
            ItemPlanningId = planning.Id,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc), Status = true,
            RepeatType = 2, RepeatEvery = 1, RepeatWeekdaysCsv = "1", DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (arp.Id, propertyId, planning.Id, areaId, areaRule.Id);
    }

    private async Task<int> SeedCompliance(
        int planningId, int propertyId, int areaId, DateTime deadline, int sdkCaseId,
        string itemName = "Fallback Item Name")
    {
        var compliance = new Compliance
        {
            ItemName = itemName,
            PlanningId = planningId,
            PropertyId = propertyId,
            AreaId = areaId,
            Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCaseId,
            MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private async Task<int> SeedTag(string name)
    {
        var tag = new PlanningTag
        {
            Name = name,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(tag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return tag.Id;
    }

    private async Task SeedArpTag(int arpId, int tagId)
    {
        await BackendConfigurationPnDbContext!.AreaRulePlanningTags.AddAsync(new AreaRulePlanningTag
        {
            AreaRulePlanningId = arpId, ItemPlanningTagId = tagId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// The whole default arrangement in one call: a two-level template with the given
    /// answerable fields, one done SDK case against it, and a compliance row.
    /// </summary>
    private async Task<Fixture> SeedOneCase(
        string name, int languageId, params (string FieldType, string Label)[] fields)
    {
        var today = DateTime.UtcNow.Date;
        var (templateId, childId) = await SeedTwoLevelTemplate(name, (languageId, name));

        var fieldIds = new List<int>();
        for (var i = 0; i < fields.Length; i++)
        {
            fieldIds.Add(await SeedField(
                childId, fields[i].FieldType, i, [(languageId, fields[i].Label)]));
        }

        var caseId = await SeedSdkCase(templateId, doneAt: today.AddDays(-1));
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            $"{name}Prop", $"{name} Title", today.AddDays(-30));
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);

        return new Fixture
        {
            TemplateId = templateId,
            ChildId = childId,
            FieldIds = fieldIds,
            CaseId = caseId,
            ArpId = arpId,
            PropertyId = propertyId,
            PlanningId = planningId,
            AreaId = areaId,
            ComplianceId = complianceId
        };
    }

    private sealed class Fixture
    {
        public int TemplateId { get; init; }
        public int ChildId { get; init; }
        public List<int> FieldIds { get; init; }
        public int CaseId { get; init; }
        public int ArpId { get; init; }
        public int PropertyId { get; init; }
        public int PlanningId { get; init; }
        public int AreaId { get; init; }
        public int ComplianceId { get; init; }
    }

    private static (DateTime From, DateTime To) Window()
    {
        var today = DateTime.UtcNow.Date;
        return (today.AddDays(-60), today.AddDays(60));
    }

    // ==================================================================
    // COLUMN DERIVATION
    // ==================================================================

    /// <summary>
    /// The single most important test in #1166: a template with ZERO directly-attached
    /// fields whose three fields hang off a child checklist still yields THREE columns.
    /// A derivation that read <c>Fields WHERE CheckListId = @templateId</c> would return
    /// an empty column set here and render the report as empty tables rather than as an
    /// error — which is the shape of the templates holding most live compliance cases.
    /// </summary>
    [Test]
    public async Task EformColumns_NestedTemplate_ZeroDirectFields_YieldsChildChecklistColumns()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Nested", da.Id,
            (Constants.FieldTypes.Comment, "Kommentar"),
            (Constants.FieldTypes.Number, "Antal"),
            (Constants.FieldTypes.Text, "Fritekst"));

        // The template itself really has no direct fields.
        Assert.That(
            await MicrotingDbContext!.Fields.CountAsync(x => x.CheckListId == fixture.TemplateId),
            Is.EqualTo(0));

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.CheckListId, Is.EqualTo(fixture.TemplateId));
        Assert.That(template.Columns, Has.Count.EqualTo(3));
        Assert.That(template.Columns.Select(c => c.FieldId), Is.EquivalentTo(fixture.FieldIds));
        Assert.That(template.Columns.Select(c => c.Key),
            Is.EqualTo(fixture.FieldIds.Select(id => $"f{id}")).AsCollection);
        Assert.That(template.MergedCheckListIds, Is.EqualTo(new List<int> { fixture.TemplateId }));
    }

    /// <summary>
    /// A field under a <c>FieldGroup</c> (non-null <c>ParentFieldId</c>) is a column;
    /// the group itself is not. Groups nest one level of fields that a flat
    /// <c>Fields WHERE ParentFieldId IS NULL</c> read would miss entirely.
    /// </summary>
    [Test]
    public async Task EformColumns_FieldGroupChild_AppearsAsColumn()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;

        var (templateId, childId) = await SeedTwoLevelTemplate("Grouped", (da.Id, "Grouped"));
        var groupId = await SeedField(childId, Constants.FieldTypes.FieldGroup, 0, [(da.Id, "Gruppe")]);
        var insideGroupId = await SeedField(
            childId, Constants.FieldTypes.Comment, 1, [(da.Id, "I gruppen")], parentFieldId: groupId);

        var caseId = await SeedSdkCase(templateId, doneAt: today.AddDays(-1));
        var (_, propertyId, planningId, areaId, _) = await SeedSeries("GroupProp", "Group", today.AddDays(-30));
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);
        await SeedFieldValue(caseId, insideGroupId, childId, "svar i gruppen");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Columns.Select(c => c.FieldId), Is.EqualTo(new[] { insideGroupId }).AsCollection);
        Assert.That(template.Cases[0].Cells[$"f{insideGroupId}"], Is.EqualTo("svar i gruppen"));
    }

    /// <summary>
    /// Seven excluded field types plus two answerable ones yield exactly TWO columns.
    /// <c>Movie</c> is the eighth in the exclusion list but cannot be seeded: the SDK's
    /// own <c>GetDataItem</c> has no <c>Movie</c> case and throws
    /// <c>IndexOutOfRangeException</c> for one, so no live template can reach this path
    /// carrying a Movie field either.
    /// </summary>
    [Test]
    public async Task EformColumns_ExcludedTypes_YieldOnlyTheAnswerableColumns()
    {
        var core = await GetCore();
        var da = await Danish();
        await SeedOneCase("Excluded", da.Id,
            (Constants.FieldTypes.None, "Overskrift"),
            (Constants.FieldTypes.Picture, "Billede"),
            (Constants.FieldTypes.Audio, "Lyd"),
            (Constants.FieldTypes.Signature, "Underskrift"),
            (Constants.FieldTypes.ShowPdf, "PDF"),
            (Constants.FieldTypes.FieldGroup, "Gruppe"),
            (Constants.FieldTypes.SaveButton, "Gem"),
            (Constants.FieldTypes.Comment, "Kommentar"),
            (Constants.FieldTypes.Number, "Antal"));

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Columns, Has.Count.EqualTo(2));
        Assert.That(template.Columns.Select(c => c.Label), Is.EqualTo(new[] { "Kommentar", "Antal" }).AsCollection);
    }

    /// <summary>
    /// <c>ShowPicture</c> is the deliberate deviation from the shipped exclusion list
    /// (#1166 §5): it is display-only and carries no answer, so this path gives it no
    /// column — unlike <c>BackendConfigurationReportService</c>, where it falls through
    /// to <c>default:</c> and renders its raw value.
    /// </summary>
    [Test]
    public async Task EformColumns_ShowPicture_IsExcludedFromColumns()
    {
        var core = await GetCore();
        var da = await Danish();
        await SeedOneCase("ShowPic", da.Id,
            (Constants.FieldTypes.ShowPicture, "Vis billede"),
            (Constants.FieldTypes.Comment, "Kommentar"));

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Columns, Has.Count.EqualTo(1));
        Assert.That(template.Columns[0].Label, Is.EqualTo("Kommentar"));
    }

    /// <summary>
    /// THE direct regression test for #1160 finding 3. <c>Audio</c> and <c>ShowPdf</c>
    /// are excluded from headers by the shipped code but still emit a cell through its
    /// <c>default:</c> arm, shifting every later column by one in a positional cell
    /// list. Here they carry real answers and the cell bag still holds exactly the two
    /// answered ANSWERABLE keys — a shift is not expressible.
    /// </summary>
    [Test]
    public async Task EformColumns_ExcludedTypeAnswers_NeverReachTheCellBag()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Desync", da.Id,
            (Constants.FieldTypes.Audio, "Lyd"),
            (Constants.FieldTypes.Comment, "Kommentar"),
            (Constants.FieldTypes.ShowPdf, "PDF"),
            (Constants.FieldTypes.Number, "Antal"));

        // Answers for BOTH excluded fields and both answerable ones.
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "lydfil.mp3");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[1], fixture.ChildId, "en kommentar");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[2], fixture.ChildId, "dokument.pdf");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[3], fixture.ChildId, "42");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Columns, Has.Count.EqualTo(2));
        var cells = template.Cases.Single().Cells;
        Assert.That(cells.Keys, Is.EquivalentTo(new[] { $"f{fixture.FieldIds[1]}", $"f{fixture.FieldIds[3]}" }));
        Assert.That(cells[$"f{fixture.FieldIds[1]}"], Is.EqualTo("en kommentar"));
        Assert.That(cells[$"f{fixture.FieldIds[3]}"], Is.EqualTo("42"));
    }

    /// <summary>
    /// Keyed addressing: a case answering only the SECOND of three columns produces a
    /// one-entry dictionary under that column's key, and the other two keys are ABSENT
    /// — never present with an empty string. That absence is the whole contract: #1167
    /// renders its empty glyph from a missing key.
    /// </summary>
    [Test]
    public async Task EformColumns_UnansweredFields_HaveNoKeyRatherThanAnEmptyCell()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Sparse", da.Id,
            (Constants.FieldTypes.Comment, "Et"),
            (Constants.FieldTypes.Comment, "To"),
            (Constants.FieldTypes.Comment, "Tre"));

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[1], fixture.ChildId, "kun midterste");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Columns, Has.Count.EqualTo(3));
        var cells = template.Cases.Single().Cells;
        Assert.That(cells, Has.Count.EqualTo(1));
        Assert.That(cells.ContainsKey($"f{fixture.FieldIds[0]}"), Is.False);
        Assert.That(cells[$"f{fixture.FieldIds[1]}"], Is.EqualTo("kun midterste"));
        Assert.That(cells.ContainsKey($"f{fixture.FieldIds[2]}"), Is.False);
    }

    // ==================================================================
    // PER-TYPE RENDERING
    // ==================================================================

    /// <summary><c>SingleSelect</c> stores an option KEY; the cell shows the option's
    /// translated label.</summary>
    [Test]
    public async Task EformColumns_SingleSelect_RendersTheOptionLabel()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Single", da.Id, (Constants.FieldTypes.SingleSelect, "Valg"));

        await SeedFieldOption(fixture.FieldIds[0], "1", (da.Id, "Ja"));
        await SeedFieldOption(fixture.FieldIds[0], "2", (da.Id, "Nej"));
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "2");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Cases.Single().Cells[$"f{fixture.FieldIds[0]}"], Is.EqualTo("Nej"));
    }

    /// <summary><c>MultiSelect</c> stores pipe-joined option keys; both labels come back,
    /// joined with a comma.</summary>
    [Test]
    public async Task EformColumns_MultiSelect_PipeJoinedKeys_RenderBothLabels()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Multi", da.Id, (Constants.FieldTypes.MultiSelect, "Valg"));

        await SeedFieldOption(fixture.FieldIds[0], "1", (da.Id, "Alfa"));
        await SeedFieldOption(fixture.FieldIds[0], "2", (da.Id, "Beta"));
        await SeedFieldOption(fixture.FieldIds[0], "3", (da.Id, "Gamma"));
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "1|3");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Cases.Single().Cells[$"f{fixture.FieldIds[0]}"], Is.EqualTo("Alfa, Gamma"));
    }

    /// <summary>
    /// The dirty legacy <c>MultiSelect</c> shape — comma-joined "0,1" — resolves to no
    /// option and therefore to NO CELL. Echoing "0,1" at the user would be worse than
    /// an empty cell.
    /// </summary>
    [Test]
    public async Task EformColumns_MultiSelect_LegacyCommaValue_YieldsNoCell()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("MultiDirty", da.Id, (Constants.FieldTypes.MultiSelect, "Valg"));

        await SeedFieldOption(fixture.FieldIds[0], "1", (da.Id, "Alfa"));
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "0,1");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        // The column count is asserted too: a degraded schema (derivation threw and
        // the group came back with ZERO columns) also produces an empty cell bag,
        // and without this the test would pass on that for the wrong reason.
        Assert.That(template.Columns, Has.Count.EqualTo(1));
        Assert.That(template.Cases.Single().Cells, Is.Empty);
    }

    /// <summary>
    /// Over a thousand live entity rows hold the LITERAL string <c>"null"</c>. Parsing
    /// it is the shipped code's guarded case; the point here is that the guard survives
    /// into the batched path — no cell, no exception.
    /// </summary>
    [Test]
    public async Task EformColumns_EntitySearch_LiteralNullValue_YieldsNoCellAndDoesNotThrow()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("EntityNull", da.Id, (Constants.FieldTypes.EntitySearch, "Enhed"));

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "null");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Columns, Has.Count.EqualTo(1));
        Assert.That(template.Cases.Single().Cells, Is.Empty);
    }

    /// <summary>
    /// An entity answer pointing at an <c>EntityItem</c> that no longer exists yields no
    /// cell. The shipped code dereferences <c>match.Name</c> with no null check and
    /// raises a <c>NullReferenceException</c> here
    /// (<c>BackendConfigurationReportService.cs:907-911</c>).
    /// </summary>
    [Test]
    public async Task EformColumns_EntitySearch_MissingEntityItem_YieldsNoCellAndDoesNotThrow()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("EntityGone", da.Id, (Constants.FieldTypes.EntitySearch, "Enhed"));

        var entityItemId = await SeedEntityItem("Slettet enhed");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, entityItemId.ToString());

        MicrotingDbContext!.EntityItems.RemoveRange(
            MicrotingDbContext.EntityItems.Where(x => x.Id == entityItemId));
        await MicrotingDbContext.SaveChangesAsync();

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        // Column count asserted for the same reason as the sibling
        // LiteralNullValue test: an empty cell bag alone would also be produced by a
        // degraded, zero-column schema.
        Assert.That(template.Columns, Has.Count.EqualTo(1));
        Assert.That(template.Cases.Single().Cells, Is.Empty);
    }

    /// <summary>An entity answer that DOES resolve renders the entity's name.</summary>
    [Test]
    public async Task EformColumns_EntitySelect_ResolvesTheEntityName()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("EntityOk", da.Id, (Constants.FieldTypes.EntitySelect, "Enhed"));

        var entityItemId = await SeedEntityItem("Stald 3");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, entityItemId.ToString());

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.Cases.Single().Cells[$"f{fixture.FieldIds[0]}"], Is.EqualTo("Stald 3"));
    }

    /// <summary>
    /// Dirty <c>CheckBox</c> values — the live data holds one <c>true</c> and one
    /// <c>false</c> alongside 591 <c>checked</c>/<c>unchecked</c> rows — normalise to
    /// the canonical tokens, which #1167 localises.
    /// </summary>
    [Test]
    public async Task EformColumns_CheckBox_DirtyTrueFalse_NormaliseToCheckedAndUnchecked()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("CheckDirty", da.Id,
            (Constants.FieldTypes.CheckBox, "Sandt"),
            (Constants.FieldTypes.CheckBox, "Falsk"));

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "true");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[1], fixture.ChildId, "false");

        var (from, to) = Window();
        var cells = OnlyTemplate(await Run(core, da, from, to)).Cases.Single().Cells;

        Assert.That(cells[$"f{fixture.FieldIds[0]}"], Is.EqualTo("checked"));
        Assert.That(cells[$"f{fixture.FieldIds[1]}"], Is.EqualTo("unchecked"));
    }

    /// <summary>The canonical <c>checked</c> token passes through unchanged.</summary>
    [Test]
    public async Task EformColumns_CheckBox_CheckedPassesThrough()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("CheckClean", da.Id, (Constants.FieldTypes.CheckBox, "Afkrydset"));

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "checked");

        var (from, to) = Window();
        var cells = OnlyTemplate(await Run(core, da, from, to)).Cases.Single().Cells;

        Assert.That(cells[$"f{fixture.FieldIds[0]}"], Is.EqualTo("checked"));
    }

    /// <summary>Comma decimals are emitted invariant, so #1167 never has to guess a locale.</summary>
    [Test]
    public async Task EformColumns_Number_CommaDecimal_RendersInvariant()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Num", da.Id, (Constants.FieldTypes.Number, "Antal"));

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "3,5");

        var (from, to) = Window();
        var cells = OnlyTemplate(await Run(core, da, from, to)).Cases.Single().Cells;

        Assert.That(cells[$"f{fixture.FieldIds[0]}"], Is.EqualTo("3.5"));
    }

    /// <summary><c>Date</c> answers pass through as stored — never reformatted server-side.</summary>
    [Test]
    public async Task EformColumns_Date_PassesThrough()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Dato", da.Id, (Constants.FieldTypes.Date, "Dato"));

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "2021-11-29");

        var (from, to) = Window();
        var cells = OnlyTemplate(await Run(core, da, from, to)).Cases.Single().Cells;

        Assert.That(cells[$"f{fixture.FieldIds[0]}"], Is.EqualTo("2021-11-29"));
    }

    /// <summary>
    /// <c>Timer</c> stores FOUR pipe-separated parts —
    /// <c>start|stop|state|elapsed_ms</c>, not two. #1166 §5 leaves the rendering to be
    /// decided and pinned: the cell is the ELAPSED duration as <c>H:mm:ss</c>, and the
    /// raw value is dropped. 38000 ms is the live example.
    /// </summary>
    [Test]
    public async Task EformColumns_Timer_FourPartValue_RendersElapsedDuration()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Tid", da.Id, (Constants.FieldTypes.Timer, "Varighed"));

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId,
            "2021-12-12 11:15:50 UTC|2021-12-12 11:16:27 UTC|paused|38000");

        var (from, to) = Window();
        var cells = OnlyTemplate(await Run(core, da, from, to)).Cases.Single().Cells;

        Assert.That(cells[$"f{fixture.FieldIds[0]}"], Is.EqualTo("0:00:38"));
    }

    /// <summary>A <c>Timer</c> value that is not in the four-part shape gets no cell
    /// rather than leaking the raw pipe-separated string into the table.</summary>
    [Test]
    public async Task EformColumns_Timer_MalformedValue_YieldsNoCell()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("TidDirty", da.Id, (Constants.FieldTypes.Timer, "Varighed"));

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "start|stop");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        // Column count asserted so the empty cell bag is attributable to the
        // malformed value and not to a degraded, zero-column schema.
        Assert.That(template.Columns, Has.Count.EqualTo(1));
        Assert.That(template.Cases.Single().Cells, Is.Empty);
    }

    /// <summary>
    /// A <c>Timer</c> whose elapsed part is a well-formed number too large for
    /// <c>TimeSpan</c> gets no cell — and, critically, does not fail the REPORT.
    ///
    /// <para>
    /// Any 16-to-19 digit value parses as a <c>long</c> and only blows up inside
    /// <c>TimeSpan.FromMilliseconds</c>. That throw would escape <c>Render</c>,
    /// <c>LoadAnswers</c> and <c>ProjectAsync</c> into <c>EformColumns</c>' outer
    /// catch, so ONE dirty cell would return <c>Success = false</c> for every tag and
    /// every template. The second, ordinary field on the same case is what proves the
    /// rest of the report still renders rather than merely that the call returned.
    /// </para>
    /// </summary>
    [Test]
    public async Task EformColumns_Timer_ElapsedMillisecondsOverflowsTimeSpan_YieldsNoCellAndDoesNotFailTheReport()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("TidOverflow", da.Id,
            (Constants.FieldTypes.Timer, "Varighed"),
            (Constants.FieldTypes.Comment, "Kommentar"));

        // Four well-formed parts; the elapsed part is 16 digits, which is a valid
        // long and roughly ten times TimeSpan's ceiling in milliseconds.
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId,
            "0|0|stopped|9999999999999999");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[1], fixture.ChildId, "Alt vel");

        var (from, to) = Window();
        var result = await BuildService(core, da).EformColumns(Request(from, to));

        Assert.That(result.Success, Is.True, result.Message);

        var template = OnlyTemplate(result.Model);
        var cells = template.Cases.Single().Cells;

        Assert.That(template.Columns, Has.Count.EqualTo(2));
        Assert.That(cells.ContainsKey($"f{fixture.FieldIds[0]}"), Is.False,
            "the out-of-range Timer must get no cell");
        Assert.That(cells[$"f{fixture.FieldIds[1]}"], Is.EqualTo("Alt vel"),
            "the rest of the report must still render");
    }

    // ==================================================================
    // TRANSLATION FALLBACK
    // ==================================================================

    /// <summary>
    /// A field with NO translation in the user's language but one in another language
    /// yields that other label, and does not throw. Run for <c>de-DE</c> specifically:
    /// that is the language most live fields lack, and the SDK's other flattener
    /// (<c>GenerateDataSetFromCasesSubSet</c>) uses a bare <c>FirstAsync</c> that throws
    /// on exactly this data.
    ///
    /// <para>
    /// The CHECKLISTS deliberately do carry a German translation. The SDK's
    /// <c>TemplateFieldReadAll</c> still resolves a field's parent name with a bare
    /// <c>CheckListTranslations.FirstAsync</c> (<c>SqlController.cs:668-670</c>), so a
    /// German-less checklist would throw inside the SDK rather than exercise the field
    /// fallback this test is about.
    /// </para>
    ///
    /// <para>
    /// Asserting the LABEL, not merely "did not throw": the service degrades an
    /// unreadable template to an empty column set, so a swallowed exception would
    /// otherwise pass silently.
    /// </para>
    /// </summary>
    [Test]
    public async Task EformColumns_TranslationFallback_MissingGermanFieldLabel_UsesAnotherLanguage()
    {
        var core = await GetCore();
        var da = await Danish();
        var de = await German();
        var today = DateTime.UtcNow.Date;

        var (templateId, childId) = await SeedTwoLevelTemplate(
            "Uebersetzung", (da.Id, "Skema"), (de.Id, "Formular"));

        // Danish only — no German translation for this field.
        var fieldId = await SeedField(childId, Constants.FieldTypes.Comment, 0, [(da.Id, "Kun dansk")]);

        var caseId = await SeedSdkCase(templateId, doneAt: today.AddDays(-1));
        var (_, propertyId, planningId, areaId, _) = await SeedSeries("DeProp", "De", today.AddDays(-30));
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);
        await SeedFieldValue(caseId, fieldId, childId, "et svar");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, de, from, to));

        Assert.That(template.Columns, Has.Count.EqualTo(1));
        Assert.That(template.Columns[0].Label, Is.EqualTo("Kun dansk"));
        Assert.That(template.CheckListName, Is.EqualTo("Formular"));
        Assert.That(template.Cases.Single().Cells[$"f{fieldId}"], Is.EqualTo("et svar"));
    }

    /// <summary>
    /// The same fallback for option labels, which the service resolves itself rather
    /// than inheriting from the SDK. The shipped code's
    /// <c>FieldOptionTranslations.FirstAsync</c> throws here.
    /// </summary>
    [Test]
    public async Task EformColumns_TranslationFallback_MissingGermanOptionLabel_UsesAnotherLanguage()
    {
        var core = await GetCore();
        var da = await Danish();
        var de = await German();
        var today = DateTime.UtcNow.Date;

        var (templateId, childId) = await SeedTwoLevelTemplate(
            "OptUebersetzung", (da.Id, "Skema"), (de.Id, "Formular"));
        var fieldId = await SeedField(
            childId, Constants.FieldTypes.SingleSelect, 0, [(da.Id, "Valg"), (de.Id, "Auswahl")]);

        // Option translated in Danish only.
        await SeedFieldOption(fieldId, "1", (da.Id, "Kun dansk valg"));

        var caseId = await SeedSdkCase(templateId, doneAt: today.AddDays(-1));
        var (_, propertyId, planningId, areaId, _) = await SeedSeries("DeOptProp", "DeOpt", today.AddDays(-30));
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);
        await SeedFieldValue(caseId, fieldId, childId, "1");

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, de, from, to));

        Assert.That(template.Cases.Single().Cells[$"f{fieldId}"], Is.EqualTo("Kun dansk valg"));
    }

    // ==================================================================
    // IMAGES
    // ==================================================================

    /// <summary>
    /// Two picture answers on one case: <c>ImagesCount == 2</c>, two entries, and the
    /// display name DERIVED as <c>{UploadedDataId}_700_{Checksum}{Extension}</c>. The
    /// stored <c>FileName</c> is only an existence check, never the name itself.
    /// </summary>
    [Test]
    public async Task EformColumns_Images_TwoPictureAnswers_CountedWithDerivedFileName()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("Billeder", da.Id,
            (Constants.FieldTypes.Picture, "Billede"),
            (Constants.FieldTypes.Comment, "Kommentar"));

        var firstUpload = await SeedUploadedData("first.jpg", "sum1", ".jpg");
        var secondUpload = await SeedUploadedData("second.jpg", "sum2", ".png");

        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, null,
            uploadedDataId: firstUpload, latitude: "56.1", longitude: "10.2");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, null,
            uploadedDataId: secondUpload);

        var (from, to) = Window();
        var caseModel = OnlyTemplate(await Run(core, da, from, to)).Cases.Single();

        Assert.That(caseModel.ImagesCount, Is.EqualTo(2));
        Assert.That(caseModel.Images, Has.Count.EqualTo(2));
        Assert.That(caseModel.Images.Select(i => i.FileName),
            Is.EquivalentTo(new[] { $"{firstUpload}_700_sum1.jpg", $"{secondUpload}_700_sum2.png" }));
        Assert.That(caseModel.Images.Single(i => i.UploadedDataId == firstUpload).GeoLink,
            Is.EqualTo("https://www.google.com/maps/place/56.1,10.2"));
        Assert.That(caseModel.Images.Single(i => i.UploadedDataId == secondUpload).GeoLink, Is.Null);

        // A Picture field is excluded from the CELLS, not from the images.
        Assert.That(caseModel.Cells, Is.Empty);
    }

    /// <summary>An empty stored <c>FileName</c> means no derived name — the guard the
    /// shipped code applies at <c>BackendConfigurationReportService.cs:741-744</c>.</summary>
    [Test]
    public async Task EformColumns_Images_EmptyStoredFileName_YieldsNoDerivedName()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("BilledeTomt", da.Id, (Constants.FieldTypes.Picture, "Billede"));

        var uploadId = await SeedUploadedData(fileName: "", checksum: "sum9", extension: ".jpg");
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, null, uploadedDataId: uploadId);

        var (from, to) = Window();
        var caseModel = OnlyTemplate(await Run(core, da, from, to)).Cases.Single();

        Assert.That(caseModel.ImagesCount, Is.EqualTo(1));
        Assert.That(caseModel.Images.Single().FileName, Is.Null);
    }

    /// <summary>A soft-removed <c>UploadedData</c> is not an image any more.</summary>
    [Test]
    public async Task EformColumns_Images_RemovedUploadedData_IsExcluded()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("BilledeFjernet", da.Id, (Constants.FieldTypes.Picture, "Billede"));

        var liveUpload = await SeedUploadedData("live.jpg", "live", ".jpg");
        var removedUpload = await SeedUploadedData("gone.jpg", "gone", ".jpg", removed: true);
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, null, uploadedDataId: liveUpload);
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, null,
            uploadedDataId: removedUpload);

        var (from, to) = Window();
        var caseModel = OnlyTemplate(await Run(core, da, from, to)).Cases.Single();

        Assert.That(caseModel.ImagesCount, Is.EqualTo(1));
        Assert.That(caseModel.Images.Single().UploadedDataId, Is.EqualTo(liveUpload));
    }

    /// <summary>A template with no picture field produces no images (and issues no image
    /// query at all — the empty <c>pictureFieldIds</c> short circuit).</summary>
    [Test]
    public async Task EformColumns_NoPictureField_YieldsZeroImages()
    {
        var core = await GetCore();
        var da = await Danish();
        var fixture = await SeedOneCase("IngenBilleder", da.Id, (Constants.FieldTypes.Comment, "Kommentar"));
        await SeedFieldValue(fixture.CaseId, fixture.FieldIds[0], fixture.ChildId, "tekst");

        var (from, to) = Window();
        var caseModel = OnlyTemplate(await Run(core, da, from, to)).Cases.Single();

        Assert.That(caseModel.ImagesCount, Is.EqualTo(0));
        Assert.That(caseModel.Images, Is.Empty);
    }

    // ==================================================================
    // TEMPLATE KEY (#1160 finding 1)
    // ==================================================================

    /// <summary>
    /// A case whose <c>AreaRule.EformId</c> points at a DIFFERENT template still lands
    /// in the group of the template it actually answered. <c>EformId</c> tracks current
    /// configuration; the case records what was answered.
    /// </summary>
    [Test]
    public async Task EformColumns_TemplateKey_UsesCaseCheckListId_NotAreaRuleEformId()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;

        var (answeredTemplateId, childId) = await SeedTwoLevelTemplate("Besvaret", (da.Id, "Besvaret"));
        await SeedField(childId, Constants.FieldTypes.Comment, 0, [(da.Id, "Kommentar")]);

        var (configuredTemplateId, otherChildId) = await SeedTwoLevelTemplate("Konfigureret", (da.Id, "Konfigureret"));
        await SeedField(otherChildId, Constants.FieldTypes.Number, 0, [(da.Id, "Andet felt")]);

        var caseId = await SeedSdkCase(answeredTemplateId, doneAt: today.AddDays(-1));
        var (_, propertyId, planningId, areaId, _) = await SeedSeries(
            "KeyProp", "Key", today.AddDays(-30), eformId: configuredTemplateId);
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.CheckListId, Is.EqualTo(answeredTemplateId));
        Assert.That(template.CheckListName, Is.EqualTo("Besvaret"));
        Assert.That(template.Columns.Single().Label, Is.EqualTo("Kommentar"));
    }

    /// <summary>A NULL <c>AreaRule.EformId</c> — 16 % of live rows — still groups, because
    /// the key never comes from there.</summary>
    [Test]
    public async Task EformColumns_TemplateKey_NullAreaRuleEformId_StillGroups()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;

        var (templateId, childId) = await SeedTwoLevelTemplate("NulEform", (da.Id, "NulEform"));
        await SeedField(childId, Constants.FieldTypes.Comment, 0, [(da.Id, "Kommentar")]);

        var caseId = await SeedSdkCase(templateId, doneAt: today.AddDays(-1));
        var (_, propertyId, planningId, areaId, _) = await SeedSeries(
            "NullProp", "Null", today.AddDays(-30), eformId: null);
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);

        var (from, to) = Window();
        var template = OnlyTemplate(await Run(core, da, from, to));

        Assert.That(template.CheckListId, Is.EqualTo(templateId));
        Assert.That(template.Cases, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// A compliance row with no backing SDK case has no answers, so it forms no group.
    /// Rapport is a report of answers; Detaljer (#1165) still shows the row.
    ///
    /// <para>
    /// Seeded WITH a positive control — a second, properly answered row in the same
    /// window. Asserting only that the response is empty would pass identically on a
    /// seeding or date-window mistake that matched nothing at all; asserting that
    /// exactly the ANSWERED row survives makes the omission attributable to the
    /// missing SDK case.
    /// </para>
    /// </summary>
    [Test]
    public async Task EformColumns_RowWithoutSdkCase_FormsNoGroup()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;

        var (_, propertyId, planningId, areaId, _) = await SeedSeries(
            "NoCaseProp", "NoCase", today.AddDays(-30));
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), sdkCaseId: 0);

        // The control: same window, same filters, but backed by a real SDK case.
        var answeredFixture = await SeedOneCase(
            "WithCase", da.Id, (Constants.FieldTypes.Text, "Bemærkning"));
        await SeedFieldValue(
            answeredFixture.CaseId, answeredFixture.FieldIds[0], answeredFixture.ChildId, "Udført");

        var (from, to) = Window();
        var groups = await Run(core, da, from, to);

        var template = OnlyTemplate(groups);
        var onlyCase = template.Cases.Single();
        Assert.That(onlyCase.SdkCaseId, Is.EqualTo(answeredFixture.CaseId));
        Assert.That(onlyCase.ComplianceId, Is.EqualTo(answeredFixture.ComplianceId));
    }

    // ==================================================================
    // GROUPING
    // ==================================================================

    /// <summary>Two templates under ONE tag: one tag group, two template groups, each
    /// with its OWN column set.</summary>
    [Test]
    public async Task EformColumns_TwoTemplatesUnderOneTag_YieldOneTagGroupWithTwoTemplateGroups()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;

        var tagId = await SeedTag("Fælles tag");

        var (templateA, childA) = await SeedTwoLevelTemplate("AaSkema", (da.Id, "AaSkema"));
        await SeedField(childA, Constants.FieldTypes.Comment, 0, [(da.Id, "A felt")]);
        var (templateB, childB) = await SeedTwoLevelTemplate("BbSkema", (da.Id, "BbSkema"));
        await SeedField(childB, Constants.FieldTypes.Number, 0, [(da.Id, "B felt")]);

        var caseA = await SeedSdkCase(templateA, doneAt: today.AddDays(-1));
        var caseB = await SeedSdkCase(templateB, doneAt: today.AddDays(-2));

        var (arpA, propertyA, planningA, areaA, _) = await SeedSeries("TagPropA", "A", today.AddDays(-30));
        var (arpB, propertyB, planningB, areaB, _) = await SeedSeries("TagPropB", "B", today.AddDays(-30));
        await SeedArpTag(arpA, tagId);
        await SeedArpTag(arpB, tagId);
        await SeedCompliance(planningA, propertyA, areaA, today.AddDays(-1), caseA);
        await SeedCompliance(planningB, propertyB, areaB, today.AddDays(-2), caseB);

        var (from, to) = Window();
        var groups = await Run(core, da, from, to);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].TagId, Is.EqualTo(tagId));
        Assert.That(groups[0].TagName, Is.EqualTo("Fælles tag"));
        Assert.That(groups[0].Templates, Has.Count.EqualTo(2));
        Assert.That(groups[0].Templates.Select(t => t.CheckListId), Is.EquivalentTo(new[] { templateA, templateB }));
        Assert.That(
            groups[0].Templates.Single(t => t.CheckListId == templateA).Columns.Single().Label,
            Is.EqualTo("A felt"));
        Assert.That(
            groups[0].Templates.Single(t => t.CheckListId == templateB).Columns.Single().Label,
            Is.EqualTo("B felt"));
    }

    /// <summary>The same template under TWO tags appears in both tag groups.</summary>
    [Test]
    public async Task EformColumns_SameTemplateUnderTwoTags_YieldsTwoTagGroups()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;

        var firstTag = await SeedTag("Aa tag");
        var secondTag = await SeedTag("Bb tag");

        var (templateId, childId) = await SeedTwoLevelTemplate("ToTags", (da.Id, "ToTags"));
        await SeedField(childId, Constants.FieldTypes.Comment, 0, [(da.Id, "Felt")]);

        var caseId = await SeedSdkCase(templateId, doneAt: today.AddDays(-1));
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries("TwoTagProp", "T", today.AddDays(-30));
        await SeedArpTag(arpId, firstTag);
        await SeedArpTag(arpId, secondTag);
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);

        var (from, to) = Window();
        var groups = await Run(core, da, from, to);

        Assert.That(groups, Has.Count.EqualTo(2));
        Assert.That(groups.Select(g => g.TagId), Is.EqualTo(new int?[] { firstTag, secondTag }).AsCollection);
        foreach (var group in groups)
        {
            Assert.That(group.Templates.Single().Cases.Single().ComplianceId, Is.EqualTo(complianceId));
        }
    }

    /// <summary>
    /// With a TAG FILTER set, only the selected tags form groups. A row tagged
    /// {A, B} filtered to {A} renders one section, not two — the filter must not
    /// look as if it leaked. The row itself is never lost.
    /// </summary>
    [Test]
    public async Task EformColumns_TagFilter_GroupsOnlyTheSelectedTags()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;

        var selectedTag = await SeedTag("Valgt tag");
        var otherTag = await SeedTag("Andet tag");

        var (templateId, childId) = await SeedTwoLevelTemplate("Filtreret", (da.Id, "Filtreret"));
        await SeedField(childId, Constants.FieldTypes.Comment, 0, [(da.Id, "Felt")]);

        var caseId = await SeedSdkCase(templateId, doneAt: today.AddDays(-1));
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries("FilterProp", "F", today.AddDays(-30));
        await SeedArpTag(arpId, selectedTag);
        await SeedArpTag(arpId, otherTag);
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);

        var (from, to) = Window();
        var request = Request(from, to);
        request.TagIds = [selectedTag];

        var result = await BuildService(core, da).EformColumns(request);
        Assert.That(result.Success, Is.True, result.Message);

        Assert.That(result.Model, Has.Count.EqualTo(1));
        Assert.That(result.Model[0].TagId, Is.EqualTo(selectedTag));
        Assert.That(result.Model[0].Templates.Single().Cases, Has.Count.EqualTo(1));
    }

    /// <summary>A row with no tag lands in the single untagged group, whose
    /// <c>TagId</c> is null — the API carries no "Uden tag" label.</summary>
    [Test]
    public async Task EformColumns_UntaggedRows_LandInTheNullTagGroup()
    {
        var core = await GetCore();
        var da = await Danish();
        await SeedOneCase("UdenTag", da.Id, (Constants.FieldTypes.Comment, "Felt"));

        var (from, to) = Window();
        var groups = await Run(core, da, from, to);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].TagId, Is.Null);
        Assert.That(groups[0].TagName, Is.Null);
    }

    /// <summary>
    /// Structurally-identical CLONED templates currently produce TWO groups. #1166 §8
    /// files the merge as a follow-up and explicitly does not build it, so this pins the
    /// present behaviour for that work to change deliberately.
    /// </summary>
    [Test]
    public async Task EformColumns_ClonedTemplates_CurrentlyProduceTwoSeparateGroups()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;

        // Same NAME, same field sequence, different CheckListIds — the 509/511 shape.
        var (firstTemplate, firstChild) = await SeedTwoLevelTemplate("Kvittering", (da.Id, "Kvittering"));
        await SeedField(firstChild, Constants.FieldTypes.Comment, 0, [(da.Id, "Kommentar")]);
        var (secondTemplate, secondChild) = await SeedTwoLevelTemplate("Kvittering", (da.Id, "Kvittering"));
        await SeedField(secondChild, Constants.FieldTypes.Comment, 0, [(da.Id, "Kommentar")]);

        var firstCase = await SeedSdkCase(firstTemplate, doneAt: today.AddDays(-1));
        var secondCase = await SeedSdkCase(secondTemplate, doneAt: today.AddDays(-2));

        var (_, propertyId, planningId, areaId, _) = await SeedSeries("CloneProp", "Clone", today.AddDays(-30));
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), firstCase);
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-2), secondCase);

        var (from, to) = Window();
        var groups = await Run(core, da, from, to);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Templates, Has.Count.EqualTo(2));
        Assert.That(groups[0].Templates.Select(t => t.CheckListId),
            Is.EqualTo(new[] { Math.Min(firstTemplate, secondTemplate), Math.Max(firstTemplate, secondTemplate) })
                .AsCollection);
        foreach (var template in groups[0].Templates)
        {
            Assert.That(template.MergedCheckListIds, Is.EqualTo(new List<int> { template.CheckListId }));
        }
    }

    // ==================================================================
    // CASE METADATA
    // ==================================================================

    /// <summary>
    /// "Udført dato" is CASE metadata — <c>DoneAtUserModifiable ?? DoneAt</c> off the SDK
    /// case — and never an answer field (#1160 finding 7).
    /// </summary>
    [Test]
    public async Task EformColumns_DoneAt_ComesFromTheCaseAndNotFromAnAnswer()
    {
        var core = await GetCore();
        var da = await Danish();
        var today = DateTime.UtcNow.Date;
        var doneAt = today.AddDays(-1).AddHours(13);

        var (templateId, childId) = await SeedTwoLevelTemplate("Udfoert", (da.Id, "Udfoert"));
        var fieldId = await SeedField(childId, Constants.FieldTypes.Date, 0, [(da.Id, "En dato")]);

        var caseId = await SeedSdkCase(templateId, doneAt: doneAt);
        var (_, propertyId, planningId, areaId, _) = await SeedSeries("DoneProp", "Done", today.AddDays(-30));
        await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-1), caseId);
        // A worker-entered Date answer that is NOT the completion timestamp.
        await SeedFieldValue(caseId, fieldId, childId, "2019-01-01");

        var (from, to) = Window();
        var caseModel = OnlyTemplate(await Run(core, da, from, to)).Cases.Single();

        Assert.That(caseModel.Completed, Is.True);
        Assert.That(caseModel.DoneAt, Is.EqualTo(doneAt));
        Assert.That(caseModel.Cells[$"f{fieldId}"], Is.EqualTo("2019-01-01"));
        Assert.That(caseModel.TaskDate, Is.EqualTo(today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Assert.That(caseModel.SdkCaseId, Is.EqualTo(caseId));
    }
}
