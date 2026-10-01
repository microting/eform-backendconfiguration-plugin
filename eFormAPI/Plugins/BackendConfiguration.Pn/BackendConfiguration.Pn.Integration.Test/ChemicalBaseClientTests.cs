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

using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BackendConfiguration.Pn.Integration.Test;

[TestFixture]
public class ChemicalBaseClientTests
{
    private WireMockServer _server = null!;

    [SetUp]
    public void SetUp() => _server = WireMockServer.Start();

    [TearDown]
    public void TearDown()
    {
        _server.Stop();
        _server.Dispose();
    }

    private const string SdsPath = "/api/chemicals-pn/get-pdf-file";

    private ChemicalBaseClient CreateSut(string? baseUrl = null, TimeSpan? sdsTimeout = null, string? httpBaseAddress = null)
    {
        var chemicalBaseOptions = new ChemicalBaseOptions { BaseUrl = baseUrl ?? _server.Url! };
        if (sdsTimeout != null)
        {
            chemicalBaseOptions.SdsDownloadTimeout = sdsTimeout.Value;
        }

        var address = httpBaseAddress ?? baseUrl ?? _server.Url!;
        var http = new HttpClient { BaseAddress = new Uri(address.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(10) };
        return new ChemicalBaseClient(http, Options.Create(chemicalBaseOptions));
    }

    [Test]
    public async Task DownloadSds_ReturnsBytes_404IsNull()
    {
        _server.Given(Request.Create().WithPath(SdsPath).WithParam("fileName", "a1b2c3").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new byte[] { 37, 80, 68, 70 }));
        _server.Given(Request.Create().WithPath(SdsPath).WithParam("fileName", "missing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        Assert.That(await CreateSut().DownloadSdsAsync("a1b2c3"), Is.EqualTo(new byte[] { 37, 80, 68, 70 }));
        Assert.That(await CreateSut().DownloadSdsAsync("missing"), Is.Null);
    }

    [Test]
    public void DownloadSds_ServerError_IsUnavailable()
    {
        _server.Given(Request.Create().WithPath(SdsPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        Assert.That(async () => await CreateSut().DownloadSdsAsync("a1b2c3"), Throws.InstanceOf<ChemicalUnavailableException>());
    }

    [Test]
    public void DownloadSds_SlowServer_IsBoundedByTheClientTimeout_AndUnavailable()
    {
        // The HttpClient itself would wait (Infinite); only the client's own SDS timeout may stop it.
        _server.Given(Request.Create().WithPath(SdsPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new byte[] { 37, 80, 68, 70 }).WithDelay(TimeSpan.FromSeconds(5)));
        var options = Options.Create(new ChemicalBaseOptions
        {
            BaseUrl = _server.Url!, SdsDownloadTimeout = TimeSpan.FromMilliseconds(300),
        });
        var sut = new ChemicalBaseClient(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, options);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Assert.That(async () => await sut.DownloadSdsAsync("a1b2c3"), Throws.InstanceOf<ChemicalUnavailableException>());
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
    }

    [Test]
    public void DownloadSds_DefaultTimeout_Is30Seconds()
    {
        Assert.That(new ChemicalBaseOptions().SdsDownloadTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void DownloadSds_CallerCancellation_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.That(async () => await CreateSut().DownloadSdsAsync("a1b2c3", cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void DownloadSds_NullFileName_IsArgumentNull()
    {
        Assert.That(async () => await CreateSut().DownloadSdsAsync(null!), Throws.InstanceOf<ArgumentNullException>());
        Assert.That(_server.LogEntries, Is.Empty);
    }

    [Test]
    public async Task DownloadSds_EncodesTheOpaqueFileName()
    {
        const string fileName = "a b \u00e6\u00f8\u00e5+/x.pdf";
        _server.Given(Request.Create().WithPath(SdsPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new byte[] { 1 }));

        await CreateSut().DownloadSdsAsync(fileName);

        var request = _server.LogEntries.Single().RequestMessage!;
        var rawQuery = request.RawQuery!.TrimStart('?');
        Assert.That(rawQuery, Is.EqualTo("fileName=a%20b%20%C3%A6%C3%B8%C3%A5%2B%2Fx.pdf"));
        Assert.That(Uri.UnescapeDataString(rawQuery["fileName=".Length..]), Is.EqualTo(fileName));
    }

    [Test]
    public async Task DownloadSds_UsesOptionsBaseUrl_NotHttpClientBaseAddress()
    {
        _server.Given(Request.Create().WithPath(SdsPath).WithParam("fileName", "a1b2c3").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new byte[] { 37, 80, 68, 70 }));

        // BaseAddress points at a refused port; only options.BaseUrl reaches the server.
        var bytes = await CreateSut(httpBaseAddress: "http://127.0.0.1:9").DownloadSdsAsync("a1b2c3");

        Assert.That(bytes, Is.EqualTo(new byte[] { 37, 80, 68, 70 }));
    }

    [Test]
    public void Unreachable_IsUnavailable()
    {
        // Port 9 (discard) on localhost refuses the connection immediately.
        Assert.That(async () => await CreateSut(baseUrl: "http://127.0.0.1:9").DownloadSdsAsync("a1b2c3"),
            Throws.InstanceOf<ChemicalUnavailableException>());
    }
}
