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

using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

/// <summary>
/// #1323 — copying a task carries its attachments.
///
/// <para>
/// <c>BackendConfigurationCalendarService.CreateTask</c> resolves
/// <see cref="CalendarTaskCreateRequestModel.CopyAttachmentsFromTaskId"/> /
/// <see cref="CalendarTaskCreateRequestModel.AttachmentIds"/> BEFORE the task wizard runs and,
/// once the new task exists, writes new <see cref="AreaRulePlanningFile"/> rows pointing at the
/// same SDK <c>UploadedData</c>. The task-list batch Copy always asks for every attachment.
/// </para>
///
/// <para>
/// The task wizard is substituted by a fake that writes the AreaRule + Planning +
/// AreaRulePlanning the real wizard would, on the requested property with the requested eForm,
/// so CreateTask's own "latest guide ARP on this property" correlation picks it up. Everything
/// the attachment copy touches — the source rows, validation and the new rows — runs against the
/// real Testcontainers database. The localization service echoes keys so a refusal can be
/// asserted by its key.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarTaskCopyAttachmentsTests : TestBaseSetup
{
    private const int EformId = 7;
    private const int CopySiteId = 900;
    private const int LogbooksFolderId = 4242;

    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private BackendConfigurationCalendarService _calendarService = null!;
    private BackendConfigurationTaskListService _taskListService = null!;

    [SetUp]
    public void SetupServices()
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage()
            .Returns(Task.FromResult(new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        var localizationService = Substitute.For<IBackendConfigurationLocalizationService>();
        localizationService.GetString(Arg.Any<string>()).Returns(callInfo => (string)callInfo[0]);

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        _taskWizardService.CreateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(callInfo => FakeWizardCreateTask(callInfo.Arg<TaskWizardCreateModel>()));

        var coreHelper = Substitute.For<IEFormCoreService>();

        _calendarService = new BackendConfigurationCalendarService(
            localizationService,
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext!));

        // The batch Copy drives the REAL calendar service above, so the copy it
        // makes goes through the same attachment path as the calendar dialog.
        _taskListService = new BackendConfigurationTaskListService(
            localizationService,
            userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            _calendarService,
            _taskWizardService,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            TestContextLogger<BackendConfigurationTaskListService>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stands in for <c>BackendConfigurationTaskWizardService.CreateTask</c>: writes the
    /// guide-created AreaRule (+translation), the items-planning Planning and the
    /// AreaRulePlanning on the requested property with the requested eForm.
    /// </summary>
    private async Task<OperationResult> FakeWizardCreateTask(TaskWizardCreateModel model)
    {
        await SeedGuideTask(model.PropertyId, model.StartDate ?? DateTime.UtcNow.Date);
        return new OperationResult(true, "TaskCreatedSuccessful");
    }

    private async Task<int> SeedProperty(string label)
    {
        var property = new Property
        {
            Name = $"{label} {Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property.Id;
    }

    /// <summary>
    /// A task as the task wizard leaves it (the shape the task-list's BuildUpdateModel
    /// reads): Area → AreaRule(CreatedInGuide, +translation) → Planning → AreaRulePlanning
    /// (+PlanningSite) → CalendarConfiguration. Returns the ARP id.
    /// </summary>
    private async Task<int> SeedGuideTask(int propertyId, DateTime startDate)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = propertyId, EformId = EformId, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        await new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Inspect the gate", Description = "Weekly check",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = ItemsPlanningRepeatType.Week, StartDate = startDate,
            RelatedEFormId = EformId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);

        await new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = 100,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        await new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        return arp.Id;
    }

    /// <summary>A source task on its own property. Returns (propertyId, arpId).</summary>
    private async Task<(int PropertyId, int ArpId)> SeedSourceTask()
    {
        var propertyId = await SeedProperty("Copy source property");
        var arpId = await SeedGuideTask(propertyId, DateTime.UtcNow.Date);
        return (propertyId, arpId);
    }

    private async Task<int> SeedDriveToken()
    {
        var token = new GoogleOAuthToken
        {
            UserId = 1, GoogleAccountEmail = "drive.owner@example.com", EncryptedRefreshToken = "x",
            ConnectedAt = DateTime.UtcNow, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await token.Create(BackendConfigurationPnDbContext!);
        return token.Id;
    }

    /// <summary>
    /// Seeds an attachment join row directly. UploadedDataId is a cross-DB reference with no
    /// FK constraint, so a distinct fake id stands in for the SDK blob.
    /// </summary>
    private async Task<AreaRulePlanningFile> SeedAttachment(int arpId, int uploadedDataId, string fileName,
        string mimeType = "application/pdf", int? driveTokenId = null)
    {
        var file = new AreaRulePlanningFile
        {
            AreaRulePlanningId = arpId,
            UploadedDataId = uploadedDataId,
            OriginalFileName = fileName,
            MimeType = mimeType,
            SizeBytes = 1000 + uploadedDataId,
            DriveFileId = driveTokenId.HasValue ? $"drive-file-{uploadedDataId}" : null,
            DriveModifiedTime = driveTokenId.HasValue ? new DateTime(2026, 3, 14, 9, 26, 53, DateTimeKind.Utc) : null,
            GoogleOAuthTokenId = driveTokenId,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await file.Create(BackendConfigurationPnDbContext!);
        return file;
    }

    /// <summary>
    /// A source with two attachments: a PDF, and an image linked to Google Drive so the
    /// Drive columns are covered too.
    /// </summary>
    private async Task<(int PropertyId, int ArpId, AreaRulePlanningFile Pdf, AreaRulePlanningFile Image)>
        SeedSourceWithTwoAttachments()
    {
        var (propertyId, arpId) = await SeedSourceTask();
        var tokenId = await SeedDriveToken();
        var pdf = await SeedAttachment(arpId, 810_001, "Gate manual.pdf");
        var image = await SeedAttachment(arpId, 810_002, "Gate photo æøå.png", "image/png", tokenId);
        return (propertyId, arpId, pdf, image);
    }

    private static CalendarTaskCreateRequestModel BuildCopy(int propertyId, int sourceArpId,
        List<int>? attachmentIds) =>
        new()
        {
            PropertyId = propertyId,
            FolderId = LogbooksFolderId,
            EformId = EformId,
            StartDate = DateTime.UtcNow.Date.AddDays(7),
            RepeatType = 2,
            RepeatEvery = 1,
            Status = 2,
            Sites = [CopySiteId],
            StartHour = 9.0,
            Duration = 1.0,
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Inspect the gate" }],
            CopyAttachmentsFromTaskId = sourceArpId,
            AttachmentIds = attachmentIds
        };

    private Task<List<AreaRulePlanningFile>> LiveFilesOf(int arpId) =>
        BackendConfigurationPnDbContext!.AreaRulePlanningFiles
            .AsNoTracking()
            .Where(f => f.AreaRulePlanningId == arpId && f.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(f => f.Id)
            .ToListAsync();

    private Task<int> ArpCount() => BackendConfigurationPnDbContext!.AreaRulePlannings.CountAsync();

    private async Task<int> CopyViaCalendar(int propertyId, int sourceArpId, List<int>? attachmentIds)
    {
        var result = await _calendarService.CreateTask(BuildCopy(propertyId, sourceArpId, attachmentIds));
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.GreaterThan(0), "the calendar must correlate the created task");
        Assert.That(result.Model, Is.Not.EqualTo(sourceArpId));
        return result.Model;
    }

    /// <summary>A refused copy creates no task at all: the wizard is never reached.</summary>
    private async Task AssertCopyRefused(CalendarTaskCreateRequestModel model, string expectedKey)
    {
        var arpsBefore = await ArpCount();
        var filesBefore = await BackendConfigurationPnDbContext!.AreaRulePlanningFiles.CountAsync();

        var result = await _calendarService.CreateTask(model);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(expectedKey));
        Assert.That(await ArpCount(), Is.EqualTo(arpsBefore), "no AreaRulePlanning may be created");
        Assert.That(await BackendConfigurationPnDbContext.AreaRulePlanningFiles.CountAsync(),
            Is.EqualTo(filesBefore), "no attachment row may be written");
        await _taskWizardService.DidNotReceive().CreateTask(Arg.Any<TaskWizardCreateModel>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Calendar CreateTask — what is copied
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CreateTask_CopyBothAttachments_NewTaskGetsOwnRowsWithSameUploadedDataAndMetadata()
    {
        var (propertyId, sourceArpId, pdf, image) = await SeedSourceWithTwoAttachments();

        var copyArpId = await CopyViaCalendar(propertyId, sourceArpId, [pdf.Id, image.Id]);

        var sourceFiles = await LiveFilesOf(sourceArpId);
        var copyFiles = await LiveFilesOf(copyArpId);
        Assert.That(sourceFiles, Has.Count.EqualTo(2), "the source keeps its attachments");
        Assert.That(copyFiles, Has.Count.EqualTo(2));

        foreach (var source in sourceFiles)
        {
            var copy = copyFiles.Single(f => f.UploadedDataId == source.UploadedDataId);
            Assert.That(copy.Id, Is.Not.EqualTo(source.Id), "the copy gets its own join row");
            Assert.That(copy.AreaRulePlanningId, Is.EqualTo(copyArpId));
            Assert.That(copy.OriginalFileName, Is.EqualTo(source.OriginalFileName));
            Assert.That(copy.MimeType, Is.EqualTo(source.MimeType));
            Assert.That(copy.SizeBytes, Is.EqualTo(source.SizeBytes));
            Assert.That(copy.DriveFileId, Is.EqualTo(source.DriveFileId));
            Assert.That(copy.DriveModifiedTime, Is.EqualTo(source.DriveModifiedTime));
            Assert.That(copy.GoogleOAuthTokenId, Is.EqualTo(source.GoogleOAuthTokenId));
        }

        // Premise: the Drive columns were actually populated on one of them.
        var driveCopy = copyFiles.Single(f => f.UploadedDataId == image.UploadedDataId);
        Assert.That(driveCopy.DriveFileId, Is.EqualTo("drive-file-810002"));
        Assert.That(driveCopy.DriveModifiedTime, Is.Not.Null);
        Assert.That(driveCopy.GoogleOAuthTokenId, Is.EqualTo(image.GoogleOAuthTokenId).And.Not.Null);
    }

    [Test]
    public async Task CreateTask_CopyOneOfTwoAttachments_OnlyThatOneIsCopied()
    {
        var (propertyId, sourceArpId, _, image) = await SeedSourceWithTwoAttachments();

        var copyArpId = await CopyViaCalendar(propertyId, sourceArpId, [image.Id]);

        var copyFiles = await LiveFilesOf(copyArpId);
        Assert.That(copyFiles.Select(f => f.UploadedDataId), Is.EqualTo(new[] { image.UploadedDataId }));
        Assert.That(await LiveFilesOf(sourceArpId), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CreateTask_EmptyAttachmentIds_CopiesNone()
    {
        var (propertyId, sourceArpId, _, _) = await SeedSourceWithTwoAttachments();

        var copyArpId = await CopyViaCalendar(propertyId, sourceArpId, []);

        Assert.That(await LiveFilesOf(copyArpId), Is.Empty);
        Assert.That(await LiveFilesOf(sourceArpId), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CreateTask_NullAttachmentIds_CopiesAllLiveAttachments()
    {
        var (propertyId, sourceArpId, pdf, image) = await SeedSourceWithTwoAttachments();
        // A soft-removed attachment of the source is not "live" and must not travel.
        var removed = await SeedAttachment(sourceArpId, 810_099, "Old manual.pdf");
        await removed.Delete(BackendConfigurationPnDbContext!);

        var copyArpId = await CopyViaCalendar(propertyId, sourceArpId, null);

        var copied = (await LiveFilesOf(copyArpId)).Select(f => f.UploadedDataId).OrderBy(x => x);
        Assert.That(copied, Is.EqualTo(new[] { pdf.UploadedDataId, image.UploadedDataId }));
    }

    [Test]
    public async Task DeleteFile_OnTheCopy_LeavesTheSourceAttachmentLive()
    {
        var (propertyId, sourceArpId, pdf, _) = await SeedSourceWithTwoAttachments();
        var copyArpId = await CopyViaCalendar(propertyId, sourceArpId, [pdf.Id]);
        var copiedPdf = (await LiveFilesOf(copyArpId)).Single();

        var deleted = await _calendarService.DeleteFile(copyArpId, copiedPdf.Id);

        Assert.That(deleted.Success, Is.True, deleted.Message);
        Assert.That(await LiveFilesOf(copyArpId), Is.Empty);
        var sourcePdf = await BackendConfigurationPnDbContext!.AreaRulePlanningFiles
            .AsNoTracking().SingleAsync(f => f.Id == pdf.Id);
        Assert.That(sourcePdf.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await LiveFilesOf(sourceArpId), Has.Count.EqualTo(2));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Calendar CreateTask — refusals create nothing
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CreateTask_AttachmentOfAnotherTask_FailsWithFileNotFound_AndCreatesNothing()
    {
        var (propertyId, sourceArpId, pdf, _) = await SeedSourceWithTwoAttachments();
        var (_, otherArpId) = await SeedSourceTask();
        var foreign = await SeedAttachment(otherArpId, 820_001, "Other task.pdf");

        await AssertCopyRefused(BuildCopy(propertyId, sourceArpId, [pdf.Id, foreign.Id]), "FileNotFound");
    }

    [Test]
    public async Task CreateTask_SoftRemovedAttachmentOfTheSource_FailsWithFileNotFound_AndCreatesNothing()
    {
        var (propertyId, sourceArpId, pdf, _) = await SeedSourceWithTwoAttachments();
        var removed = await SeedAttachment(sourceArpId, 820_002, "Removed.pdf");
        await removed.Delete(BackendConfigurationPnDbContext!);

        await AssertCopyRefused(BuildCopy(propertyId, sourceArpId, [pdf.Id, removed.Id]), "FileNotFound");
    }

    [Test]
    public async Task CreateTask_SourceTaskRemoved_FailsWithAreaRulePlanningNotFound_AndCreatesNothing()
    {
        var (propertyId, sourceArpId, _, _) = await SeedSourceWithTwoAttachments();
        var source = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == sourceArpId);
        source.WorkflowState = Constants.WorkflowStates.Removed;
        await source.Update(BackendConfigurationPnDbContext);

        await AssertCopyRefused(BuildCopy(propertyId, sourceArpId, null), "AreaRulePlanningNotFound");
    }

    [Test]
    public async Task CreateTask_SourcePropertyRemoved_FailsWithAreaRulePlanningNotFound_AndCreatesNothing()
    {
        var (sourcePropertyId, sourceArpId, _, _) = await SeedSourceWithTwoAttachments();
        var targetPropertyId = await SeedProperty("Copy target property");
        var sourceProperty = await BackendConfigurationPnDbContext!.Properties.SingleAsync(x => x.Id == sourcePropertyId);
        sourceProperty.WorkflowState = Constants.WorkflowStates.Removed;
        await sourceProperty.Update(BackendConfigurationPnDbContext);

        await AssertCopyRefused(BuildCopy(targetPropertyId, sourceArpId, null), "AreaRulePlanningNotFound");
    }

    [Test]
    public async Task CreateTask_MoreThanTenInheritedAttachments_FailsWithAttachmentLimitReached_AndCreatesNothing()
    {
        // Upload caps a task at 10, so 11 live rows can only come from seeding directly.
        var (propertyId, sourceArpId) = await SeedSourceTask();
        for (var i = 0; i < 11; i++)
        {
            await SeedAttachment(sourceArpId, 830_000 + i, $"Document {i}.pdf");
        }

        await AssertCopyRefused(BuildCopy(propertyId, sourceArpId, null), "AttachmentLimitReached");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Task-list batch Copy
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The batch copy leaves FolderId null, so CreateTask resolves the target property's
    /// "00. Logbøger" folder. Seeding that link makes the lookup hit its first branch
    /// (an existing folder) instead of creating an SDK folder.
    /// </summary>
    private async Task SeedLogbooksFolder(int propertyId)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        await new AreaTranslation
        {
            AreaId = area.Id, LanguageId = 1, Name = "00. Logbøger", Description = "",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var areaProperty = new AreaProperty
        {
            PropertyId = propertyId, AreaId = area.Id, Checked = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaProperty.Create(BackendConfigurationPnDbContext!);

        await new ProperyAreaFolder
        {
            ProperyAreaAsignmentId = areaProperty.Id, FolderId = LogbooksFolderId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);
    }

    [Test]
    public async Task BatchCopy_ToAnotherProperty_CarriesEveryAttachment()
    {
        var (sourcePropertyId, sourceArpId, pdf, image) = await SeedSourceWithTwoAttachments();

        var targetPropertyId = await SeedProperty("Copy target property");
        Assume.That(targetPropertyId, Is.Not.EqualTo(sourcePropertyId));
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = targetPropertyId, WorkerId = CopySiteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        var board = new CalendarBoard
        {
            Name = $"Target board {Guid.NewGuid()}", PropertyId = targetPropertyId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await board.Create(BackendConfigurationPnDbContext);
        await SeedLogbooksFolder(targetPropertyId);

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [sourceArpId], TargetPropertyId = targetPropertyId, TargetBoardId = board.Id,
            StartDate = DateTime.UtcNow.Date.AddDays(7), SiteId = CopySiteId
        });

        Assert.That(result.Success, Is.True, result.Message);
        var copy = await BackendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => x.PropertyId == targetPropertyId && x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();

        var copied = (await LiveFilesOf(copy.Id)).Select(f => f.UploadedDataId).OrderBy(x => x);
        Assert.That(copied, Is.EqualTo(new[] { pdf.UploadedDataId, image.UploadedDataId }));
        Assert.That(await LiveFilesOf(sourceArpId), Has.Count.EqualTo(2), "the source keeps its attachments");
    }
}
