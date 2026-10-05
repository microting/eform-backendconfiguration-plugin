#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.TailBite;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TailBite;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

/// <summary>A call the client cancelled surfaces as Cancelled and is not logged as a server error.</summary>
public class TailBiteGrpcServiceCancellationTests
{
    private ITailBiteAccess _access = null!;
    private ITailBiteRegistrationService _regs = null!;
    private ITailBiteOutbreakService _outbreaks = null!;
    private RecordingLogger _logger = null!;

    private TailBiteGrpcService Sut() => new(_access, _regs, Substitute.For<ITailBiteSetupService>(), _outbreaks,
        Substitute.For<IGrpcSiteResolver>(), _logger);

    [SetUp]
    public void SetUp()
    {
        _access = Substitute.For<ITailBiteAccess>();
        _regs = Substitute.For<ITailBiteRegistrationService>();
        _outbreaks = Substitute.For<ITailBiteOutbreakService>();
        _logger = new RecordingLogger();
        _access.RequireCallerSiteAsync().Returns(7);
    }

    private static CallContext Cancelled()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return new CallContext(source.Token);
    }

    private async Task AssertCancelledAndNotLogged(Func<Task> call)
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() => call());
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Cancelled));
        Assert.That(_logger.Errors, Is.Zero);
    }

    [Test]
    public async Task OperationCanceled_OnCancelledCall_IsCancelled()
    {
        _outbreaks.CloseAsync(7, 5).Throws(new OperationCanceledException());
        await AssertCancelledAndNotLogged(() => Sut().CloseOutbreak(new TbCloseOutbreakRequest { OutbreakId = "5" }, Cancelled()));
    }

    [Test]
    public async Task TaskCanceled_OnCancelledCall_IsCancelled()
    {
        _outbreaks.CloseAsync(7, 5).Throws(new TaskCanceledException());
        await AssertCancelledAndNotLogged(() => Sut().CloseOutbreak(new TbCloseOutbreakRequest { OutbreakId = "5" }, Cancelled()));
    }

    [Test]
    public async Task RpcCancelled_OnCancelledCall_IsCancelled()
    {
        _outbreaks.CloseAsync(7, 5).Throws(new RpcException(new Status(StatusCode.Cancelled, "x")));
        await AssertCancelledAndNotLogged(() => Sut().CloseOutbreak(new TbCloseOutbreakRequest { OutbreakId = "5" }, Cancelled()));
    }

    [Test]
    public async Task UploadPhoto_ReadCancelled_IsCancelled()
    {
        var reader = Substitute.For<IAsyncStreamReader<TbUploadPhotoChunk>>();
        reader.MoveNext(Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        await AssertCancelledAndNotLogged(() => Sut().UploadPhoto(reader, Cancelled()));
    }

    [Test]
    public async Task OperationCanceled_WithoutCancelledCall_StaysInternalAndLogged()
    {
        _outbreaks.CloseAsync(7, 5).Throws(new OperationCanceledException());
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Sut().CloseOutbreak(new TbCloseOutbreakRequest { OutbreakId = "5" }, new CallContext(CancellationToken.None)));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Internal));
        Assert.That(_logger.Errors, Is.EqualTo(1));
    }

    private sealed class RecordingLogger : ILogger<TailBiteGrpcService>
    {
        public int Errors { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error) Errors++;
        }
    }

    /// <summary>A server call context with a chosen cancellation; everything else at safe defaults.</summary>
    private sealed class CallContext(CancellationToken cancellation) : ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => cancellation;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(string.Empty, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
