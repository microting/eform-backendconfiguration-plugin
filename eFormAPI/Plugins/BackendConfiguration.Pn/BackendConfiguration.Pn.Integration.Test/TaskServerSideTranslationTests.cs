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

using BackendConfiguration.Pn.Infrastructure.Enums;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.TaskTranslation;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Abstractions.Translation;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;

/// <summary>
/// #1384 — the task's title and description are translated server-side, in the task
/// wizard's CreateTask/UpdateTask and in the reconcile that expands teams, so every
/// entry point fills an English assignee's language, not only the calendar dialog.
///
/// <para>
/// The REAL wizard, calendar, task list, reconcile engine and resolver run against the
/// fixture's SDK core. The host's <see cref="ITranslationService"/> is a fake that
/// prefixes the target code, the same shape as the CI translator (FAKE_TRANSLATE_E2E).
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskServerSideTranslationTests : TestBaseSetup
{
    private const string CommentTemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9061</Id>
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
            <Id>9061</Id>
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
                    <Id>73661</Id>
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

    private const string DanishTitle = "Tjek ventilation";
    private const string DanishDescription = "Kontrollér filtre og noter trykket.";

    public enum TranslatorBehaviour { Translates, ReturnsFailure, Throws }

    /// <summary>
    /// Records every call. By default answers "[target] source", like the CI translator;
    /// can also stand for a configured-but-failing or an unconfigured host translator.
    /// </summary>
    private sealed class FakeTranslator(
        TranslatorBehaviour behaviour = TranslatorBehaviour.Translates, bool configured = true,
        string? failOnlyText = null) : ITranslationService
    {
        public List<(string Text, string Target)> Calls { get; } = [];

        public bool IsConfigured => configured;

        public Task<OperationDataResult<string>> TranslateText(string sourceText, string sourceLanguageCode,
            string targetLanguageCode)
        {
            Calls.Add((sourceText, targetLanguageCode));
            if (failOnlyText != null)
            {
                return Task.FromResult(sourceText == failOnlyText
                    ? new OperationDataResult<string>(false, "Translate failed (400)")
                    : new OperationDataResult<string>(true, "", $"[{targetLanguageCode}] {sourceText}"));
            }

            return behaviour switch
            {
                TranslatorBehaviour.Throws => throw new HttpRequestException("translator is down"),
                TranslatorBehaviour.ReturnsFailure => Task.FromResult(
                    new OperationDataResult<string>(false, "Translate failed (403)")),
                _ => Task.FromResult(
                    new OperationDataResult<string>(true, "", $"[{targetLanguageCode}] {sourceText}"))
            };
        }
    }

    private const string Notice = "TaskSavedWithoutTranslation";

    private sealed record TaskServices(
        BackendConfigurationTaskWizardService Wizard,
        BackendConfigurationCalendarService Calendar,
        BackendConfigurationTaskListService TaskList,
        CalendarAssignmentReconciliationService Reconciliation,
        TaskTranslationFiller Filler,
        IEventDeployService EventDeploy);

    private sealed record Scenario(
        int PropertyId, int EformId, int FolderId, int DanishId, int EnglishId,
        int DanishWorker, int EnglishWorker, int TeamWithEnglishMember);

    private Core _core = null!;
    private FakeTranslator _translator = null!;

    [SetUp]
    public async Task SetupCore()
    {
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");
        // Core FIRST: SDK rows must not be inserted before Core.StartSqlOnly has migrated the schema.
        _core = await GetCore();
        _translator = new FakeTranslator();
    }

    /// <summary>
    /// Builds the services as the plugin's DI would. <paramref name="translator"/> null is
    /// a host that registers no translation service.
    /// </summary>
    private async Task<TaskServices> BuildServices(ITranslationService? translator)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(_core));

        var hostServices = new ServiceCollection();
        if (translator != null)
        {
            hostServices.AddSingleton(translator);
        }

        var membership = new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext!);
        var resolver = new CalendarAssignmentResolver(BackendConfigurationPnDbContext!, membership);
        var filler = new TaskTranslationFiller(coreHelper, BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!, hostServices.BuildServiceProvider(),
            TestContextLogger<TaskTranslationFiller>.Instance);

        var eventDeployService = Substitute.For<IEventDeployService>();
        var retraction = Substitute.For<ICalendarOccurrenceRetractionService>();
        var wizard = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            ItemsPlanningPnDbContext!,
            eventDeployService,
            retraction,
            TestContextLogger<BackendConfigurationTaskWizardService>.Instance,
            filler);

        var reconciliation = new CalendarAssignmentReconciliationService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            eventDeployService, resolver,
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<CalendarAssignmentReconciliationService>.Instance,
            filler);

        var calendar = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            eventDeployService,
            ItemsPlanningPnDbContext!,
            wizard,
            reconciliation,
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            retraction,
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            membership);

        var taskList = new BackendConfigurationTaskListService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            calendar,
            wizard,
            retraction,
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            TestContextLogger<BackendConfigurationTaskListService>.Instance);

        return new TaskServices(wizard, calendar, taskList, reconciliation, filler, eventDeployService);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<int> EnsureLanguage(string name, string code)
    {
        var language = await MicrotingDbContext!.Languages.FirstOrDefaultAsync(x => x.LanguageCode == code);
        if (language == null)
        {
            language = new Language { Name = name, LanguageCode = code, IsActive = true };
            await MicrotingDbContext.Languages.AddAsync(language);
        }
        else
        {
            // Targets are the ACTIVE languages, as in the calendar dialog.
            language.IsActive = true;
        }

        await MicrotingDbContext.SaveChangesAsync();
        return language.Id;
    }

    private async Task<Scenario> SeedScenario()
    {
        var danishId = await EnsureLanguage("Dansk", "da");
        var englishId = await EnsureLanguage("English", "en-US");

        var eformId = await _core.TemplateCreate(await _core.TemplateFromXml(CommentTemplateXml));

        var folder = new Folder
        {
            Name = $"translation-folder-{Guid.NewGuid()}", MicrotingUid = Random.Shared.Next(900_000, 999_999),
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Folders.AddAsync(folder);
        await MicrotingDbContext.SaveChangesAsync();

        // The wizard tags the Planning with the property's items-planning tag (real FK).
        var propertyPlanningTag = new PlanningTag
        {
            Name = $"Property A tag {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(propertyPlanningTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"Property A {Guid.NewGuid()}", ItemPlanningTagId = propertyPlanningTag.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var team = new Tag { Name = $"Team A {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext.Tags.AddAsync(team);
        await MicrotingDbContext.SaveChangesAsync();

        var danishWorker = await SeedWorker("Worker A", danishId, property.Id, teamId: null);
        var englishWorker = await SeedWorker("Worker B", englishId, property.Id, teamId: null);
        await SeedWorker("Worker C", englishId, property.Id, team.Id);

        return new Scenario(property.Id, eformId, folder.Id, danishId, englishId,
            danishWorker, englishWorker, team.Id);
    }

    private async Task<int> SeedWorker(string name, int languageId, int propertyId, int? teamId)
    {
        var site = new Site
        {
            Name = $"{name} {Guid.NewGuid()}", MicrotingUid = null, LanguageId = languageId,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        if (teamId.HasValue)
        {
            await MicrotingDbContext.SiteTags.AddAsync(new SiteTag
            {
                TagId = teamId.Value, SiteId = site.Id, WorkflowState = Constants.WorkflowStates.Created
            });
            await MicrotingDbContext.SaveChangesAsync();
        }

        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = propertyId, WorkerId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>A weekly series starting on a future Monday, so nothing is in the past.</summary>
    private static DateTime SeriesStart()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return DateTime.SpecifyKind(today.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday), DateTimeKind.Utc);
    }

    private static CalendarTaskCreateRequestModel BuildCreate(
        Scenario s, List<int> sites, List<int> teams, List<CommonTranslationsModel> translates) =>
        new()
        {
            PropertyId = s.PropertyId,
            FolderId = s.FolderId,
            EformId = s.EformId,
            StartDate = SeriesStart(),
            RepeatType = (int)RepeatType.Week,
            RepeatEvery = 1,
            Status = (int)TaskWizardStatuses.Active,
            Sites = sites,
            WorkerTagIds = teams,
            StartHour = 9.0,
            Duration = 1.0,
            Translates = translates
        };

    private static List<CommonTranslationsModel> DanishOnly(Scenario s) =>
        [new CommonTranslationsModel { LanguageId = s.DanishId, Name = DanishTitle, Description = DanishDescription }];

    private static async Task<int> CreateViaCalendar(TaskServices services, CalendarTaskCreateRequestModel model)
    {
        var result = await services.Calendar.CreateTask(model);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.GreaterThan(0), "the calendar must correlate the created task");
        return result.Model;
    }

    private async Task<(AreaRuleTranslation? Rule, PlanningNameTranslation? Name)> TranslationOf(int arpId, int languageId)
    {
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == arpId);
        var rule = await BackendConfigurationPnDbContext.AreaRuleTranslations.AsNoTracking()
            .Where(x => x.AreaRuleId == arp.AreaRuleId && x.LanguageId == languageId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleOrDefaultAsync();
        var name = await ItemsPlanningPnDbContext!.PlanningNameTranslation.AsNoTracking()
            .Where(x => x.PlanningId == arp.ItemPlanningId && x.LanguageId == languageId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleOrDefaultAsync();
        return (rule, name);
    }

    private async Task AssertEnglishFilled(Scenario s, int arpId)
    {
        var (rule, name) = await TranslationOf(arpId, s.EnglishId);
        Assert.Multiple(() =>
        {
            Assert.That(rule?.Name, Is.EqualTo($"[en-US] {DanishTitle}"), "AreaRuleTranslation.Name (en)");
            Assert.That(rule?.Description, Is.EqualTo($"[en-US] {DanishDescription}"),
                "AreaRuleTranslation.Description (en)");
            Assert.That(name?.Name, Is.EqualTo($"[en-US] {DanishTitle}"),
                "PlanningNameTranslation (en) — the device label reads it");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CalendarCreate_ForAnEnglishWorker_StoresTheEnglishTitleAndDescription()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);

        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [s.EnglishWorker], teams: [], DanishOnly(s)));

        await AssertEnglishFilled(s, arpId);
        Assert.That(_translator.Calls.Select(c => c.Target), Has.None.EqualTo("da"),
            "Danish is the source, never a target");
    }

    /// <summary>
    /// A team-only task: the wizard does not know the team, its links are written after
    /// it. The reconcile that expands the team fills the English member's language.
    /// </summary>
    [Test]
    public async Task CalendarCreate_ForATeamWithAnEnglishMember_StoresTheEnglishTranslation()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);

        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [], teams: [s.TeamWithEnglishMember], DanishOnly(s)));

        await AssertEnglishFilled(s, arpId);
    }

    /// <summary>A task-list batch action that adds an English worker fills English.</summary>
    [Test]
    public async Task BatchAddWorker_AddingAnEnglishWorker_StoresTheEnglishTranslation()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);
        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [s.DanishWorker], teams: [], DanishOnly(s)));
        Assert.That((await TranslationOf(arpId, s.EnglishId)).Rule, Is.Null,
            "a Danish-only assignee needs no English");
        Assert.That(_translator.Calls, Is.Empty, "nothing to translate for a Danish worker");

        var result = await services.TaskList.AddWorker(new TaskListBatchAssignModel
        {
            TaskIds = [arpId], SiteId = s.EnglishWorker
        });

        Assert.That(result.Success, Is.True, result.Message);
        await AssertEnglishFilled(s, arpId);
    }

    /// <summary>A title typed by hand is kept; only the empty description is translated.</summary>
    [Test]
    public async Task CalendarCreate_KeepsAManualEnglishTitle_AndFillsOnlyTheEmptyDescription()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);
        var translates = DanishOnly(s);
        translates.Add(new CommonTranslationsModel { LanguageId = s.EnglishId, Name = "Check the vents", Description = "" });

        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [s.EnglishWorker], teams: [], translates));

        var (rule, name) = await TranslationOf(arpId, s.EnglishId);
        Assert.Multiple(() =>
        {
            Assert.That(rule?.Name, Is.EqualTo("Check the vents"), "a manual title is never overwritten");
            Assert.That(name?.Name, Is.EqualTo("Check the vents"));
            Assert.That(rule?.Description, Is.EqualTo($"[en-US] {DanishDescription}"));
            Assert.That(_translator.Calls.Select(c => c.Text), Has.None.EqualTo(DanishTitle),
                "the title was not even sent for translation");
        });
    }

    /// <summary>
    /// A stored English text is not overwritten by an update that does not carry it, and
    /// an update whose Danish text changed does not re-translate it.
    /// </summary>
    [Test]
    public async Task WizardUpdate_WithoutEnglishInTheRequest_KeepsTheStoredEnglish()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);
        var translates = DanishOnly(s);
        translates.Add(new CommonTranslationsModel
        {
            LanguageId = s.EnglishId, Name = "Check the vents", Description = "Check filters by hand."
        });
        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [s.EnglishWorker], teams: [], translates));
        var result = await services.Wizard.UpdateTask(new TaskWizardCreateModel
        {
            Id = arpId,
            PropertyId = s.PropertyId,
            FolderId = s.FolderId,
            EformId = s.EformId,
            StartDate = SeriesStart(),
            RepeatType = RepeatType.Week,
            RepeatEvery = 1,
            Status = TaskWizardStatuses.Active,
            Sites = [s.EnglishWorker],
            Translates =
            [
                new CommonTranslationsModel
                {
                    LanguageId = s.DanishId, Name = "Tjek ventilation igen", Description = DanishDescription
                }
            ]
        });

        Assert.That(result.Success, Is.True, result.Message);
        var (rule, _) = await TranslationOf(arpId, s.EnglishId);
        Assert.Multiple(() =>
        {
            Assert.That(rule?.Name, Is.EqualTo("Check the vents"));
            Assert.That(rule?.Description, Is.EqualTo("Check filters by hand."));
            Assert.That(_translator.Calls, Is.Empty, "nothing was empty, so nothing was translated");
        });
    }

    /// <summary>
    /// A host without a translation service: the task is saved in Danish only, the save
    /// succeeds, and the wizard's result carries the notice.
    /// </summary>
    [Test]
    public async Task WizardCreate_WithoutATranslationService_SavesDanishOnly_WithANotice()
    {
        var s = await SeedScenario();
        var services = await BuildServices(translator: null);

        var result = await services.Wizard.CreateTask(new TaskWizardCreateModel
        {
            PropertyId = s.PropertyId,
            FolderId = s.FolderId,
            EformId = s.EformId,
            StartDate = SeriesStart(),
            RepeatType = RepeatType.Week,
            RepeatEvery = 1,
            Status = TaskWizardStatuses.Active,
            Sites = [s.EnglishWorker],
            Translates = DanishOnly(s)
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Message, Does.Contain("TaskSavedWithoutTranslation"));

        var arpId = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .Where(x => x.PropertyId == s.PropertyId)
            .OrderByDescending(x => x.Id)
            .Select(x => x.Id)
            .FirstAsync();
        var (englishRule, englishName) = await TranslationOf(arpId, s.EnglishId);
        var (danishRule, _) = await TranslationOf(arpId, s.DanishId);
        Assert.Multiple(() =>
        {
            Assert.That(englishRule, Is.Null);
            Assert.That(englishName, Is.Null);
            Assert.That(danishRule?.Name, Is.EqualTo(DanishTitle));
        });
    }

    /// <summary>The calendar path without a translator also saves (Danish only) without failing.</summary>
    [Test]
    public async Task CalendarCreate_WithoutATranslationService_SavesDanishOnly()
    {
        var s = await SeedScenario();
        var services = await BuildServices(translator: null);

        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [s.EnglishWorker], teams: [s.TeamWithEnglishMember], DanishOnly(s)));

        Assert.That((await TranslationOf(arpId, s.EnglishId)).Rule, Is.Null);
        Assert.That((await TranslationOf(arpId, s.DanishId)).Rule?.Name, Is.EqualTo(DanishTitle));
    }

    /// <summary>A Danish worker needs nothing: the translator is never called.</summary>
    [Test]
    public async Task CalendarCreate_ForADanishWorker_TranslatesNothing()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);

        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [s.DanishWorker], teams: [], DanishOnly(s)));

        Assert.That(_translator.Calls, Is.Empty);
        Assert.That((await TranslationOf(arpId, s.EnglishId)).Rule, Is.Null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A translator that is there but does not deliver
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A configured translator that answers unsuccessfully or throws: the save still
    /// succeeds in Danish and the calendar result carries the notice. A failure is asked for
    /// once per (text, target), so the reconcile after the wizard does not repeat it (this
    /// fixture shares one filler between the two). One unsuccessful answer still lets the
    /// description be tried, the second in a row stops the filler; a throw stops it at once.
    /// </summary>
    [TestCase(TranslatorBehaviour.ReturnsFailure, 2)]
    [TestCase(TranslatorBehaviour.Throws, 1)]
    public async Task CalendarCreate_WithAFailingTranslator_SavesDanishOnly_WithTheNotice(
        TranslatorBehaviour behaviour, int expectedCalls)
    {
        var s = await SeedScenario();
        var translator = new FakeTranslator(behaviour);
        var services = await BuildServices(translator);

        var result = await services.Calendar.CreateTask(
            BuildCreate(s, sites: [s.EnglishWorker], teams: [], DanishOnly(s)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Message, Does.Contain(Notice), "the calendar passes the wizard's notice on");
        Assert.That((await TranslationOf(result.Model, s.EnglishId)).Rule, Is.Null);
        Assert.That((await TranslationOf(result.Model, s.DanishId)).Rule?.Name, Is.EqualTo(DanishTitle));
        Assert.That(translator.Calls, Has.Count.EqualTo(expectedCalls));
    }

    /// <summary>A registered translator without an API key is treated as absent.</summary>
    [Test]
    public async Task CalendarCreate_WithAnUnconfiguredTranslator_SavesDanishOnly_WithoutCallingIt()
    {
        var s = await SeedScenario();
        var translator = new FakeTranslator(configured: false);
        var services = await BuildServices(translator);

        var result = await services.Calendar.CreateTask(
            BuildCreate(s, sites: [s.EnglishWorker], teams: [], DanishOnly(s)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Message, Does.Contain(Notice));
        Assert.That(translator.Calls, Is.Empty);
        Assert.That((await TranslationOf(result.Model, s.EnglishId)).Rule, Is.Null);
    }

    /// <summary>The task-list batch summary keeps a task's Danish-only notice.</summary>
    [Test]
    public async Task BatchAddWorker_WithoutATranslationService_ReportsTheNotice()
    {
        var s = await SeedScenario();
        var services = await BuildServices(translator: null);
        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [s.DanishWorker], teams: [], DanishOnly(s)));

        var result = await services.TaskList.AddWorker(new TaskListBatchAssignModel
        {
            TaskIds = [arpId], SiteId = s.EnglishWorker
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Message, Does.Contain(Notice));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Team membership and language changes
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An English worker who joins a task's team later: the device-user tag change runs
    /// ReconcileEventsForWorkerTagsAsync, which fills English.
    /// </summary>
    [Test]
    public async Task TagChangeReconcile_AnEnglishWorkerJoiningTheTeam_StoresTheEnglishTranslation()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);
        var teamB = new Tag { Name = $"Team B {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext!.Tags.AddAsync(teamB);
        await MicrotingDbContext.SaveChangesAsync();
        await MicrotingDbContext.SiteTags.AddAsync(new SiteTag
        {
            TagId = teamB.Id, SiteId = s.DanishWorker, WorkflowState = Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();

        var arpId = await CreateViaCalendar(services,
            BuildCreate(s, sites: [], teams: [teamB.Id], DanishOnly(s)));
        Assert.That((await TranslationOf(arpId, s.EnglishId)).Rule, Is.Null, "the team is Danish-only so far");

        await MicrotingDbContext.SiteTags.AddAsync(new SiteTag
        {
            TagId = teamB.Id, SiteId = s.EnglishWorker, WorkflowState = Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();
        await services.Reconciliation.ReconcileEventsForWorkerTagsAsync([teamB.Id]);

        await AssertEnglishFilled(s, arpId);
    }

    /// <summary>
    /// The cap binds: with an English and a German worker and a translator that always answers
    /// unsuccessfully, the filler stops after two failures in a row (the English title and
    /// description). Without the cap it would make four calls.
    /// </summary>
    [Test]
    public async Task CalendarCreate_TwoLanguagesAndAFailingTranslator_StopsAfterTwoFailuresInARow()
    {
        var s = await SeedScenario();
        var germanId = await EnsureLanguage("Deutsch", "de-DE");
        var germanWorker = await SeedWorker("Worker D", germanId, s.PropertyId, teamId: null);
        var translator = new FakeTranslator(TranslatorBehaviour.ReturnsFailure);
        var services = await BuildServices(translator);

        var result = await services.Calendar.CreateTask(
            BuildCreate(s, sites: [s.EnglishWorker, germanWorker], teams: [], DanishOnly(s)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Message, Does.Contain(Notice));
        Assert.That(translator.Calls, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// A success resets the count: when only the title fails, every language's description
    /// is still translated. Without the reset the second failed title would stop the filler
    /// before the second description.
    /// </summary>
    [Test]
    public async Task CalendarCreate_ASuccessBetweenFailures_KeepsTranslatingTheOtherTexts()
    {
        var s = await SeedScenario();
        var germanId = await EnsureLanguage("Deutsch", "de-DE");
        var germanWorker = await SeedWorker("Worker D", germanId, s.PropertyId, teamId: null);
        var translator = new FakeTranslator(failOnlyText: DanishTitle);
        var services = await BuildServices(translator);

        var result = await services.Calendar.CreateTask(
            BuildCreate(s, sites: [s.EnglishWorker, germanWorker], teams: [], DanishOnly(s)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Message, Does.Contain(Notice), "the titles stayed Danish");
        Assert.That(translator.Calls, Has.Count.EqualTo(4));
        var (english, _) = await TranslationOf(result.Model, s.EnglishId);
        var (german, _) = await TranslationOf(result.Model, germanId);
        Assert.Multiple(() =>
        {
            Assert.That(english?.Description, Is.EqualTo($"[en-US] {DanishDescription}"));
            Assert.That(german?.Description, Is.EqualTo($"[de-DE] {DanishDescription}"));
        });
    }

    /// <summary>
    /// Reactivating an inactive task through the wizard (inactive -> active branch) for an
    /// English worker: the filled English title also gets its PlanningNameTranslation, which
    /// the device label reads - that branch used to update existing name rows only.
    /// </summary>
    [Test]
    public async Task WizardUpdate_ReactivatingForAnEnglishWorker_CreatesTheEnglishPlanningName()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);
        // A future start: PairItemWithSiteHelper.Pair then takes its "series has not started
        // yet" short-circuit instead of deploying to the fixture's sites.
        var start = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(30), DateTimeKind.Utc);
        TaskWizardCreateModel Model(int id, int site, TaskWizardStatuses status) => new()
        {
            Id = id,
            PropertyId = s.PropertyId,
            FolderId = s.FolderId,
            EformId = s.EformId,
            StartDate = start,
            RepeatType = RepeatType.Week,
            RepeatEvery = 1,
            Status = status,
            Sites = [site],
            Translates = DanishOnly(s)
        };

        var created = await services.Wizard.CreateTask(Model(0, s.DanishWorker, TaskWizardStatuses.NotActive));
        Assert.That(created.Success, Is.True, created.Message);
        var arpId = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .Where(x => x.PropertyId == s.PropertyId)
            .OrderByDescending(x => x.Id)
            .Select(x => x.Id)
            .FirstAsync();

        var updated = await services.Wizard.UpdateTask(Model(arpId, s.EnglishWorker, TaskWizardStatuses.Active));

        Assert.That(updated.Success, Is.True, updated.Message);
        await AssertEnglishFilled(s, arpId);
    }

    private async Task<(Site Site, string Email, string LastName, IUserService UserService,
        Microsoft.AspNetCore.Identity.UserManager<Microting.eFormApi.BasePn.Infrastructure.Database.Entities.EformUser> UserManager)>
        CreateDanishDeviceUser()
    {
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);
        var email = $"{Guid.NewGuid()}@example.com";
        // Unique per test: the SDK refuses a second worker with the same name in this fixture's database.
        var lastName = $"Doe {Guid.NewGuid():N}";
        var created = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(new DeviceUserModel
        {
            LanguageCode = "da",
            UserFirstName = "Jane",
            UserLastName = lastName,
            WorkerEmail = email
        }, _core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(created.Success, Is.True, created.Message);
        var site = await MicrotingDbContext!.Sites.AsNoTracking().OrderByDescending(x => x.Id).FirstAsync();
        return (site, email, lastName, userService, userManager);
    }

    /// <summary>
    /// A worker whose language changes from Danish to English: UpdateDeviceUser fills English
    /// on that worker's events only - after the SDK site has the new language - and deploys
    /// nothing: no reconcile runs for a language change.
    /// </summary>
    [Test]
    public async Task UpdateDeviceUser_ChangingTheLanguageToEnglish_FillsOnlyThatWorkersEvents_WithoutDeploying()
    {
        var s = await SeedScenario();
        var services = await BuildServices(_translator);
        var (site, email, lastName, userService, userManager) = await CreateDanishDeviceUser();

        var janesEvent = await CreateViaCalendar(services,
            BuildCreate(s, sites: [site.Id], teams: [], DanishOnly(s)));
        var otherEvent = await CreateViaCalendar(services,
            BuildCreate(s, sites: [s.DanishWorker], teams: [], DanishOnly(s)));
        Assert.That((await TranslationOf(janesEvent, s.EnglishId)).Rule, Is.Null, "a Danish worker needs no English");
        services.EventDeploy.ClearReceivedCalls();
        var reconciliation = Substitute.For<ICalendarAssignmentReconciliationService>();

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(new DeviceUserModel
            {
                SiteMicrotingUid = (int)site.MicrotingUid!,
                LanguageCode = "en-US",
                UserFirstName = "Jane",
                UserLastName = lastName,
                WorkerEmail = email
            }, _core, 1, userService, userManager, BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, Substitute.For<ILogger>(), ItemsPlanningPnDbContext!,
            reconciliation, services.Filler);

        Assert.That(result.Success, Is.True, result.Message);
        await AssertEnglishFilled(s, janesEvent);
        Assert.That((await TranslationOf(otherEvent, s.EnglishId)).Rule, Is.Null,
            "another worker's event is not touched");
        Assert.That(services.EventDeploy.ReceivedCalls(), Is.Empty, "a language change deploys nothing");
        await reconciliation.DidNotReceive().ReconcileEventAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await reconciliation.DidNotReceive()
            .ReconcileEventsForWorkerTagsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The calendar follow-up is the last, non-critical step of a device-user save: a reconcile
    /// that throws is logged, and the save still succeeds with its tag change in place.
    /// </summary>
    [Test]
    public async Task UpdateDeviceUser_WhenTheReconcileThrows_StillSucceeds()
    {
        await SeedScenario();
        var (site, email, lastName, userService, userManager) = await CreateDanishDeviceUser();
        var team = new Tag { Name = $"Team C {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext!.Tags.AddAsync(team);
        await MicrotingDbContext.SaveChangesAsync();
        var reconciliation = Substitute.For<ICalendarAssignmentReconciliationService>();
        reconciliation.ReconcileEventsForWorkerTagsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("reconcile failed"));

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(new DeviceUserModel
            {
                SiteMicrotingUid = (int)site.MicrotingUid!,
                LanguageCode = "da",
                UserFirstName = "Jane",
                UserLastName = lastName,
                WorkerEmail = email,
                Tags = [team.Id]
            }, _core, 1, userService, userManager, BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, Substitute.For<ILogger>(), ItemsPlanningPnDbContext!,
            reconciliation);

        Assert.That(result.Success, Is.True, result.Message);
        await reconciliation.Received(1)
            .ReconcileEventsForWorkerTagsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>());
        Assert.That(await MicrotingDbContext.SiteTags.AsNoTracking()
            .AnyAsync(x => x.SiteId == site.Id && x.TagId == team.Id
                           && x.WorkflowState != Constants.WorkflowStates.Removed), Is.True);
    }
}
