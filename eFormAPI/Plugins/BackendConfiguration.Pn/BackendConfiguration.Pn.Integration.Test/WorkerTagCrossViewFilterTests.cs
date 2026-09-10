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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Infrastructure.Models.TaskTracker;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1231 / #1232 / #1233 — worker-tag ("team") awareness of the assignee/employee
/// filter in the FOUR surfaces that had it wrong, plus the calendar week view that
/// already had it right, all driven through their own real service methods against
/// ONE seeded dataset.
///
/// <para>
/// <b>The defect, one sentence.</b> Deployment never materialises a worker tag's
/// members into <c>PlanningSites</c> — <c>EventDeployService</c> unions
/// <c>AreaRulePlanningWorkerTags</c> → SDK <c>SiteTags</c> separately — so an event
/// assigned to a team instead of to named individuals has NO <c>PlanningSites</c> row
/// for anyone in that team, and every filter that matched only against
/// <c>PlanningSites</c> hid it from the very people it was assigned to.
/// </para>
///
/// <para>
/// <b>Why one fixture rather than four.</b> The product requirement behind these three
/// issues is that no view may show more, or less, than the others for the same
/// filter. Asking the shared <c>IWorkerTagMembershipService</c> the same question five
/// times would prove nothing about that; each helper below therefore calls the real
/// entry point of its view and reduces the response to the set of
/// <c>AreaRulePlanning</c> ids it rendered:
/// </para>
/// <list type="bullet">
///   <item><c>BackendConfigurationCalendarService.GetTasksForWeek</c> — calendar week grid</item>
///   <item><c>BackendConfigurationCalendarService.Index</c> — calendar task list (#1233)</item>
///   <item><c>BackendConfigurationTaskTrackerHelper.Index</c> — web Task Tracker (#1231a)</item>
///   <item><c>BackendConfigurationCalendarService.GetTaskTrackerList</c> — gRPC/mobile (#1231b)</item>
///   <item><c>BackendConfigurationComplianceReportService.Index</c> — Compliance report (#1232)</item>
/// </list>
///
/// <para>
/// <b>Seeding shape.</b> Every event gets BOTH a recurrence series (a weekly
/// <c>AreaRulePlanning</c> anchored on the week's Monday) and a <c>Compliance</c> row
/// on the Wednesday, because the five views do not read the same table: the week view
/// renders both, the task list reads <c>AreaRulePlannings</c> only, and the two task
/// trackers plus the compliance report are <c>Compliance</c>-backed. One compliance
/// per planning per date — <c>Compliances</c> is UNIQUE on
/// <c>(PlanningId, Deadline)</c>.
/// </para>
///
/// <para>
/// <b>Read this before trusting the parity test — it proves less than it looks like.</b>
/// There are TWO unrelated <c>PlanningSite</c> entities in this product:
/// <c>Microting.ItemsPlanningBase...PlanningSite</c>, keyed by <c>PlanningId</c>, and
/// <c>Microting.EformBackendConfigurationBase...PlanningSite</c>, keyed by
/// <c>AreaRulePlanningsId</c>. They live in different databases. The two task trackers
/// FILTER on the items-planning one; the calendar and the compliance report filter on
/// the BC one. Nothing keeps the two in step. That is a claim about the FILTERS only —
/// the gRPC tracker is not even consistent with itself: it filters on the
/// items-planning <c>PlanningSites</c> and then renders <c>AssigneeIds</c> off
/// <c>arp.PlanningSites</c>, the BC ones. Tracked as issue #1235 — read that before
/// re-deriving any of this.
/// </para>
///
/// <para>
/// <c>AssignSite</c> below therefore writes BOTH rows, DELIBERATELY, and that is the
/// only reason the five views are comparable at all. What this fixture measures is
/// worker-TAG behaviour — that no view sees more or fewer team-assigned events than
/// another for the same filter. It does NOT measure, and must not be read as
/// establishing, that the two assignment tables agree on real data. That divergence is
/// pre-existing, independent of worker tags, and tracked separately.
/// </para>
///
/// <para>
/// The same caution applies to date scoping: THREE of the five views — the calendar
/// task list (<c>BackendConfigurationCalendarService.Index</c>, whose filter set has no
/// date clause at all) and the two task trackers — have no date window, while the week
/// view and the compliance report do. Parity here is asserted
/// over the ASSIGNEE FILTER only, and holds because every seeded event has exactly one
/// compliance inside the queried week. Making the views agree on scoping would be a
/// product decision and is not attempted.
/// </para>
///
/// <para>
/// <b>Fixture hygiene.</b> <c>TestBaseSetup.ResetDatabasePerTest</c> is <c>false</c>, so
/// rows accumulate across the tests in this fixture. Every test seeds its own property,
/// SDK sites and SDK tag with GUID names and auto-generated ids, and every assertion is
/// scoped to ids it seeded — no whole-table counts and no hard-coded ids.
/// </para>
///
/// <para>
/// Dates are derived from the CURRENT UTC week rather than pinned to a literal, because
/// the web Task Tracker DELETES a compliance whose deadline has passed when the series
/// has <c>ComplianceEnabled</c> false. Every series seeded here sets
/// <c>ComplianceEnabled = true</c> for the same reason, so that branch is never taken
/// whatever weekday the suite runs on.
/// </para>
/// </summary>
[TestFixture]
public class WorkerTagCrossViewFilterTests : TestBaseSetup
{
    /// <summary>Monday 00:00 UTC of the current week.</summary>
    private static readonly DateTime WeekMonday =
        DateTime.SpecifyKind(
            DateTime.UtcNow.Date.AddDays(-(((int)DateTime.UtcNow.Date.DayOfWeek + 6) % 7)),
            DateTimeKind.Utc);

    /// <summary>The Wednesday of that week — where every seeded Compliance falls.</summary>
    private static readonly DateTime ComplianceDeadline = WeekMonday.AddDays(2);

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// Everything one seeded event is addressed by. The views disagree about which of
    /// these ids they key on, so they all travel together.
    /// </summary>
    private sealed record SeededEvent(int ArpId, int PlanningId, int AreaId, int AreaRuleId);

