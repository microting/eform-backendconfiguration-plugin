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

    private ChemicalBaseClient CreateSut(string? baseUrl = null)
    {
        var options = Options.Create(new ChemicalBaseOptions { BaseUrl = baseUrl ?? _server.Url! });
        var http = new HttpClient { BaseAddress = new Uri((baseUrl ?? _server.Url!).TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(10) };
        return new ChemicalBaseClient(http, options);
    }

    [Test]
    public async Task DownloadSds_ReturnsBytes_404IsNull()
    {
        _server.Given(Request.Create().WithPath("/api/chemicals-pn/get-pdf-file").WithParam("fileName", "a1b2c3").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new byte[] { 37, 80, 68, 70 }));
        _server.Given(Request.Create().WithPath("/api/chemicals-pn/get-pdf-file").WithParam("fileName", "missing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        Assert.That(await CreateSut().DownloadSdsAsync("a1b2c3"), Is.EqualTo(new byte[] { 37, 80, 68, 70 }));
        Assert.That(await CreateSut().DownloadSdsAsync("missing"), Is.Null);
    }

    [Test]
    public void DownloadSds_ServerError_IsUnavailable()
    {
        _server.Given(Request.Create().WithPath("/api/chemicals-pn/get-pdf-file").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        Assert.That(async () => await CreateSut().DownloadSdsAsync("a1b2c3"), Throws.InstanceOf<ChemicalUnavailableException>());
    }

    [Test]
    public void Unreachable_IsUnavailable()
    {
        // Port 9 (discard) on localhost refuses the connection immediately.
        Assert.That(async () => await CreateSut(baseUrl: "http://127.0.0.1:9").DownloadSdsAsync("a1b2c3"),
            Throws.InstanceOf<ChemicalUnavailableException>());
    }
}
