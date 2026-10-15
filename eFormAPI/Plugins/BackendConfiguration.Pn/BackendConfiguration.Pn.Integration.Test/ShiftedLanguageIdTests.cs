/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction.
*/

namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationAreaRulesService;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using CommonDictionaryModel = Microting.eFormApi.BasePn.Infrastructure.Models.Common.CommonDictionaryModel;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;

/// <summary>
/// eform-angular-frontend#8141: SDK lookup rows are seeded through AUTO_INCREMENT, so on a
/// MariaDB Galera cluster (auto_increment_increment=3) a fresh install gets Dansk=3,
/// English=6, Deutsch=9 instead of 1/2/3. Code that filters on the literal ids 1/2/3 then
/// reads the wrong language: LanguageId==3 is DANISH on such a tenant, not German.
///
/// Each test runs against a tenant whose SDK Languages are permuted into that Galera
/// layout (no deletes, so no FK cascades): id 1 = Norsk, id 2 = Français, id 3 = Dansk,
/// id 6 = English, id 9 = Deutsch. Ids 1 and 2 deliberately still EXIST, so a literal
/// lookup silently finds the wrong language instead of failing loudly.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ShiftedLanguageIdTests : TestBaseSetup
{
    private const int GaleraDanishId = 3;
    private const int GaleraEnglishId = 6;
    private const int GaleraGermanId = 9;

    private async Task ForceGaleraLanguageIds()
    {
        // Idempotent: keyed by Id, so a second run in the same fixture is a no-op.
        var layout = new (int Id, string Name, string Code)[]
        {
            (1, "Norsk", "no-NO"),
            (2, "Français", "fr-FR"),
            (GaleraDanishId, "Dansk", "da"),
            (GaleraEnglishId, "English", "en-US"),
            (GaleraGermanId, "Deutsch", "de-DE"),
        };
        foreach (var (id, name, code) in layout)
        {
            await MicrotingDbContext!.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Languages SET Name = {name}, LanguageCode = {code}, IsActive = 1 WHERE Id = {id}");
        }

        var resolved = await MicrotingDbContext!.Languages.AsNoTracking()
            .Where(x => x.LanguageCode == "da" || x.LanguageCode == "en-US" || x.LanguageCode == "de-DE")
            .ToDictionaryAsync(x => x.LanguageCode, x => x.Id);
        Assert.That(resolved, Is.EquivalentTo(new Dictionary<string, int>
        {
            ["da"] = GaleraDanishId, ["en-US"] = GaleraEnglishId, ["de-DE"] = GaleraGermanId,
        }), "Test precondition: SDK Languages must be in the Galera layout da=3, en-US=6, de-DE=9");
    }

    private static List<CommonTranslationsModel> FolderNames(string name) =>
    [
        new() { LanguageId = GaleraDanishId, Name = name + " (da)", Description = "" },
        new() { LanguageId = GaleraEnglishId, Name = name + " (en)", Description = "" },
        new() { LanguageId = GaleraGermanId, Name = name + " (de)", Description = "" },
    ];

    /// <summary>
    /// Seeds Area(type) → Property(with planning tag) → AreaRule with da/en/de translations stored
    /// under the tenant's REAL SDK language ids (what the seed remap writes), plus a parent SDK folder.
    /// Returns the area rule re-read with Area and translations, as the planning helpers expect it.
    /// </summary>
    private async Task<AreaRule> SeedAreaRule(Core core, AreaTypesEnum type, string danish, string english,
        string german, AreaRuleT2TypesEnum? ruleType = null)
    {
        var parentFolderId = await core.FolderCreate(FolderNames($"Parent {Guid.NewGuid()}"), null);

        var planningTag = new PlanningTag { Name = $"Property tag {Guid.NewGuid()}", CreatedByUserId = 1, UpdatedByUserId = 1 };
        await planningTag.Create(ItemsPlanningPnDbContext!);

        var area = new Area
        {
            Type = type, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"ShiftedLanguageIdTest-{Guid.NewGuid()}", ItemPlanningTagId = planningTag.Id,
            FolderId = parentFolderId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0, FolderId = parentFolderId, Type = ruleType,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        foreach (var (languageId, name) in new[]
                 {
                     (GaleraDanishId, danish), (GaleraEnglishId, english), (GaleraGermanId, german)
                 })
        {
            await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
            {
                AreaRuleId = areaRule.Id, LanguageId = languageId, Name = name,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        }
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return await BackendConfigurationPnDbContext.AreaRules
            .Include(x => x.Area)
            .Include(x => x.AreaRuleTranslations)
            .Include(x => x.AreaRulesPlannings).ThenInclude(x => x.PlanningSites)
            .SingleAsync(x => x.Id == areaRule.Id);
    }

    private static AreaRulePlanningModel EnabledPlanningModel(AreaRule areaRule) => new()
    {
        RuleId = areaRule.Id,
        PropertyId = areaRule.PropertyId,
        Status = true,
        ComplianceEnabled = true,
        StartDate = DateTime.UtcNow,
        AssignedSites = [],
        TypeSpecificFields = new AreaRuleTypePlanningModel
        {
            DayOfMonth = 1, DayOfWeek = 1, RepeatEvery = 1, RepeatType = 1, HoursAndEnergyEnabled = false
        }
    };

    private async Task<Dictionary<int, string>> PlanningNamesEndingWith(string suffix, string danishPrefix)
    {
        // Locate the planning by its Danish row, then return every language row of that planning.
        var planningId = await ItemsPlanningPnDbContext!.PlanningNameTranslation.AsNoTracking()
            .Where(x => x.Name == danishPrefix + suffix)
            .Select(x => x.PlanningId)
            .SingleOrDefaultAsync();
        Assert.That(planningId, Is.Not.EqualTo(0),
            $"Expected a planning whose Danish name is '{danishPrefix + suffix}'");
        return await ItemsPlanningPnDbContext.PlanningNameTranslation.AsNoTracking()
            .Where(x => x.PlanningId == planningId)
            .ToDictionaryAsync(x => x.LanguageId, x => x.Name);
    }

    [Test]
    public async Task CreatePlanningType6_GaleraLanguageIds_EachLanguageRowUsesItsOwnAreaRuleName()
    {
        await ForceGaleraLanguageIds();
        var core = await GetCore();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var areaRule = await SeedAreaRule(core, AreaTypesEnum.Type6,
            $"Varmepumpe {tag}", $"Heat pump {tag}", $"Wärmepumpe {tag}");

        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType6(areaRule,
            MicrotingDbContext!, EnabledPlanningModel(areaRule), core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, null);

        var names = await PlanningNamesEndingWith(": Logbog", $"Varmepumpe {tag}");
        Assert.That(names, Is.EquivalentTo(new Dictionary<int, string>
        {
            [GaleraDanishId] = $"Varmepumpe {tag}: Logbog",
            [GaleraEnglishId] = $"Heat pump {tag}: Logbook",
            [GaleraGermanId] = $"Wärmepumpe {tag}: Logbook",
        }));
    }

    [Test]
    public async Task CreatePlanningType2_GaleraLanguageIds_EachLanguageRowUsesItsOwnAreaRuleName()
    {
        await ForceGaleraLanguageIds();
        var core = await GetCore();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var areaRule = await SeedAreaRule(core, AreaTypesEnum.Type2,
            $"Beholder {tag}", $"Tank {tag}", $"Behälter {tag}", AreaRuleT2TypesEnum.Open);

        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType2(areaRule,
            MicrotingDbContext!, EnabledPlanningModel(areaRule), core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, null);

        var names = await PlanningNamesEndingWith(": Flydelag", $"Beholder {tag}");
        Assert.That(names, Is.EquivalentTo(new Dictionary<int, string>
        {
            [GaleraDanishId] = $"Beholder {tag}: Flydelag",
            [GaleraEnglishId] = $"Tank {tag}: Floating layer",
            [GaleraGermanId] = $"Behälter {tag}: Schwimmende Ebene",
        }));
        // The report-group tag is named after the Danish area rule name.
        Assert.That(await ItemsPlanningPnDbContext!.PlanningTags.AnyAsync(x => x.Name == $"Beholder {tag}"), Is.True,
            "The area rule's report-group planning tag must carry the Danish name");
    }

    [Test]
    public async Task UpdatePlanning_Type2EnabledFromDisabled_GaleraLanguageIds_WritesRealLanguageIds()
    {
        await ForceGaleraLanguageIds();
        var core = await GetCore();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var areaRule = await SeedAreaRule(core, AreaTypesEnum.Type2,
            $"Beholder {tag}", $"Tank {tag}", $"Behälter {tag}", AreaRuleT2TypesEnum.Open);

        // An existing, disabled planning: enabling it walks the "create item planning" update branch.
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = areaRule.PropertyId, AreaId = areaRule.AreaId,
            FolderId = areaRule.FolderId, ItemPlanningId = 0, Status = false, StartDate = DateTime.UtcNow.Date,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var model = EnabledPlanningModel(areaRule);
        model.Id = arp.Id;
        var result = await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(model, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext!, null);
        Assert.That(result.Success, Is.True, result.Message);

        var names = await PlanningNamesEndingWith(": Flydelag", $"Beholder {tag}");
        // The German row has always reused the ENGLISH area rule name on this path; only the
        // language ids are under test here.
        Assert.That(names, Is.EquivalentTo(new Dictionary<int, string>
        {
            [GaleraDanishId] = $"Beholder {tag}: Flydelag",
            [GaleraEnglishId] = $"Tank {tag}: Floating layer",
            [GaleraGermanId] = $"Tank {tag}: Schwimmende Ebene",
        }));
    }

    [Test]
    public async Task CreatePlanningDefaultType_GaleraLanguageIds_FolderNameIsTheDanishFolderName()
    {
        await ForceGaleraLanguageIds();
        var core = await GetCore();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var areaRule = await SeedAreaRule(core, AreaTypesEnum.Type1, $"Regel {tag}", $"Rule {tag}", $"Regel de {tag}");

        // The property-area folder has a Danish name AND a row on id 1 (Norsk on this tenant),
        // so a literal LanguageId==1 lookup silently returns the Norwegian name.
        var areaFolderId = await core.FolderCreate(
        [
            new() { LanguageId = 1, Name = $"Norsk mappe {tag}", Description = "" },
            new() { LanguageId = GaleraDanishId, Name = $"Dansk mappe {tag}", Description = "" },
            new() { LanguageId = GaleraEnglishId, Name = $"English folder {tag}", Description = "" },
        ], null);
        var areaProperty = new AreaProperty
        {
            AreaId = areaRule.AreaId, PropertyId = areaRule.PropertyId, Checked = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaProperties.AddAsync(areaProperty);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.ProperyAreaFolders.AddAsync(new ProperyAreaFolder
        {
            ProperyAreaAsignmentId = areaProperty.Id, FolderId = areaFolderId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        areaRule.FolderId = 0;

        var model = EnabledPlanningModel(areaRule);
        model.Status = false;
        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningDefaultType(areaRule,
            MicrotingDbContext!, model, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext!, 1, null);

        var stored = await BackendConfigurationPnDbContext.AreaRules.AsNoTracking().SingleAsync(x => x.Id == areaRule.Id);
        Assert.That(stored.FolderId, Is.EqualTo(areaFolderId));
        Assert.That(stored.FolderName, Is.EqualTo($"Dansk mappe {tag}"));
    }

    [Test]
    public async Task CreateAreaRuleType7_GaleraLanguageIds_FindsTheParentFolderByItsEnglishName()
    {
        await ForceGaleraLanguageIds();
        var core = await GetCore();
        var seedRule = Infrastructure.Data.Seed.Data.BackendConfigurationSeedAreas.AreaRulesForType7[0];

        // The Type7 eForm is resolved by name; this fixture's SDK dump does not ship it.
        var checkList = new CheckList { OriginalId = Guid.NewGuid().ToString(), WorkflowState = Constants.WorkflowStates.Created };
        await checkList.Create(MicrotingDbContext!);
        await new CheckListTranslation
        {
            CheckListId = checkList.Id, LanguageId = GaleraDanishId, Text = seedRule.EformName,
        }.Create(MicrotingDbContext!);

        var area = new Area
        {
            Type = AreaTypesEnum.Type7, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        var property = new Property
        {
            Name = $"ShiftedLanguageIdTest-{Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        var areaProperty = new AreaProperty
        {
            AreaId = area.Id, PropertyId = property.Id, Checked = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaProperties.AddAsync(areaProperty);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // The parent folder's ENGLISH name is the seed FolderName; no row sits on id 2 (Français here).
        var parentFolderId = await core.FolderCreate(
        [
            new() { LanguageId = GaleraDanishId, Name = "23.00 Aflæsninger miljøledelse", Description = "" },
            new() { LanguageId = GaleraEnglishId, Name = seedRule.FolderName, Description = "" },
        ], null);
        await BackendConfigurationPnDbContext.ProperyAreaFolders.AddAsync(new ProperyAreaFolder
        {
            ProperyAreaAsignmentId = areaProperty.Id, FolderId = parentFolderId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var danish = await MicrotingDbContext!.Languages.SingleAsync(x => x.LanguageCode == "da");
        var result = await BackendConfigurationAreaRulesServiceHelper.Create(new AreaRulesCreateModel
        {
            PropertyAreaId = areaProperty.Id,
            AreaRules =
            [
                new()
                {
                    TranslatedNames = [new CommonDictionaryModel { Id = danish.Id, Name = seedRule.AreaRuleTranslations[0].Name }],
                    TypeSpecificFields = new TypeSpecificFields()
                }
            ]
        }, core, 1, BackendConfigurationPnDbContext, danish);

        Assert.That(result.Success, Is.True, result.Message);
        var created = await BackendConfigurationPnDbContext.AreaRules.AsNoTracking()
            .SingleAsync(x => x.PropertyId == property.Id);
        Assert.That(created.FolderId, Is.EqualTo(parentFolderId));
    }

    [Test]
    public async Task RemapSeedLanguageIds_CommonTranslations_GaleraLanguageIds_MapsSeedIdsByLanguageCode()
    {
        await ForceGaleraLanguageIds();
        // A literal da/en/de folder name list as built by DocumentHelper, the document folder
        // service and the Type10 "Morgenrundtur" folder: 1=da, 2=en-US, 3=de-DE.
        var translations = new List<CommonTranslationsModel>
        {
            new() { LanguageId = 1, Name = "26. Dokumenter", Description = "" },
            new() { LanguageId = 2, Name = "26. Documents", Description = "" },
            new() { LanguageId = 3, Name = "26. Dokumenten", Description = "" },
        };

        await AreaRuleLanguageHelper.RemapSeedLanguageIdsAsync(translations, MicrotingDbContext!);

        Assert.That(translations.ToDictionary(x => x.Name, x => x.LanguageId), Is.EquivalentTo(new Dictionary<string, int>
        {
            ["26. Dokumenter"] = GaleraDanishId,
            ["26. Documents"] = GaleraEnglishId,
            ["26. Dokumenten"] = GaleraGermanId,
        }));
    }

    private BackendConfigurationAreaRulesService AreaRulesServiceFor(Core core, Language currentUserLanguage)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(currentUserLanguage));
        return new BackendConfigurationAreaRulesService(coreHelper, userService, BackendConfigurationPnDbContext!,
            Substitute.For<IBackendConfigurationLocalizationService>(), ItemsPlanningPnDbContext!);
    }

    [Test]
    public async Task GetAreaRulesForType7_GaleraLanguageIds_DanishUserGetsTheDanishSeedNames()
    {
        await ForceGaleraLanguageIds();
        var core = await GetCore();
        var danish = await MicrotingDbContext!.Languages.AsNoTracking().SingleAsync(x => x.LanguageCode == "da");

        var result = await AreaRulesServiceFor(core, danish).GetAreaRulesForType7();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.SelectMany(x => x.AreaRuleNames), Does.Contain("23.00.01 Aflæsning vand"),
            "A Danish user (SDK id 3 here) must get the seed's Danish names, not its German (seed id 3) ones");
    }

    [Test]
    public async Task GetAreaRulesForType8_GaleraLanguageIds_DanishUserGetsTheDanishSeedNames()
    {
        await ForceGaleraLanguageIds();
        var core = await GetCore();
        var danish = await MicrotingDbContext!.Languages.AsNoTracking().SingleAsync(x => x.LanguageCode == "da");

        var result = await AreaRulesServiceFor(core, danish).GetAreaRulesForType8();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.SelectMany(x => x.AreaRuleNames), Does.Contain("24.00.01 Aflæsning vand"));
        Assert.That(result.Model.SelectMany(x => x.AreaRuleNames), Does.Not.Contain("24.00.01 Wasserlesen"));
    }

    [Test]
    public async Task CalendarCreateTask_LogbooksAreaWithoutTranslations_GaleraLanguageIds_ReseedsUnderRealLanguageIds()
    {
        await ForceGaleraLanguageIds();
        var core = await GetCore();
        const int logbooksAreaId = 31;
        // The recovery branch: the Logbøger Area row exists but its translations are gone.
        var lostTranslations = await BackendConfigurationPnDbContext!.AreaTranslations
            .Where(x => x.AreaId == logbooksAreaId).ToListAsync();
        BackendConfigurationPnDbContext.AreaTranslations.RemoveRange(lostTranslations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        Assert.That(await BackendConfigurationPnDbContext.Areas.AnyAsync(x => x.Id == logbooksAreaId), Is.True,
            "Test precondition: the seeded Logbøger area row must exist");

        var propertyFolderId = await core.FolderCreate(FolderNames($"Property {Guid.NewGuid()}"), null);
        var property = new Property
        {
            Name = $"ShiftedLanguageIdTest-{Guid.NewGuid()}", FolderId = propertyFolderId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var calendarService = new BackendConfigurationCalendarService(
            Substitute.For<IBackendConfigurationLocalizationService>(), userService, BackendConfigurationPnDbContext,
            coreHelper, Substitute.For<IEventDeployService>(), ItemsPlanningPnDbContext!,
            Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(), Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(), Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext));

        // Only the folder resolution is under test; what the (stubbed) task wizard does after it is not.
        await calendarService.CreateTask(new CalendarTaskCreateRequestModel
        {
            PropertyId = property.Id, FolderId = null, EformId = 1, Sites = [1],
            StartDate = DateTime.UtcNow.Date.AddDays(7), RepeatType = 1, RepeatEvery = 1, Status = 2,
            StartHour = 9.0, Duration = 1.0,
            Translates = [new CommonTranslationsModel { LanguageId = GaleraDanishId, Name = "Logbog" }],
        });

        var reseeded = await BackendConfigurationPnDbContext.AreaTranslations.AsNoTracking()
            .Where(x => x.AreaId == logbooksAreaId)
            .ToDictionaryAsync(x => x.Name, x => x.LanguageId);
        Assert.That(reseeded, Is.EquivalentTo(new Dictionary<string, int>
        {
            ["00. Logbøger"] = GaleraDanishId, ["01. Log books"] = GaleraEnglishId, ["01. Logbücher"] = GaleraGermanId,
        }));

        var folderId = await BackendConfigurationPnDbContext.ProperyAreaFolders.AsNoTracking()
            .Where(x => x.AreaProperty.PropertyId == property.Id)
            .Select(x => x.FolderId).SingleAsync();
        var folderNames = await MicrotingDbContext!.FolderTranslations.AsNoTracking()
            .Where(x => x.FolderId == folderId)
            .ToDictionaryAsync(x => x.Name, x => x.LanguageId);
        Assert.That(folderNames, Is.EquivalentTo(new Dictionary<string, int>
        {
            ["00. Logbøger"] = GaleraDanishId, ["01. Log books"] = GaleraEnglishId, ["01. Logbücher"] = GaleraGermanId,
        }));
    }
}