    // ─────────────────────────────────────────────────────────────────────────
    // Per-test cleanup
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FK-safe clean of the rows this fixture writes. Assertions are scoped to seeded
    /// ids and would survive accumulation, but THREE of the five views have NO date
    /// window at all — the two task trackers, and the calendar task list, which reads
    /// <c>AreaRulePlannings</c> with no date clause — so without this every test would
    /// re-render every previous test's rows: the trackers its compliances, the task
    /// list its series. NUnit runs the base-class [SetUp] first, so the contexts exist
    /// here.
    /// </summary>
    [SetUp]
    public async Task CleanAccumulatingTables()
    {
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");

        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRules.RemoveRange(
            BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.PlanningSites.RemoveRange(
            ItemsPlanningPnDbContext.PlanningSites);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Property> SeedProperty()
    {
        var property = new Property
        {
            Name = $"CrossViewProp-{Guid.NewGuid()}",
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
    /// Area → AreaRule → Planning → AreaRulePlanning → Compliance. The series is weekly
    /// and anchored on <see cref="WeekMonday"/> so the week view's recurrence branch
    /// emits it; the Compliance on <see cref="ComplianceDeadline"/> is what the two task
    /// trackers and the compliance report read.
    /// </summary>
    private async Task<SeededEvent> SeedEvent(int propertyId)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            PropertyId = propertyId,
            EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = WeekMonday,
            // The web Task Tracker casts NextExecutionTime to a non-nullable DateTime.
            NextExecutionTime = WeekMonday.AddDays(7),
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = propertyId,
            AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = WeekMonday,
            Status = true,
            // Not decoration: the web Task Tracker DELETES a compliance whose deadline
            // has passed when this is false, which would silently empty the fixture on
            // any run later in the week.
            ComplianceEnabled = true,
            RepeatType = 2,
            RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            ItemName = $"cross-view-{Guid.NewGuid()}",
            PlanningId = planning.Id,
            PropertyId = propertyId,
            AreaId = area.Id,
            Deadline = ComplianceDeadline,
            StartDate = WeekMonday,
            // No backing SDK Case: the compliance report reads "done" off
            // Case.Status == 100, so 0 keeps every seeded row open.
            MicrotingSdkCaseId = 0,
            MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new SeededEvent(arp.Id, planning.Id, area.Id, areaRule.Id);
    }

    /// <summary>
    /// Explicit (named-individual) assignment. Writes BOTH assignment tables — the two
    /// distinct <c>PlanningSite</c> entities described in the fixture remarks. Seeding
    /// only one would make a view look broken for a reason that has nothing to do with
    /// worker tags.
    /// </summary>
    private async Task AssignSite(SeededEvent evt, int siteId)
    {
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = evt.ArpId,
            SiteId = siteId,
            AreaId = evt.AreaId,
            AreaRuleId = evt.AreaRuleId,
            Status = 33,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext!.PlanningSites.AddAsync(new IpPlanningSite
        {
            PlanningId = evt.PlanningId,
            SiteId = siteId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Team assignment — an <c>AreaRulePlanningWorkerTag</c> link and deliberately NO
    /// row in either PlanningSites table. That absence is the whole defect.
    /// </summary>
    private async Task AssignWorkerTag(SeededEvent evt, int tagId)
    {
        await BackendConfigurationPnDbContext!.AreaRulePlanningWorkerTags.AddAsync(
            new AreaRulePlanningWorkerTag
            {
                AreaRulePlanningId = evt.ArpId,
                TagId = tagId,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1,
                UpdatedByUserId = 1
            });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <param name="removed">
    /// Seeds the Site already soft-deleted — the state <c>Core.SiteDelete</c> leaves
    /// behind. It removes the Site but NOT the SiteTags rows, and it does not set
    /// <c>Worker.Resigned</c>, so this exclusion is the Site clause's alone. No
    /// SiteWorker is created either way.
    /// </param>
    private async Task<int> SeedSdkSite(bool removed = false)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid()}",
            MicrotingUid = null,
            LanguageId = language.Id,
            WorkflowState = removed
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>An SDK Tag is what the product calls a worker tag / team.</summary>
    private async Task<int> SeedSdkWorkerTag()
    {
        var tag = new Tag
        {
            Name = $"team-{Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Tags.AddAsync(tag);
        await MicrotingDbContext.SaveChangesAsync();
        return tag.Id;
    }

    private async Task LinkSiteToTag(int tagId, int siteId, bool removed = false)
    {
        await MicrotingDbContext!.SiteTags.AddAsync(new SiteTag
        {
            TagId = tagId,
            SiteId = siteId,
            WorkflowState = removed
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Site + Worker + SiteWorker triple with the worker's <c>Resigned</c> flag set as
    /// asked. MUST be seeded through an SDK context handed out AFTER the Core has run
    /// its migrations — <c>SQL/420_SDK.sql</c> creates <c>Workers</c> without a
    /// <c>Resigned</c> column.
    /// </summary>
    private static async Task<int> SeedSdkSiteWithWorker(MicrotingDbContext sdkDbContext, bool resigned)
    {
        var language = await sdkDbContext.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid()}",
            MicrotingUid = null,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdkDbContext.Sites.AddAsync(site);
        await sdkDbContext.SaveChangesAsync();

        var worker = new Worker
        {
            FirstName = $"member-{Guid.NewGuid():N}",
            LastName = "Worker",
            Email = $"{Guid.NewGuid():N}@example.test",
            Resigned = resigned,
            ResignedAtDate = resigned ? DateTime.UtcNow.AddDays(-1) : default,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdkDbContext.Workers.AddAsync(worker);
        await sdkDbContext.SaveChangesAsync();

        await sdkDbContext.SiteWorkers.AddAsync(new SiteWorker
        {
            SiteId = site.Id,
            WorkerId = worker.Id,
            WorkflowState = Constants.WorkflowStates.Created
        });
        await sdkDbContext.SaveChangesAsync();

        return site.Id;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Services under test — the REAL implementations
    // ─────────────────────────────────────────────────────────────────────────

    private eFormCore.Core? _sharedCore;

    /// <summary>
    /// ONE SDK <c>Core</c> for the whole fixture. Every accessor below needs a Core, and
    /// the parity test alone reaches them ~27 times, so <c>TestBaseSetup.GetCore</c>'s
    /// cost is what this fixture's runtime mostly is.
    ///
    /// <para>
    /// That cost is not a constructor. <c>GetCore</c> runs <c>Core.StartSqlOnly</c>,
    /// which builds a <c>SqlController</c> (an EF <c>GetPendingMigrations()</c> check
    /// plus <c>SettingCheckAll()</c>) and then calls <c>SettingCheckAll()</c> AGAIN, and
    /// each of those walks all 40 members of the <c>Settings</c> enum calling
    /// <c>SettingRead</c> — every one of which opens its OWN <c>MicrotingDbContext</c>
    /// through <c>MicrotingDbContextFactory</c>, whose <c>ServerVersion.AutoDetect</c>
    /// makes a second connection of its own. Roughly a hundred fresh contexts and twice
    /// that many connections, per call, against a Testcontainers MariaDB.
    /// </para>
    ///
    /// <para>
    /// Reuse is safe here, and the reasons are worth stating because they are what a
    /// later "optimisation" would have to preserve:
    /// </para>
    /// <list type="bullet">
    ///   <item>A <c>Core</c> holds no context. <c>DbContextHelper</c> keeps a connection
    ///   STRING and mints a brand-new <c>MicrotingDbContext</c> per
    ///   <c>GetDbContext()</c>, so the five accessors still get five independent
    ///   contexts and no change tracker is shared between them.</item>
    ///   <item>Nothing disposes a <c>Core</c>. <c>TestBaseSetup.TearDown</c> disposes the
    ///   fixture's own contexts only, so a Core outlives the test that made it either
    ///   way.</item>
    ///   <item><c>ResetDatabasePerTest</c> is <c>false</c> for this fixture, so the
    ///   databases this Core migrated on the first test are never rebuilt under it. A
    ///   fixture that overrides that to <c>true</c> must NOT copy this.</item>
    ///   <item>NUnit keeps one instance per fixture, which is what makes an instance
    ///   field last across the tests — the same assumption <c>TestBaseSetup</c> already
    ///   makes with <c>_schemaBootstrapped</c>. That is what makes the field REUSABLE;
    ///   it is NOT what makes the assignment safe. <c>??=</c> over an <c>await</c> is a
    ///   read, a suspension and a write, so concurrent tests would both observe null and
    ///   both build a Core. What actually protects it is that this fixture carries no
    ///   <c>[Parallelizable]</c> attribute, so its tests run SEQUENTIALLY. Putting
    ///   <c>[Parallelizable(ParallelScope.Children)]</c> or <c>ParallelScope.All</c> on
    ///   this fixture breaks that and must not be done. The
    ///   <c>[Parallelizable(ParallelScope.Fixtures)]</c> that sibling fixtures carry is
    ///   harmless here — it parallelises ACROSS fixtures, never the tests within one.</item>
    /// </list>
    ///
    /// <para>
    /// The SDK pre-warm exemption, recorded because its ABSENCE would otherwise read as
    /// an oversight. <c>SQL/420_SDK.sql</c> seeds an <c>__EFMigrationsHistory</c> that
    /// stops at <c>20221129082337_AddingReceivedByServerAtToCases</c>; eight SDK
    /// migrations have landed since, adding <c>Workers.Initials</c>,
    /// <c>Workers.PinCode</c>/<c>EmployeeNo</c>, <c>Workers.PhoneNumber</c>,
    /// <c>Workers.Resigned</c>, <c>UploadedDatas.OriginalFileLocation</c> and columns on
    /// <c>Folders</c> and <c>CheckListSites</c>. Only <c>GetCore()</c> applies them, which
    /// is why sibling fixtures carry loud warnings to call it and then REBUILD their
    /// context before reading those tables. This fixture is exempt, and deliberately so:
    /// all eight migrations touch only <c>Workers</c>, <c>UploadedDatas</c>,
    /// <c>Folders</c> and <c>CheckListSites</c>, while every helper that goes through the
    /// fixture's own <c>MicrotingDbContext</c> reaches only <c>Sites</c>, <c>Tags</c>,
    /// <c>SiteTags</c> and <c>Languages</c> — all pre-cutoff tables. The one helper that
    /// writes <c>Workers</c>, <c>SeedSdkSiteWithWorker</c>, is handed a context obtained
    /// from the Core (<c>core.DbContextHelper.GetDbContext()</c>) for exactly this reason.
    /// </para>
    /// <para>
    /// That exemption is a live constraint, not history. <c>TestBaseSetup</c> builds
    /// <c>MicrotingDbContext</c> in its <c>[SetUp]</c> — a fresh instance per test, but
    /// always before any Core exists — and this fixture leaves <c>ResetDatabasePerTest</c>
    /// <c>false</c>, so the schema is seeded once and then stays migrated for the rest of
    /// the run. Adding a <c>Workers</c> or <c>UploadedDatas</c> read to a
    /// <c>MicrotingDbContext</c> helper here would therefore fail on the FIRST test of the
    /// fixture and pass in every later one: a run-order-dependent failure, and the kind
    /// that is hardest to read off a CI log.
    /// </para>
    /// <para>
    /// One last thing to know before sharing a Core more widely: <c>SqlController</c>
    /// lazily caches the <c>FieldTypes</c> table in its <c>_converter</c> list, which is
    /// the only DB-derived state a Core carries. It is inert here — nothing in this
    /// fixture writes <c>FieldTypes</c>, and the rows are a fixed lookup — but it is the
    /// thing to check if a future test ever mutates them.
    /// </para>
    /// </summary>
    private async Task<eFormCore.Core> SharedCore()
    {
        _sharedCore ??= await GetCore();
        return _sharedCore;
    }

    private IEFormCoreService CoreHelper(eFormCore.Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return coreHelper;
    }

    private IUserService UserService()
    {
        var language = MicrotingDbContext!.Languages.OrderBy(x => x.Id).First();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        return userService;
    }

    private BackendConfigurationCalendarService BuildCalendarService(eFormCore.Core core)
    {
        var coreHelper = CoreHelper(core);
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), UserService(),
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            Substitute.For<IBackendConfigurationComplianceReportService>(),
            // The real membership service — this fixture is entirely about the
            // site → worker-tag expansion it owns, so a substitute would make every
            // assertion below vacuous.
            new WorkerTagMembershipService(coreHelper));
    }

    private BackendConfigurationComplianceReportService BuildComplianceReportService(eFormCore.Core core)
    {
        var coreHelper = CoreHelper(core);
        return new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), UserService(),
            BackendConfigurationPnDbContext!, coreHelper, ItemsPlanningPnDbContext!,
            NullLogger<BackendConfigurationComplianceReportService>.Instance,
            new WorkerTagMembershipService(coreHelper));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // One accessor per view. Each drives the view's own real entry point and
    // reduces the response to the AreaRulePlanning ids it rendered.
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HashSet<int>> WeekViewIds(int propertyId, List<int> siteIds)
    {
        var core = await SharedCore();
        var res = await BuildCalendarService(core).GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(WeekMonday),
            WeekEnd = IsoUtc(WeekMonday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [],
            TagNames = [],
            SiteIds = siteIds,
            WorkerTagIds = []
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Select(t => t.Id).ToHashSet();
    }

    private async Task<HashSet<int>> TaskListIds(int propertyId, List<int> siteIds)
    {
        var core = await SharedCore();
        var res = await BuildCalendarService(core).Index(new CalendarTaskIndexRequestModel
        {
            Filters = new CalendarTaskListFiltrationModel
            {
                PropertyIds = [propertyId],
                AssignToIds = siteIds
            }
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Select(t => t.Id).ToHashSet();
    }

    private async Task<HashSet<int>> TaskTrackerWebIds(int propertyId, List<int> workerIds)
    {
        var core = await SharedCore();
        var res = await BackendConfigurationTaskTrackerHelper.Index(
            new TaskTrackerFiltrationModel
            {
                PropertyIds = [propertyId],
                TagIds = [],
                WorkerIds = workerIds
            },
            BackendConfigurationPnDbContext!, core, 1, ItemsPlanningPnDbContext!,
            new WorkerTagMembershipService(CoreHelper(core)));
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Select(x => x.AreaRulePlanId).ToHashSet();
    }

    /// <summary>
    /// The gRPC/mobile list. Its filter is a single SDK site id (the calling worker's
    /// own), not a list — <c>EventsGrpcService</c> passes exactly one — so parity with
    /// the other four is only asserted for single-site filters.
    /// </summary>
    private async Task<HashSet<int>> TaskTrackerGrpcIds(int propertyId, int? sdkSiteId)
    {
        var core = await SharedCore();
        var res = await BuildCalendarService(core).GetTaskTrackerList(propertyId, sdkSiteId, 1);
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Select(t => t.Id).ToHashSet();
    }

    private async Task<HashSet<int>> ComplianceReportIds(int propertyId, List<int> siteIds)
    {
        var core = await SharedCore();
        var res = await BuildComplianceReportService(core).Index(new ComplianceReportRequestModel
        {
            PropertyId = propertyId,
            BoardIds = [],
            TagIds = [],
            SiteIds = siteIds,
            // "all" rather than the default "open": completion is irrelevant here and
            // the default would make the fixture depend on the seeded case status.
            Status = "all",
            DateFrom = WeekMonday,
            DateTo = WeekMonday.AddDays(6),
            PageSize = 0
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Entities
            .Where(e => e.AreaRulePlanningId.HasValue)
            .Select(e => e.AreaRulePlanningId!.Value)
            .ToHashSet();
    }

    /// <summary>
    /// Every view, for a single-site filter. The gRPC list is included because its
    /// one-site contract is satisfied.
    /// </summary>
    private async Task<Dictionary<string, HashSet<int>>> AllViews(int propertyId, int siteId) =>
        new()
        {
            ["calendar week view"] = await WeekViewIds(propertyId, [siteId]),
            ["calendar task list"] = await TaskListIds(propertyId, [siteId]),
            ["task tracker (web)"] = await TaskTrackerWebIds(propertyId, [siteId]),
            ["task tracker (gRPC)"] = await TaskTrackerGrpcIds(propertyId, siteId),
            ["compliance report"] = await ComplianceReportIds(propertyId, [siteId])
        };

    /// <summary>Every view, with no assignee filter at all.</summary>
    private async Task<Dictionary<string, HashSet<int>>> AllViewsUnfiltered(int propertyId) =>
        new()
        {
            ["calendar week view"] = await WeekViewIds(propertyId, []),
            ["calendar task list"] = await TaskListIds(propertyId, []),
            ["task tracker (web)"] = await TaskTrackerWebIds(propertyId, []),
            ["task tracker (gRPC)"] = await TaskTrackerGrpcIds(propertyId, null),
            ["compliance report"] = await ComplianceReportIds(propertyId, [])
        };

    // ─────────────────────────────────────────────────────────────────────────
    // 1 — THE REPRO, in every view
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// #1231 / #1232 / #1233 verbatim: assign an event to a team, then filter by one of
    /// that team's members. Pre-fix the <c>Does.Contain</c> assertion FAILS in the four
    /// broken views (the event has no PlanningSites row for the member, so their
    /// explicit-assignee match found nothing) and passes only on the week view, which
    /// #1212 had already fixed.
    ///
    /// <para>
    /// The non-member half is the positive control: it proves the event is found
    /// BECAUSE of the membership, and that the filter has not silently degraded into
    /// "return everything".
    /// </para>
    /// </summary>
    [Test]
    public async Task TeamAssignedEvent_IsVisibleToTeamMember_InEveryView()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        var nonMemberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        foreach (var (view, ids) in await AllViews(property.Id, memberSiteId))
        {
            Assert.That(ids, Does.Contain(teamEvent.ArpId),
                $"{view}: filtering by a member of the assigned team must return the "
                + "team's event — pre-fix it had no PlanningSites row and vanished");
        }

        foreach (var (view, ids) in await AllViews(property.Id, nonMemberSiteId))
        {
            Assert.That(ids, Does.Not.Contain(teamEvent.ArpId),
                $"{view}: positive control — a site outside the team must NOT see the "
                + "team's event");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2 — individually-assigned events are unaffected
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes on pre-fix code. Named-individual assignment is the path every
    /// view already handled; this locks it against the new OR branch. A fix that
    /// widened the site match, or that started REQUIRING a worker tag, breaks here.
    /// </summary>
    [Test]
    public async Task Tripwire_IndividuallyAssignedEvent_Unaffected_InEveryView()
    {
        var property = await SeedProperty();
        var assignedSiteId = await SeedSdkSite();
        var otherSiteId = await SeedSdkSite();

        var individualEvent = await SeedEvent(property.Id);
        await AssignSite(individualEvent, assignedSiteId);

        var teamTagId = await SeedSdkWorkerTag();
        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        foreach (var (view, ids) in await AllViews(property.Id, assignedSiteId))
        {
            Assert.That(ids, Does.Contain(individualEvent.ArpId),
                $"{view}: an explicitly assigned site must still find its own event");
            Assert.That(ids, Does.Not.Contain(teamEvent.ArpId),
                $"{view}: a site that belongs to no team must not pick up team-assigned events");
        }

        foreach (var (view, ids) in await AllViews(property.Id, otherSiteId))
        {
            Assert.That(ids, Does.Not.Contain(individualEvent.ArpId),
                $"{view}: an unrelated site must not see the individually-assigned event");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3 — no filter at all
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes on pre-fix code. Guards the "empty or absent filter reproduces
    /// today's results exactly" requirement: with no assignee filter the tag expansion
    /// must not run at all, so a team-assigned event and an individually-assigned one
    /// both render in every view.
    /// </summary>
    [Test]
    public async Task Tripwire_NoAssigneeFilter_ReturnsEverything_InEveryView()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        await LinkSiteToTag(teamTagId, await SeedSdkSite());

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        var individualEvent = await SeedEvent(property.Id);
        await AssignSite(individualEvent, await SeedSdkSite());

        foreach (var (view, ids) in await AllViewsUnfiltered(property.Id))
        {
            Assert.That(ids, Does.Contain(teamEvent.ArpId), $"{view}: team event");
            Assert.That(ids, Does.Contain(individualEvent.ArpId), $"{view}: individual event");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4 — removed SiteTag membership does not expand
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removing a worker from a team soft-deletes the <c>SiteTag</c> row and nothing
    /// else, so a former member must stop matching the team's events. Without the
    /// <c>WorkflowState != Removed</c> clause on the SiteTags read, the first assertion
    /// fails in every fixed view.
    /// </summary>
    [Test]
    public async Task RemovedTeamMembership_DoesNotExpand_InEveryView()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var currentMemberId = await SeedSdkSite();
        var formerMemberId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, currentMemberId);
        await LinkSiteToTag(teamTagId, formerMemberId, removed: true);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        foreach (var (view, ids) in await AllViews(property.Id, formerMemberId))
        {
            Assert.That(ids, Does.Not.Contain(teamEvent.ArpId),
                $"{view}: a soft-deleted SiteTag row must not make a former member match "
                + "the team's events");
        }

        foreach (var (view, ids) in await AllViews(property.Id, currentMemberId))
        {
            Assert.That(ids, Does.Contain(teamEvent.ArpId),
                $"{view}: control — a live team member must match, or the assertion above "
                + "proved nothing");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5 — a soft-deleted Site does not expand
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Core.SiteDelete</c> soft-removes the <c>Site</c> and leaves its
    /// <c>SiteTags</c> rows behind entirely, and it does NOT set <c>Worker.Resigned</c>,
    /// so this is a different exclusion from test 6 and neither clause implies the
    /// other. Neither site here has a <c>SiteWorker</c> at all, which pins that.
    /// Without the <c>Site.WorkflowState != Removed</c> clause the first assertion fails.
    /// </summary>
    [Test]
    public async Task SoftDeletedSite_DoesNotExpand_InEveryView()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var liveMemberId = await SeedSdkSite();
        var deletedMemberId = await SeedSdkSite(removed: true);

        // Both SiteTags rows stay Created — that is exactly what SiteDelete leaves.
        await LinkSiteToTag(teamTagId, liveMemberId);
        await LinkSiteToTag(teamTagId, deletedMemberId);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        foreach (var (view, ids) in await AllViews(property.Id, deletedMemberId))
        {
            Assert.That(ids, Does.Not.Contain(teamEvent.ArpId),
                $"{view}: a soft-deleted Site must not resolve to the team its surviving "
                + "SiteTags row still points at");
        }

        foreach (var (view, ids) in await AllViews(property.Id, liveMemberId))
        {
            Assert.That(ids, Does.Contain(teamEvent.ArpId),
                $"{view}: control — the live member of the same team matches");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6 — a resigned worker does not expand
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Team membership ends when the member resigns, so a site whose SDK
    /// <c>Worker.Resigned</c> is set no longer resolves to the tags its stale
    /// <c>SiteTags</c> rows still point at.
    ///
    /// <para>
    /// The explicit half of the OR is deliberately NOT changed: a named assignment to a
    /// since-resigned worker is a fact about that event and still matches. The second
    /// assertion pins that asymmetry as a recorded choice rather than an oversight.
    /// </para>
    /// </summary>
    [Test]
    public async Task ResignedTeamMember_DoesNotExpand_InEveryView()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();

        // Workers.Resigned only exists after the Core has migrated the SDK schema, so
        // seed through a context the Core handed out, not MicrotingDbContext.
        var core = await SharedCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var resignedMemberId = await SeedSdkSiteWithWorker(sdkDbContext, resigned: true);
        var activeMemberId = await SeedSdkSiteWithWorker(sdkDbContext, resigned: false);
        await LinkSiteToTag(teamTagId, resignedMemberId);
        await LinkSiteToTag(teamTagId, activeMemberId);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        // An event assigned to the resigned worker BY NAME — the untouched half of the OR.
        var explicitEvent = await SeedEvent(property.Id);
        await AssignSite(explicitEvent, resignedMemberId);

        foreach (var (view, ids) in await AllViews(property.Id, resignedMemberId))
        {
            Assert.That(ids, Does.Not.Contain(teamEvent.ArpId),
                $"{view}: a resigned worker is no longer a team member, so their site must "
                + "not resolve to the team's tag");
            Assert.That(ids, Does.Contain(explicitEvent.ArpId),
                $"{view}: the explicit half of the OR is unchanged — an event assigned to "
                + "this person by name still matches, resigned or not");
        }

        foreach (var (view, ids) in await AllViews(property.Id, activeMemberId))
        {
            Assert.That(ids, Does.Contain(teamEvent.ArpId),
                $"{view}: control — an employed member of the same team still matches");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7 — CROSS-VIEW PARITY: no view may show more than the others
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The actual deliverable. One dataset holding all four assignment shapes the fix
    /// has to get right, then the SAME filter through all five views, asserting they
    /// agree exactly rather than asserting each against a hand-written expectation.
    ///
    /// <para>
    /// Every filter is checked against the union of what the views returned, so a view
    /// that shows MORE and a view that shows LESS both fail, and the failure message
    /// names the view and the ids it disagreed on.
    /// </para>
    ///
    /// <para>
    /// Pre-fix this fails on the very first filter: the week view (fixed by #1212)
    /// returns the team event for a member and the other four do not.
    /// </para>
    /// </summary>
    [Test]
    public async Task CrossViewParity_SameFilter_SameVisibleEvents()
    {
        var property = await SeedProperty();

        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        var secondMemberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);
        await LinkSiteToTag(teamTagId, secondMemberSiteId);

        var formerMemberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, formerMemberSiteId, removed: true);

        var individualSiteId = await SeedSdkSite();
        var unrelatedSiteId = await SeedSdkSite();

        // (a) assigned to the team only
        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        // (b) assigned to one named individual only
        var individualEvent = await SeedEvent(property.Id);
        await AssignSite(individualEvent, individualSiteId);

        // (c) both: a named individual AND the team
        var mixedEvent = await SeedEvent(property.Id);
        await AssignSite(mixedEvent, individualSiteId);
        await AssignWorkerTag(mixedEvent, teamTagId);

        // (d) assigned to somebody who is in neither
        var unrelatedEvent = await SeedEvent(property.Id);
        await AssignSite(unrelatedEvent, unrelatedSiteId);

        var seeded = new[]
        {
            teamEvent.ArpId, individualEvent.ArpId, mixedEvent.ArpId, unrelatedEvent.ArpId
        };

        foreach (var siteId in new[]
                 {
                     memberSiteId, secondMemberSiteId, formerMemberSiteId,
                     individualSiteId, unrelatedSiteId
                 })
        {
            var byView = await AllViews(property.Id, siteId);

            // Scope to this test's own events: the fixture reuses one database and the
            // two task-tracker views have no date window.
            var scoped = byView.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Where(seeded.Contains).OrderBy(x => x).ToList());

            var reference = scoped["calendar week view"];
            foreach (var (view, ids) in scoped)
            {
                Assert.That(ids, Is.EqualTo(reference),
                    $"filtering by site {siteId}: '{view}' returned {Fmt(ids)} while the "
                    + $"calendar week view returned {Fmt(reference)}. No view may show more "
                    + "or fewer events than another for the same assignee filter.");
            }
        }

        // The parity assertion above is satisfied by five views that all return NOTHING,
        // and equally by five views that agree on the same WRONG answer, so pin what the
        // shared answer actually IS. One absolute pin per filter is enough for all five
        // views: the loop above has already asserted every view equals the week view, so
        // pinning ONE of them pins the lot. The compliance report is the one pinned,
        // arbitrarily but consistently — any of the five would do.
        var byMember = await ComplianceReportIds(property.Id, [memberSiteId]);
        Assert.That(byMember, Does.Contain(teamEvent.ArpId));
        Assert.That(byMember, Does.Contain(mixedEvent.ArpId));
        Assert.That(byMember, Does.Not.Contain(individualEvent.ArpId));
        Assert.That(byMember, Does.Not.Contain(unrelatedEvent.ArpId));

        var byIndividual = await ComplianceReportIds(property.Id, [individualSiteId]);
        Assert.That(byIndividual, Does.Contain(individualEvent.ArpId));
        Assert.That(byIndividual, Does.Contain(mixedEvent.ArpId));
        Assert.That(byIndividual, Does.Not.Contain(teamEvent.ArpId));
        Assert.That(byIndividual, Does.Not.Contain(unrelatedEvent.ArpId));

        // The SECOND live member of the same team — a filter that reaches its events
        // ONLY through the tag expansion, since it has no PlanningSites row anywhere.
        // Its expectation is identical to memberSiteId's, which is the point: five
        // views agreeing on the wrong answer for it would otherwise pass.
        var bySecondMember = await ComplianceReportIds(property.Id, [secondMemberSiteId]);
        Assert.That(bySecondMember, Does.Contain(teamEvent.ArpId));
        Assert.That(bySecondMember, Does.Contain(mixedEvent.ArpId));
        Assert.That(bySecondMember, Does.Not.Contain(individualEvent.ArpId));
        Assert.That(bySecondMember, Does.Not.Contain(unrelatedEvent.ArpId));

        // The former member: a soft-deleted SiteTag row and no PlanningSites row
        // anywhere, so it is assigned to nothing at all and must see NONE of the four.
        // Without this the parity loop would accept "the former member sees the team's
        // event" as long as all five views said it together.
        var byFormerMember = await ComplianceReportIds(property.Id, [formerMemberSiteId]);
        Assert.That(byFormerMember, Does.Not.Contain(teamEvent.ArpId));
        Assert.That(byFormerMember, Does.Not.Contain(mixedEvent.ArpId));
        Assert.That(byFormerMember, Does.Not.Contain(individualEvent.ArpId));
        Assert.That(byFormerMember, Does.Not.Contain(unrelatedEvent.ArpId));
    }

    private static string Fmt(IEnumerable<int> ids) => $"[{string.Join(", ", ids)}]";

    // ─────────────────────────────────────────────────────────────────────────
    // 8 — the compliance report's worker column populates for a tag-assigned row
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The second half of #1232. <c>siteIdsByArpId</c> was built solely from
    /// <c>detail.PlanningSites</c>, so a tag-assigned row survived the filter with an
    /// EMPTY worker column — the report would show the user a row for a team they had
    /// just filtered to, listing nobody.
    ///
    /// <para>
    /// Only LIVE members appear: the removed-membership site is seeded precisely so the
    /// column cannot be satisfied by dumping every <c>SiteTags</c> row into it.
    /// </para>
    ///
    /// <para>
    /// The column is <c>WorkerNames</c>. <c>WorkerSiteIds</c> is asserted EMPTY in the
    /// same test on purpose: it is the row's EXPLICIT INDIVIDUAL assignment, not its
    /// worker column, and it must stay the ARP's own <c>PlanningSites</c> — the
    /// frontend hands it to the complete-event modal as <c>assigneeIds</c>, where a
    /// single id pre-selects the completing worker. This assertion is what stops a
    /// later "the two fields should surely match" edit.
    /// </para>
    ///
    /// <para>
    /// #1236 answered the product question this left open by adding a SECOND field,
    /// <c>TeamAssigneeIds</c>, rather than widening this one: the modal now groups team
    /// members under "assigned to this event" and still pre-selects from
    /// <c>WorkerSiteIds</c> alone. See section 11 below.
    /// </para>
    /// </summary>
    [Test]
    public async Task ComplianceReport_WorkerColumn_PopulatesFromTeamMembership()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        var formerMemberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);
        await LinkSiteToTag(teamTagId, formerMemberSiteId, removed: true);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        var core = await SharedCore();
        var res = await BuildComplianceReportService(core).Index(new ComplianceReportRequestModel
        {
            PropertyId = property.Id,
            Status = "all",
            DateFrom = WeekMonday,
            DateTo = WeekMonday.AddDays(6),
            PageSize = 0
        });
        Assert.That(res.Success, Is.True, res.Message);

        var row = res.Model!.Entities.Single(e => e.AreaRulePlanningId == teamEvent.ArpId);

        var memberName = await MicrotingDbContext!.Sites
            .Where(s => s.Id == memberSiteId)
            .Select(s => s.Name)
            .FirstAsync();
        var formerMemberName = await MicrotingDbContext.Sites
            .Where(s => s.Id == formerMemberSiteId)
            .Select(s => s.Name)
            .FirstAsync();

        Assert.That(row.WorkerNames, Does.Contain(memberName),
            "a live team member must appear in the worker column of a tag-assigned row");
        Assert.That(row.WorkerNames, Does.Not.Contain(formerMemberName),
            "a soft-deleted SiteTag row must not put a former member in the worker column");

        // The ASSIGNMENT, not the column: PlanningSites only, and this ARP has none.
        Assert.That(row.WorkerSiteIds, Is.Empty,
            "WorkerSiteIds is the row's PlanningSites assignment, which the complete-event "
            + "modal pre-selects from — a worker-tag member must NOT be written into it");
    }

    /// <summary>
    /// Tripwire for the same enrichment — passes on pre-fix code. An
    /// individually-assigned row's worker column must be exactly what it was: the
    /// explicit PlanningSites rows, and nothing appended.
    /// </summary>
    [Test]
    public async Task Tripwire_ComplianceReport_WorkerColumn_UnchangedForIndividualAssignment()
    {
        var property = await SeedProperty();
        var assignedSiteId = await SeedSdkSite();

        var individualEvent = await SeedEvent(property.Id);
        await AssignSite(individualEvent, assignedSiteId);

        var core = await SharedCore();
        var res = await BuildComplianceReportService(core).Index(new ComplianceReportRequestModel
        {
            PropertyId = property.Id,
            Status = "all",
            DateFrom = WeekMonday,
            DateTo = WeekMonday.AddDays(6),
            PageSize = 0
        });
        Assert.That(res.Success, Is.True, res.Message);

        var row = res.Model!.Entities.Single(e => e.AreaRulePlanningId == individualEvent.ArpId);
        Assert.That(row.WorkerSiteIds, Is.EqualTo(new List<int> { assignedSiteId }));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9 — the web Task Tracker's worker column, same defect, same fix
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Task Tracker's <c>WorkerNames</c>/<c>WorkerIds</c> were projected from the
    /// items-planning <c>PlanningSites</c> rows alone, so a team-assigned row that
    /// survived the widened filter would have listed NOBODY — the screen would answer a
    /// filter on a person with a row that does not name them.
    ///
    /// <para>
    /// This covers the Excel export as well: <c>GenerateExcelReport</c> runs the same
    /// helper and <c>ExcelService</c> writes <c>string.Join(", ", WorkerNames)</c> off
    /// these very rows, so there is one projection behind both surfaces and no separate
    /// export assertion to make.
    /// </para>
    ///
    /// <para>
    /// Only LIVE members appear: the removed-membership site is seeded precisely so the
    /// column cannot be satisfied by dumping every <c>SiteTags</c> row into it.
    /// </para>
    /// </summary>
    [Test]
    public async Task TaskTracker_WorkerColumn_PopulatesFromTeamMembership()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        var formerMemberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);
        await LinkSiteToTag(teamTagId, formerMemberSiteId, removed: true);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        var row = await TaskTrackerRow(property.Id, teamEvent.ArpId);

        Assert.That(row.WorkerIds, Does.Contain(memberSiteId),
            "a live team member must appear in the Task Tracker's Workers column for a "
            + "tag-assigned row");
        Assert.That(row.WorkerIds, Does.Not.Contain(formerMemberSiteId),
            "a soft-deleted SiteTag row must not put a former member in the column");

        var memberName = await MicrotingDbContext!.Sites
            .Where(s => s.Id == memberSiteId)
            .Select(s => s.Name)
            .FirstAsync();
        Assert.That(row.WorkerNames, Does.Contain(memberName),
            "WorkerNames feeds both the grid and the Excel export and must resolve the "
            + "member's name");
    }

    /// <summary>
    /// Tripwire — passes on pre-fix code. An individually-assigned row's Workers column
    /// must be exactly what it was: the explicit PlanningSites ids, nothing appended.
    /// </summary>
    [Test]
    public async Task Tripwire_TaskTracker_WorkerColumn_UnchangedForIndividualAssignment()
    {
        var property = await SeedProperty();
        var assignedSiteId = await SeedSdkSite();

        var individualEvent = await SeedEvent(property.Id);
        await AssignSite(individualEvent, assignedSiteId);

        // A team the assigned site is NOT in, carried by a DIFFERENT event: proves the
        // union is per-event and does not leak the page's other teams into this row.
        var teamTagId = await SeedSdkWorkerTag();
        await LinkSiteToTag(teamTagId, await SeedSdkSite());
        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        var row = await TaskTrackerRow(property.Id, individualEvent.ArpId);

        Assert.That(row.WorkerIds, Is.EqualTo(new List<int> { assignedSiteId }));

        var assignedName = await MicrotingDbContext!.Sites
            .Where(s => s.Id == assignedSiteId)
            .Select(s => s.Name)
            .FirstAsync();
        Assert.That(row.WorkerNames, Is.EqualTo(new List<string> { assignedName }));
    }

    /// <summary>
    /// The web Task Tracker row for one seeded event, unfiltered by worker so the row
    /// is reached on its own merits rather than by the filter under test.
    /// </summary>
    private async Task<TaskTrackerModel> TaskTrackerRow(int propertyId, int arpId)
    {
        var core = await SharedCore();
        var res = await BackendConfigurationTaskTrackerHelper.Index(
            new TaskTrackerFiltrationModel
            {
                PropertyIds = [propertyId],
                TagIds = [],
                WorkerIds = []
            },
            BackendConfigurationPnDbContext!, core, 1, ItemsPlanningPnDbContext!,
            new WorkerTagMembershipService(CoreHelper(core)));
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Single(x => x.AreaRulePlanId == arpId);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // 11 — #1236: the team half of the assignment, carried BESIDE the narrow one
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The compliance-report row of a team-assigned event carries its team's live
    /// members in <c>TeamAssigneeIds</c> while <c>WorkerSiteIds</c> stays the ARP's own
    /// (here empty) PlanningSites. The frontend's complete-event modal groups on the
    /// union of the two and pre-selects from the narrow one alone, so this pair is what
    /// makes "team members are assigned, but nobody is auto-selected" true.
    ///
    /// <para>
    /// The three exclusions are asserted through the model rather than re-derived:
    /// membership belongs to <c>IWorkerTagMembershipService</c> and each of these sites
    /// is excluded by a different clause of its rule — a soft-deleted <c>SiteTag</c>, a
    /// soft-deleted <c>Site</c>, and a <c>Worker</c> with <c>Resigned</c> set.
    /// </para>
    /// </summary>
    [Test]
    public async Task ComplianceReport_TeamAssigneeIds_HoldsLiveMembersOnly()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();

        // Workers.Resigned only exists after the Core has migrated the SDK schema.
        var core = await SharedCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var memberSiteId = await SeedSdkSiteWithWorker(sdkDbContext, resigned: false);
        var formerMemberSiteId = await SeedSdkSite();
        var removedSiteId = await SeedSdkSite(removed: true);
        var resignedMemberSiteId = await SeedSdkSiteWithWorker(sdkDbContext, resigned: true);

        await LinkSiteToTag(teamTagId, memberSiteId);
        await LinkSiteToTag(teamTagId, formerMemberSiteId, removed: true);
        await LinkSiteToTag(teamTagId, removedSiteId);
        await LinkSiteToTag(teamTagId, resignedMemberSiteId);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        var row = await ComplianceReportRow(property.Id, teamEvent.ArpId);

        Assert.That(row.TeamAssigneeIds, Is.EquivalentTo(new[] { memberSiteId }),
            "TeamAssigneeIds is the team's LIVE members: the removed SiteTag, the removed "
            + "Site and the resigned Worker are each excluded by the shared membership rule");
        Assert.That(row.WorkerSiteIds, Is.Empty,
            "WorkerSiteIds must stay the ARP's own PlanningSites — it is the only set the "
            + "complete-event modal pre-selects a completer from (#1236)");
    }

    /// <summary>
    /// Tripwire. An individually-assigned row is untouched by #1236: the explicit id in
    /// <c>WorkerSiteIds</c> and an EMPTY team half, so the modal groups and pre-selects
    /// exactly as it did.
    /// </summary>
    [Test]
    public async Task Tripwire_ComplianceReport_IndividualAssignment_HasNoTeamHalf()
    {
        var property = await SeedProperty();
        var assignedSiteId = await SeedSdkSite();

        var individualEvent = await SeedEvent(property.Id);
        await AssignSite(individualEvent, assignedSiteId);

        var row = await ComplianceReportRow(property.Id, individualEvent.ArpId);

        Assert.That(row.WorkerSiteIds, Is.EqualTo(new List<int> { assignedSiteId }));
        Assert.That(row.TeamAssigneeIds, Is.Empty,
            "an event with no worker tag has an empty team half — never null");
    }

    /// <summary>
    /// The calendar grid's half of the same pair, and the reason both callers had to
    /// change together: the week view feeds the SAME modal, so its
    /// <c>AssigneeIds</c>/<c>TeamAssigneeIds</c> split must match the compliance
    /// report's <c>WorkerSiteIds</c>/<c>TeamAssigneeIds</c> split for one event. Two
    /// views disagreeing about who is assigned is the defect this batch removes.
    ///
    /// <para>
    /// The event is assigned to a team AND to one named individual, so the assertion
    /// can see both halves at once and can see that they are not merged.
    /// </para>
    ///
    /// <para>
    /// <b>The named individual is ALSO a member of the team</b>, and that is the point of
    /// the seed rather than an accident of it. The two halves are not a partition: a site
    /// that is both must appear in BOTH, because the client unions them and pre-selects
    /// from the explicit half alone. Both producers say so in prose
    /// (<c>CalendarTaskResponseModel.TeamAssigneeIds</c> and the compliance report's
    /// <c>WorkerSiteSets.TeamSiteIds</c>) and they are two INDEPENDENT implementations,
    /// so without this overlap seeded, a later "tidy-up" that subtracted the explicit half
    /// out of the team half in one of them would break nothing here.
    /// </para>
    ///
    /// <para>
    /// Team-half assertions are order-INDEPENDENT on purpose: within one tag the members
    /// come out of a <c>HashSet</c>, so only tag-granularity ordering is specified.
    /// </para>
    /// </summary>
    [Test]
    public async Task WeekView_And_ComplianceReport_AgreeOnBothHalvesOfTheAssignment()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        var formerMemberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);
        await LinkSiteToTag(teamTagId, formerMemberSiteId, removed: true);

        // Explicitly assigned AND a live member of the same team — the overlap case.
        var namedSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, namedSiteId);

        var mixedEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(mixedEvent, teamTagId);
        await AssignSite(mixedEvent, namedSiteId);

        var tasks = await WeekViewTasks(property.Id, mixedEvent.ArpId);
        Assert.That(tasks, Is.Not.Empty,
            "the seeded event must render in the week view for this assertion to mean anything");

        foreach (var task in tasks)
        {
            Assert.That(task.AssigneeIds, Is.EqualTo(new List<int> { namedSiteId }),
                "AssigneeIds stays the explicit individual assignment");
            Assert.That(task.TeamAssigneeIds, Is.EquivalentTo(new[] { memberSiteId, namedSiteId }),
                "TeamAssigneeIds is the team's live members, and the removed membership "
                + "is not among them");
            Assert.That(task.TeamAssigneeIds, Does.Contain(namedSiteId),
                "a site that is both an explicit assignee and a live team member belongs "
                + "in BOTH halves — they are unioned by the client, not partitioned");
        }

        var row = await ComplianceReportRow(property.Id, mixedEvent.ArpId);
        Assert.That(row.WorkerSiteIds, Is.EqualTo(new List<int> { namedSiteId }));
        Assert.That(row.TeamAssigneeIds, Is.EquivalentTo(new[] { memberSiteId, namedSiteId }));
        Assert.That(row.TeamAssigneeIds, Does.Contain(namedSiteId),
            "the compliance report is the second, INDEPENDENT implementation of the same "
            + "rule: it must not subtract WorkerSiteIds out of the team half either");

        // The pair the two views hand the modal, compared directly rather than each
        // against a literal — the requirement is that the views AGREE. Grouping is the
        // union of the two halves; the pre-select reads the narrow half only, which is
        // why the halves stay apart.
        Assert.That(row.WorkerSiteIds.OrderBy(x => x).ToList(),
            Is.EqualTo(tasks[0].AssigneeIds.OrderBy(x => x).ToList()),
            "the two views must agree on the explicit half");
        Assert.That(row.TeamAssigneeIds.OrderBy(x => x).ToList(),
            Is.EqualTo(tasks[0].TeamAssigneeIds.OrderBy(x => x).ToList()),
            "the two views must agree on the team half");
    }

    /// <summary>
    /// The gRPC/mobile task tracker builds <c>CalendarTaskResponseModel</c> too, from
    /// its own worker-tag dictionary. Pinned so the third producer cannot be left
    /// behind the two the modal reads from.
    /// </summary>
    [Test]
    public async Task TaskTrackerGrpcList_CarriesTheTeamHalfToo()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        var core = await SharedCore();
        var res = await BuildCalendarService(core).GetTaskTrackerList(property.Id, null, 1);
        Assert.That(res.Success, Is.True, res.Message);

        var task = res.Model!.Single(t => t.Id == teamEvent.ArpId);
        Assert.That(task.TeamAssigneeIds, Is.EquivalentTo(new[] { memberSiteId }));
        Assert.That(task.AssigneeIds, Is.Empty);
    }

    /// <summary>
    /// The calendar task list (<c>Index</c>) is the fourth producer. It has no date
    /// window, so it is queried by property alone.
    /// </summary>
    [Test]
    public async Task CalendarTaskList_CarriesTheTeamHalfToo()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);

        var teamEvent = await SeedEvent(property.Id);
        await AssignWorkerTag(teamEvent, teamTagId);

        var core = await SharedCore();
        var res = await BuildCalendarService(core).Index(new CalendarTaskIndexRequestModel
        {
            Filters = new CalendarTaskListFiltrationModel
            {
                PropertyIds = [property.Id],
                AssignToIds = []
            }
        });
        Assert.That(res.Success, Is.True, res.Message);

        var task = res.Model!.Single(t => t.Id == teamEvent.ArpId);
        Assert.That(task.TeamAssigneeIds, Is.EquivalentTo(new[] { memberSiteId }));
        Assert.That(task.AssigneeIds, Is.Empty);
    }

    /// <summary>
    /// One seeded event's compliance-report row, unfiltered by worker so the row is
    /// reached on its own merits rather than by a filter.
    /// </summary>
    private async Task<ComplianceReportRowModel> ComplianceReportRow(int propertyId, int arpId)
    {
        var core = await SharedCore();
        var res = await BuildComplianceReportService(core).Index(new ComplianceReportRequestModel
        {
            PropertyId = propertyId,
            BoardIds = [],
            TagIds = [],
            SiteIds = [],
            Status = "all",
            DateFrom = WeekMonday,
            DateTo = WeekMonday.AddDays(6),
            PageSize = 0
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Entities.Single(e => e.AreaRulePlanningId == arpId);
    }

    /// <summary>
    /// Every week-view task belonging to one seeded event. A seeded event renders on
    /// BOTH paths of <c>GetTasksForWeek</c> — the weekly recurrence series and the
    /// Wednesday compliance row — so this returns a list and the caller asserts over
    /// all of them, which is what pins the two paths to the same answer.
    /// </summary>
    private async Task<List<CalendarTaskResponseModel>> WeekViewTasks(int propertyId, int arpId)
    {
        var core = await SharedCore();
        var res = await BuildCalendarService(core).GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(WeekMonday),
            WeekEnd = IsoUtc(WeekMonday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [],
            TagNames = [],
            SiteIds = [],
            WorkerTagIds = []
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Where(t => t.Id == arpId).ToList();
    }
}
