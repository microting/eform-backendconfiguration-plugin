#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.TailBite;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TailBite;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

public class TailBiteGrpcServiceMappingTests
{
    private ITailBiteAccess _access = null!;
    private ITailBiteRegistrationService _regs = null!;
    private ITailBiteSetupService _setup = null!;
    private ITailBiteOutbreakService _outbreaks = null!;
    private IGrpcSiteResolver _resolver = null!;

    private TailBiteGrpcService Sut() => new(_access, _regs, _setup, _outbreaks, _resolver, NullLogger<TailBiteGrpcService>.Instance);
    private static ServerCallContext Ctx() => Substitute.For<ServerCallContext>();
    private static Timestamp Ts(int day) => Timestamp.FromDateTime(new DateTime(2026, 10, day, 6, 0, 0, DateTimeKind.Utc));
    private static Timestamp OutOfRange() => new() { Seconds = long.MaxValue / 4 };

    private static async Task<RpcException> AssertRpc(StatusCode expected, Func<Task> call)
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() => call());
        Assert.That(ex!.StatusCode, Is.EqualTo(expected));
        return ex;
    }

    private static TbPhotoMeta Meta(string? photoUuid = null, string? registrationUuid = null) => new()
    {
        PhotoUuid = photoUuid ?? Guid.NewGuid().ToString(), PropertyId = "1",
        RegistrationClientUuid = registrationUuid ?? Guid.NewGuid().ToString(), ContentType = "image/jpeg"
    };

    private static TbUploadPhotoChunk MetaChunk(TbPhotoMeta? meta = null) => new() { Meta = meta ?? Meta() };
    private static TbUploadPhotoChunk Bytes(params byte[] bytes) => new() { Chunk = ByteString.CopyFrom(bytes) };

    [SetUp]
    public void SetUp()
    {
        _access = Substitute.For<ITailBiteAccess>();
        _regs = Substitute.For<ITailBiteRegistrationService>();
        _setup = Substitute.For<ITailBiteSetupService>();
        _outbreaks = Substitute.For<ITailBiteOutbreakService>();
        _resolver = Substitute.For<IGrpcSiteResolver>();
        _access.RequireCallerSiteAsync().Returns(7);
    }

    [Test]
    public async Task CreateRegistration_MapsRequestAndResponse()
    {
        var uuid = Guid.NewGuid();
        _regs.CreateAsync(7, Arg.Any<CreateRegistrationCommand>()).Returns(new CreateRegistrationResult(11, [new OutbreakOutcome(3, true)]));
        var resp = await Sut().CreateRegistration(new TbCreateRegistrationRequest
        {
            ClientUuid = uuid.ToString(), PropertyId = "1", RegisteredAt = Ts(1),
            Locations = { new TbRegistrationLocation { LocationId = "4", Minor = 2, Severe = 1 } }, ActionTypeIds = { "9" }, Comment = "  "
        }, Ctx());
        Assert.That(resp.RegistrationId, Is.EqualTo("11"));
        Assert.That(resp.Outbreaks.Single().OutbreakId, Is.EqualTo("3"));
        Assert.That(resp.Outbreaks.Single().Opened, Is.True);
        await _regs.Received().CreateAsync(7, Arg.Is<CreateRegistrationCommand>(c =>
            c.ClientUuid == uuid && c.PropertyId == 1 && c.RegisteredAtUtc == Ts(1).ToDateTime() && c.Comment == null
            && c.Locations.Single().LocationId == 4 && c.Locations.Single().Minor == 2 && c.Locations.Single().Severe == 1
            && c.ActionTypeIds.Single() == 9));
    }

    [Test]
    public async Task CreateRegistration_MissingRegisteredAt_InvalidArgument()
    {
        await AssertRpc(StatusCode.InvalidArgument, () => Sut().CreateRegistration(
            new TbCreateRegistrationRequest { ClientUuid = Guid.NewGuid().ToString(), PropertyId = "1" }, Ctx()));
    }

    [Test]
    public async Task CreateRegistration_OutOfRangeRegisteredAt_InvalidArgument()
        => await AssertRpc(StatusCode.InvalidArgument, () => Sut().CreateRegistration(
            new TbCreateRegistrationRequest { ClientUuid = Guid.NewGuid().ToString(), PropertyId = "1", RegisteredAt = OutOfRange() }, Ctx()));

    [Test]
    public async Task CreateRegistration_BadUuid_InvalidArgument()
    {
        await AssertRpc(StatusCode.InvalidArgument, () => Sut().CreateRegistration(
            new TbCreateRegistrationRequest { ClientUuid = "nope", PropertyId = "1", RegisteredAt = Ts(1) }, Ctx()));
    }

    [TestCase(typeof(TailBiteForbiddenException), StatusCode.PermissionDenied)]
    [TestCase(typeof(TailBiteNotFoundException), StatusCode.NotFound)]
    [TestCase(typeof(TailBiteValidationException), StatusCode.InvalidArgument)]
    [TestCase(typeof(TailBiteConflictException), StatusCode.FailedPrecondition)]
    public async Task Exceptions_MapToStatusCodes(System.Type exType, StatusCode expected)
    {
        _outbreaks.CloseAsync(7, 5).Throws((Exception)Activator.CreateInstance(exType, "x")!);
        await AssertRpc(expected, () => Sut().CloseOutbreak(new TbCloseOutbreakRequest { OutbreakId = "5" }, Ctx()));
    }

    [Test]
    public async Task UnexpectedException_InternalWithGenericMessage()
    {
        _outbreaks.CloseAsync(7, 5).Throws(new InvalidOperationException("secret detail"));
        var ex = await AssertRpc(StatusCode.Internal, () => Sut().CloseOutbreak(new TbCloseOutbreakRequest { OutbreakId = "5" }, Ctx()));
        Assert.That(ex.Status.Detail, Does.Not.Contain("secret"));
    }

    [Test]
    public async Task RpcException_IsRethrownUnchanged()
    {
        _outbreaks.CloseAsync(7, 5).Throws(new RpcException(new Status(StatusCode.Unauthenticated, "x")));
        await AssertRpc(StatusCode.Unauthenticated, () => Sut().CloseOutbreak(new TbCloseOutbreakRequest { OutbreakId = "5" }, Ctx()));
    }

    [TestCase("abc")]
    [TestCase("")]
    [TestCase("0")]
    [TestCase("-3")]
    [TestCase("1.5")]
    public async Task BadId_InvalidArgument(string raw)
    {
        await AssertRpc(StatusCode.InvalidArgument, () => Sut().CloseOutbreak(new TbCloseOutbreakRequest { OutbreakId = raw }, Ctx()));
    }

    [Test]
    public async Task UnresolvedCaller_PermissionDenied()
    {
        _access.RequireCallerSiteAsync().Throws(new TailBiteForbiddenException("no worker"));
        await AssertRpc(StatusCode.PermissionDenied, () => Sut().GetCurrentWorker(new TbGetCurrentWorkerRequest(), Ctx()));
    }

    [Test]
    public async Task GetCurrentWorker_MapsDisplayNameAndEnabledProperties()
    {
        _resolver.GetDisplayNameAsync(7).Returns("Jane Doe");
        _setup.ListEnabledPropertiesAsync(7).Returns([(1, "Stald 1", true), (2, "Stald 2", false)]);
        var resp = await Sut().GetCurrentWorker(new TbGetCurrentWorkerRequest(), Ctx());
        Assert.That(resp.SiteId, Is.EqualTo("7"));
        Assert.That(resp.DisplayName, Is.EqualTo("Jane Doe"));
        Assert.That(resp.Properties.Select(p => (p.PropertyId, p.Name, p.IsManager)),
            Is.EqualTo(new[] { ("1", "Stald 1", true), ("2", "Stald 2", false) }));
    }

    [Test]
    public async Task GetLocationTree_MapsNodesAndActionTypes()
    {
        _setup.GetTreeAsync(7, 1).Returns(new LocationTree(1, 42,
            [new LocationNode(10, null, "Stald 1", 0, "qr-root", 0, false), new LocationNode(11, 10, "Sti 309", 2, "qr-pen", 1, true)],
            [new ActionTypeDto(5, "HALM", "Halm", 1)]));
        var resp = await Sut().GetLocationTree(new TbGetLocationTreeRequest { PropertyId = "1" }, Ctx());
        Assert.That(resp.PropertyId, Is.EqualTo("1"));
        Assert.That(resp.TreeVersion, Is.EqualTo(42));
        Assert.That(resp.Locations[0].ParentId, Is.EqualTo(""));
        var pen = resp.Locations[1];
        Assert.That((pen.Id, pen.ParentId, pen.Name, pen.SortOrder, pen.QrCode, pen.Depth, pen.Removed), Is.EqualTo(("11", "10", "Sti 309", 2, "qr-pen", 1, true)));
        Assert.That((resp.ActionTypes.Single().Id, resp.ActionTypes.Single().Code, resp.ActionTypes.Single().Name), Is.EqualTo(("5", "HALM", "Halm")));
    }

    [Test]
    public async Task ListMyRecentRegistrations_MapsSinceAndRows()
    {
        _regs.ListRecentAsync(7, 1, Ts(2).ToDateTime()).Returns([
            new RecentRegistration(8, Ts(3).ToDateTime(), [new RegistrationLocationInput(4, 1, 0)], true)]);
        var resp = await Sut().ListMyRecentRegistrations(new TbListRecentRequest { PropertyId = "1", Since = Ts(2) }, Ctx());
        var r = resp.Registrations.Single();
        Assert.That((r.Id, r.Cancelled, r.EffectiveAt), Is.EqualTo(("8", true, Ts(3))));
        Assert.That((r.Locations.Single().LocationId, r.Locations.Single().Minor), Is.EqualTo(("4", 1)));
    }

    [Test]
    public async Task ListMyRecentRegistrations_SinceUnset_UsesUnixEpoch()
    {
        _regs.ListRecentAsync(7, 1, DateTime.UnixEpoch).Returns([]);
        var resp = await Sut().ListMyRecentRegistrations(new TbListRecentRequest { PropertyId = "1" }, Ctx());
        Assert.That(resp.Registrations, Is.Empty);
        await _regs.Received().ListRecentAsync(7, 1, DateTime.UnixEpoch);
    }

    [Test]
    public async Task ListMyRecentRegistrations_SinceOutOfRange_InvalidArgument()
        => await AssertRpc(StatusCode.InvalidArgument, () => Sut().ListMyRecentRegistrations(
            new TbListRecentRequest { PropertyId = "1", Since = OutOfRange() }, Ctx()));

    [Test]
    public async Task ListOutbreaks_MapsFilterAndSummary()
    {
        _outbreaks.ListAsync(7, 1, true).Returns([new OutbreakSummary(3, 4, Ts(1).ToDateTime(), true, 2, false)]);
        var resp = await Sut().ListOutbreaks(new TbListOutbreaksRequest { PropertyId = "1", OpenOnly = true }, Ctx());
        var s = resp.Outbreaks.Single();
        Assert.That((s.Id, s.LocationId, s.OpenedAt, s.Assessed, s.OpenActions, s.Closed), Is.EqualTo(("3", "4", Ts(1), true, 2, false)));
    }

    private static OutbreakDetail Detail(FactorAnswers? answers, params OutbreakActionDetail[] actions)
        => new(new OutbreakSummary(5, 4, Ts(1).ToDateTime(), answers != null, actions.Length, false), 12, 3, [20, 21], answers, actions);

    [Test]
    public async Task GetOutbreak_MapsDetail()
    {
        _outbreaks.GetAsync(7, 5).Returns(Detail(new FactorAnswers(true, false, false, true, false, false),
            new OutbreakActionDetail(30, TailBiteFactor.Climate, "Open vindue", 9, Ts(4).ToDateTime(), Ts(5).ToDateTime(), null)));
        var d = await Sut().GetOutbreak(new TbGetOutbreakRequest { OutbreakId = "5" }, Ctx());
        Assert.That(d.Summary.Id, Is.EqualTo("5"));
        Assert.That((d.RuleId, d.RuleVersion), Is.EqualTo(("12", 3)));
        Assert.That(d.RegistrationIds, Is.EqualTo(new[] { "20", "21" }));
        Assert.That(d.HasAnswers, Is.True);
        Assert.That((d.Answers.Water, d.Answers.Feed, d.Answers.Climate), Is.EqualTo((true, false, true)));
        var a = d.Actions.Single();
        Assert.That((a.Id, a.Factor, a.Description, a.ResponsibleSiteId), Is.EqualTo(("30", TbFactor.Climate, "Open vindue", "9")));
        Assert.That(a.FollowUpDate, Is.EqualTo(Ts(4)));
        Assert.That(a.DoneAt, Is.EqualTo(Ts(5)));
        Assert.That(a.WithdrawnAt, Is.Null);
    }

    [Test]
    public async Task GetOutbreak_MapsWithdrawnAt_WhenSet()
    {
        _outbreaks.GetAsync(7, 5).Returns(Detail(null,
            new OutbreakActionDetail(31, TailBiteFactor.Water, "Tjek vand", 9, Ts(4).ToDateTime(), null, Ts(6).ToDateTime())));
        var a = (await Sut().GetOutbreak(new TbGetOutbreakRequest { OutbreakId = "5" }, Ctx())).Actions.Single();
        Assert.That(a.WithdrawnAt, Is.EqualTo(Ts(6)));
        Assert.That(a.DoneAt, Is.Null);
    }

    [Test]
    public async Task GetOutbreak_NoAssessment_HasAnswersFalse()
    {
        _outbreaks.GetAsync(7, 5).Returns(Detail(null));
        var d = await Sut().GetOutbreak(new TbGetOutbreakRequest { OutbreakId = "5" }, Ctx());
        Assert.That(d.HasAnswers, Is.False);
        Assert.That(d.Answers, Is.Null);
    }

    [Test]
    public async Task SaveRiskAssessment_MapsAnswersFactorsAndFollowUp_ThenReturnsDetail()
    {
        _outbreaks.GetAsync(7, 5).Returns(Detail(new FactorAnswers(true, false, false, false, true, false)));
        var resp = await Sut().SaveRiskAssessment(new TbSaveRiskAssessmentRequest
        {
            OutbreakId = "5",
            Answers = new TbAnswers { Water = true, Health = true },
            NewActions = { new TbNewAction { Factor = TbFactor.Health, Description = "Tilkald dyrlaege", ResponsibleSiteId = "9", FollowUpDate = Ts(6) } }
        }, Ctx());
        await _outbreaks.Received().SaveAssessmentAsync(7, 5,
            Arg.Is<FactorAnswers>(a => a == new FactorAnswers(true, false, false, false, true, false)),
            Arg.Is<System.Collections.Generic.IReadOnlyList<ActionInput>>(l => l.Count == 1 && l[0].Factor == TailBiteFactor.Health
                && l[0].Description == "Tilkald dyrlaege" && l[0].ResponsibleSiteId == 9 && l[0].FollowUpDate == Ts(6).ToDateTime()));
        Assert.That(resp.Summary.Id, Is.EqualTo("5"));
    }

    private Task SaveWithNewAction(TbNewAction action)
        => Sut().SaveRiskAssessment(new TbSaveRiskAssessmentRequest { OutbreakId = "5", Answers = new TbAnswers(), NewActions = { action } }, Ctx());

    private static TbNewAction NewAction(TbFactor factor = TbFactor.Water, Timestamp? followUp = null) => new()
    {
        Factor = factor, Description = "x", ResponsibleSiteId = "9", FollowUpDate = followUp
    };

    [Test]
    public async Task SaveRiskAssessment_MissingFollowUp_InvalidArgument()
        => await AssertRpc(StatusCode.InvalidArgument, () => SaveWithNewAction(NewAction(followUp: null)));

    [Test]
    public async Task SaveRiskAssessment_OutOfRangeFollowUp_InvalidArgument()
        => await AssertRpc(StatusCode.InvalidArgument, () => SaveWithNewAction(NewAction(followUp: OutOfRange())));

    [Test]
    public async Task SaveRiskAssessment_FactorUnspecified_InvalidArgument()
        => await AssertRpc(StatusCode.InvalidArgument, () => SaveWithNewAction(new TbNewAction
        {
            Description = "x", ResponsibleSiteId = "9", FollowUpDate = Ts(6)
        }));

    [Test]
    public async Task SaveRiskAssessment_FactorOutOfRange_InvalidArgument()
        => await AssertRpc(StatusCode.InvalidArgument, () => SaveWithNewAction(NewAction((TbFactor)99, Ts(6))));

    [Test]
    public async Task SaveRiskAssessment_MissingAnswers_InvalidArgument()
    {
        await AssertRpc(StatusCode.InvalidArgument, () => Sut().SaveRiskAssessment(new TbSaveRiskAssessmentRequest { OutbreakId = "5" }, Ctx()));
    }

    [Test]
    public async Task SetActionDone_Withdraw_Reassign_ForwardAndReturnDetail()
    {
        // The action RPCs carry no outbreak id, so the detail is looked up from the action id.
        _outbreaks.GetForActionAsync(7, 30).Returns(Detail(null));
        var sut = Sut();
        var done = await sut.SetActionDone(new TbSetActionDoneRequest { ActionId = "30", Done = true }, Ctx());
        var withdrawn = await sut.WithdrawAction(new TbWithdrawActionRequest { ActionId = "30", Reason = "dup" }, Ctx());
        var reassigned = await sut.ReassignAction(new TbReassignActionRequest { ActionId = "30", ResponsibleSiteId = "9" }, Ctx());
        Assert.That(new[] { done.Summary.Id, withdrawn.Summary.Id, reassigned.Summary.Id }, Is.All.EqualTo("5"));
        await _outbreaks.Received().SetActionDoneAsync(7, 30, true);
        await _outbreaks.Received().WithdrawActionAsync(7, 30, "dup");
        await _outbreaks.Received().ReassignActionAsync(7, 30, 9);
    }

    [TestCase(typeof(TailBiteForbiddenException), StatusCode.PermissionDenied)]
    [TestCase(typeof(TailBiteNotFoundException), StatusCode.NotFound)]
    public async Task ActionRpc_ServiceExceptions_MapThroughGrpcLayer(System.Type exType, StatusCode expected)
    {
        _outbreaks.SetActionDoneAsync(7, 30, true).Throws((Exception)Activator.CreateInstance(exType, "x")!);
        await AssertRpc(expected, () => Sut().SetActionDone(new TbSetActionDoneRequest { ActionId = "30", Done = true }, Ctx()));
    }

    [Test]
    public async Task CancelRegistration_ForwardsAndReturnsEmpty()
    {
        var resp = await Sut().CancelRegistration(new TbCancelRegistrationRequest { RegistrationId = "8", Reason = "typo" }, Ctx());
        Assert.That(resp, Is.Not.Null);
        await _outbreaks.Received().CancelRegistrationAsync(7, 8, "typo");
    }

    [Test]
    public async Task UploadPhoto_AssemblesChunksAndForwards()
    {
        var photo = Guid.NewGuid();
        var reg = Guid.NewGuid();
        _regs.SavePhotoAsync(7, 1, photo, reg, Arg.Any<byte[]>(), "image/jpeg").Returns(photo);
        var reader = new FakeAsyncStreamReader<TbUploadPhotoChunk>([MetaChunk(Meta(photo.ToString(), reg.ToString())), Bytes(1, 2), Bytes(3)]);
        var resp = await Sut().UploadPhoto(reader, Ctx());
        Assert.That(resp.PhotoUuid, Is.EqualTo(photo.ToString()));
        await _regs.Received().SavePhotoAsync(7, 1, photo, reg, Arg.Is<byte[]>(b => b.SequenceEqual(new byte[] { 1, 2, 3 })), "image/jpeg");
    }

    private Task Upload(params TbUploadPhotoChunk[] chunks) => Sut().UploadPhoto(new FakeAsyncStreamReader<TbUploadPhotoChunk>(chunks), Ctx());

    [Test]
    public async Task UploadPhoto_EmptyStream_InvalidArgument() => await AssertRpc(StatusCode.InvalidArgument, () => Upload());

    [Test]
    public async Task UploadPhoto_FirstNotMeta_InvalidArgument() => await AssertRpc(StatusCode.InvalidArgument, () => Upload(Bytes(1)));

    [Test]
    public async Task UploadPhoto_MetaOnly_NoBytes_InvalidArgument() => await AssertRpc(StatusCode.InvalidArgument, () => Upload(MetaChunk()));

    [Test]
    public async Task UploadPhoto_SecondMeta_InvalidArgument() => await AssertRpc(StatusCode.InvalidArgument, () => Upload(MetaChunk(), MetaChunk()));

    [Test]
    public async Task UploadPhoto_OverCap_InvalidArgument()
    {
        var big = new byte[11 * 1024 * 1024];
        await AssertRpc(StatusCode.InvalidArgument, () => Upload(MetaChunk(), Bytes(big), Bytes(big)));
    }

    [Test]
    public async Task GetPhoto_SendsContentTypeFirstThenChunks()
    {
        var photo = Guid.NewGuid();
        var bytes = new byte[64 * 1024 + 10];
        _regs.GetPhotoAsync(7, photo).Returns((new MemoryStream(bytes), "image/png"));
        var writer = new FakeServerStreamWriter<TbPhotoChunk>();
        await Sut().GetPhoto(new TbGetPhotoRequest { PhotoUuid = photo.ToString() }, writer, Ctx());
        Assert.That(writer.Written[0].ContentType, Is.EqualTo("image/png"));
        Assert.That(writer.Written.Skip(1).Select(w => w.Chunk.Length), Is.EqualTo(new[] { 64 * 1024, 10 }));
    }
}
