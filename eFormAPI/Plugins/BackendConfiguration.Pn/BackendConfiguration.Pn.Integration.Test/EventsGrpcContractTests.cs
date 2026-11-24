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

namespace BackendConfiguration.Pn.Integration.Test;

using System.Globalization;
using System.Threading;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using eFormCore;
using Google.Protobuf;
using GrpcCore = global::Grpc.Core;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using Compliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using BcProperty = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Property;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// Contract tests for the mobile <see cref="EventsGrpcService"/> write and read
/// RPCs: the backward-compatibility pins for requests that do NOT carry the
/// contract-version-1 fields (old apps already installed), plus the
/// request-id idempotency, GetEvent, GetServerInfo and Event
/// version/etag behaviour that the new fields switch on.
/// <para>
/// Same harness shape as <c>EventsGrpcCompleteEventTests</c>: a REAL eFormCore
/// and real databases, every other collaborator an NSubstitute stub. The
/// calendar service is stubbed to return whatever <see cref="Scenario.Tasks"/>
/// holds, so the "echo the current Event" reads in each write handler see a
/// realistic task row. Seeded cases have <c>MicrotingUid == null</c>, so
/// <c>core.CaseDelete</c> (a live cloud call) is never reached.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventsGrpcContractTests : TestBaseSetup
{
    /// <summary>One Picture, one Text and one Comment field.</summary>
    private const string TemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9070</Id>
    <Repeated>0</Repeated>
    <Label>ContractMain</Label>
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
            <Id>9070</Id>
            <Label>ContractDataElement</Label>
            <Description><![CDATA[ContractDataElementDescription]]></Description>
            <DisplayOrder>0</DisplayOrder>
            <ReviewEnabled>false</ReviewEnabled>
            <ManualSync>false</ManualSync>
            <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
            <DoneButtonDisabled>false</DoneButtonDisabled>
            <ApprovalEnabled>false</ApprovalEnabled>
            <DataItemList>
                <DataItem type='Picture'>
                    <Id>73670</Id>
                    <Label>PictureField</Label>
                    <Description><![CDATA[]]></Description>
                    <DisplayOrder>0</DisplayOrder>
                    <Mandatory>false</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
                <DataItem type='Text'>
                    <Id>73671</Id>
                    <Label>TextField</Label>
                    <Description><![CDATA[]]></Description>
                    <DisplayOrder>1</DisplayOrder>
                    <Multi>0</Multi>
                    <GeolocationEnabled>false</GeolocationEnabled>
                    <Split>false</Split>
                    <Value/>
                    <ReadOnly>false</ReadOnly>
                    <Mandatory>false</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
                <DataItem type='Comment'>
                    <Id>73672</Id>
                    <Label>CommentField</Label>
                    <Description><![CDATA[]]></Description>
                    <DisplayOrder>2</DisplayOrder>
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

    /// <summary>A valid 1x1 PNG.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static readonly DateTimeOffset ClientTapInstant =
        new(2026, 6, 15, 14, 37, 11, TimeSpan.Zero);

    private sealed class TestServerCallContext(CancellationToken token = default) : GrpcCore.ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override GrpcCore.Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => token;
        protected override GrpcCore.Metadata ResponseTrailersCore { get; } = new();
        protected override GrpcCore.Status StatusCore { get; set; }
        protected override GrpcCore.WriteOptions? WriteOptionsCore { get; set; }

        protected override GrpcCore.AuthContext AuthContextCore { get; } =
            new(string.Empty, new Dictionary<string, List<GrpcCore.AuthProperty>>());

        protected override GrpcCore.ContextPropagationToken CreatePropagationTokenCore(
            GrpcCore.ContextPropagationOptions? options) => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(GrpcCore.Metadata responseHeaders)
            => Task.CompletedTask;
    }

    private sealed class ListStreamReader<T>(IEnumerable<T> items) : GrpcCore.IAsyncStreamReader<T>
        where T : class
    {
        private readonly Queue<T> _items = new(items);
        public T Current { get; private set; } = null!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_items.Count == 0)
            {
                return Task.FromResult(false);
            }

            Current = _items.Dequeue();
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingStreamWriter<T> : GrpcCore.IServerStreamWriter<T>
    {
        private readonly object _gate = new();
        private readonly List<T> _written = [];

        public List<T> Snapshot()
        {
            lock (_gate)
            {
                return _written.ToList();
            }
        }

        public GrpcCore.WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            lock (_gate)
            {
                _written.Add(message);
            }

            return Task.CompletedTask;
        }

        public Task WriteAsync(T message, CancellationToken cancellationToken) => WriteAsync(message);
    }

    private sealed record Scenario(
        EventsGrpcService Service,
        Func<BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, EventsGrpcService> ServiceOn,
        List<string> StoredFiles,
        StorageControl Storage,
        List<CalendarTaskResponseModel> Tasks,
        IBackendConfigurationUserPropertyAccess Access,
        RecordingLogger<EventsGrpcService> Logger,
        int PropertyId,
        int ArpId,
        int PlanningId,
        int ComplianceId,
        int SdkCaseId,
        int SiteId,
        int TemplateId,
        int PictureFieldId,
        int TextFieldId,
        DateTime Deadline,
        IBackendConfigurationCalendarService Calendar)
    {
        public string EventId => ArpId.ToString(CultureInfo.InvariantCulture);
        public string DayKey => Deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Lets a test hold the fake storage write (e.g. a slow S3 put).</summary>
    private sealed class StorageControl
    {
        public Func<string, Task>? BeforeStore { get; set; }
    }

    /// <summary>Counts SQL commands whose text contains a marker.</summary>
    private sealed class CommandCounter(string marker) : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);

        private void Observe(DbCommand command)
        {
            if (command.CommandText.Contains(marker, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Observe(command);
            return base.ReaderExecuting(command, eventData, result);
        }
    }

    private int _templateId;

    private async Task<int> GetOrCreateTemplateAsync(Core core)
    {
        if (_templateId != 0)
        {
            return _templateId;
        }

        var template = await core.TemplateFromXml(TemplateXml);
        _templateId = await core.TemplateCreate(template);
        return _templateId;
    }

    private async Task<int> FindFieldIdAsync(int templateId, string fieldType)
    {
        var fieldTypeId = await MicrotingDbContext!.FieldTypes
            .Where(ft => ft.Type == fieldType).Select(ft => ft.Id).FirstAsync();
        return await MicrotingDbContext.Fields
            .Where(f => f.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(f => f.FieldTypeId == fieldTypeId)
            .Where(f => f.CheckListId == templateId
                        || MicrotingDbContext.CheckLists
                            .Any(cl => cl.Id == f.CheckListId && cl.ParentId == templateId))
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .FirstAsync();
    }

    /// <summary>
    /// Seeds one occurrence: property, area rule planning, planning, a live
    /// compliance and an open SDK case homed on the calling worker's site
    /// (so the legacy compliance_id = 0 lookup resolves the same row), with
    /// the case's FieldValues materialised through <c>core.CaseRead</c>.
    /// </summary>
    private async Task<Scenario> SeedAsync(string label, bool withEchoTask = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var templateId = await GetOrCreateTemplateAsync(core);

        var site = new Site
        {
            Name = $"{label}-worker-{suffix}",
            MicrotingUid = Random.Shared.Next(500000, 999999),
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkCase = new SdkCase
        {
            SiteId = site.Id,
            CheckListId = templateId,
            Status = 66,
            MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created,
            Custom = string.Empty
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        // Materialise the per-field FieldValues rows, as an admin read would.
        await core.CaseRead(sdkCase.Id, language);

        var deadline = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-2), DateTimeKind.Utc);

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

        var property = new BcProperty
        {
            Name = $"{label}-{suffix}",
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
            EformId = templateId,
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
            StartDate = deadline,
            RelatedEFormId = templateId,
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
            StartDate = deadline,
            Status = true,
            RepeatType = 1,
            RepeatEvery = 1,
            DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id,
            SiteId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            PlanningId = planning.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            Deadline = deadline,
            StartDate = deadline.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = templateId,
            Version = 1,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = planning.Id,
            Status = 66,
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(new PlanningCaseSite
        {
            PlanningCaseId = planningCase.Id,
            PlanningId = planning.Id,
            Status = 66,
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkSiteId = (int)site.MicrotingUid!,
            MicrotingCheckListSitId = templateId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var tasks = new List<CalendarTaskResponseModel>();
        if (withEchoTask)
        {
            tasks.Add(new CalendarTaskResponseModel
            {
                Id = arp.Id,
                Title = $"{label}-task",
                StartHour = 9.5,
                TaskDate = deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Color = "#123456",
                Status = true,
                Completed = false,
                PropertyId = property.Id,
                ComplianceId = compliance.Id,
                IsFromCompliance = true,
                Deadline = deadline,
                PlanningId = planning.Id,
                EformId = templateId,
                SdkCaseId = sdkCase.Id,
                DescriptionHtml = "<p>desc</p>"
            });
        }

        var siteResolver = Substitute.For<IGrpcSiteResolver>();
        siteResolver.GetSdkSiteIdAsync().Returns(Task.FromResult(site.Id));
        siteResolver.GetSiteLanguageIdAsync(Arg.Any<int?>())
            .Returns(Task.FromResult<int?>(language.Id));

        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(site.Id, property.Id).Returns(Task.FromResult(true));

        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(_ => Task.FromResult(new OperationDataResult<List<CalendarTaskResponseModel>>(
                true, tasks.ToList())));
        calendar.GetTaskTrackerList(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>())
            .Returns(_ => Task.FromResult(new OperationDataResult<List<CalendarTaskResponseModel>>(
                true, tasks.ToList())));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(core);

        var logger = new RecordingLogger<EventsGrpcService>();
        // The SDK's S3 client is process-static, so photo storage goes
        // through the service's internal seam instead of real S3.
        var storedFiles = new List<string>();
        var storage = new StorageControl();

        EventsGrpcService ServiceOn(BackendConfigurationPnDbContext bc, ItemsPlanningPnDbContext ip) =>
            new(
                calendar,
                Substitute.For<IBackendConfigurationPropertiesService>(),
                access,
                siteResolver,
                coreHelper,
                bc,
                ip,
                Substitute.For<IEventDeployService>(),
                logger)
            {
                PutFileToS3StorageAsync = async (_, _, fileName) =>
                {
                    if (storage.BeforeStore is { } gate)
                    {
                        await gate(fileName);
                    }

                    lock (storedFiles)
                    {
                        storedFiles.Add(fileName);
                    }
                }
            };

        return new Scenario(
            ServiceOn(BackendConfigurationPnDbContext, ItemsPlanningPnDbContext), ServiceOn, storedFiles, storage,
            tasks, access, logger, property.Id, arp.Id, planning.Id, compliance.Id, sdkCase.Id,
            site.Id, templateId,
            await FindFieldIdAsync(templateId, Constants.FieldTypes.Picture),
            await FindFieldIdAsync(templateId, Constants.FieldTypes.Text),
            deadline, calendar);
    }

    private static CompleteEventRequest LegacyComplete(Scenario s) => new()
    {
        EventId = s.EventId,
        Completed = true,
        CompletedBy = "contract-worker",
        ClientTsUnix = ClientTapInstant.ToUnixTimeSeconds(),
        ComplianceId = s.ComplianceId,
        MicrotingSdkCaseId = s.SdkCaseId,
        Comment = string.Empty,
        DoneAtUserModifiable = string.Empty
    };

    private static SetCommentRequest LegacyComment(Scenario s, string text) => new()
    {
        EventId = s.EventId,
        Text = text,
        ClientTsUnix = ClientTapInstant.ToUnixTimeSeconds(),
        ComplianceId = s.ComplianceId,
        MicrotingSdkCaseId = s.SdkCaseId
    };

    private static SetFieldValueRequest LegacyFieldValue(Scenario s, string value) => new()
    {
        EventId = s.EventId,
        FieldId = s.TextFieldId,
        Value = value,
        ClientTsUnix = ClientTapInstant.ToUnixTimeSeconds(),
        ComplianceId = s.ComplianceId,
        MicrotingSdkCaseId = s.SdkCaseId
    };

    private static UploadPhotoMeta LegacyPhotoMeta(Scenario s, int slot = 0) => new()
    {
        EventId = s.EventId,
        Slot = slot,
        ContentType = "image/png",
        ClientTsUnix = ClientTapInstant.ToUnixTimeSeconds(),
        ComplianceId = s.ComplianceId,
        MicrotingSdkCaseId = s.SdkCaseId,
        FieldId = s.PictureFieldId
    };

    private static RemovePhotoRequest LegacyRemove(Scenario s, int slot = 0) => new()
    {
        EventId = s.EventId,
        Slot = slot,
        ClientTsUnix = ClientTapInstant.ToUnixTimeSeconds(),
        ComplianceId = s.ComplianceId,
        MicrotingSdkCaseId = s.SdkCaseId
    };

    private static Task<UploadPhotoResponse> UploadAsync(Scenario s, UploadPhotoMeta meta) =>
        s.Service.UploadPhoto(
            new ListStreamReader<UploadPhotoChunk>(
            [
                new UploadPhotoChunk { Meta = meta },
                new UploadPhotoChunk { Chunk = ByteString.CopyFrom(OnePixelPng) }
            ]),
            new TestServerCallContext());

    private Task<string> CaseCustomAsync(Scenario s) =>
        MicrotingDbContext!.Cases.AsNoTracking()
            .Where(c => c.Id == s.SdkCaseId).Select(c => c.Custom ?? string.Empty).FirstAsync();

    private Task<int> PictureFieldValueRowsAsync(Scenario s) =>
        MicrotingDbContext!.FieldValues.AsNoTracking()
            .CountAsync(fv => fv.CaseId == s.SdkCaseId && fv.FieldId == s.PictureFieldId && fv.UploadedDataId != null);

    private Task<int> UploadedDataRowsAsync(Scenario s, IEnumerable<int> ids)
    {
        var idList = ids.ToList();
        return MicrotingDbContext!.UploadedDatas.AsNoTracking().CountAsync(u => idList.Contains(u.Id));
    }

    // ==================================================================
    // Backward compatibility: requests WITHOUT the contract-1 fields.
    // ==================================================================

    /// <summary>
    /// SetComment from an old app: the reply is the echo Event with the
    /// written text and, as today, compliance_id / microting_sdk_case_id /
    /// eform_id at 0 and no fields. The stored envelope is the exact JSON
    /// today's server writes. A second identical call answers the same.
    /// </summary>
    [Test]
    public async Task Legacy_SetComment_ReplyAndStorageUnchanged_AndRepeatAnswersTheSame()
    {
        var s = await SeedAsync("legacy-comment");

        var first = await s.Service.SetComment(LegacyComment(s, "hello  "), new TestServerCallContext());
        var customAfterFirst = await CaseCustomAsync(s);
        var second = await s.Service.SetComment(LegacyComment(s, "hello  "), new TestServerCallContext());

        var expected = new Event
        {
            Id = s.EventId,
            PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture),
            BoardId = string.Empty,
            PlanDayKey = s.DayKey,
            PlannedAt = "09:30",
            TaskText = "legacy-comment-task",
            CalendarColor = "#123456",
            Completed = false,
            CompletedBy = string.Empty,
            DescriptionHtml = "<p>desc</p>",
            Comment = "hello"
        };

        Assert.Multiple(() =>
        {
            Assert.That(first.Event, Is.EqualTo(expected), "old-app SetComment reply is byte-for-byte today's echo");
            Assert.That(second, Is.EqualTo(first), "a repeat answers exactly the same");
            Assert.That(customAfterFirst, Is.EqualTo(
                "{\"event_comment\":{\"text\":\"hello\",\"ts_unix\":" + ClientTapInstant.ToUnixTimeSeconds()
                + "},\"event_photos\":null}"),
                "the stored envelope is exactly today's JSON");
        });
    }

    /// <summary>
    /// SetFieldValue from an old app: echo Event carries eform_id + fields but,
    /// as today, compliance_id / microting_sdk_case_id at 0; repeat answers the
    /// same; after completion the write is refused with FailedPrecondition.
    /// </summary>
    [Test]
    public async Task Legacy_SetFieldValue_ReplyUnchanged_RepeatSame_AfterCompletionFailedPrecondition()
    {
        var s = await SeedAsync("legacy-fv");

        var first = await s.Service.SetFieldValue(LegacyFieldValue(s, "abc"), new TestServerCallContext());
        var second = await s.Service.SetFieldValue(LegacyFieldValue(s, "abc"), new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(first.Event.Id, Is.EqualTo(s.EventId));
            Assert.That(first.Event.ComplianceId, Is.EqualTo(0), "old-app reply keeps compliance_id = 0");
            Assert.That(first.Event.MicrotingSdkCaseId, Is.EqualTo(0));
            Assert.That(first.Event.EformId, Is.EqualTo(s.TemplateId));
            Assert.That(first.Event.Fields.Single(f => f.Id == s.TextFieldId).Value, Is.EqualTo("abc"));
            Assert.That(second, Is.EqualTo(first), "a repeat answers exactly the same");
            Assert.That(first.Replayed, Is.False);
            Assert.That(first.Event.HasVersion, Is.False, "no contract-1 field on an old-app write reply");
            Assert.That(first.Event.Etag, Is.Empty);
            Assert.That(first.Event.DueDateKey, Is.Empty);
            Assert.That(first.Event.DueTimeLocal, Is.Empty);
        });

        await s.Service.CompleteEvent(LegacyComplete(s), new TestServerCallContext());

        var ex = await Assert.ThrowsAsync<GrpcCore.RpcException>(async () =>
            await s.Service.SetFieldValue(LegacyFieldValue(s, "abc"), new TestServerCallContext()));
        Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.FailedPrecondition));
    }

    /// <summary>
    /// UploadPhoto from an old app: today every call is a full re-run, so a
    /// repeat creates a second UploadedData and a second Picture FieldValue
    /// row and returns a new storage id; the first UploadedData is soft-deleted.
    /// Pinned as-is: old apps keep this behaviour.
    /// </summary>
    [Test]
    public async Task Legacy_UploadPhoto_RepeatIsAFullRerun()
    {
        var s = await SeedAsync("legacy-upload");

        var first = await UploadAsync(s, LegacyPhotoMeta(s));
        var second = await UploadAsync(s, LegacyPhotoMeta(s));

        var firstId = int.Parse(first.StorageId, CultureInfo.InvariantCulture);
        var secondId = int.Parse(second.StorageId, CultureInfo.InvariantCulture);
        var firstRow = await MicrotingDbContext!.UploadedDatas.AsNoTracking().FirstAsync(u => u.Id == firstId);

        Assert.Multiple(async () =>
        {
            Assert.That(secondId, Is.Not.EqualTo(firstId));
            Assert.That(second.Replayed, Is.False);
            Assert.That(s.StoredFiles.Count(f => !f.Contains("_300_") && !f.Contains("_700_")), Is.EqualTo(2),
                "two originals were stored");
            Assert.That(await PictureFieldValueRowsAsync(s), Is.EqualTo(2),
                "today a repeat adds a second Picture FieldValue row");
            Assert.That(firstRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(first.CalculateSize(), Is.EqualTo(new UploadPhotoResponse { StorageId = first.StorageId }.CalculateSize()),
                "old-app reply carries only storage_id");
        });
    }

    /// <summary>
    /// RemovePhoto from an old app: empty reply both times, the slot's
    /// UploadedData is soft-deleted and the envelope entry dropped.
    /// </summary>
    [Test]
    public async Task Legacy_RemovePhoto_EmptyReply_RepeatIsOk()
    {
        var s = await SeedAsync("legacy-remove");
        var upload = await UploadAsync(s, LegacyPhotoMeta(s));
        var uploadedId = int.Parse(upload.StorageId, CultureInfo.InvariantCulture);

        var first = await s.Service.RemovePhoto(LegacyRemove(s), new TestServerCallContext());
        var second = await s.Service.RemovePhoto(LegacyRemove(s), new TestServerCallContext());

        var row = await MicrotingDbContext!.UploadedDatas.AsNoTracking().FirstAsync(u => u.Id == uploadedId);
        Assert.Multiple(async () =>
        {
            Assert.That(first.CalculateSize(), Is.EqualTo(0), "old-app reply is empty");
            Assert.That(second.CalculateSize(), Is.EqualTo(0));
            Assert.That(row.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await CaseCustomAsync(s), Is.EqualTo(string.Empty), "the envelope collapses to empty");
        });
    }

    /// <summary>
    /// CompleteEvent from an old app, then the same worker's retry carrying
    /// answers: the retry gets today's "already completed" answer with
    /// answers_discarded = true and exactly one Error log (the known false
    /// alarm, left unchanged for old apps).
    /// </summary>
    [Test]
    public async Task Legacy_CompleteEvent_OwnRetryWithAnswers_KeepsTodaysAlreadyCompletedAnswer()
    {
        var s = await SeedAsync("legacy-complete", withEchoTask: false);
        var request = LegacyComplete(s);
        request.FieldValues.Add(new FieldValueWrite { FieldId = s.TextFieldId, Value = "x" });

        var first = await s.Service.CompleteEvent(request, new TestServerCallContext());
        var retry = await s.Service.CompleteEvent(request, new TestServerCallContext());

        var siteName = await MicrotingDbContext!.Sites.AsNoTracking()
            .Where(x => x.Id == s.SiteId).Select(x => x.Name).FirstAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.AnswersDiscarded, Is.False);
            Assert.That(first.Event, Is.EqualTo(new Event
            {
                Id = s.EventId,
                PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture),
                BoardId = string.Empty,
                PlanDayKey = s.DayKey,
                PlannedAt = string.Empty,
                TaskText = string.Empty,
                CalendarColor = string.Empty,
                Completed = true,
                CompletedBy = "contract-worker",
                DescriptionHtml = string.Empty,
                Comment = string.Empty
            }), "old-app success reply is today's synthesised Event");
            Assert.That(retry.AnswersDiscarded, Is.True, "unchanged for old apps");
            Assert.That(retry.Replayed, Is.False);
            Assert.That(retry.Event.Etag, Is.Empty, "no contract-1 field on an old-app write reply");
            Assert.That(retry.Event.HasVersion, Is.False);
            Assert.That(retry.Event.CompletedBy, Is.EqualTo(siteName));
            Assert.That(retry.Event.PlanDayKey, Is.Empty);
            Assert.That(s.Logger.Entries.Count(e => e.Level == LogLevel.Error), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// ListEvents for an old app still returns the same Event field values it
    /// does today (the new fields are additive, see the contract tests).
    /// </summary>
    [Test]
    public async Task Legacy_ListEvents_WholeEventUnchanged_NewFieldsAdditive()
    {
        var s = await SeedAsync("legacy-list");

        var response = await s.Service.ListEvents(new ListEventsRequest
        {
            PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture),
            FromDateKey = s.DayKey,
            ToDateKey = s.DayKey
        }, new TestServerCallContext());

        var evt = response.Events.Single();
        AssertPreContractShape(s, evt);
        Assert.Multiple(() =>
        {
            // Additive contract-1 fields, ignored by old apps.
            Assert.That(evt.HasVersion, Is.True);
            Assert.That(evt.Etag, Has.Length.EqualTo(64));
            Assert.That(evt.DueDateKey, Is.EqualTo(s.DayKey));
            Assert.That(evt.DueTimeLocal, Is.EqualTo("09:30"));
        });
    }

    [Test]
    public async Task Legacy_ListTaskTracker_WholeEventUnchanged()
    {
        var s = await SeedAsync("legacy-tracker");

        var response = await s.Service.ListTaskTracker(
            new ListTaskTrackerRequest { PropertyId = s.PropertyId }, new TestServerCallContext());

        AssertPreContractShape(s, response.Events.Single());
    }

    [Test]
    public async Task Legacy_Stream_InitialEmissionWholeEventUnchanged()
    {
        var s = await SeedAsync("legacy-stream");
        var writer = new RecordingStreamWriter<EventChange>();
        using var cts = new CancellationTokenSource();
        await using var bc = NewBackendContext();
        await using var ip = NewItemsPlanningContext();

        var streaming = s.ServiceOn(bc, ip).StreamEventChanges(
            new StreamEventChangesRequest { PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture) },
            writer, new TestServerCallContext(cts.Token));
        try
        {
            await WaitForAsync(() => writer.Snapshot().Count >= 1, TimeSpan.FromSeconds(30));
            var first = writer.Snapshot()[0];
            Assert.That(first.KindCase, Is.EqualTo(EventChange.KindOneofCase.Upserted));
            AssertPreContractShape(s, first.Upserted);
            Assert.That(first.Upserted.Etag, Is.Not.Empty, "sent events carry etag");
            Assert.That(first.Upserted.HasVersion, Is.True, "sent events carry version");
        }
        finally
        {
            await cts.CancelAsync();
            await streaming;
        }
    }

    /// <summary>
    /// The read-path Event exactly as the server built it before contract
    /// version 1: every field 1-18 compared as a whole message after clearing
    /// the additive fields 19-22. Form fields come from the real SDK template
    /// (their own mapping is unchanged code), so they are checked for ids and
    /// types, then carried over.
    /// </summary>
    private static void AssertPreContractShape(Scenario s, Event actual)
    {
        var withoutContractFields = actual.Clone();
        withoutContractFields.ClearVersion();
        withoutContractFields.Etag = string.Empty;
        withoutContractFields.DueDateKey = string.Empty;
        withoutContractFields.DueTimeLocal = string.Empty;

        Assert.That(actual.Fields.Select(f => (f.Id, f.FieldType)), Is.EquivalentTo(new[]
        {
            (s.PictureFieldId, "Picture"), (s.TextFieldId, "Text"),
            (actual.Fields.Single(f => f.FieldType == "Comment").Id, "Comment")
        }));

        var expected = new Event
        {
            Id = s.EventId,
            PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture),
            BoardId = string.Empty,
            PlanDayKey = s.DayKey,
            PlannedAt = "09:30",
            TaskText = s.Tasks[0].Title,
            CalendarColor = "#123456",
            Completed = false,
            CompletedBy = string.Empty,
            DescriptionHtml = "<p>desc</p>",
            Comment = string.Empty,
            EformId = s.TemplateId,
            ComplianceId = s.ComplianceId,
            MicrotingSdkCaseId = s.SdkCaseId,
            TaskIsExpired = false
        };
        expected.Fields.AddRange(actual.Fields);

        Assert.That(withoutContractFields, Is.EqualTo(expected),
            "fields 1-18 are byte-for-byte the pre-contract Event");
    }

    // ==================================================================
    // contract_version 1
    // ==================================================================

    private static string NewRequestId() => Guid.NewGuid().ToString();

    private BackendConfigurationPnDbContext NewBackendContext(IInterceptor? interceptor = null)
    {
        var cs = BackendConfigurationPnDbContext!.Database.GetConnectionString()!;
        var ob = new DbContextOptionsBuilder<BackendConfigurationPnDbContext>();
        ob.UseMySql(cs, ServerVersion.AutoDetect(cs), b => b.EnableRetryOnFailure());
        if (interceptor != null)
        {
            ob.AddInterceptors(interceptor);
        }
        var context = new BackendConfigurationPnDbContext(ob.Options);
        context.Database.SetCommandTimeout(300);
        return context;
    }

    private ItemsPlanningPnDbContext NewItemsPlanningContext()
    {
        var cs = ItemsPlanningPnDbContext!.Database.GetConnectionString()!;
        var ob = new DbContextOptionsBuilder<ItemsPlanningPnDbContext>();
        ob.UseMySql(cs, ServerVersion.AutoDetect(cs), b => b.EnableRetryOnFailure());
        var context = new ItemsPlanningPnDbContext(ob.Options);
        context.Database.SetCommandTimeout(300);
        return context;
    }

    private async Task SetCaseCustomAsync(Scenario s, string custom)
    {
        var theCase = await MicrotingDbContext!.Cases.FirstAsync(c => c.Id == s.SdkCaseId);
        theCase.Custom = custom;
        await theCase.Update(MicrotingDbContext);
    }

    private async Task<Event> ListSingleAsync(Scenario s)
    {
        var response = await s.Service.ListEvents(new ListEventsRequest
        {
            PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture),
            FromDateKey = s.DayKey,
            ToDateKey = s.DayKey
        }, new TestServerCallContext());
        return response.Events.Single();
    }

    [Test]
    public async Task GetServerInfo_AnswersContractVersion2AndItsCapabilities()
    {
        var s = await SeedAsync("server-info", withEchoTask: false);

        var info = await s.Service.GetServerInfo(new GetServerInfoRequest(), new TestServerCallContext());

        Assert.Multiple(() =>
        {
            // contract_version 2 adds ListChanges; every version-1 capability stays.
            // "live_hub" (the SignalR hub, outside the proto) leaves the version at 2.
            Assert.That(info.ContractVersion, Is.EqualTo(2));
            Assert.That(info.Capabilities, Is.EquivalentTo(new[]
            {
                "request_id", "remove_photo_expected_storage_id", "write_reply_compliance_id",
                "get_event", "event_version", "event_etag", "event_due_local", "list_changes",
                "live_hub", "task_notifications"
            }));
        });
    }

    [Test]
    public async Task RequestId_LongerThan128_IsInvalidArgument()
    {
        // No database needed: validation runs before any lookup.
        var service = new EventsGrpcService(
            Substitute.For<IBackendConfigurationCalendarService>(),
            Substitute.For<IBackendConfigurationPropertiesService>(),
            Substitute.For<IBackendConfigurationUserPropertyAccess>(),
            Substitute.For<IGrpcSiteResolver>(),
            Substitute.For<IEFormCoreService>(),
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            Substitute.For<IEventDeployService>(),
            new RecordingLogger<EventsGrpcService>());

        var ex = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => service.SetComment(
            new SetCommentRequest { EventId = "1", Text = "x", RequestId = new string('a', 129) },
            new TestServerCallContext()));
        Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.InvalidArgument));
    }

    // ---------------- CompleteEvent ----------------

    /// <summary>
    /// The S4 retry: the same request id after it was applied answers success
    /// with replayed=true, answers_discarded=false, no Error log, and the
    /// cascade is not run a second time.
    /// </summary>
    [Test]
    public async Task CompleteEvent_SameRequestIdRepeated_IsReplayedWithoutDiscardOrError()
    {
        var s = await SeedAsync("complete-replay", withEchoTask: false);
        var request = LegacyComplete(s);
        request.RequestId = NewRequestId();
        request.FieldValues.Add(new FieldValueWrite { FieldId = s.TextFieldId, Value = "answer" });
        request.Comment = "done";

        var first = await s.Service.CompleteEvent(request, new TestServerCallContext());
        var auditRowsAfterFirst = await BackendConfigurationPnDbContext!.ComplianceVersions.AsNoTracking()
            .CountAsync(x => x.ComplianceId == s.ComplianceId);
        var caseVersionAfterFirst = await MicrotingDbContext!.Cases.AsNoTracking()
            .Where(c => c.Id == s.SdkCaseId).Select(c => c.Version).FirstAsync();

        var retry = await s.Service.CompleteEvent(request, new TestServerCallContext());

        Assert.Multiple(async () =>
        {
            Assert.That(first.Replayed, Is.False);
            Assert.That(first.Event.HasVersion, Is.True, "request-id write replies carry version");
            Assert.That(first.Event.Etag, Is.Not.Empty);
            Assert.That(await CaseCustomAsync(s), Does.Contain(request.RequestId));

            Assert.That(retry.Replayed, Is.True);
            Assert.That(retry.AnswersDiscarded, Is.False);
            Assert.That(retry.DiscardedReason, Is.Empty);
            Assert.That(retry.Event.Completed, Is.True);
            Assert.That(retry.Event.PlanDayKey, Is.EqualTo(s.DayKey), "the current Event, not the minimal loser reply");
            Assert.That(s.Logger.Entries.Any(e => e.Level == LogLevel.Error), Is.False, "no false Sentry alarm");
            Assert.That(await BackendConfigurationPnDbContext.ComplianceVersions.AsNoTracking()
                    .CountAsync(x => x.ComplianceId == s.ComplianceId), Is.EqualTo(auditRowsAfterFirst),
                "the claim did not run again");
            Assert.That(await MicrotingDbContext.Cases.AsNoTracking()
                    .Where(c => c.Id == s.SdkCaseId).Select(c => c.Version).FirstAsync(),
                Is.EqualTo(caseVersionAfterFirst), "the case was not written again");
            Assert.That(await MicrotingDbContext.FieldValues.AsNoTracking()
                    .Where(fv => fv.CaseId == s.SdkCaseId && fv.FieldId == s.TextFieldId)
                    .Select(fv => fv.Value).FirstAsync(), Is.EqualTo("answer"));
        });
    }

    /// <summary>
    /// A DIFFERENT request (other request id) against the already completed
    /// occurrence keeps today's "already completed" answer and Error log.
    /// </summary>
    [Test]
    public async Task CompleteEvent_DifferentRequestIdAfterCompletion_KeepsAlreadyCompletedAnswer()
    {
        var s = await SeedAsync("complete-different", withEchoTask: false);
        var first = LegacyComplete(s);
        first.RequestId = NewRequestId();
        await s.Service.CompleteEvent(first, new TestServerCallContext());

        var other = LegacyComplete(s);
        other.RequestId = NewRequestId();
        other.FieldValues.Add(new FieldValueWrite { FieldId = s.TextFieldId, Value = "late" });
        var response = await s.Service.CompleteEvent(other, new TestServerCallContext());

        var siteName = await MicrotingDbContext!.Sites.AsNoTracking()
            .Where(x => x.Id == s.SiteId).Select(x => x.Name).FirstAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.Replayed, Is.False);
            Assert.That(response.AnswersDiscarded, Is.True);
            Assert.That(response.Event.CompletedBy, Is.EqualTo(siteName));
            Assert.That(s.Logger.Entries.Count(e => e.Level == LogLevel.Error), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Two identical requests in flight at once (the app's 30 s retry while
    /// the first is still running): exactly one applies the cascade (the
    /// Compliance FOR UPDATE claim), the other is answered as a replay.
    /// </summary>
    [Test]
    public async Task CompleteEvent_IdenticalRequestsConcurrently_ApplyOnce()
    {
        var s = await SeedAsync("complete-concurrent", withEchoTask: false);
        await using var bcA = NewBackendContext();
        await using var ipA = NewItemsPlanningContext();
        await using var bcB = NewBackendContext();
        await using var ipB = NewItemsPlanningContext();
        var request = LegacyComplete(s);
        request.RequestId = NewRequestId();
        request.FieldValues.Add(new FieldValueWrite { FieldId = s.TextFieldId, Value = "once" });

        var responses = await Task.WhenAll(
            s.ServiceOn(bcA, ipA).CompleteEvent(request.Clone(), new TestServerCallContext()),
            s.ServiceOn(bcB, ipB).CompleteEvent(request.Clone(), new TestServerCallContext()));

        var auditRows = await BackendConfigurationPnDbContext!.ComplianceVersions.AsNoTracking()
            .CountAsync(x => x.ComplianceId == s.ComplianceId && x.WorkflowState == Constants.WorkflowStates.Removed);
        Assert.Multiple(() =>
        {
            Assert.That(responses.Count(r => !r.Replayed), Is.EqualTo(1), "exactly one delivery applied");
            Assert.That(responses.All(r => !r.AnswersDiscarded), Is.True);
            Assert.That(auditRows, Is.EqualTo(1), "the compliance was claimed once");
            Assert.That(s.Logger.Entries.Any(e => e.Level == LogLevel.Error), Is.False);
        });
    }

    // ---------------- UploadPhoto ----------------

    [Test]
    public async Task UploadPhoto_SameRequestIdRepeated_ReturnsSameStorageId_NoDuplicateRows()
    {
        var s = await SeedAsync("upload-replay", withEchoTask: false);
        var meta = LegacyPhotoMeta(s);
        meta.RequestId = NewRequestId();

        var first = await UploadAsync(s, meta);
        var storedAfterFirst = s.StoredFiles.Count;
        var second = await UploadAsync(s, meta);

        var id = int.Parse(first.StorageId, CultureInfo.InvariantCulture);
        Assert.Multiple(async () =>
        {
            Assert.That(first.Replayed, Is.False);
            Assert.That(second.StorageId, Is.EqualTo(first.StorageId));
            Assert.That(second.Replayed, Is.True);
            Assert.That(await PictureFieldValueRowsAsync(s), Is.EqualTo(1), "no second Picture FieldValue row");
            Assert.That(await MicrotingDbContext!.UploadedDatas.AsNoTracking()
                .CountAsync(u => u.Id > id), Is.EqualTo(0), "no second UploadedData row");
            Assert.That(s.StoredFiles.Count, Is.EqualTo(storedAfterFirst), "nothing stored again");
            Assert.That((await MicrotingDbContext.UploadedDatas.AsNoTracking().FirstAsync(u => u.Id == id))
                .WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        });
    }

    [Test]
    public async Task UploadPhoto_IdenticalRequestsConcurrently_StoreOnce()
    {
        var s = await SeedAsync("upload-concurrent", withEchoTask: false);
        await using var bcA = NewBackendContext();
        await using var ipA = NewItemsPlanningContext();
        await using var bcB = NewBackendContext();
        await using var ipB = NewItemsPlanningContext();
        var meta = LegacyPhotoMeta(s);
        meta.RequestId = NewRequestId();

        Task<UploadPhotoResponse> Upload(EventsGrpcService service) => service.UploadPhoto(
            new ListStreamReader<UploadPhotoChunk>(
            [
                new UploadPhotoChunk { Meta = meta.Clone() },
                new UploadPhotoChunk { Chunk = ByteString.CopyFrom(OnePixelPng) }
            ]),
            new TestServerCallContext());

        var maxIdBefore = await MicrotingDbContext!.UploadedDatas.AsNoTracking()
            .Select(u => (int?)u.Id).MaxAsync() ?? 0;

        var responses = await Task.WhenAll(Upload(s.ServiceOn(bcA, ipA)), Upload(s.ServiceOn(bcB, ipB)));

        var newRows = await MicrotingDbContext.UploadedDatas.AsNoTracking()
            .Where(u => u.Id > maxIdBefore).Select(u => new { u.Id, u.WorkflowState }).ToListAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(responses[1].StorageId, Is.EqualTo(responses[0].StorageId));
            Assert.That(responses.Count(r => r.Replayed), Is.EqualTo(1));
            Assert.That(await PictureFieldValueRowsAsync(s), Is.EqualTo(1));
            // A surplus row (when both prepared before either attached) is
            // soft-deleted, never left live and never hard-deleted.
            Assert.That(newRows.Where(r => r.WorkflowState != Constants.WorkflowStates.Removed).Select(r => r.Id),
                Is.EqualTo(new[] { int.Parse(responses[0].StorageId, CultureInfo.InvariantCulture) }));
        });
    }

    /// <summary>
    /// The slow storage work of a request-id upload runs outside the
    /// occurrence lock: while an upload is held inside its storage write, an
    /// old app's CompleteEvent on the same occurrence completes at once.
    /// </summary>
    [Test]
    public async Task UploadPhoto_HeldInStorage_DoesNotBlockLegacyCompleteEvent()
    {
        var s = await SeedAsync("upload-not-blocking", withEchoTask: false);
        await using var bcUpload = NewBackendContext();
        await using var ipUpload = NewItemsPlanningContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Storage.BeforeStore = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var meta = LegacyPhotoMeta(s);
        meta.RequestId = NewRequestId();
        var upload = s.ServiceOn(bcUpload, ipUpload).UploadPhoto(
            new ListStreamReader<UploadPhotoChunk>(
            [
                new UploadPhotoChunk { Meta = meta },
                new UploadPhotoChunk { Chunk = ByteString.CopyFrom(OnePixelPng) }
            ]),
            new TestServerCallContext());

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

            var complete = s.Service.CompleteEvent(LegacyComplete(s), new TestServerCallContext());
            var finished = await Task.WhenAny(complete, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.That(finished, Is.SameAs(complete), "CompleteEvent must not wait behind the held upload");
            Assert.That((await complete).Event.Completed, Is.True);
        }
        finally
        {
            release.TrySetResult();
            s.Storage.BeforeStore = null;
        }

        var uploaded = await upload;
        Assert.Multiple(async () =>
        {
            Assert.That(uploaded.Replayed, Is.False);
            Assert.That(await PictureFieldValueRowsAsync(s), Is.EqualTo(1), "the upload still attached afterwards");
        });
    }

    // ---------------- RemovePhoto ----------------

    /// <summary>
    /// expected_storage_id guards slot reuse: a stale remove (slot now holds a
    /// newer photo) removes nothing and says so; the right one removes; its
    /// pure repeat is a replay even after yet another upload into the slot.
    /// </summary>
    [Test]
    public async Task RemovePhoto_ExpectedStorageId_StaleIsRefused_RepeatIsReplayNotConflict()
    {
        var s = await SeedAsync("remove-expected", withEchoTask: false);
        var metaA = LegacyPhotoMeta(s);
        metaA.RequestId = NewRequestId();
        var photoA = await UploadAsync(s, metaA);
        var metaB = LegacyPhotoMeta(s);
        metaB.RequestId = NewRequestId();
        var photoB = await UploadAsync(s, metaB);

        var stale = LegacyRemove(s);
        stale.RequestId = NewRequestId();
        stale.ExpectedStorageId = photoA.StorageId;
        var staleResponse = await s.Service.RemovePhoto(stale, new TestServerCallContext());

        var idB = int.Parse(photoB.StorageId, CultureInfo.InvariantCulture);
        Assert.Multiple(async () =>
        {
            Assert.That(staleResponse.Outcome, Is.EqualTo(RemovePhotoOutcome.SlotHoldsOtherPhoto));
            Assert.That(staleResponse.CurrentStorageId, Is.EqualTo(photoB.StorageId));
            Assert.That((await MicrotingDbContext!.UploadedDatas.AsNoTracking().FirstAsync(u => u.Id == idB))
                .WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed), "B was not removed");
        });

        var right = LegacyRemove(s);
        right.RequestId = NewRequestId();
        right.ExpectedStorageId = photoB.StorageId;
        var removed = await s.Service.RemovePhoto(right, new TestServerCallContext());

        var metaC = LegacyPhotoMeta(s);
        metaC.RequestId = NewRequestId();
        var photoC = await UploadAsync(s, metaC);
        var repeat = await s.Service.RemovePhoto(right, new TestServerCallContext());

        var idC = int.Parse(photoC.StorageId, CultureInfo.InvariantCulture);
        Assert.Multiple(async () =>
        {
            Assert.That(removed.Outcome, Is.EqualTo(RemovePhotoOutcome.Removed));
            Assert.That(removed.Replayed, Is.False);
            Assert.That((await MicrotingDbContext!.UploadedDatas.AsNoTracking().FirstAsync(u => u.Id == idB))
                .WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(repeat.Replayed, Is.True);
            Assert.That(repeat.Outcome, Is.EqualTo(RemovePhotoOutcome.Removed), "a pure repeat is not a conflict");
            Assert.That((await MicrotingDbContext.UploadedDatas.AsNoTracking().FirstAsync(u => u.Id == idC))
                .WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed), "the newer photo C survived");
        });
    }

    [Test]
    public async Task RemovePhoto_ExpectedStorageIdOnEmptySlot_IsAlreadyAbsent()
    {
        var s = await SeedAsync("remove-absent", withEchoTask: false);
        var request = LegacyRemove(s);
        request.ExpectedStorageId = "123456";

        var response = await s.Service.RemovePhoto(request, new TestServerCallContext());

        Assert.That(response.Outcome, Is.EqualTo(RemovePhotoOutcome.AlreadyAbsent));
    }

    // ---------------- SetComment / SetFieldValue ----------------

    [Test]
    public async Task SetComment_WithRequestId_CarriesRealIds_RepeatIsReplay_NeverOverwritesNewer()
    {
        var s = await SeedAsync("comment-replay");
        var first = LegacyComment(s, "first");
        first.RequestId = NewRequestId();

        var applied = await s.Service.SetComment(first, new TestServerCallContext());
        var newer = LegacyComment(s, "newer");
        newer.RequestId = NewRequestId();
        await s.Service.SetComment(newer, new TestServerCallContext());
        var lateRepeat = await s.Service.SetComment(first, new TestServerCallContext());

        Assert.Multiple(async () =>
        {
            Assert.That(applied.Replayed, Is.False);
            Assert.That(applied.Event.ComplianceId, Is.EqualTo(s.ComplianceId), "real compliance_id (item 5)");
            Assert.That(applied.Event.MicrotingSdkCaseId, Is.EqualTo(s.SdkCaseId));
            Assert.That(applied.Event.Comment, Is.EqualTo("first"));
            Assert.That(applied.Event.HasVersion, Is.True);
            Assert.That(applied.Event.DueDateKey, Is.EqualTo(s.DayKey));
            Assert.That(lateRepeat.Replayed, Is.True);
            Assert.That(lateRepeat.Event.Comment, Is.EqualTo("newer"), "echoes what is stored now");
            Assert.That(await CaseCustomAsync(s), Does.Contain("\"text\":\"newer\""), "the newer comment survived");
        });
    }

    [Test]
    public async Task SetFieldValue_WithRequestId_CarriesRealIds_RepeatIsReplay_EvenAfterCompletion()
    {
        var s = await SeedAsync("fv-replay");
        var request = LegacyFieldValue(s, "v1");
        request.RequestId = NewRequestId();

        var first = await s.Service.SetFieldValue(request, new TestServerCallContext());
        var fieldValueVersion = await MicrotingDbContext!.FieldValues.AsNoTracking()
            .Where(fv => fv.CaseId == s.SdkCaseId && fv.FieldId == s.TextFieldId)
            .Select(fv => fv.Version).FirstAsync();
        var repeat = await s.Service.SetFieldValue(request, new TestServerCallContext());
        await s.Service.CompleteEvent(LegacyComplete(s), new TestServerCallContext());
        var repeatAfterCompletion = await s.Service.SetFieldValue(request, new TestServerCallContext());

        Assert.Multiple(async () =>
        {
            Assert.That(first.Replayed, Is.False);
            Assert.That(first.Event.ComplianceId, Is.EqualTo(s.ComplianceId), "real compliance_id (item 5)");
            Assert.That(first.Event.MicrotingSdkCaseId, Is.EqualTo(s.SdkCaseId));
            Assert.That(first.Event.Fields.Single(f => f.Id == s.TextFieldId).Value, Is.EqualTo("v1"));
            Assert.That(repeat.Replayed, Is.True);
            Assert.That(await MicrotingDbContext.FieldValues.AsNoTracking()
                    .Where(fv => fv.CaseId == s.SdkCaseId && fv.FieldId == s.TextFieldId)
                    .Select(fv => fv.Version).FirstAsync(), Is.EqualTo(fieldValueVersion),
                "the repeat wrote nothing");
            Assert.That(repeatAfterCompletion.Replayed, Is.True, "not FAILED_PRECONDITION");
        });
    }

    // ---------------- Envelope tolerance and bound ----------------

    /// <summary>
    /// Non-JSON legacy <c>Custom</c> content never fails a request-id call and
    /// is never overwritten by request-id bookkeeping alone.
    /// </summary>
    [Test]
    public async Task Envelope_NonJsonLegacyCustom_IsToleratedAndLeftAlone()
    {
        var s = await SeedAsync("legacy-custom", withEchoTask: false);
        const string legacy = "free text written by something else";
        await SetCaseCustomAsync(s, legacy);

        var remove = LegacyRemove(s);
        remove.RequestId = NewRequestId();
        var removeResponse = await s.Service.RemovePhoto(remove, new TestServerCallContext());
        var customAfterRemove = await CaseCustomAsync(s);

        var complete = LegacyComplete(s);
        complete.RequestId = NewRequestId();
        var completeResponse = await s.Service.CompleteEvent(complete, new TestServerCallContext());

        Assert.Multiple(async () =>
        {
            Assert.That(removeResponse.Outcome, Is.EqualTo(RemovePhotoOutcome.AlreadyAbsent));
            Assert.That(customAfterRemove, Is.EqualTo(legacy));
            Assert.That(completeResponse.Event.Completed, Is.True);
            Assert.That(await CaseCustomAsync(s), Is.EqualTo(legacy), "the completion did not overwrite it");
        });
    }

    /// <summary>
    /// An envelope written by an older server (no request-id keys) is read
    /// and extended without losing what it held.
    /// </summary>
    [Test]
    public async Task Envelope_WrittenByOlderServer_IsExtendedWithoutLoss()
    {
        var s = await SeedAsync("old-envelope");
        await SetCaseCustomAsync(s,
            "{\"event_comment\":{\"text\":\"old\",\"ts_unix\":1},\"event_photos\":[{\"slot\":3,\"uploaded_data_id\":42,\"ts_unix\":1,\"content_type\":\"image/png\"}]}");

        var remove = LegacyRemove(s, slot: 5);
        remove.RequestId = NewRequestId();
        var response = await s.Service.RemovePhoto(remove, new TestServerCallContext());

        var custom = await CaseCustomAsync(s);
        Assert.Multiple(() =>
        {
            Assert.That(response.Outcome, Is.EqualTo(RemovePhotoOutcome.AlreadyAbsent));
            Assert.That(custom, Does.Contain("\"text\":\"old\""));
            Assert.That(custom, Does.Contain("\"uploaded_data_id\":42"));
            Assert.That(custom, Does.Contain(remove.RequestId));
        });
    }

    [Test]
    public async Task Envelope_RemembersOnlyTheLast16RequestIds()
    {
        var s = await SeedAsync("envelope-bound");
        var ids = Enumerable.Range(0, 20).Select(_ => NewRequestId()).ToList();
        foreach (var id in ids)
        {
            var request = LegacyComment(s, $"c-{id}");
            request.RequestId = id;
            await s.Service.SetComment(request, new TestServerCallContext());
        }

        var custom = await CaseCustomAsync(s);
        var oldest = LegacyComment(s, $"c-{ids[0]}");
        oldest.RequestId = ids[0];
        var newest = LegacyComment(s, $"c-{ids[^1]}");
        newest.RequestId = ids[^1];

        Assert.Multiple(async () =>
        {
            Assert.That(ids.Count(custom.Contains), Is.EqualTo(16), "bounded to the last 16");
            Assert.That(custom, Does.Not.Contain(ids[0]));
            Assert.That((await s.Service.SetComment(newest, new TestServerCallContext())).Replayed, Is.True);
            Assert.That((await s.Service.SetComment(oldest, new TestServerCallContext())).Replayed, Is.False,
                "an evicted id is applied again (documented limit)");
        });
    }

    // ---------------- GetEvent ----------------

    [Test]
    public async Task GetEvent_Found_MatchesListShapeAndEtag()
    {
        var s = await SeedAsync("get-found");
        var listed = await ListSingleAsync(s);

        var response = await s.Service.GetEvent(new GetEventRequest
        {
            Key = new EventKey { EventId = s.EventId, PlanDayKey = s.DayKey, ComplianceId = s.ComplianceId }
        }, new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(response.Found, Is.True);
            Assert.That(response.Event, Is.EqualTo(listed), "same path and shape as ListEvents");
        });
    }

    [Test]
    public async Task GetEvent_NotFound_ForUnknownWrongDayOrOtherCompliance()
    {
        var s = await SeedAsync("get-missing");

        async Task<GetEventResponse> Get(EventKey key) =>
            await s.Service.GetEvent(new GetEventRequest { Key = key }, new TestServerCallContext());

        var unknown = await Get(new EventKey { EventId = "999999999", PlanDayKey = s.DayKey });
        var wrongDay = await Get(new EventKey
        {
            EventId = s.EventId,
            PlanDayKey = s.Deadline.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
        var otherCompliance = await Get(new EventKey
        {
            EventId = s.EventId, PlanDayKey = s.DayKey, ComplianceId = s.ComplianceId + 100000
        });

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Found, Is.False);
            Assert.That(unknown.Event, Is.Null);
            // The calendar stub ignores the day, so "wrong day" is decided by
            // plan_day_key matching; the real calendar would return nothing.
            Assert.That(wrongDay.Found, Is.False);
            Assert.That(otherCompliance.Found, Is.False);
        });
    }

    /// <summary>
    /// A task on a property the worker is not assigned to answers not-found,
    /// the same as a task that does not exist (no PERMISSION_DENIED, so ids
    /// cannot be probed).
    /// </summary>
    [Test]
    public async Task GetEvent_OtherPropertysTask_IsNotFound_NotPermissionDenied()
    {
        var s = await SeedAsync("get-other-property");
        s.Access.HasAccessAsync(s.SiteId, s.PropertyId).Returns(Task.FromResult(false));

        var response = await s.Service.GetEvent(new GetEventRequest
        {
            Key = new EventKey { EventId = s.EventId, PlanDayKey = s.DayKey }
        }, new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(response.Found, Is.False);
            Assert.That(response.Event, Is.Null);
        });
    }

    /// <summary>
    /// A disabled account is rejected before any task lookup, so an unknown
    /// id and an existing one answer the same UNAUTHENTICATED.
    /// </summary>
    [Test]
    public async Task GetEvent_DisabledAccount_IsUnauthenticatedEvenForUnknownIds()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns<Task<int>>(_ =>
            throw new GrpcCore.RpcException(new GrpcCore.Status(GrpcCore.StatusCode.Unauthenticated, "account_disabled")));
        var service = new EventsGrpcService(
            Substitute.For<IBackendConfigurationCalendarService>(),
            Substitute.For<IBackendConfigurationPropertiesService>(),
            Substitute.For<IBackendConfigurationUserPropertyAccess>(),
            resolver,
            Substitute.For<IEFormCoreService>(),
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            Substitute.For<IEventDeployService>(),
            new RecordingLogger<EventsGrpcService>());

        var ex = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => service.GetEvent(
            new GetEventRequest { Key = new EventKey { EventId = "999999999", PlanDayKey = "2026-01-01" } },
            new TestServerCallContext()));

        Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.Unauthenticated));
    }

    /// <summary>
    /// Security: a worker assigned to property A who sends the compliance /
    /// case ids of property B's occurrence (with A's own event id) must get a
    /// reply that carries nothing derived from B's rows. B's rows are stamped
    /// far in the future, so any leak shows up as version >= that stamp.
    /// </summary>
    [Test]
    public async Task ForeignComplianceAndCaseIds_DoNotReachTheVersionStamp()
    {
        var a = await SeedAsync("scope-a");
        var b = await SeedAsync("scope-b");
        var future = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await BackendConfigurationPnDbContext!.Compliances.Where(x => x.Id == b.ComplianceId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, future));
        await MicrotingDbContext!.Cases.Where(x => x.Id == b.SdkCaseId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, future));
        await MicrotingDbContext.FieldValues.Where(x => x.CaseId == b.SdkCaseId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, future));
        var futureMicros = (future - DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond;

        var retap = LegacyComplete(a);
        retap.Completed = false;
        retap.RequestId = NewRequestId();
        retap.ComplianceId = b.ComplianceId;
        retap.MicrotingSdkCaseId = b.SdkCaseId;
        var retapReply = await a.Service.CompleteEvent(retap, new TestServerCallContext());

        var comment = LegacyComment(a, "probe");
        comment.RequestId = NewRequestId();
        comment.ComplianceId = b.ComplianceId;
        comment.MicrotingSdkCaseId = b.SdkCaseId;
        var commentReply = await a.Service.SetComment(comment, new TestServerCallContext());

        Assert.Multiple(async () =>
        {
            Assert.That(retapReply.Event.Version, Is.LessThan(futureMicros), "CompleteEvent(completed=false)");
            Assert.That(retapReply.Event.ComplianceId, Is.Not.EqualTo(b.ComplianceId));
            Assert.That(commentReply.Event.Version, Is.LessThan(futureMicros), "SetComment");
            Assert.That(commentReply.Event.ComplianceId, Is.EqualTo(0), "B's compliance is not resolved for A");
            Assert.That(await MicrotingDbContext.Cases.AsNoTracking().Where(c => c.Id == b.SdkCaseId)
                .Select(c => c.Custom).FirstAsync(), Is.Empty.Or.Null, "B's case was not written");
        });
    }

    // ---------------- PR #1516 review findings ----------------

    /// <summary>
    /// Finding 2: with request_id and compliance_id = 0, a retry after the
    /// first delivery claimed its occurrence must replay, not fall back to the
    /// earliest LIVE occurrence and complete the next rotation.
    /// </summary>
    [Test]
    public async Task CompleteEvent_RequestIdWithoutComplianceId_RetryDoesNotCompleteNextRotation()
    {
        var s = await SeedAsync("next-rotation", withEchoTask: false);
        var nextCase = new SdkCase
        {
            SiteId = s.SiteId, CheckListId = s.TemplateId, Status = 66,
            WorkflowState = Constants.WorkflowStates.Created, Custom = string.Empty
        };
        await MicrotingDbContext!.Cases.AddAsync(nextCase);
        await MicrotingDbContext.SaveChangesAsync();
        var current = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .FirstAsync(x => x.Id == s.ComplianceId);
        var next = new Compliance
        {
            PlanningId = s.PlanningId, PropertyId = s.PropertyId, AreaId = current.AreaId,
            Deadline = s.Deadline.AddDays(7), StartDate = s.Deadline,
            MicrotingSdkCaseId = nextCase.Id, MicrotingSdkeFormId = s.TemplateId,
            Version = 1, WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(next);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var request = LegacyComplete(s);
        request.RequestId = NewRequestId();
        request.ComplianceId = 0;
        request.MicrotingSdkCaseId = s.SdkCaseId;

        var first = await s.Service.CompleteEvent(request, new TestServerCallContext());
        var retry = await s.Service.CompleteEvent(request, new TestServerCallContext());

        var nextAfter = await BackendConfigurationPnDbContext.Compliances.AsNoTracking()
            .FirstAsync(x => x.Id == next.Id);
        Assert.Multiple(() =>
        {
            Assert.That(first.Replayed, Is.False);
            Assert.That(retry.Replayed, Is.True);
            Assert.That(nextAfter.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "the next rotation was not completed by the retry");
        });
    }

    /// <summary>
    /// Finding 3: the replay marker is published only after the bundled
    /// answers. While this request is still completing a repeat answers
    /// ABORTED (retry); if the completion stopped part-way (marker older than
    /// the in-flight window) a repeat gets today's honest "already completed,
    /// answers discarded" answer, never replayed=true.
    /// </summary>
    [Test]
    public async Task CompleteEvent_OwnCompletionInFlight_IsAborted_StalePartialIsNotReplayed()
    {
        var s = await SeedAsync("in-flight", withEchoTask: false);
        var requestId = NewRequestId();
        var compliance = await BackendConfigurationPnDbContext!.Compliances.FirstAsync(x => x.Id == s.ComplianceId);
        await compliance.Delete(BackendConfigurationPnDbContext);
        var theCase = await MicrotingDbContext!.Cases.FirstAsync(c => c.Id == s.SdkCaseId);
        theCase.Status = 100;
        theCase.DoneAt = DateTime.UtcNow;
        theCase.Custom = "{\"completing_request_id\":\"" + requestId + "\",\"completing_ts_unix\":"
                         + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + "}";
        await theCase.Update(MicrotingDbContext);

        var request = LegacyComplete(s);
        request.RequestId = requestId;
        request.FieldValues.Add(new FieldValueWrite { FieldId = s.TextFieldId, Value = "x" });
        var aborted = await Assert.ThrowsAsync<GrpcCore.RpcException>(() =>
            s.Service.CompleteEvent(request, new TestServerCallContext()));

        await SetCaseCustomAsync(s, "{\"completing_request_id\":\"" + requestId + "\",\"completing_ts_unix\":"
                                    + DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds() + "}");
        var afterFailure = await s.Service.CompleteEvent(request, new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(aborted!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.Aborted));
            Assert.That(afterFailure.Replayed, Is.False, "a partial completion is never reported as replayed");
            Assert.That(afterFailure.AnswersDiscarded, Is.True, "today's honest answer");
        });
    }

    [Test]
    public async Task CompleteEvent_WithRequestId_PublishesReplayMarkerAndClearsInFlight()
    {
        var s = await SeedAsync("publish", withEchoTask: false);
        var request = LegacyComplete(s);
        request.RequestId = NewRequestId();
        request.Comment = "bundled";
        await s.Service.CompleteEvent(request, new TestServerCallContext());

        var custom = await CaseCustomAsync(s);
        Assert.Multiple(() =>
        {
            Assert.That(custom, Does.Contain("\"completed_request_id\":\"" + request.RequestId + "\""));
            Assert.That(custom, Does.Not.Contain("completing_request_id"));
            Assert.That(custom, Does.Contain("\"text\":\"bundled\""), "the bundled comment survived the publish");
        });
    }

    /// <summary>
    /// Finding 4: a completion that claims the occurrence after SetFieldValue's
    /// lookup but before its lock must win: under the lock the request-id
    /// path re-reads the state and refuses, writing nothing.
    /// </summary>
    [Test]
    public async Task SetFieldValue_WithRequestId_ClosedWhileWaitingForLock_IsRefused()
    {
        var s = await SeedAsync("fv-lock");
        await using var holder = NewBackendContext();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlAsync(
            $"SELECT `Id` FROM `Compliances` WHERE `Id` = {s.ComplianceId} FOR UPDATE");

        var request = LegacyFieldValue(s, "must-not-land");
        request.RequestId = NewRequestId();
        var write = s.Service.SetFieldValue(request, new TestServerCallContext());

        // Let the write pass its (non-locking) lookup and queue on the lock,
        // then close the occurrence the way the completion claim does.
        await Task.Delay(TimeSpan.FromSeconds(3));
        await holder.Database.ExecuteSqlAsync(
            $"UPDATE `Compliances` SET `WorkflowState` = 'removed' WHERE `Id` = {s.ComplianceId}");
        await tx.CommitAsync();

        var ex = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => write);
        var value = await MicrotingDbContext!.FieldValues.AsNoTracking()
            .Where(fv => fv.CaseId == s.SdkCaseId && fv.FieldId == s.TextFieldId)
            .Select(fv => fv.Value).FirstAsync();
        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.FailedPrecondition));
            Assert.That(value, Is.Not.EqualTo("must-not-land"));
        });
    }

    /// <summary>
    /// Finding 5: bookkeeping-only writes never rewrite a Custom JSON object
    /// that carries keys the envelope does not model.
    /// </summary>
    [Test]
    public async Task Envelope_WithForeignKeys_IsNotRewrittenByBookkeeping()
    {
        var s = await SeedAsync("foreign-keys");
        const string foreign = "{\"external_reference\":\"123\"}";
        await SetCaseCustomAsync(s, foreign);

        var remove = LegacyRemove(s);
        remove.RequestId = NewRequestId();
        await s.Service.RemovePhoto(remove, new TestServerCallContext());
        var afterRemove = await CaseCustomAsync(s);

        var fieldValue = LegacyFieldValue(s, "v");
        fieldValue.RequestId = NewRequestId();
        await s.Service.SetFieldValue(fieldValue, new TestServerCallContext());
        var afterFieldValue = await CaseCustomAsync(s);

        var complete = LegacyComplete(s);
        complete.RequestId = NewRequestId();
        await s.Service.CompleteEvent(complete, new TestServerCallContext());

        Assert.Multiple(async () =>
        {
            Assert.That(afterRemove, Is.EqualTo(foreign));
            Assert.That(afterFieldValue, Is.EqualTo(foreign));
            Assert.That(await CaseCustomAsync(s), Is.EqualTo(foreign));
        });
    }

    /// <summary>
    /// Finding 6: a calendar row carrying a compliance id but no Deadline
    /// (the recurrence fallback) still gets due_date_key from the compliance.
    /// </summary>
    [Test]
    public async Task DueDateKey_ResolvedFromComplianceWhenCalendarRowLacksDeadline()
    {
        var s = await SeedAsync("due-fallback");
        s.Tasks[0].Deadline = null;
        s.Tasks[0].TaskDate = s.Deadline.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var evt = (await s.Service.ListEvents(new ListEventsRequest
        {
            PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture),
            FromDateKey = s.DayKey,
            ToDateKey = s.DayKey
        }, new TestServerCallContext())).Events.Single();

        Assert.That(evt.DueDateKey, Is.EqualTo(s.DayKey), "the compliance deadline, not TaskDate");
    }

    /// <summary>
    /// Finding 1 (residual window): if a legacy writer holding a stale copy
    /// erases a request-id record, the repeat is applied again (today's
    /// behaviour) - it is never reported as replayed without being applied.
    /// </summary>
    [Test]
    public async Task LostRecord_DegradesToReapply_NeverToAFalseReplay()
    {
        var s = await SeedAsync("lost-record", withEchoTask: false);
        var meta = LegacyPhotoMeta(s);
        meta.RequestId = NewRequestId();
        var first = await UploadAsync(s, meta);
        await SetCaseCustomAsync(s, "{\"event_comment\":null,\"event_photos\":[{\"slot\":0,\"uploaded_data_id\":"
                                    + first.StorageId + ",\"ts_unix\":1,\"content_type\":\"image/png\"}]}");

        var repeat = await UploadAsync(s, meta);

        Assert.Multiple(() =>
        {
            Assert.That(repeat.Replayed, Is.False);
            Assert.That(repeat.StorageId, Is.Not.EqualTo(first.StorageId), "re-applied, as an old app would");
        });
    }

    // ---------------- version / etag / stream ----------------

    [Test]
    public async Task Version_IncreasesAfterAWrite_EtagStableUntilContentChanges()
    {
        var s = await SeedAsync("version");
        var before = await ListSingleAsync(s);
        var again = await ListSingleAsync(s);
        // SDK Cases.UpdatedAt is stored at whole-second precision, so a write
        // in the same second as the seed would tie (documented limitation).
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        var comment = LegacyComment(s, "changed");
        comment.RequestId = NewRequestId();
        await s.Service.SetComment(comment, new TestServerCallContext());
        var after = await ListSingleAsync(s);

        Assert.Multiple(() =>
        {
            Assert.That(again.Etag, Is.EqualTo(before.Etag), "unchanged content, equal etag");
            Assert.That(again.Version, Is.EqualTo(before.Version));
            Assert.That(after.Version, Is.GreaterThan(before.Version), "the write moved the version forward");
            Assert.That(after.Etag, Is.Not.EqualTo(before.Etag), "changed content, different etag");
            Assert.That(after.Comment, Is.EqualTo("changed"));
        });
    }

    /// <summary>
    /// The stream hashes the Event without version/etag: a version that moves
    /// with no visible change (an UpdatedAt bump) does not resend; a real
    /// content change still does. Takes two poll intervals (~12 s).
    /// </summary>
    [Test]
    public async Task Stream_DoesNotResendWhenOnlyVersionMoves_ButDoesOnContentChange()
    {
        var s = await SeedAsync("stream");
        var writer = new RecordingStreamWriter<EventChange>();
        using var cts = new CancellationTokenSource();
        // Version stamping is the only reader of CalendarConfigurations on
        // the stream's own plugin context (the calendar service is stubbed).
        var versionQueries = new CommandCounter("`CalendarConfigurations`");
        await using var streamBc = NewBackendContext(versionQueries);
        await using var streamIp = NewItemsPlanningContext();
        var streamService = s.ServiceOn(streamBc, streamIp);

        var streaming = streamService.StreamEventChanges(
            new StreamEventChangesRequest { PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture) },
            writer, new TestServerCallContext(cts.Token));

        try
        {
            await WaitForAsync(() => writer.Snapshot().Count >= 1, TimeSpan.FromSeconds(30));
            var initialVersion = writer.Snapshot()[0].Upserted.Version;
            var initialStamped = versionQueries.Count;
            versionQueries.Reset();

            // Bump the ARP's UpdatedAt without changing anything visible.
            await BackendConfigurationPnDbContext!.AreaRulePlannings
                .Where(x => x.Id == s.ArpId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, DateTime.UtcNow.AddSeconds(5)));
            // Comment write below must land in a later second than the seed.
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
            var versionMoved = (await ListSingleAsync(s)).Version;
            await Task.Delay(TimeSpan.FromSeconds(11));
            var afterVersionBump = writer.Snapshot().Count;
            var versionQueriesWhileUnchanged = versionQueries.Count;

            var comment = LegacyComment(s, "visible change");
            await s.Service.SetComment(comment, new TestServerCallContext());
            await WaitForAsync(() => writer.Snapshot().Count >= 2, TimeSpan.FromSeconds(30));

            Assert.Multiple(() =>
            {
                Assert.That(versionMoved, Is.GreaterThan(initialVersion), "precondition: the version moved");
                Assert.That(afterVersionBump, Is.EqualTo(1), "no resend for a version-only change");
                Assert.That(initialStamped, Is.GreaterThan(0), "the initial snapshot is stamped");
                Assert.That(versionQueriesWhileUnchanged, Is.EqualTo(0), "unchanged polls run no version queries");
                Assert.That(writer.Snapshot()[1].Upserted.Comment, Is.EqualTo("visible change"));
                Assert.That(writer.Snapshot()[1].Upserted.HasVersion, Is.True, "a sent change carries version");
                Assert.That(writer.Snapshot()[1].Upserted.Etag, Is.Not.Empty, "a sent change carries etag");
            });
        }
        finally
        {
            await cts.CancelAsync();
            await streaming;
        }
    }

    // ---------------- PR #1516 review round 2 ----------------

    private async Task<(Compliance Later, SdkCase LaterCase)> SeedLaterOccurrenceAsync(Scenario s)
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var laterCase = new SdkCase
        {
            SiteId = s.SiteId, CheckListId = s.TemplateId, Status = 66,
            WorkflowState = Constants.WorkflowStates.Created, Custom = string.Empty
        };
        await MicrotingDbContext.Cases.AddAsync(laterCase);
        await MicrotingDbContext.SaveChangesAsync();
        await core.CaseRead(laterCase.Id, language);
        var earliest = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .FirstAsync(x => x.Id == s.ComplianceId);
        var later = new Compliance
        {
            PlanningId = s.PlanningId, PropertyId = s.PropertyId, AreaId = earliest.AreaId,
            Deadline = s.Deadline.AddDays(7), StartDate = s.Deadline,
            MicrotingSdkCaseId = laterCase.Id, MicrotingSdkeFormId = s.TemplateId,
            Version = 1, WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(later);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return (later, laterCase);
    }

    private Task<string> ComplianceStateAsync(int complianceId) =>
        BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .Where(x => x.Id == complianceId).Select(x => x.WorkflowState ?? string.Empty).FirstAsync();

    private Task<int> CaseStatusAsync(int caseId) =>
        MicrotingDbContext!.Cases.AsNoTracking().Where(c => c.Id == caseId).Select(c => c.Status ?? 0).FirstAsync();

    /// <summary>
    /// Round 2, finding 1: a request-id completion with compliance_id = 0
    /// that names SDK case B acts on B only, even when an older live
    /// occurrence A of the same planning would win the legacy "earliest
    /// live" lookup - on the first delivery and on the retry.
    /// </summary>
    [Test]
    public async Task CompleteEvent_RequestIdNamingALaterCase_CompletesOnlyThatCase_FirstDeliveryAndRetry()
    {
        var s = await SeedAsync("named-later-case", withEchoTask: false);
        var (later, laterCase) = await SeedLaterOccurrenceAsync(s);

        var request = LegacyComplete(s);
        request.RequestId = NewRequestId();
        request.ComplianceId = 0;
        request.MicrotingSdkCaseId = laterCase.Id;

        var first = await s.Service.CompleteEvent(request, new TestServerCallContext());
        var earliestAfterFirst = await ComplianceStateAsync(s.ComplianceId);
        var retry = await s.Service.CompleteEvent(request, new TestServerCallContext());

        var earliestAfterRetry = await ComplianceStateAsync(s.ComplianceId);
        var laterAfter = await ComplianceStateAsync(later.Id);
        var earliestCaseStatus = await CaseStatusAsync(s.SdkCaseId);
        var laterCaseStatus = await CaseStatusAsync(laterCase.Id);
        Assert.Multiple(() =>
        {
            Assert.That(earliestAfterFirst, Is.EqualTo(Constants.WorkflowStates.Created),
                "the first delivery did not complete the earliest occurrence");
            Assert.That(earliestAfterRetry, Is.EqualTo(Constants.WorkflowStates.Created),
                "the retry did not complete the earliest occurrence either");
            Assert.That(earliestCaseStatus, Is.EqualTo(66), "the earliest case stays open");
            Assert.That(laterAfter, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the named occurrence was completed");
            Assert.That(laterCaseStatus, Is.EqualTo(100));
            Assert.That(first.Replayed, Is.False);
            Assert.That(retry.Replayed, Is.True);
        });
    }

    /// <summary>
    /// Round 2, finding 1 (legacy pin): WITHOUT a request id the same
    /// request keeps today's earliest-live-occurrence fallback.
    /// </summary>
    [Test]
    public async Task Legacy_CompleteEvent_ComplianceIdZero_StillCompletesEarliestLiveOccurrence()
    {
        var s = await SeedAsync("legacy-earliest", withEchoTask: false);
        var (later, laterCase) = await SeedLaterOccurrenceAsync(s);

        var request = LegacyComplete(s);
        request.ComplianceId = 0;
        request.MicrotingSdkCaseId = laterCase.Id;
        await s.Service.CompleteEvent(request, new TestServerCallContext());

        var earliestAfter = await ComplianceStateAsync(s.ComplianceId);
        var laterAfter = await ComplianceStateAsync(later.Id);
        Assert.Multiple(() =>
        {
            Assert.That(earliestAfter, Is.EqualTo(Constants.WorkflowStates.Removed),
                "legacy: the earliest live occurrence is completed, as today");
            Assert.That(laterAfter, Is.EqualTo(Constants.WorkflowStates.Created));
        });
    }

    /// <summary>
    /// Round 2, finding 4: GetEvent selects the requested occurrence from
    /// the calendar rows BEFORE hydrating, so unrelated tasks on the same
    /// day are not hydrated (no envelope / SDK case read, no per-task
    /// attachment query, no version stamp).
    /// </summary>
    [Test]
    public async Task GetEvent_DoesNotHydrateUnrelatedTasksOfTheDay()
    {
        var s = await SeedAsync("get-filter-first");
        var listed = await ListSingleAsync(s);
        for (var i = 0; i < 5; i++)
        {
            s.Tasks.Add(new CalendarTaskResponseModel
            {
                Id = s.ArpId + 1_000_000 + i,
                Title = $"unrelated-{i}",
                TaskDate = s.DayKey,
                Status = true,
                PropertyId = s.PropertyId,
                // Template-less rows are always deliverable, so hydrating
                // them would cost one attachment query each.
                EformId = null
            });
        }

        var attachmentQueries = new CommandCounter("`AreaRulePlanningFiles`");
        await using var bc = NewBackendContext(attachmentQueries);
        await using var ip = NewItemsPlanningContext();
        var service = s.ServiceOn(bc, ip);
        var response = await service.GetEvent(new GetEventRequest
        {
            Key = new EventKey { EventId = s.EventId, PlanDayKey = s.DayKey, ComplianceId = s.ComplianceId }
        }, new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(response.Found, Is.True);
            Assert.That(response.Event, Is.EqualTo(listed), "same Event as the one-task list");
            // One attachment read for the matched event + one batched version stamp.
            Assert.That(attachmentQueries.Count, Is.EqualTo(2),
                "attachment queries do not scale with the day's other tasks");
        });
    }

    /// <summary>
    /// Round 2, finding 5: a failed calendar read answers UNAVAILABLE from
    /// GetEvent (retry), never found=false. ListEvents keeps today's empty
    /// answer for the same failure.
    /// </summary>
    [Test]
    public async Task GetEvent_FailedCalendarRead_IsUnavailable_ListEventsUnchanged()
    {
        var s = await SeedAsync("get-read-failure");
        s.Calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(_ => Task.FromResult(new OperationDataResult<List<CalendarTaskResponseModel>>(
                false, "ErrorWhileGettingCalendarTasks: boom")));

        var thrown = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => s.Service.GetEvent(new GetEventRequest
        {
            Key = new EventKey { EventId = s.EventId, PlanDayKey = s.DayKey }
        }, new TestServerCallContext()));
        var listed = await s.Service.ListEvents(new ListEventsRequest
        {
            PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture),
            FromDateKey = s.DayKey,
            ToDateKey = s.DayKey
        }, new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(thrown.StatusCode, Is.EqualTo(GrpcCore.StatusCode.Unavailable));
            Assert.That(listed.Events, Is.Empty, "ListEvents: today's empty answer");
        });
    }

    // ---------------- PR #1516 review round 3 ----------------

    private Task<string> CaseCustomOfAsync(int caseId) =>
        MicrotingDbContext!.Cases.AsNoTracking()
            .Where(c => c.Id == caseId).Select(c => c.Custom ?? string.Empty).FirstAsync();

    private Task<string> TextValueOfAsync(Scenario s, int caseId) =>
        MicrotingDbContext!.FieldValues.AsNoTracking()
            .Where(fv => fv.CaseId == caseId && fv.FieldId == s.TextFieldId)
            .Select(fv => fv.Value ?? string.Empty).FirstAsync();

    /// <summary>
    /// Round 3, finding 1: Core.CaseRead returns null (it swallows its own
    /// exception) for a case that EXISTS but cannot be read. GetEvent answers
    /// UNAVAILABLE instead of found=false; ListEvents keeps skipping the task.
    /// </summary>
    [Test]
    public async Task GetEvent_FormReadFailureOfTheMatchedOccurrence_IsUnavailable_ListEventsUnchanged()
    {
        var s = await SeedAsync("get-form-read-failure");
        // CheckRead looks up the case's checklist with FirstAsync: a case
        // without one makes the read fail inside the SDK.
        await MicrotingDbContext!.Cases.Where(c => c.Id == s.SdkCaseId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.CheckListId, (int?)null));

        var thrown = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => s.Service.GetEvent(new GetEventRequest
        {
            Key = new EventKey { EventId = s.EventId, PlanDayKey = s.DayKey, ComplianceId = s.ComplianceId }
        }, new TestServerCallContext()));
        var listed = await s.Service.ListEvents(new ListEventsRequest
        {
            PropertyId = s.PropertyId.ToString(CultureInfo.InvariantCulture),
            FromDateKey = s.DayKey,
            ToDateKey = s.DayKey
        }, new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(thrown.StatusCode, Is.EqualTo(GrpcCore.StatusCode.Unavailable));
            Assert.That(listed.Events, Is.Empty, "ListEvents: the task is skipped, as today");
        });
    }

    /// <summary>
    /// Round 3, finding 1 (unchanged half): a calendar row whose SDK case does
    /// not exist (the create-to-deploy window) is still found=false.
    /// </summary>
    [Test]
    public async Task GetEvent_CaseNotCreatedYet_IsStillNotFound()
    {
        var s = await SeedAsync("get-case-not-yet");
        s.Tasks[0].SdkCaseId = 2_000_000_000;

        var response = await s.Service.GetEvent(new GetEventRequest
        {
            Key = new EventKey { EventId = s.EventId, PlanDayKey = s.DayKey, ComplianceId = s.ComplianceId }
        }, new TestServerCallContext());

        Assert.That(response.Found, Is.False);
    }

    /// <summary>Round 3, finding 2: SetComment with a request id naming case B writes B only.</summary>
    [Test]
    public async Task SetComment_RequestIdNamingALaterCase_WritesOnlyThatCase()
    {
        var s = await SeedAsync("r3-comment", withEchoTask: false);
        var (_, laterCase) = await SeedLaterOccurrenceAsync(s);
        var request = LegacyComment(s, "for-b");
        request.RequestId = NewRequestId();
        request.ComplianceId = 0;
        request.MicrotingSdkCaseId = laterCase.Id;

        await s.Service.SetComment(request, new TestServerCallContext());
        var retry = await s.Service.SetComment(request, new TestServerCallContext());

        var earliestCustom = await CaseCustomOfAsync(s.SdkCaseId);
        var laterCustom = await CaseCustomOfAsync(laterCase.Id);
        Assert.Multiple(() =>
        {
            Assert.That(laterCustom, Does.Contain("for-b"));
            Assert.That(earliestCustom, Is.Empty, "the earliest occurrence was not written");
            Assert.That(retry.Replayed, Is.True);
        });
    }

    /// <summary>
    /// Round 3, finding 2: SetFieldValue with a request id naming case B
    /// writes B; after the earliest occurrence A is completed, the retry is a
    /// replay and A is never written.
    /// </summary>
    [Test]
    public async Task SetFieldValue_RequestIdNamingALaterCase_WritesOnlyThatCase_RetryAfterEarliestCompletes()
    {
        var s = await SeedAsync("r3-field", withEchoTask: false);
        var (_, laterCase) = await SeedLaterOccurrenceAsync(s);
        var request = LegacyFieldValue(s, "value-b");
        request.RequestId = NewRequestId();
        request.ComplianceId = 0;
        request.MicrotingSdkCaseId = laterCase.Id;

        var first = await s.Service.SetFieldValue(request, new TestServerCallContext());
        var earliestAfterFirst = await TextValueOfAsync(s, s.SdkCaseId);
        var laterAfterFirst = await TextValueOfAsync(s, laterCase.Id);
        await s.Service.CompleteEvent(LegacyComplete(s), new TestServerCallContext());
        var retry = await s.Service.SetFieldValue(request, new TestServerCallContext());
        var earliestAfterRetry = await TextValueOfAsync(s, s.SdkCaseId);

        Assert.Multiple(() =>
        {
            Assert.That(laterAfterFirst, Is.EqualTo("value-b"), "the named case was written");
            Assert.That(earliestAfterFirst, Is.Not.EqualTo("value-b"), "the earliest case was not written");
            Assert.That(first.Replayed, Is.False);
            Assert.That(retry.Replayed, Is.True, "the retry finds its record on the named case");
            Assert.That(earliestAfterRetry, Is.Not.EqualTo("value-b"));
            Assert.That(retry.Event.MicrotingSdkCaseId, Is.EqualTo(laterCase.Id));
        });
    }

    /// <summary>Round 3, finding 2: UploadPhoto with a request id naming case B attaches to B only.</summary>
    [Test]
    public async Task UploadPhoto_RequestIdNamingALaterCase_AttachesOnlyToThatCase()
    {
        var s = await SeedAsync("r3-upload", withEchoTask: false);
        var (_, laterCase) = await SeedLaterOccurrenceAsync(s);
        var meta = LegacyPhotoMeta(s);
        meta.RequestId = NewRequestId();
        meta.ComplianceId = 0;
        meta.MicrotingSdkCaseId = laterCase.Id;

        var first = await UploadAsync(s, meta);
        var retry = await UploadAsync(s, meta);

        var earliestCustom = await CaseCustomOfAsync(s.SdkCaseId);
        var laterCustom = await CaseCustomOfAsync(laterCase.Id);
        Assert.Multiple(() =>
        {
            Assert.That(laterCustom, Does.Contain("event_photos"));
            Assert.That(earliestCustom, Does.Not.Contain("event_photos"), "the earliest case got no photo");
            Assert.That(retry.Replayed, Is.True);
            Assert.That(retry.StorageId, Is.EqualTo(first.StorageId));
        });
    }

    /// <summary>Round 3, finding 2: RemovePhoto with a request id naming case B removes B's photo only.</summary>
    [Test]
    public async Task RemovePhoto_RequestIdNamingALaterCase_RemovesOnlyThatCasesPhoto()
    {
        var s = await SeedAsync("r3-remove", withEchoTask: false);
        var (later, laterCase) = await SeedLaterOccurrenceAsync(s);
        await UploadAsync(s, LegacyPhotoMeta(s));
        var laterMeta = LegacyPhotoMeta(s);
        laterMeta.ComplianceId = later.Id;
        laterMeta.MicrotingSdkCaseId = laterCase.Id;
        await UploadAsync(s, laterMeta);

        var remove = LegacyRemove(s);
        remove.RequestId = NewRequestId();
        remove.ComplianceId = 0;
        remove.MicrotingSdkCaseId = laterCase.Id;
        var reply = await s.Service.RemovePhoto(remove, new TestServerCallContext());

        var earliestCustom = await CaseCustomOfAsync(s.SdkCaseId);
        var laterCustom = await CaseCustomOfAsync(laterCase.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reply.Outcome, Is.EqualTo(RemovePhotoOutcome.Removed));
            Assert.That(laterCustom, Does.Not.Contain("uploaded_data_id"), "the named case's photo was removed");
            Assert.That(earliestCustom, Does.Contain("uploaded_data_id"), "the earliest case keeps its photo");
        });
    }

    /// <summary>
    /// Round 3, finding 3: a SetFieldValue replay that arrives after the
    /// occurrence was completed (the actionable-only echo has no row) reports
    /// completed=true with the real ids.
    /// </summary>
    [Test]
    public async Task SetFieldValue_ReplayAfterCompletion_ReportsCompleted()
    {
        var s = await SeedAsync("r3-replay-completed", withEchoTask: false);
        var request = LegacyFieldValue(s, "before-completion");
        request.RequestId = NewRequestId();

        await s.Service.SetFieldValue(request, new TestServerCallContext());
        await s.Service.CompleteEvent(LegacyComplete(s), new TestServerCallContext());
        var replay = await s.Service.SetFieldValue(request, new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Event.MicrotingSdkCaseId, Is.EqualTo(s.SdkCaseId));
            Assert.That(replay.Event.Completed, Is.True);
        });
    }

    /// <summary>
    /// Round 3, finding 3: the same fallback in a request-id SetComment on a
    /// completed occurrence reports completed=true; a legacy SetComment keeps
    /// today's reply (completed=false, ids 0).
    /// </summary>
    [Test]
    public async Task SetComment_WithRequestIdOnCompletedOccurrence_ReportsCompleted_LegacyUnchanged()
    {
        var s = await SeedAsync("r3-comment-completed", withEchoTask: false);
        await s.Service.CompleteEvent(LegacyComplete(s), new TestServerCallContext());

        var withId = LegacyComment(s, "after");
        withId.RequestId = NewRequestId();
        var contractReply = await s.Service.SetComment(withId, new TestServerCallContext());
        var legacyReply = await s.Service.SetComment(LegacyComment(s, "after-legacy"), new TestServerCallContext());

        Assert.Multiple(() =>
        {
            Assert.That(contractReply.Event.ComplianceId, Is.EqualTo(s.ComplianceId));
            Assert.That(contractReply.Event.Completed, Is.True);
            Assert.That(legacyReply.Event.Completed, Is.False, "legacy reply unchanged");
            Assert.That(legacyReply.Event.ComplianceId, Is.EqualTo(0), "legacy reply unchanged");
        });
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"condition not met within {timeout}");
            }

            await Task.Delay(200);
        }
    }
}

