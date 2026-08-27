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
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// DB-backed integration coverage for
/// <see cref="BackendConfigurationCalendarService.PrepareComplete"/> — the
/// combined-complete-modal's "resolve, but do NOT complete" step. It
/// deliberately duplicates <c>ToggleComplete</c>'s resolution logic (existing
/// Compliance lookup, on-demand materialisation via
/// <see cref="IEventDeployService.EnsureComplianceForOccurrenceAsync"/>, and
/// the EventStart triple: Compliance.Deadline day + CalendarOccurrenceException
/// StartHour override ?? CalendarConfiguration.StartHour ?? 9.0), so this file
/// pins the same shapes WITHOUT ever touching Case.Status or removing the
/// Compliance row.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarPrepareCompleteTests : TestBaseSetup
{
    // A minimal real eForm — content is irrelevant to PrepareComplete (it
    // never inspects mandatory fields), we just need a real CheckListId to
    // back the SDK Case. Copied from EventDeployServiceTest.CommentTemplateXml.
    private const string CommentTemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9060</Id>
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
            <Id>9060</Id>
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
                    <Id>73660</Id>
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

    private static string ExpectedIso(DateTime deadlineDate, double hour)
    {
        var whole = (int)Math.Floor(hour);
        var minute = (int)Math.Round((hour - whole) * 60);
        return DateTime.SpecifyKind(deadlineDate.Date, DateTimeKind.Utc)
            .AddHours(whole).AddMinutes(minute)
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    private sealed class Scenario
    {
        public BackendConfigurationCalendarService Service = null!;
        public AreaRulePlanning Arp = null!;
        public Property Property = null!;
        public Area Area = null!;
        public Planning Planning = null!;
        public Site SdkSite = null!;
        public int TemplateId;
        public eFormCore.Core Core = null!;
    }

    /// <summary>
    /// Seeds a full Area → Property → AreaRule → Planning → AreaRulePlanning
    /// graph plus a real SDK site/language/eForm template, and builds a
    /// <see cref="BackendConfigurationCalendarService"/> against it. The
    /// deployed site is wired both as a (BC) PlanningSite of the ARP — the
    /// source PrepareComplete's on-demand branch reads
    /// (<c>arp.PlanningSites.FirstOrDefault</c>) — and as an active
    /// PropertyWorker, so the deploy pipeline's leak guard
    /// (EventDeployService.cs:497-547) never blocks materialisation.
    ///
    /// When <paramref name="useRealEventDeployService"/> is true the service
    /// is wired to a REAL <see cref="EventDeployService"/> (needed for the
    /// on-demand-materialisation scenarios); otherwise it gets a mock that is
    /// never expected to be reached (existing-compliance / early-failure
    /// scenarios).
    /// </summary>
    private async Task<Scenario> SeedGraphAsync(
        string tag,
        int microtingUid,
        DateTime arpStartDate,
        bool useRealEventDeployService,
        int? areaRuleEformIdOverride = null)
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // The per-test SQL fixture reset predates the calendar tables: it
        // drops/recreates AreaRulePlannings (auto-increment restarts at 1)
        // but never mentions CalendarConfigurations / CalendarOccurrenceExceptions,
        // so those tables SURVIVE across tests within this fixture. A StartHour
        // row created by an earlier test would silently attach to this test's
        // freshly-seeded ARP (which reuses the same id). Clear them explicitly.
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM CalendarConfigurations");
        await BackendConfigurationPnDbContext.Database
            .ExecuteSqlRawAsync("DELETE FROM CalendarOccurrenceExceptions");

        var template = await core.TemplateFromXml(CommentTemplateXml);
        var templateId = await core.TemplateCreate(template);

        var sdkSite = new Site
        {
            Name = $"prepare-complete-{tag}",
            MicrotingUid = microtingUid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"PrepareComplete-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id,
            EformId = areaRuleEformIdOverride ?? templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = arpStartDate,
            RelatedEFormId = templateId, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = arpStartDate, Status = true,
            RepeatType = 1, RepeatEvery = 1, DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planningSite = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(planningSite);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = property.Id, WorkerId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Reload with the Includes PrepareComplete itself requires.
        arp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .Include(x => x.AreaRule)
            .Include(x => x.PlanningSites)
            .FirstAsync(x => x.Id == arp.Id);

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));

        IEventDeployService eventDeployService;
        if (useRealEventDeployService)
        {
            var calendarMock = Substitute.For<IBackendConfigurationCalendarService>();
            var services = new ServiceCollection();
            services.AddSingleton(calendarMock);
            var sp = services.BuildServiceProvider();
            eventDeployService = new EventDeployService(
                BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, coreHelper, sp,
                NullLogger<EventDeployService>.Instance);
        }
        else
        {
            eventDeployService = Substitute.For<IEventDeployService>();
        }

        var service = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext, coreHelper, eventDeployService,
            ItemsPlanningPnDbContext, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            NullLogger<BackendConfigurationCalendarService>.Instance);

        return new Scenario
        {
            Service = service, Arp = arp, Property = property, Area = area,
            Planning = planning, SdkSite = sdkSite, TemplateId = templateId, Core = core
        };
    }

    /// <summary>Creates and persists a live Compliance row backed by a real SDK Case.</summary>
    private async Task<(Compliance Compliance, Case SdkCase)> SeedComplianceAsync(
        Scenario s, DateTime deadlineDate, int caseStatus = 66)
    {
        var sdkCase = new Case
        {
            SiteId = s.SdkSite.Id,
            CheckListId = s.TemplateId,
            Status = caseStatus,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            PlanningId = s.Planning.Id, PropertyId = s.Property.Id, AreaId = s.Area.Id,
            Deadline = deadlineDate.Date, StartDate = deadlineDate.Date.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = s.TemplateId,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (compliance, sdkCase);
    }

    // ------------------------------------------------------------------
    // 1. Existing Compliance row → resolves it without any side effects.
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_ExistingCompliance_ReturnsIdsAndDoesNotComplete()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(5);
        var s = await SeedGraphAsync("existing", 5001, deadline.AddDays(-14), useRealEventDeployService: false);

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = s.Arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var (compliance, sdkCase) = await SeedComplianceAsync(s, deadline);

        var result = await s.Service.PrepareComplete(
            s.Arp.Id, compliance.Id, deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Model!.SdkCaseId, Is.EqualTo(sdkCase.Id));
            Assert.That(result.Model.TemplateId, Is.EqualTo(s.TemplateId));
            Assert.That(result.Model.ComplianceId, Is.EqualTo(compliance.Id));
            Assert.That(result.Model.PropertyId, Is.EqualTo(s.Property.Id));
            Assert.That(result.Model.AssignedSiteId, Is.EqualTo(sdkCase.SiteId));
            Assert.That(result.Model.Deadline, Is.EqualTo(ExpectedIso(deadline, 0.0)));
            Assert.That(result.Model.EventStart, Is.EqualTo(ExpectedIso(deadline, 9.0)));
        });

        // CRITICAL invariant — nothing was completed.
        var reloadedCase = await MicrotingDbContext!.Cases.AsNoTracking().FirstAsync(x => x.Id == sdkCase.Id);
        var reloadedCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == compliance.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloadedCase.Status, Is.EqualTo(66), "SDK case Status must be untouched (not completed)");
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "Compliance row must remain live (not removed)");
        });
    }

    // ------------------------------------------------------------------
    // 2. No Compliance row → materialises on demand; nothing completed.
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_NoCompliance_MaterialisesOnDemand()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(3);
        var s = await SeedGraphAsync("ondemand", 5002, deadline.AddDays(-14), useRealEventDeployService: true);

        var beforeCount = await BackendConfigurationPnDbContext!.Compliances.CountAsync();

        var result = await s.Service.PrepareComplete(
            s.Arp.Id, null, deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Model!.SdkCaseId, Is.GreaterThan(0));
            Assert.That(result.Model.ComplianceId, Is.GreaterThan(0));
            Assert.That(result.Model.TemplateId, Is.Not.Null.And.GreaterThan(0));
            Assert.That(result.Model.PropertyId, Is.EqualTo(s.Property.Id));
            Assert.That(result.Model.EventStart, Is.EqualTo(ExpectedIso(deadline, 9.0)));
        });

        var afterCount = await BackendConfigurationPnDbContext.Compliances.CountAsync();
        Assert.That(afterCount, Is.EqualTo(beforeCount + 1), "exactly one Compliance row must be materialised");

        // Nothing completed: the freshly materialised case/compliance are untouched.
        var newCase = await MicrotingDbContext!.Cases.AsNoTracking().FirstAsync(x => x.Id == result.Model!.SdkCaseId);
        var newCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == result.Model!.ComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(newCase.Status, Is.Not.EqualTo(100), "newly materialised case must not be completed");
            Assert.That(newCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "newly materialised Compliance row must remain live");
            Assert.That(result.Model!.AssignedSiteId, Is.EqualTo(newCase.SiteId));
        });
    }

    // ------------------------------------------------------------------
    // 3. EventStart honors CalendarConfiguration.StartHour (13.5 -> 13:30).
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_EventStart_HonorsCalendarConfigurationStartHour()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(6);
        var s = await SeedGraphAsync("cfgstarthour", 5003, deadline.AddDays(-14), useRealEventDeployService: false);

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = s.Arp.Id, StartHour = 13.5, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var (compliance, _) = await SeedComplianceAsync(s, deadline);

        var result = await s.Service.PrepareComplete(
            s.Arp.Id, compliance.Id, deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.EventStart, Is.EqualTo(ExpectedIso(deadline, 13.5)));
        Assert.That(result.Model.EventStart, Does.Contain("T13:30:00.000Z"));
    }

    // ------------------------------------------------------------------
    // 4. EventStart honors a CalendarOccurrenceException StartHour override.
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_EventStart_HonorsOccurrenceExceptionStartHour()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(7);
        var s = await SeedGraphAsync("exceptionstarthour", 5004, deadline.AddDays(-14), useRealEventDeployService: false);

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = s.Arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var exception = new CalendarOccurrenceException
        {
            AreaRulePlanningId = s.Arp.Id, OriginalDate = deadline.Date, NewDate = null,
            IsDeleted = false, StartHour = 15.25,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.AddAsync(exception);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var (compliance, _) = await SeedComplianceAsync(s, deadline);

        var result = await s.Service.PrepareComplete(
            s.Arp.Id, compliance.Id, deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.EventStart, Is.EqualTo(ExpectedIso(deadline, 15.25)));
        Assert.That(result.Model.EventStart, Does.Contain("T15:15:00.000Z"),
            "the occurrence exception's StartHour (15:15) must win over the CalendarConfiguration default (09:00)");
    }

    // ------------------------------------------------------------------
    // 5. EventStart defaults to 09:00 when no CalendarConfiguration exists.
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_EventStart_DefaultsTo9AM_WhenNoCalendarConfiguration()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(8);
        var s = await SeedGraphAsync("nocfg", 5005, deadline.AddDays(-14), useRealEventDeployService: false);

        // Deliberately no CalendarConfiguration row for this ARP.
        var (compliance, _) = await SeedComplianceAsync(s, deadline);

        var result = await s.Service.PrepareComplete(
            s.Arp.Id, compliance.Id, deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.EventStart, Is.EqualTo(ExpectedIso(deadline, 9.0)));
        Assert.That(result.Model.EventStart, Does.Contain("T09:00:00.000Z"));
    }

    // ------------------------------------------------------------------
    // 6. Missing/removed ARP id -> failure result.
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_RemovedAreaRulePlanning_ReturnsFailure()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedGraphAsync("removedarp", 5006, deadline.AddDays(-14), useRealEventDeployService: false);

        // Soft-remove the ARP after seeding — mirrors the WorkflowState filter
        // PrepareComplete applies (`WorkflowState != Removed`).
        var trackedArp = await BackendConfigurationPnDbContext!.AreaRulePlannings.FirstAsync(x => x.Id == s.Arp.Id);
        trackedArp.WorkflowState = Constants.WorkflowStates.Removed;
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await s.Service.PrepareComplete(
            s.Arp.Id, null, deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.That(result.Success, Is.False, "a removed AreaRulePlanning must not resolve");

        var complianceCount = await BackendConfigurationPnDbContext.Compliances
            .CountAsync(c => c.PlanningId == s.Planning.Id);
        Assert.That(complianceCount, Is.EqualTo(0), "no Compliance row may be written for a removed ARP");
    }

    // ------------------------------------------------------------------
    // 7. No Compliance row + ARP whose AreaRule has no EformId -> failure.
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_NoCompliance_AreaRuleMissingEformId_ReturnsFailure()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedGraphAsync(
            "noeform", 5007, deadline.AddDays(-14), useRealEventDeployService: false, areaRuleEformIdOverride: 0);

        var result = await s.Service.PrepareComplete(
            s.Arp.Id, null, deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.That(result.Success, Is.False, "an AreaRule with no EformId has nothing to materialise");

        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == s.Planning.Id);
        Assert.That(complianceCount, Is.EqualTo(0));
    }

    // ------------------------------------------------------------------
    // 8. No Compliance row + invalid occurrenceDate format -> failure.
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_NoCompliance_InvalidOccurrenceDateFormat_ReturnsFailure()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedGraphAsync("badformat", 5008, deadline.AddDays(-14), useRealEventDeployService: false);

        var result = await s.Service.PrepareComplete(s.Arp.Id, null, "13-07-2026");

        Assert.That(result.Success, Is.False, "a non-yyyy-MM-dd occurrenceDate must be rejected");

        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == s.Planning.Id);
        Assert.That(complianceCount, Is.EqualTo(0));
    }

    // ------------------------------------------------------------------
    // 9. Calling PrepareComplete twice for the same occurrence is idempotent.
    // ------------------------------------------------------------------
    [Test]
    public async Task PrepareComplete_CalledTwice_SameOccurrence_IsIdempotent()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(9);
        var s = await SeedGraphAsync("idempotent", 5009, deadline.AddDays(-14), useRealEventDeployService: true);
        var occurrenceDate = deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var first = await s.Service.PrepareComplete(s.Arp.Id, null, occurrenceDate);
        Assert.That(first.Success, Is.True, first.Message);

        var second = await s.Service.PrepareComplete(s.Arp.Id, null, occurrenceDate);
        Assert.That(second.Success, Is.True, second.Message);

        Assert.That(second.Model!.ComplianceId, Is.EqualTo(first.Model!.ComplianceId),
            "repeated resolution of the same occurrence must return the same Compliance row");
        Assert.That(second.Model.SdkCaseId, Is.EqualTo(first.Model.SdkCaseId));

        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == s.Planning.Id && c.Deadline.Date == deadline.Date);
        Assert.That(complianceCount, Is.EqualTo(1), "no duplicate Compliance row may be created");
    }
}
