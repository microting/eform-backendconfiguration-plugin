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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using BackendConfiguration.Pn.Services.ChemicalReporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

[TestFixture]
public class ChemicalReportsControllerTests
{
    private const int UserId = 5;
    private static readonly ChemicalHistoryRequest Request = new("2026-09-01", "2026-09-29", null, null, null);
    private static readonly ChemicalHistoryQuery Query = new(new DateTime(2026, 9, 1), new DateTime(2026, 9, 29), null, null, null);

    private IChemicalExportService _exports;
    private IChemicalHistoryService _history;

    private ChemicalReportsController CreateSut()
    {
        _exports = Substitute.For<IChemicalExportService>();
        _history = Substitute.For<IChemicalHistoryService>();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(UserId);
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new ChemicalReportsController(_exports, _history, userService, localization)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static IEnumerable<string> RoutedActions() => typeof(ChemicalReportsController).GetMethods()
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .Select(m => m.Name)
        .OrderBy(n => n, StringComparer.Ordinal);

    [Test]
    public void Routes_MatchThePublishedContract()
    {
        Assert.That(typeof(ChemicalReportsController).GetCustomAttribute<AuthorizeAttribute>()?.Policy,
            Is.EqualTo(BackendConfigurationClaims.AccessBackendConfigurationPlugin));
        var prefix = typeof(ChemicalReportsController).GetCustomAttribute<RouteAttribute>()!.Template;
        var routes = typeof(ChemicalReportsController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => $"{a.HttpMethods.Single()} {prefix}/{a.Template}"))
            .OrderBy(r => r)
            .ToList();

        Assert.That(routes, Is.EqualTo(new[]
        {
            "GET api/backend-configuration-pn/chemicals/properties/{propertyId:int}/exports/authority-list",
            "GET api/backend-configuration-pn/chemicals/properties/{propertyId:int}/exports/removed-placements",
            "POST api/backend-configuration-pn/chemicals/properties/{propertyId:int}/exports/inventory",
            "POST api/backend-configuration-pn/chemicals/properties/{propertyId:int}/history",
            "POST api/backend-configuration-pn/chemicals/properties/{propertyId:int}/history/export",
        }));
    }

    [Test]
    public async Task History_ReturnsThePage()
    {
        var sut = CreateSut();
        var page = new ChemicalHistoryPageModel([], 0, []);
        _history.GetPageAsync(3, Query).Returns(page);

        var result = await sut.GetHistory(3, Request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.SameAs(page));
    }

