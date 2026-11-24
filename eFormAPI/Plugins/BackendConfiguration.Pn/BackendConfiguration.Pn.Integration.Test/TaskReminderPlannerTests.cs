#nullable enable

namespace BackendConfiguration.Pn.Integration.Test;

using System.Globalization;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.TaskReminders;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using BcProperty = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Property;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// The task reminder planner (package 12) on the real database, the real calendar and the
/// base package's outbox: who gets a reminder for which of today's tasks, at what time, in which
/// language, and how a change (finish/remove, move, reassign, rename) cancels, withdraws, revives
/// or re-renders it. Plus the minute sweep, bounded passes, the capture gate with no hub audience,
/// and host safety.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskReminderPlannerTests : TestBaseSetup
{
    private sealed record World(Core Core, Language Language, int SiteA, int SiteB, int AreaId, int PropertyId,
        string PropertyName, List<int> Extra)
    {
        public IEnumerable<int> AssignedSites => new[] { SiteA, SiteB }.Concat(Extra);
    }

    private sealed record Series(int ArpId, int PlanningId, int AreaRuleId, string Title);

    private FakeTimeProvider _clock = null!;
    private TaskReminderSignals _signals = null!;
    private TaskReminderOptions _options = null!;

    /// <summary>Tomorrow (local), in winter time: 09:00 local is 08:00 UTC.</summary>
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));

    private static string DayKey => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TimeZoneInfo Zone => PropertyTimeZones.Resolve(null);

    private static DateTime Utc(double localHour) => TaskReminderTime.ToUtc(Day, localHour, Zone);

    [SetUp]
    public void SetUpPlanner()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(Utc(6), TimeSpan.Zero)); // 06:00 local
        _signals = new TaskReminderSignals();
        _options = new TaskReminderOptions();
    }

    // ------------------------------------------------------------------ seeding

    private async Task<World> SeedWorldAsync(string languageCode = "da", bool siteAWithoutLanguage = false,
        int extraOptedInWorkers = 0)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstOrDefaultAsync(l => l.LanguageCode == languageCode)
                       ?? await AddLanguageAsync(languageCode);

        async Task<int> SiteAsync(string name, bool withoutLanguage = false)
        {
            var site = new Site
            {
                Name = $"{name}-{suffix}", MicrotingUid = Random.Shared.Next(500000, 999999),
                LanguageId = withoutLanguage ? 0 : language.Id, WorkflowState = Constants.WorkflowStates.Created
            };
            await MicrotingDbContext.Sites.AddAsync(site);
            await MicrotingDbContext.SaveChangesAsync();
            return site.Id;
        }

        var siteA = await SiteAsync("a", siteAWithoutLanguage);
        var siteB = await SiteAsync("b");
        var extra = new List<int>();
        for (var i = 0; i < extraOptedInWorkers; i++) extra.Add(await SiteAsync("x" + i));
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        var property = new BcProperty
        {
            Name = $"Farm {suffix}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        foreach (var site in new[] { siteA, siteB }.Concat(extra))
        {
            await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
            {
                PropertyId = property.Id, WorkerId = site,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        }

        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await TokenAsync(siteA, optedIn: true);
        await TokenAsync(siteB, optedIn: false);
        foreach (var site in extra) await TokenAsync(site, optedIn: true);
        return new World(core, language, siteA, siteB, area.Id, property.Id, property.Name, extra);
    }

    private async Task<Language> AddLanguageAsync(string code)
    {
        var language = new Language { Name = code, LanguageCode = code, IsActive = true };
        await MicrotingDbContext!.Languages.AddAsync(language);
        await MicrotingDbContext.SaveChangesAsync();
        return language;
    }

    private async Task TokenAsync(int site, bool optedIn)
    {
        await new DeviceToken
        {
            AppId = "eform", InstallationId = Guid.NewGuid().ToString(), FcmToken = "fcm-" + Guid.NewGuid(),
            SdkSiteId = site, Platform = "android", TaskReminders = optedIn
        }.Create(BackendConfigurationPnDbContext!);
    }

    /// <summary>A daily series assigned to both sites, starting at <paramref name="startHour"/> local.</summary>
    private async Task<Series> SeedDailySeriesAsync(World w, double startHour, string title)
    {
        var bc = BackendConfigurationPnDbContext!;
        var start = DateTime.SpecifyKind(Day.ToDateTime(TimeOnly.MinValue).AddDays(-3), DateTimeKind.Utc);
        var areaRule = new AreaRule
        {
            AreaId = w.AreaId, PropertyId = w.PropertyId, EformId = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await bc.AreaRules.AddAsync(areaRule);
        await bc.SaveChangesAsync();
        await bc.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = w.Language.Id, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Day, StartDate = start,
            DayOfWeek = start.DayOfWeek, RelatedEFormId = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = w.PropertyId, AreaId = w.AreaId, ItemPlanningId = planning.Id,
            StartDate = start, Status = true, RepeatType = 1, RepeatEvery = 1, DayOfWeek = (int)start.DayOfWeek,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await bc.AreaRulePlannings.AddAsync(arp);
        await bc.SaveChangesAsync();
        foreach (var site in w.AssignedSites)
        {
            await ItemsPlanningPnDbContext.PlanningSites.AddAsync(new IpPlanningSite
            {
                PlanningId = planning.Id, SiteId = site, WorkflowState = Constants.WorkflowStates.Created
            });
            await bc.PlanningSites.AddAsync(new BcPlanningSite
            {
                AreaRulePlanningsId = arp.Id, SiteId = site,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        }

        await ItemsPlanningPnDbContext.SaveChangesAsync();
        await bc.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = startHour, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await bc.SaveChangesAsync();
        return new Series(arp.Id, planning.Id, areaRule.Id, title);
    }

    // ------------------------------------------------------------------ the planner under test

    private static BackendConfigurationCalendarService NewCalendar(World w, BackendConfigurationPnDbContext bc,
        ItemsPlanningPnDbContext ip)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(w.Core));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        // As in production outside an HTTP request: there is no current user.
        userService.GetCurrentUserLanguage().Returns<Task<Language>>(_ => throw new UnauthorizedAccessException("User not authorized!"));
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService, bc, coreHelper, Substitute.For<IEventDeployService>(),
            ip, Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(), Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(), Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, bc));
    }

    private LiveChangeTestHost NewHost(World w, bool connected = true, int? throwForProperty = null) =>
        new(ConnectionStringFor("420_eform-backend-configuration-plugin"),
            ConnectionStringFor("420_eform-angular-items-planning-plugin"), w.Core,
            s =>
            {
                s.AddScoped<IBackendConfigurationCalendarService>(sp =>
                {
                    var real = NewCalendar(w, sp.GetRequiredService<BackendConfigurationPnDbContext>(),
                        sp.GetRequiredService<ItemsPlanningPnDbContext>());
                    if (throwForProperty is not { } bad) return real;
                    var calendar = Substitute.For<IBackendConfigurationCalendarService>();
                    calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>()).Returns(ci =>
                    {
                        var model = ci.Arg<CalendarTaskRequestModel>();
                        return model.PropertyId == bad ? throw new InvalidOperationException("calendar broken") : real.GetTasksForWeek(model);
                    });
                    return calendar;
                });
                s.AddSingleton(_signals);
            }, connected);

    private TaskReminderPlanner NewPlanner(LiveChangeTestHost host) =>
        new(host.Provider.GetRequiredService<IServiceScopeFactory>(), _signals, _options, _clock,
            host.Provider.GetRequiredService<ILogger<TaskReminderPlanner>>());

    private async Task<List<TaskNotification>> RowsAsync(World w)
    {
        await using var db = CreateFreshBackendConfigurationDbContext();
        return await db.TaskNotifications.AsNoTracking().Where(n => n.PropertyId == w.PropertyId)
            .OrderBy(n => n.Id).ToListAsync();
    }

    private async Task SetStateAsync(long id, TaskNotificationState state)
    {
        await using var db = CreateFreshBackendConfigurationDbContext();
        await db.TaskNotifications.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.State, state));
    }

    // ------------------------------------------------------------------ planning

    [Test]
    public async Task ATimedTaskOfToday_IsPlannedForTheOptedInWorkerOnly_TenMinutesAhead_InTheirLanguage()
    {
        var w = await SeedWorldAsync("da");
        var s = await SeedDailySeriesAsync(w, 9.0, "Fodring");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);

        Assert.That(await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None), Is.True);

        var row = (await RowsAsync(w)).Single();
        var (title, body) = TaskReminderText.Render("da", "Fodring", new TimeOnly(9, 0), w.PropertyName);
        Assert.Multiple(() =>
        {
            Assert.That(row.SdkSiteId, Is.EqualTo(w.SiteA), "site B has no opted-in device");
            Assert.That(row.AreaRulePlanningId, Is.EqualTo(s.ArpId));
            Assert.That(row.PlanDayKey, Is.EqualTo(DayKey));
            Assert.That(row.PlannedStartUtc, Is.EqualTo(Utc(9)));
            Assert.That(row.SendAtUtc, Is.EqualTo(Utc(9) - TimeSpan.FromMinutes(10)));
            Assert.That(row.State, Is.EqualTo(TaskNotificationState.Pending));
            Assert.That((row.Title, row.Body), Is.EqualTo((title, body)));
            Assert.That(body, Does.StartWith("Starter kl. 09:00"));
        });
    }

    [Test]
    public async Task ATaskStartingInLessThanTenMinutes_IsDueAtOnce_AndOneThatStartedIsNotPlanned()
    {
        var w = await SeedWorldAsync();
        await SeedDailySeriesAsync(w, 9.0, "Soon");
        await SeedDailySeriesAsync(w, 8.0, "Started");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        _clock.SetUtcNow(new DateTimeOffset(Utc(8.95), TimeSpan.Zero)); // 08:57 local

        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);

        var row = (await RowsAsync(w)).Single();
        Assert.That(row.PlannedStartUtc, Is.EqualTo(Utc(9)));
        Assert.That(row.SendAtUtc, Is.LessThanOrEqualTo(_clock.GetUtcNow().UtcDateTime), "due at once");
    }

    [Test]
    public async Task RemovingTasks_CancelsAPendingReminder_AndWithdrawsASentOne()
    {
        var w = await SeedWorldAsync();
        var sent = await SeedDailySeriesAsync(w, 9.0, "Sent");
        var pending = await SeedDailySeriesAsync(w, 11.0, "Pending");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);
        var rows = await RowsAsync(w);
        await SetStateAsync(rows.Single(r => r.AreaRulePlanningId == sent.ArpId).Id, TaskNotificationState.Sent);

        foreach (var s in new[] { sent, pending })
        {
            var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == s.ArpId);
            await arp.Delete(BackendConfigurationPnDbContext);
        }

        Assert.That(await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None), Is.True);
        rows = await RowsAsync(w);
        Assert.That(rows.Single(r => r.AreaRulePlanningId == sent.ArpId).State, Is.EqualTo(TaskNotificationState.WithdrawPending));
        Assert.That(rows.Single(r => r.AreaRulePlanningId == pending.ArpId).State, Is.EqualTo(TaskNotificationState.Cancelled));
    }

    [Test]
    public async Task MovingATask_CancelsTheOldTime_AndMovingItBackRevivesIt()
    {
        var w = await SeedWorldAsync();
        var s = await SeedDailySeriesAsync(w, 9.0, "Move");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);

        async Task MoveToAsync(double hour)
        {
            await using var db = CreateFreshBackendConfigurationDbContext();
            await db.CalendarConfigurations.Where(c => c.AreaRulePlanningId == s.ArpId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.StartHour, hour));
        }

        await MoveToAsync(11.0);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);
        var rows = await RowsAsync(w);
        Assert.That(rows.Single(r => r.PlannedStartUtc == Utc(9)).State, Is.EqualTo(TaskNotificationState.Cancelled));
        Assert.That(rows.Single(r => r.PlannedStartUtc == Utc(11)).State, Is.EqualTo(TaskNotificationState.Pending));

        await MoveToAsync(9.0);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);
        rows = await RowsAsync(w);
        Assert.That(rows, Has.Count.EqualTo(2), "the old row is revived, not duplicated");
        Assert.That(rows.Single(r => r.PlannedStartUtc == Utc(9)).State, Is.EqualTo(TaskNotificationState.Pending));
        Assert.That(rows.Single(r => r.PlannedStartUtc == Utc(11)).State, Is.EqualTo(TaskNotificationState.Cancelled));
    }

    [Test]
    public async Task UnassigningTheWorker_CancelsTheirReminder()
    {
        var w = await SeedWorldAsync();
        await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);

        var link = await BackendConfigurationPnDbContext!.PropertyWorkers
            .SingleAsync(x => x.PropertyId == w.PropertyId && x.WorkerId == w.SiteA);
        await link.Delete(BackendConfigurationPnDbContext);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);

        Assert.That((await RowsAsync(w)).Single().State, Is.EqualTo(TaskNotificationState.Cancelled));
    }

    [Test]
    public async Task RenamingATask_UpdatesThePendingText()
    {
        var w = await SeedWorldAsync();
        var s = await SeedDailySeriesAsync(w, 9.0, "Old name");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);

        await using (var db = CreateFreshBackendConfigurationDbContext())
        {
            await db.AreaRuleTranslations.Where(t => t.AreaRuleId == s.AreaRuleId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.Name, "New name"));
        }

        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);
        var row = (await RowsAsync(w)).Single();
        Assert.That((row.Title, row.State), Is.EqualTo(("New name", TaskNotificationState.Pending)));
    }

    [Test]
    public async Task AnUnknownWorkerLanguage_FallsBackToEnglish()
    {
        var w = await SeedWorldAsync("zz-ZZ");
        await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);

        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);

        Assert.That((await RowsAsync(w)).Single().Body, Does.StartWith("Starts at 09:00"));
    }

    [Test]
    public async Task ARowInFlight_ThatShouldBeRetracted_KeepsThePropertyDirty()
    {
        var w = await SeedWorldAsync();
        var s = await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);
        await SetStateAsync((await RowsAsync(w)).Single().Id, TaskNotificationState.Sending); // a deliverer holds it
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == s.ArpId);
        await arp.Delete(BackendConfigurationPnDbContext);

        Assert.That(await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None), Is.False, "not settled");
        Assert.That(_signals.IsDirty(w.PropertyId), Is.True, "planned again on a later pass");
    }

    [Test]
    public async Task ThePlannersRead_SendsNoHint_AndWritesNoTaskRow()
    {
        var w = await SeedWorldAsync();
        var s = await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w, connected: true);
        using var planner = NewPlanner(host);
        int Compliances() => BackendConfigurationPnDbContext!.Compliances.AsNoTracking().Count(c => c.PlanningId == s.PlanningId);
        int Cases() => ItemsPlanningPnDbContext!.PlanningCases.AsNoTracking().Count(c => c.PlanningId == s.PlanningId);

        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);
        await host.Publisher.WhenIdleAsync();

        Assert.That(host.Notifier.Calls, Is.Empty);
        Assert.That((Compliances(), Cases()), Is.EqualTo((0, 0)), "no deploy");
        Assert.That(await RowsAsync(w), Has.Count.EqualTo(1));
    }

    // ------------------------------------------------------------------ the capture gate

    [Test]
    public async Task WithNoHubClient_ButAnOptedInDevice_ASaveMarksThePropertyDirty_AndItIsPlanned()
    {
        var w = await SeedWorldAsync();
        var s = await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w, connected: false);
        using var planner = NewPlanner(host);
        _signals.AnyOptedIn = true;

        await host.InBackgroundAsync(async sp =>
        {
            var db = sp.GetRequiredService<BackendConfigurationPnDbContext>();
            var config = await db.CalendarConfigurations.SingleAsync(c => c.AreaRulePlanningId == s.ArpId);
            config.StartHour = 10.0;
            await config.Update(db);
        });

        Assert.That(_signals.IsDirty(w.PropertyId), Is.True);
        await planner.RunPassAsync(CancellationToken.None);
        Assert.That((await RowsAsync(w)).Single().PlannedStartUtc, Is.EqualTo(Utc(10)));
    }

    // ------------------------------------------------------------------ review round 1

    [Test]
    public async Task ASiteWithoutALanguage_GetsItsRemindersInDanish_AndTheOtherWorkersToo()
    {
        var w = await SeedWorldAsync("en-US", siteAWithoutLanguage: true, extraOptedInWorkers: 1);
        await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);

        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);

        var rows = await RowsAsync(w);
        Assert.That(rows.Select(r => r.SdkSiteId), Is.EquivalentTo(new[] { w.SiteA, w.Extra[0] }));
        Assert.That(rows.Single(r => r.SdkSiteId == w.SiteA).Body, Does.StartWith("Starter kl. 09:00"),
            "the code base's default language (Danish)");
        Assert.That(rows.Single(r => r.SdkSiteId == w.Extra[0]).Body, Does.StartWith("Starts at 09:00"));
    }

    [Test]
    public async Task APropertyThatThrows_DoesNotStopLaterProperties_IsLeftToTheSweep_AndWarnsOnce()
    {
        var bad = await SeedWorldAsync();
        await SeedDailySeriesAsync(bad, 9.0, "Bad");
        var good = await SeedWorldAsync();
        await SeedDailySeriesAsync(good, 9.0, "Good");
        Assert.That(bad.PropertyId, Is.LessThan(good.PropertyId), "the failing property drains first");
        using var host = NewHost(good, throwForProperty: bad.PropertyId);
        using var planner = NewPlanner(host);

        for (var i = 0; i < 3; i++)
        {
            _signals.MarkProperty(bad.PropertyId);
            _signals.MarkProperty(good.PropertyId);
            await planner.RunPassAsync(CancellationToken.None);
            Assert.That(_signals.IsDirty(bad.PropertyId), Is.False, "not retried every 2 s");
        }

        Assert.That(await RowsAsync(good), Has.Count.EqualTo(1), "the later property was planned");
        Assert.That(await RowsAsync(bad), Is.Empty);
        Assert.That(host.Warnings.WarningsOf<TaskReminderPlanner>(), Is.EqualTo(1), "one warning per property per hour");

        await planner.SweepAsync(CancellationToken.None);
        Assert.That(_signals.IsDirty(bad.PropertyId), Is.True, "the minute sweep retries it");
    }

    [Test]
    public async Task ABudgetSpentMidProperty_CancelsNoRowOfAnUnprocessedWorker_AndTheNextPassesFinish()
    {
        var w = await SeedWorldAsync(extraOptedInWorkers: 2);
        var s = await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);
        var workers = new[] { w.SiteA }.Concat(w.Extra).OrderBy(x => x).ToList();
        Assert.That((await RowsAsync(w)).Select(r => r.SdkSiteId), Is.EquivalentTo(workers));

        await using (var db = CreateFreshBackendConfigurationDbContext())
        {
            await db.CalendarConfigurations.Where(c => c.AreaRulePlanningId == s.ArpId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.StartHour, 11.0));
        }

        _options.PassBudget = TimeSpan.Zero; // one worker per pass
        _signals.MarkProperty(w.PropertyId);    // the change; afterwards the planner keeps it queued itself
        for (var pass = 1; pass <= workers.Count; pass++)
        {
            await planner.RunPassAsync(CancellationToken.None);
            var rows = await RowsAsync(w);
            for (var i = 0; i < workers.Count; i++)
            {
                var oldRow = rows.Single(r => r.SdkSiteId == workers[i] && r.PlannedStartUtc == Utc(9));
                var done = i < pass;
                Assert.That(oldRow.State, Is.EqualTo(done ? TaskNotificationState.Cancelled : TaskNotificationState.Pending),
                    $"pass {pass}, worker {i}");
                Assert.That(rows.Any(r => r.SdkSiteId == workers[i] && r.PlannedStartUtc == Utc(11)), Is.EqualTo(done));
            }

            Assert.That(_signals.IsDirty(w.PropertyId), Is.EqualTo(pass < workers.Count), "unfinished work stays queued");
        }
    }

    // ------------------------------------------------------------------ review round 2

    private async Task MoveAsync(Series s, double hour)
    {
        await using var db = CreateFreshBackendConfigurationDbContext();
        await db.CalendarConfigurations.Where(c => c.AreaRulePlanningId == s.ArpId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.StartHour, hour));
    }

    [Test]
    public async Task ARealChangeBetweenPasses_RestartsAnUnfinishedPropertyFromAllWorkers()
    {
        var w = await SeedWorldAsync(extraOptedInWorkers: 2);
        var s = await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        await planner.PlanPropertyAsync(w.PropertyId, CancellationToken.None);
        var first = new[] { w.SiteA }.Concat(w.Extra).Min();
        _options.PassBudget = TimeSpan.Zero; // one worker per pass

        _signals.MarkProperty(w.PropertyId);
        await planner.RunPassAsync(CancellationToken.None); // the first worker; two are left
        await MoveAsync(s, 11.0);
        _signals.MarkProperty(w.PropertyId);                 // a real change: the round starts over
        for (var pass = 0; pass < 3; pass++) await planner.RunPassAsync(CancellationToken.None);

        var rows = await RowsAsync(w);
        foreach (var site in new[] { w.SiteA }.Concat(w.Extra))
        {
            Assert.That(rows.Single(r => r.SdkSiteId == site && r.PlannedStartUtc == Utc(9)).State,
                Is.EqualTo(TaskNotificationState.Cancelled), $"worker {site} read again after the change");
            Assert.That(rows.Any(r => r.SdkSiteId == site && r.PlannedStartUtc == Utc(11)), Is.True);
        }

        Assert.That(first, Is.GreaterThan(0));
    }

    [Test]
    public async Task ANewLocalDayBetweenPasses_RestartsAnUnfinishedPropertyFromAllWorkers()
    {
        var w = await SeedWorldAsync(extraOptedInWorkers: 2);
        await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        var first = new[] { w.SiteA }.Concat(w.Extra).Min();
        _options.PassBudget = TimeSpan.Zero;

        _signals.MarkProperty(w.PropertyId);
        await planner.RunPassAsync(CancellationToken.None); // day D: the first worker only
        _clock.SetUtcNow(new DateTimeOffset(TaskReminderTime.ToUtc(Day.AddDays(1), 6.0, Zone), TimeSpan.Zero));
        for (var pass = 0; pass < 3; pass++) await planner.RunPassAsync(CancellationToken.None); // day D + 1

        var nextDay = Day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rows = await RowsAsync(w);
        Assert.That(rows.Where(r => r.PlanDayKey == nextDay).Select(r => r.SdkSiteId),
            Is.EquivalentTo(new[] { w.SiteA }.Concat(w.Extra)), "the new day's round reaches every worker, the first included");
        Assert.That(first, Is.GreaterThan(0));
    }

    [Test]
    public async Task AnUnfinishedProperty_GoesToTheBack_SoSmallerDirtyPropertiesAreNotHeldBack()
    {
        var big = await SeedWorldAsync(extraOptedInWorkers: 2);
        await SeedDailySeriesAsync(big, 9.0, "Big");
        var small1 = await SeedWorldAsync();
        await SeedDailySeriesAsync(small1, 9.0, "Small 1");
        var small2 = await SeedWorldAsync();
        await SeedDailySeriesAsync(small2, 9.0, "Small 2");
        using var host = NewHost(big);
        using var planner = NewPlanner(host);
        _options.PassBudget = TimeSpan.Zero; // one worker per pass: progress is still guaranteed
        _signals.MarkProperty(big.PropertyId);
        _signals.MarkProperty(small1.PropertyId);
        _signals.MarkProperty(small2.PropertyId);

        async Task<int> CountAsync(World w) => (await RowsAsync(w)).Count;

        await planner.RunPassAsync(CancellationToken.None);
        Assert.That((await CountAsync(big), await CountAsync(small1), await CountAsync(small2)), Is.EqualTo((1, 0, 0)));
        await planner.RunPassAsync(CancellationToken.None);
        Assert.That((await CountAsync(big), await CountAsync(small1), await CountAsync(small2)), Is.EqualTo((1, 1, 0)),
            "the unfinished property went to the back");
        await planner.RunPassAsync(CancellationToken.None);
        Assert.That((await CountAsync(big), await CountAsync(small1), await CountAsync(small2)), Is.EqualTo((1, 1, 1)));
        await planner.RunPassAsync(CancellationToken.None);
        Assert.That(await CountAsync(big), Is.EqualTo(2), "and then continues");
    }

    [Test]
    public async Task ABusyPropertyMarkedBeforeEveryPass_StillReachesEveryWorker_WithinOneRotation()
    {
        var w = await SeedWorldAsync(extraOptedInWorkers: 3); // 4 opted-in workers
        await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        _options.PassBudget = TimeSpan.Zero; // K = 1 worker per pass, N = 4
        var workers = new[] { w.SiteA }.Concat(w.Extra).ToList();

        for (var pass = 0; pass < workers.Count; pass++)
        {
            _signals.MarkProperty(w.PropertyId); // a real change before every pass
            await planner.RunPassAsync(CancellationToken.None);
        }

        Assert.That((await RowsAsync(w)).Select(r => r.SdkSiteId).Distinct(), Is.EquivalentTo(workers),
            "every worker was read within ceil(N/K) passes");
    }

    // ------------------------------------------------------------------ sweep and passes

    [Test]
    public async Task TheSweep_MarksAPropertyOnlyWhenNeverPlanned_StaleOrOnANewLocalDay()
    {
        var w = await SeedWorldAsync();
        await SeedDailySeriesAsync(w, 9.0, "Task");
        using var host = NewHost(w);
        using var planner = NewPlanner(host);

        await planner.SweepAsync(CancellationToken.None);
        Assert.That(_signals.AnyOptedIn, Is.True);
        Assert.That(_signals.IsDirty(w.PropertyId), Is.True, "never planned");
        await planner.RunPassAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(5));
        await planner.SweepAsync(CancellationToken.None);
        Assert.That(_signals.IsDirty(w.PropertyId), Is.False, "planned 5 minutes ago");

        _clock.Advance(TimeSpan.FromMinutes(11));
        await planner.SweepAsync(CancellationToken.None);
        Assert.That(_signals.IsDirty(w.PropertyId), Is.True, "older than 15 minutes");
        await planner.RunPassAsync(CancellationToken.None);

        _clock.SetUtcNow(new DateTimeOffset(TaskReminderTime.ToUtc(Day.AddDays(1), 0.1, Zone), TimeSpan.Zero));
        await planner.SweepAsync(CancellationToken.None);
        Assert.That(_signals.IsDirty(w.PropertyId), Is.True, "a new local day");
    }

    [Test]
    public async Task WithNoOptedInDeviceAnywhere_TheSweepTurnsTheGateOff_AndMarksNothing()
    {
        var w = await SeedWorldAsync();
        await using (var db = CreateFreshBackendConfigurationDbContext())
        {
            await db.DeviceTokens.ExecuteUpdateAsync(u => u.SetProperty(t => t.TaskReminders, false));
        }

        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        _signals.AnyOptedIn = true;

        await planner.SweepAsync(CancellationToken.None);

        Assert.That(_signals.AnyOptedIn, Is.False);
        Assert.That(_signals.DirtyCount, Is.Zero);
    }

    [Test]
    public async Task APass_IsBounded_ABurstSpreadsOverPasses()
    {
        var w = await SeedWorldAsync();
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        _options.MaxPropertiesPerPass = 10;
        for (var p = 1; p <= 30; p++) _signals.MarkProperty(900000 + p);

        Assert.That(await planner.RunPassAsync(CancellationToken.None), Is.EqualTo(10));
        Assert.That(_signals.DirtyCount, Is.EqualTo(20));
    }

    // ------------------------------------------------------------------ host safety

    [Test]
    public async Task AThrowingContext_NeverEscapesATick_AndBacksOff()
    {
        var w = await SeedWorldAsync();
        using var host = NewHost(w);
        using var planner = NewPlanner(host);
        planner.ContextFor = _ => throw new InvalidOperationException("database down");
        _signals.MarkProperty(w.PropertyId);

        Assert.That(await planner.TickAsync(CancellationToken.None), Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(await planner.TickAsync(CancellationToken.None), Is.EqualTo(TimeSpan.FromSeconds(8)));
        Assert.That(_signals.IsDirty(w.PropertyId), Is.True, "a failed pass loses no dirty property");
    }

    [Test]
    public async Task ThePlanner_RunsWithAGenericHost_AndStopsPromptly()
    {
        var w = await SeedWorldAsync();
        using var testHost = NewHost(w);
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                testHost.RegisterInto(services);
                services.AddSingleton(new TaskReminderOptions { PassInterval = TimeSpan.FromMilliseconds(100) });
                services.AddBackendConfigurationTaskReminders();
            })
            .Build();

        await host.StartAsync();
        var planner = host.Services.GetServices<IHostedService>().OfType<TaskReminderPlanner>().Single();
        var stop = host.StopAsync();
        Assert.That(await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(stop));
        Assert.That(planner.ExecuteTask?.IsFaulted, Is.Not.True);
    }
}
