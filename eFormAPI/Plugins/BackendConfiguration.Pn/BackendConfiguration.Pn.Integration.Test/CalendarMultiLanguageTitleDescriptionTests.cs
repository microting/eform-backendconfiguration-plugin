/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction.
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System.Globalization;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
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
/// Verifies the calendar read path surfaces per-language Title AND Description
/// (AreaRuleTranslation.Name / .Description, added in base 10.0.42). A task with
/// Danish/English/German translations must render in the CALLER's language and
/// expose all translations for edit-mode prefill.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarMultiLanguageTitleDescriptionTests : TestBaseSetup
{
    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    [SetUp]
    public async Task CleanState()
    {
        BackendConfigurationPnDbContext!.AreaRuleTranslations.RemoveRange(BackendConfigurationPnDbContext.AreaRuleTranslations);
        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.AreaRules.RemoveRange(BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.Areas.RemoveRange(BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        ItemsPlanningPnDbContext!.Plannings.RemoveRange(ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
    }

    private sealed record Langs(int Da, int En, int De);

    // Ensure da/en/de languages exist; return their ids.
    private async Task<Langs> EnsureLanguages()
    {
        async Task<int> Ensure(string name, string code)
        {
            var existing = await MicrotingDbContext!.Languages
                .FirstOrDefaultAsync(x => x.LanguageCode == code);
            if (existing != null) return existing.Id;
            var lang = new Language { Name = name, LanguageCode = code, IsActive = true };
            await MicrotingDbContext.Languages.AddAsync(lang);
            await MicrotingDbContext.SaveChangesAsync();
            return lang.Id;
        }
        return new Langs(await Ensure("Dansk", "da"), await Ensure("English", "en-US"), await Ensure("Deutsch", "de-DE"));
    }

    [Test]
    public async Task GetTasksForWeek_ReturnsCallerLanguageTitleDescription_AndAllTranslations()
    {
        var core = await GetCore();
        var langs = await EnsureLanguages();

        var sdkSite = new Site
        {
            Name = $"ml-site-{Guid.NewGuid()}", MicrotingUid = 7711,
            LanguageId = langs.De, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area { Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property { Name = $"MlProp-{Guid.NewGuid()}", ItemPlanningTagId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule { AreaId = area.Id, PropertyId = property.Id, EformId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Three per-language translations, each carrying BOTH name and description.
        foreach (var (langId, name, desc) in new[]
                 {
                     (langs.Da, "Tank 4 inspektion", "Kontrollér ventiler og noter tryk."),
                     (langs.En, "Tank 4 inspection", "Check valves and note the pressure."),
                     (langs.De, "Tank 4 Inspektion", "Ventile prüfen und den Druck notieren."),
                 })
        {
            await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
            { AreaRuleId = areaRule.Id, LanguageId = langId, Name = name, Description = desc, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 });
        }
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 6, 0, 0, 0, DateTimeKind.Utc); // 1st Saturday of June 2026
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Month, StartDate = startDate,
            DayOfMonth = 0, Description = "Kontrollér ventiler og noter tryk.", RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id, ItemPlanningId = planning.Id,
            StartDate = startDate, Status = true, RepeatType = 3, RepeatEvery = 1,
            RepeatOrdinalWeek = 1, DayOfWeek = 6, // 1st Saturday
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        { AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Caller language = German.
        var svc = BuildService(core, langs.De);
        var weekStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc); // Mon containing Sat 6 Jun
        var res = await svc.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = property.Id, WeekStart = IsoUtc(weekStart),
            WeekEnd = IsoUtc(weekStart.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false, BoardIds = [], TagNames = [], SiteIds = []
        });

        Assert.That(res.Success, Is.True, res.Message);
        var tile = res.Model!.SingleOrDefault(t => t.TaskDate == "2026-06-06");
        Assert.That(tile, Is.Not.Null, "expected one tile on Sat 6 Jun");

        // Rendered in the caller (German) language.
        Assert.That(tile!.Title, Is.EqualTo("Tank 4 Inspektion"), "title in caller language");
        Assert.That(tile.DescriptionHtml, Is.EqualTo("Ventile prüfen und den Druck notieren."), "description in caller language");

        // All three translations exposed for edit-mode prefill, each with name + description.
        Assert.That(tile.Translations.Count, Is.EqualTo(3));
        var en = tile.Translations.Single(t => t.LanguageId == langs.En);
        Assert.That(en.Name, Is.EqualTo("Tank 4 inspection"));
        Assert.That(en.Description, Is.EqualTo("Check valves and note the pressure."));
        var da = tile.Translations.Single(t => t.LanguageId == langs.Da);
        Assert.That(da.Description, Is.EqualTo("Kontrollér ventiler og noter tryk."));
    }

    // Seeds a monthly 1st-Saturday task with da/en/de AreaRuleTranslations
    // (Name+Description) and returns the property id + language ids.
    private async Task<(int PropertyId, Langs Langs)> SeedTriLingualTask()
    {
        var langs = await EnsureLanguages();

        var area = new Area { Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property { Name = $"MlProp-{Guid.NewGuid()}", ItemPlanningTagId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule { AreaId = area.Id, PropertyId = property.Id, EformId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        foreach (var (langId, name, desc) in new[]
                 {
                     (langs.Da, "Tank 4 inspektion", "Kontrollér ventiler og noter tryk."),
                     (langs.En, "Tank 4 inspection", "Check valves and note the pressure."),
                     (langs.De, "Tank 4 Inspektion", "Ventile prüfen und den Druck notieren."),
                 })
        {
            await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
            { AreaRuleId = areaRule.Id, LanguageId = langId, Name = name, Description = desc, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 });
        }
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 6, 0, 0, 0, DateTimeKind.Utc);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Month, StartDate = startDate,
            DayOfMonth = 0, Description = "Kontrollér ventiler og noter tryk.", RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id, ItemPlanningId = planning.Id,
            StartDate = startDate, Status = true, RepeatType = 3, RepeatEvery = 1,
            RepeatOrdinalWeek = 1, DayOfWeek = 6,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        { AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (property.Id, langs);
    }

    // The gRPC seam: requestModel.LanguageId overrides the current-user language
    // (the worker's Site.LanguageId), and null falls back to the current user.
    [Test]
    public async Task GetTasksForWeek_RequestLanguageId_OverridesCurrentUserLanguage()
    {
        var core = await GetCore();
        var (propertyId, langs) = await SeedTriLingualTask();

        // Current user is DANISH; the request asks for ENGLISH (the worker's site).
        var svc = BuildService(core, langs.Da);
        var weekStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        async Task<CalendarTaskResponseModel> Tile(int? languageId)
        {
            var res = await svc.GetTasksForWeek(new CalendarTaskRequestModel
            {
                PropertyId = propertyId, WeekStart = IsoUtc(weekStart),
                WeekEnd = IsoUtc(weekStart.AddDays(6).AddHours(23).AddMinutes(59)),
                ActionableOnly = false, BoardIds = [], TagNames = [], SiteIds = [], LanguageId = languageId
            });
            Assert.That(res.Success, Is.True, res.Message);
            return res.Model!.Single(t => t.TaskDate == "2026-06-06");
        }

        var overridden = await Tile(langs.En);
        Assert.That(overridden.Title, Is.EqualTo("Tank 4 inspection"), "LanguageId override → English title");
        Assert.That(overridden.DescriptionHtml, Is.EqualTo("Check valves and note the pressure."), "LanguageId override → English description");

        var fallback = await Tile(null);
        Assert.That(fallback.Title, Is.EqualTo("Tank 4 inspektion"), "null LanguageId → current-user (Danish) title");
        Assert.That(fallback.DescriptionHtml, Is.EqualTo("Kontrollér ventiler og noter tryk."), "null LanguageId → current-user (Danish) description");
    }

    private BackendConfigurationCalendarService BuildService(eFormCore.Core core, int callerLanguageId)
    {
        var language = MicrotingDbContext!.Languages.First(x => x.Id == callerLanguageId);
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            Substitute.For<IBackendConfigurationComplianceReportService>());
    }
}
