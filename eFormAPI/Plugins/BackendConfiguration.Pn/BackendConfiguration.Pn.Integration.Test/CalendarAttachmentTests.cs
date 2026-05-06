using System.Security.Cryptography;
using System.Text;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
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
public class CalendarAttachmentTests : TestBaseSetup
{
    private IUserService _userService = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private BackendConfigurationCalendarService _calendarService = null!;
    private string _sdkConnectionString = null!;

    [SetUp]
    public async Task SetupCalendarService()
    {
        // Mirror CalendarRepeatPersistenceTests bootstrap — clean ARP-related
        // tables FK-safe so each test starts fresh. The base [SetUp] starts
        // the Testcontainers MariaDB container.
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

        // Pre-warm the SDK Core so its EF migrations apply (this brings the
        // UploadedDatas table up to the column shape the entity expects, in
        // particular OriginalFileLocation which the bootstrap SQL lacks).
        // After this we can safely query UploadedDatas via the test DbContext.
        await GetCore();

        // Refresh the test's MicrotingDbContext so it sees the post-migration
        // schema. EF caches the model on first query, so a context that ran
        // before the migration would still error on UploadedDatas reads.
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
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            NullLogger<BackendConfigurationCalendarService>.Instance
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
            Name = $"AttachProp-{Guid.NewGuid()}{suffix}",
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

    /// <summary>
    /// Synthesize an IFormFile from raw bytes. Uses Microsoft.AspNetCore.Http's
    /// FormFile implementation so the service's ContentType / FileName / Length
    /// reads behave the same as in production.
    /// </summary>
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
    public async Task Upload_PdfPngJpeg_ListReturnsAll()
    {
        var arpId = await SeedPlanning();

        var pdfRes = await _calendarService.UploadFile(arpId,
            MakeFormFile(Encoding.UTF8.GetBytes("%PDF-1.4 hello"), "doc.pdf", "application/pdf"));
        var pngRes = await _calendarService.UploadFile(arpId,
            MakeFormFile(MakeBytes(512, 0xAB), "image.png", "image/png"));
        var jpegRes = await _calendarService.UploadFile(arpId,
            MakeFormFile(MakeBytes(256, 0xCD), "photo.jpg", "image/jpeg"));

        Assert.That(pdfRes.Success, Is.True, pdfRes.Message);
        Assert.That(pngRes.Success, Is.True, pngRes.Message);
        Assert.That(jpegRes.Success, Is.True, jpegRes.Message);

        var listed = await _calendarService.ListFiles(arpId);
        Assert.That(listed.Success, Is.True);
        Assert.That(listed.Model, Has.Count.EqualTo(3));

        var mimes = listed.Model.Select(x => x.MimeType).OrderBy(x => x).ToList();
        Assert.That(mimes, Is.EqualTo(new[] { "application/pdf", "image/jpeg", "image/png" }));

        var names = listed.Model.Select(x => x.OriginalFileName).OrderBy(x => x).ToList();
        Assert.That(names, Is.EqualTo(new[] { "doc.pdf", "image.png", "photo.jpg" }));
    }

    [Test]
    public async Task Upload_RoundTripsBinary()
    {
        var arpId = await SeedPlanning();
        var bytes = MakeBytes(1024, 0x42);
        var inputMd5 = Md5(bytes);

        var uploadRes = await _calendarService.UploadFile(arpId,
            MakeFormFile(bytes, "blob.png", "image/png"));
        Assert.That(uploadRes.Success, Is.True, uploadRes.Message);

        var download = await _calendarService.DownloadFile(arpId, uploadRes.Model.Id);
        Assert.That(download, Is.Not.Null);

        using var ms = new MemoryStream();
        await download!.Content.CopyToAsync(ms);
        var roundtrip = ms.ToArray();
        Assert.That(Md5(roundtrip), Is.EqualTo(inputMd5));
    }

    [Test]
    public async Task Upload_RejectsOver25Mb()
    {
        var arpId = await SeedPlanning();
        // Exactly 26 MB — over the 25 MB cap.
        var oversized = MakeBytes(26 * 1024 * 1024, 0x01);

        var res = await _calendarService.UploadFile(arpId,
            MakeFormFile(oversized, "huge.pdf", "application/pdf"));
        Assert.That(res.Success, Is.False);
    }

    [Test]
    public async Task Upload_RejectsWrongMime()
    {
        var arpId = await SeedPlanning();
        var res = await _calendarService.UploadFile(arpId,
            MakeFormFile(Encoding.UTF8.GetBytes("hello"), "notes.txt", "text/plain"));
        Assert.That(res.Success, Is.False);
    }

    [Test]
    public async Task Upload_RejectsExtensionMimeMismatch()
    {
        var arpId = await SeedPlanning();
        var res = await _calendarService.UploadFile(arpId,
            MakeFormFile(MakeBytes(64, 0x10), "report.png", "application/pdf"));
        Assert.That(res.Success, Is.False);
    }

    [Test]
    public async Task Upload_RejectsEleventhFile()
    {
        var arpId = await SeedPlanning();
        for (int i = 0; i < 10; i++)
        {
            var res = await _calendarService.UploadFile(arpId,
                MakeFormFile(MakeBytes(64, (byte)i), $"doc{i}.pdf", "application/pdf"));
            Assert.That(res.Success, Is.True, $"#{i}: {res.Message}");
        }

        var rejected = await _calendarService.UploadFile(arpId,
            MakeFormFile(MakeBytes(64, 0xFF), "doc10.pdf", "application/pdf"));
        Assert.That(rejected.Success, Is.False);
    }

    [Test]
    public async Task Delete_SoftDeletesJoin_PreservesUploadedData()
    {
        var arpId = await SeedPlanning();

        var u1 = await _calendarService.UploadFile(arpId,
            MakeFormFile(MakeBytes(32, 0x11), "a.pdf", "application/pdf"));
        var u2 = await _calendarService.UploadFile(arpId,
            MakeFormFile(MakeBytes(32, 0x22), "b.pdf", "application/pdf"));
        Assert.That(u1.Success, Is.True);
        Assert.That(u2.Success, Is.True);

        // Capture row counts before delete.
        var sdkUploadedDataCountBefore = await MicrotingDbContext!.UploadedDatas.CountAsync();
        var rawArpfBefore = await BackendConfigurationPnDbContext!.AreaRulePlanningFiles.CountAsync();

        var del = await _calendarService.DeleteFile(arpId, u1.Model.Id);
        Assert.That(del.Success, Is.True, del.Message);

        var listed = await _calendarService.ListFiles(arpId);
        Assert.That(listed.Success, Is.True);
        Assert.That(listed.Model, Has.Count.EqualTo(1));
        Assert.That(listed.Model[0].Id, Is.EqualTo(u2.Model.Id));

        // Raw arpf count should be unchanged (soft-delete keeps the row).
        var rawArpfAfter = await BackendConfigurationPnDbContext.AreaRulePlanningFiles.CountAsync();
        Assert.That(rawArpfAfter, Is.EqualTo(rawArpfBefore));

        // The deleted row is now Removed.
        var deletedRow = await BackendConfigurationPnDbContext.AreaRulePlanningFiles
            .FirstAsync(x => x.Id == u1.Model.Id);
        Assert.That(deletedRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        // SDK UploadedData survives — the audit trail to the original blob
        // is intact even after the join-side soft-delete.
        var sdkUploadedDataCountAfter = await MicrotingDbContext.UploadedDatas.CountAsync();
        Assert.That(sdkUploadedDataCountAfter, Is.EqualTo(sdkUploadedDataCountBefore));
    }

    [Test]
    public async Task Download_StreamsExactBinary()
    {
        var arpId = await SeedPlanning();
        var bytes = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog");

        var up = await _calendarService.UploadFile(arpId,
            MakeFormFile(bytes, "story.pdf", "application/pdf"));
        Assert.That(up.Success, Is.True, up.Message);

        var dl = await _calendarService.DownloadFile(arpId, up.Model.Id);
        Assert.That(dl, Is.Not.Null);
        Assert.That(dl!.MimeType, Is.EqualTo("application/pdf"));
        Assert.That(dl.FileName, Is.EqualTo("story.pdf"));

        using var ms = new MemoryStream();
        await dl.Content.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public async Task Files_ScopedToPlanning()
    {
        var arpA = await SeedPlanning("-A");
        var arpB = await SeedPlanning("-B");

        var u1 = await _calendarService.UploadFile(arpA,
            MakeFormFile(MakeBytes(16, 0x77), "a.pdf", "application/pdf"));
        Assert.That(u1.Success, Is.True);

        var listB = await _calendarService.ListFiles(arpB);
        Assert.That(listB.Success, Is.True);
        Assert.That(listB.Model, Is.Empty);

        // Cross-planning download is also rejected (returns null → 404 at
        // the controller boundary).
        var crossDl = await _calendarService.DownloadFile(arpB, u1.Model.Id);
        Assert.That(crossDl, Is.Null);
    }

    [Test]
    public async Task Upload_DoesNotLeakTempFile()
    {
        // Regression: an earlier implementation wrote each upload to a
        // ticks/guid-named file under
        // Path.GetTempPath()/calendar-attachments/ and never deleted it,
        // leaking one file per upload. The current implementation stages to
        // an intermediate, moves it to a checksum-deterministic canonical
        // path, and removes the intermediate in a finally block. Verify
        // that no ticks-named file is left behind after a successful upload.
        var arpId = await SeedPlanning();

        var folder = Path.Combine(Path.GetTempPath(), "calendar-attachments");
        Directory.CreateDirectory(folder);

        // Snapshot ticks-named intermediate files (filename starts with
        // <digits>_<32-hex>) before the upload. The canonical, checksum-
        // named files use 32-hex.<ext> with no underscore separator.
        bool IsIntermediate(string p)
        {
            var name = Path.GetFileNameWithoutExtension(p);
            var underscore = name.IndexOf('_');
            if (underscore <= 0 || underscore == name.Length - 1) return false;
            var prefix = name[..underscore];
            return prefix.All(char.IsDigit);
        }

        var beforeIntermediates = Directory.GetFiles(folder).Where(IsIntermediate).ToHashSet();

        var bytes = MakeBytes(1024, 0x55);
        var res = await _calendarService.UploadFile(arpId,
            MakeFormFile(bytes, "leak.pdf", "application/pdf"));
        Assert.That(res.Success, Is.True, res.Message);

        var afterIntermediates = Directory.GetFiles(folder).Where(IsIntermediate).ToHashSet();
        var newlyLeaked = afterIntermediates.Except(beforeIntermediates).ToList();
        Assert.That(newlyLeaked, Is.Empty,
            $"Upload left {newlyLeaked.Count} intermediate file(s) behind: " +
            string.Join(", ", newlyLeaked));
    }
}
