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


#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

public class ComplianceReportControllerTests
{
    private static ComplianceReportController Sut(ITailBiteComplianceReportService tailBite) => new(
        Substitute.For<IBackendConfigurationComplianceReportService>(),
        Substitute.For<IBackendConfigurationComplianceExportService>(),
        tailBite);

    [Test]
    public async Task TailBite_PassesTheRequestThrough_AndReturnsTheReport()
    {
        var request = new ComplianceReportRequestModel();
        var report = new TailBiteComplianceReport(Array.Empty<TailBiteReportOutbreak>(), new TailBiteReportTotals(0, 0, 0, 0, 0, 0));
        var service = Substitute.For<ITailBiteComplianceReportService>();
        service.BuildAsync(request).Returns(report);

        var res = await Sut(service).TailBite(request);

        Assert.That(res.Success, Is.True);
        Assert.That(res.Model, Is.SameAs(report));
        await service.Received(1).BuildAsync(request);
    }

    [Test]
    // Null model = the caller sees no Halebid section; still a successful result, so the web shows nothing and no error.
    public async Task TailBite_NoSection_IsASuccessWithANullModel()
    {
        var request = new ComplianceReportRequestModel();
        var service = Substitute.For<ITailBiteComplianceReportService>();
        service.BuildAsync(request).Returns((TailBiteComplianceReport?)null);

        var res = await Sut(service).TailBite(request);

        Assert.That(res.Success, Is.True);
        Assert.That(res.Model, Is.Null);
    }

    [Test]
    public async Task TailBite_NullBody_IsAFailedResult_WithoutCallingTheService()
    {
        var service = Substitute.For<ITailBiteComplianceReportService>();

        var res = await Sut(service).TailBite(null!);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("Request body is required."));
        await service.DidNotReceiveWithAnyArgs().BuildAsync(default!);
    }

    [Test]
    public void TailBite_IsAPostOnTheReportPrefix()
    {
        var action = typeof(ComplianceReportController).GetMethod(nameof(ComplianceReportController.TailBite))!;
        Assert.That(action.GetCustomAttributes<HttpPostAttribute>().Single().Template, Is.EqualTo("tail-bite"));
        Assert.That(typeof(ComplianceReportController).GetCustomAttribute<RouteAttribute>()!.Template,
            Is.EqualTo("api/backend-configuration-pn/compliance-report"));
    }
}
