using System.Security.Cryptography;
using System.Text;
using BackendConfiguration.Pn.Grpc.Documents;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using BackendConfiguration.Pn.Services.EventDeployService;
using Grpc.Core;
using static Grpc.Core.ServerCallContext;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class DocumentsGrpcServiceCalendarFileTest : TestBaseSetup
{
    private IUserService _userService = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private IBackendConfigurationUserPropertyAccess _userPropertyAccess = null!;
    private IGrpcSiteResolver _siteResolver = null!;
    private BackendConfigurationCalendarService _calendarService = null!;
    private DocumentsGrpcService _documentsService = null!;
    private string _sdkConnectionString = null!;

    [SetUp]
    public async Task SetupDocumentsService()
    {
        // Mirror CalendarAttachmentTests bootstrap — clean ARP-related
        // tables FK-safe so each test starts fresh.
        BackendConfigurationPnDbContext!.AreaRulePlanningFiles.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningFiles);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
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

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        _sdkConnectionString = MicrotingDbContext!.Database.GetConnectionString()!;

        // Pre-warm the SDK Core so its EF migrations apply.
        await GetCore();

        // Refresh the test's MicrotingDbContext so it sees the post-migration schema.
        await MicrotingDbContext.DisposeAsync();
        MicrotingDbContext = new MicrotingDbContext(
            new DbContextOptionsBuilder<MicrotingDbContext>()
                .UseMySql(_sdkConnectionString,
                    new MariaDbServerVersion(ServerVersion.AutoDetect(_sdkConnectionString)),
                    o => o.EnableRetryOnFailure())
                .Options);

        MicrotingDbContext.UploadedDatas.RemoveRange(MicrotingDbContext.UploadedDatas);
        await MicrotingDbContext.SaveChangesAsync();

        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        var mockLanguage = new Language { Id = 1, Name = "English", LanguageCode = "en-US" };
        _userService.GetCurrentUserLanguage().Returns(Task.FromResult(mockLanguage));

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        _taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        _calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            _userService,
            BackendConfigurationPnDbContext!,
            new EFormCoreService(_sdkConnectionString),
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            NullLogger<BackendConfigurationCalendarService>.Instance
        );

        // Setup mocks for DocumentsGrpcService dependencies
        _userPropertyAccess = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        _userPropertyAccess.HasAccessAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(true));

        _siteResolver = Substitute.For<IGrpcSiteResolver>();
        _siteResolver.GetSdkSiteIdAsync()
            .Returns(Task.FromResult(1));

        _documentsService = new DocumentsGrpcService(
            _calendarService,
            _userPropertyAccess,
            _siteResolver,
            BackendConfigurationPnDbContext!
        );
    }

    private async Task<int> SeedPlanning(string suffix = "")
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

        var property = new Property
        {
            Name = $"DocsProp-{Guid.NewGuid()}{suffix}",
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            PropertyId = property.Id,
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
            StartDate = DateTime.UtcNow.Date,
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
            PropertyId = property.Id,
            AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = DateTime.UtcNow.Date,
            Status = true,
            RepeatType = 2,
            RepeatEvery = 1,
            DayOfWeek = 1,
            DayOfMonth = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return arp.Id;
    }

    private static IFormFile MakeFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] MakeBytes(int len, byte fill)
    {
        var b = new byte[len];
        for (int i = 0; i < len; i++) b[i] = fill;
        return b;
    }

    private static string Md5(byte[] data)
    {
        using var md5 = MD5.Create();
        var h = md5.ComputeHash(data);
        return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
    }

    [Test]
    public async Task GetAttachment_CalendarFile_ReturnsBytesForExistingFile()
    {
        var arpId = await SeedPlanning();

        // Upload a file to the planning
        var fileBytes = MakeBytes(512, 0xAB);
        var fileName = "calendar-attachment.pdf";
        var mimeType = "application/pdf";

        var uploadRes = await _calendarService.UploadFile(arpId,
            MakeFormFile(fileBytes, fileName, mimeType));
        Assert.That(uploadRes.Success, Is.True, uploadRes.Message);
        var fileId = uploadRes.Model.Id;

        // Create a mock ServerCallContext
        var context = Substitute.For<ServerCallContext>();

        // Call GetAttachment with CALENDAR_FILE source
        var request = new GetAttachmentRequest
        {
            EventId = arpId.ToString(),
            Source = AttachmentSource.CalendarFile,
            Index = 0  // First (and only) file
        };

        var response = await _documentsService.GetAttachment(request, context);

        // Verify the response
        Assert.That(response.ContentType, Is.EqualTo(mimeType));
        Assert.That(response.Filename, Is.EqualTo(fileName));
        Assert.That(response.Data.Length, Is.GreaterThan(0));

        // Verify data integrity via MD5 round-trip
        var downloadedBytes = response.Data.ToByteArray();
        Assert.That(Md5(downloadedBytes), Is.EqualTo(Md5(fileBytes)));
    }

    [Test]
    public async Task GetAttachment_CalendarFile_OutOfRangeIndex_ThrowsNotFound()
    {
        var arpId = await SeedPlanning();

        // Upload a file to the planning
        var uploadRes = await _calendarService.UploadFile(arpId,
            MakeFormFile(MakeBytes(64, 0x10), "file1.pdf", "application/pdf"));
        Assert.That(uploadRes.Success, Is.True, uploadRes.Message);

        // Create a mock ServerCallContext
        var context = Substitute.For<ServerCallContext>();

        // Try to get index 5 (out of range — only 1 file exists)
        var request = new GetAttachmentRequest
        {
            EventId = arpId.ToString(),
            Source = AttachmentSource.CalendarFile,
            Index = 5
        };

        // Should throw RpcException with NotFound status
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await _documentsService.GetAttachment(request, context));

        Assert.That(ex.Status.StatusCode, Is.EqualTo(StatusCode.NotFound));
        Assert.That(ex.Status.Detail, Does.Contain("attachment index out of range"));
    }

    [Test]
    public async Task GetAttachment_CalendarFile_NoAccess_ThrowsPermissionDenied()
    {
        var arpId = await SeedPlanning();

        // Upload a file to the planning
        var uploadRes = await _calendarService.UploadFile(arpId,
            MakeFormFile(MakeBytes(512, 0xCD), "restricted.pdf", "application/pdf"));
        Assert.That(uploadRes.Success, Is.True, uploadRes.Message);

        // Arrange: stub HasAccessAsync to return false for the planning's property
        _userPropertyAccess.HasAccessAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(false));

        // Create a mock ServerCallContext
        var context = Substitute.For<ServerCallContext>();

        // Act: try to get the attachment as a user without property access
        var request = new GetAttachmentRequest
        {
            EventId = arpId.ToString(),
            Source = AttachmentSource.CalendarFile,
            Index = 0
        };

        // Assert: expect PermissionDenied
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await _documentsService.GetAttachment(request, context));

        Assert.That(ex.Status.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
        Assert.That(ex.Status.Detail, Does.Contain("PropertyWorker access"));
    }
}