    [Test]
    public async Task History_InvalidPeriod_IsAnUnsuccessfulResultWithTheReason()
    {
        var sut = CreateSut();
        _history.GetPageAsync(3, Query).ThrowsAsync(new ArgumentException("The period ends before it starts."));

        var result = await sut.GetHistory(3, Request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ErrorWhileReadingChemicalHistory: The period ends before it starts."));
    }

    [TestCase("2026-09-01T00:00:00")]
    [TestCase("2026-09-01T00:00:00Z")]
    [TestCase("2026-09-01T10:30:00+02:00")]
    [TestCase("2026-09-01 10:30")]
    [TestCase("2026-9-1")]
    [TestCase("01/09/2026")]
    [TestCase("")]
    [TestCase(null)]
    public async Task History_AFromThatIsNotExactlyADate_Is400AndNeverReachesTheService(string from)
    {
        var sut = CreateSut();

        var result = await sut.GetHistory(3, Request with { From = from });

        Assert.That(result.Success, Is.False);
        Assert.That(sut.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        await _history.DidNotReceiveWithAnyArgs().GetPageAsync(default, default);
    }

    [Test]
    public async Task History_AToWithATimeOffset_Is400()
    {
        var sut = CreateSut();

        var result = await sut.GetHistory(3, Request with { To = "2026-09-29T00:00:00+00:00" });

        Assert.That(result.Success, Is.False);
        Assert.That(sut.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task History_MissingBody_Is400()
    {
        var sut = CreateSut();

        var result = await sut.GetHistory(3, null);

        Assert.That(result.Success, Is.False);
        Assert.That(sut.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task History_UnexpectedFailure_StillReportsAFailedResult()
    {
        var sut = CreateSut();
        _history.GetPageAsync(3, Query).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await sut.GetHistory(3, Request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ErrorWhileReadingChemicalHistory: boom"));
    }

    [Test]
    public async Task HistoryExport_AFromWithATime_Is400AndNeverReachesTheService()
    {
        var sut = CreateSut();

        var result = await sut.ExportHistory(3, Request with { From = "2026-09-01T00:00:00Z" });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        await _exports.DidNotReceiveWithAnyArgs().HistoryXlsxAsync(default, default);
    }

    [Test]
    public async Task PdfDownload_SetsContentDispositionWithUtf8Name()
    {
        var sut = CreateSut();
        _exports.AuthorityListPdfAsync(UserId, 3)
            .Returns(new ChemicalExportFile([37, 80, 68, 70], "Kemikalieliste - \u00c6bleg\u00e5rden - 2026-09-29.pdf", "application/pdf"));

        var result = (FileContentResult)await sut.ExportAuthorityList(3);

        Assert.That(result.ContentType, Is.EqualTo("application/pdf"));
        var header = sut.Response.Headers["Content-Disposition"].ToString();
        Assert.That(header, Does.StartWith("attachment; filename=\"Kemikalieliste - _bleg_rden - 2026-09-29.pdf\""));
        Assert.That(header, Does.Contain("filename*=UTF-8''Kemikalieliste%20-%20%C3%86bleg%C3%A5rden%20-%202026-09-29.pdf"));
    }

    [Test]
    public async Task InventoryExport_PassesTheCallerAndTheVisibleIds()
    {
        var sut = CreateSut();
        _exports.InventoryXlsxAsync(UserId, 3, Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new ChemicalExportFile([1], "x.xlsx", ChemicalExportService.XlsxContentType));

        await sut.ExportInventory(3, new ChemicalInventoryExportBody([7, 8]));

        await _exports.Received(1).InventoryXlsxAsync(UserId, 3, Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 7, 8 })));
    }

    [Test]
    public async Task InventoryExport_WithoutBody_ExportsEveryOpenPlacement()
    {
        var sut = CreateSut();
        _exports.InventoryXlsxAsync(UserId, 3, null).Returns(new ChemicalExportFile([1], "x.xlsx", ChemicalExportService.XlsxContentType));

        Assert.That(await sut.ExportInventory(3, null), Is.InstanceOf<FileContentResult>());
    }

    [Test]
    public async Task ExportFailure_Is400WithAReadableMessage()
    {
        var sut = CreateSut();
        _exports.RemovedPlacementsXlsxAsync(UserId, 3).ThrowsAsync(new ChemicalNotFoundException("Property 3 not found."));

        var result = await sut.ExportRemovedPlacements(3);

        var bad = (BadRequestObjectResult)result;
        Assert.That(bad.Value, Is.EqualTo("ErrorWhileExportingChemicals: Property 3 not found."));
    }

    [Test]
    public async Task HistoryExport_IsAnXlsxFile()
    {
        var sut = CreateSut();
        _exports.HistoryXlsxAsync(3, Query).Returns(new ChemicalExportFile([1], "h.xlsx", ChemicalExportService.XlsxContentType));

        var result = (FileContentResult)await sut.ExportHistory(3, Request);

        Assert.That(result.ContentType, Is.EqualTo(ChemicalExportService.XlsxContentType));
    }

    // ---- TODO(chemistry-GA): remove with the [FirstUserOnly] on the controller, like ChemicalsControllerFirstUserGateTests ----

    private const int FirstUserId = 1;
    private const int OtherAdminId = 7;

    [Test]
    public void TheGateCoversEveryRoute()
    {
        Assert.That(RoutedActions().Count(), Is.EqualTo(5), "gate-review a new route, then bump this");
    }

    [TestCaseSource(nameof(RoutedActions))]
    public async Task AnAdminWhoIsNotTheFirstUser_IsForbiddenOnEveryRoute(string actionName)
    {
        var result = await FirstUserGate.AuthorizeAsync(typeof(ChemicalReportsController), actionName,
            FirstUserGate.Caller(OtherAdminId, FirstUserId));

        FirstUserGate.AssertForbidden(result);
    }

    [TestCaseSource(nameof(RoutedActions))]
    public async Task TheFirstUser_PassesTheGate(string actionName)
    {
        var result = await FirstUserGate.AuthorizeAsync(typeof(ChemicalReportsController), actionName,
            FirstUserGate.Caller(FirstUserId, FirstUserId));

        Assert.That(result, Is.Null);
    }
}
