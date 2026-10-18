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
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using BackendConfiguration.Pn.Services.ChemicalReporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

// Sentry's static hub is process-wide: the capture tests below swap it, so this fixture runs alone.
[TestFixture]
[NonParallelizable]
public class ChemicalReportsControllerTests
{
    private const int UserId = 5;
    private static readonly ChemicalHistoryRequest SampleRequest = new("2026-09-01", "2026-09-29", null, null, null);
    private static readonly ChemicalHistoryQuery SampleQuery = SampleRequest.ToQuery();

    private static readonly string[] ExpectedRoutes =
    [
        "GET api/backend-configuration-pn/chemicals/properties/{propertyId:int}/exports/authority-list",
        "GET api/backend-configuration-pn/chemicals/properties/{propertyId:int}/exports/removed-placements",
        "POST api/backend-configuration-pn/chemicals/properties/{propertyId:int}/exports/inventory",
        "POST api/backend-configuration-pn/chemicals/properties/{propertyId:int}/history",
        "POST api/backend-configuration-pn/chemicals/properties/{propertyId:int}/history/export",
    ];

    private IChemicalExportService _exports;
    private IChemicalHistoryService _history;
    private IChemicalPermissionService _permissions;

    private ChemicalReportsController CreateSut()
    {
        _exports = Substitute.For<IChemicalExportService>();
        _history = Substitute.For<IChemicalHistoryService>();
        _permissions = Substitute.For<IChemicalPermissionService>();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(UserId);
        return new ChemicalReportsController(_exports, _history, _permissions, userService, ControllerTestKit.EchoLocalization())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static void AssertStatus(IActionResult result, int status) =>
        Assert.That(result, Is.InstanceOf<ObjectResult>().With.Property(nameof(ObjectResult.StatusCode)).EqualTo(status));

    [Test]
    public void Routes_MatchThePublishedContract()
    {
        Assert.That(typeof(ChemicalReportsController).GetCustomAttribute<AuthorizeAttribute>()?.Policy,
            Is.EqualTo(BackendConfigurationClaims.AccessBackendConfigurationPlugin));

        Assert.That(ControllerTestKit.RoutesOf(typeof(ChemicalReportsController)), Is.EqualTo(ExpectedRoutes));
    }

    // ---- history ----

    [Test]
    public async Task GetHistory_ChecksViewOnTheProperty_AndReturnsThePage()
    {
        var sut = CreateSut();
        var page = new ChemicalHistoryPageModel([], 0, []);
        _history.GetPageAsync(3, SampleQuery).Returns(page);

        var result = await sut.GetHistory(3, SampleRequest);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.SameAs(page));
        await _permissions.Received(1).RequireAsync(ChemicalCaller.Web(UserId), 3, ChemicalPermission.View);
    }

    [Test]
    public async Task GetHistory_WithoutViewPermission_IsAFailedResultAndNeverReachesTheService()
    {
        var sut = CreateSut();
        _permissions.RequireAsync(ChemicalCaller.Web(UserId), 3, ChemicalPermission.View)
            .ThrowsAsync(new ChemicalPermissionDeniedException("No View permission on property 3."));

        var result = await sut.GetHistory(3, SampleRequest);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("No View permission"));
        await _history.DidNotReceiveWithAnyArgs().GetPageAsync(default, default);
    }

    [Test]
    public async Task GetHistory_ServiceValidationError_IsA200FailedResultWithTheReason()
    {
        var sut = CreateSut();
        _history.GetPageAsync(3, SampleQuery).ThrowsAsync(new ArgumentException("The period ends before it starts."));

        var result = await sut.GetHistory(3, SampleRequest);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ErrorWhileReadingChemicalHistory: The period ends before it starts."));
        Assert.That(sut.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    // E-17: From/To are exactly yyyy-MM-dd; a time, an offset or "Z" is refused like any validation error.
    [TestCase("2026-09-01T00:00:00")]
    [TestCase("2026-09-01T00:00:00Z")]
    [TestCase("2026-09-01T10:30:00+02:00")]
    [TestCase("2026-09-01 10:30")]
    [TestCase("2026-9-1")]
    [TestCase("01/09/2026")]
    [TestCase("")]
    [TestCase(null)]
    public async Task GetHistory_AFromThatIsNotExactlyADate_IsA200FailedResultAndNeverReachesTheService(string from)
    {
        var sut = CreateSut();

        var result = await sut.GetHistory(3, SampleRequest with { From = from });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.StartWith("ErrorWhileReadingChemicalHistory: From must be"));
        Assert.That(sut.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        await _history.DidNotReceiveWithAnyArgs().GetPageAsync(default, default);
    }

    [Test]
    public async Task GetHistory_AToWithATimeOffset_IsA200FailedResultNamingTo()
    {
        var sut = CreateSut();

        var result = await sut.GetHistory(3, SampleRequest with { To = "2026-09-29T00:00:00+00:00" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.StartWith("ErrorWhileReadingChemicalHistory: To must be"));
    }

    [Test]
    public async Task GetHistory_MissingBody_IsA200FailedResult()
    {
        var sut = CreateSut();

        var result = await sut.GetHistory(3, null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ErrorWhileReadingChemicalHistory: A request body is required."));
    }

    [Test]
    public async Task GetHistory_UnexpectedFailure_HidesTheRawMessage()
    {
        var sut = CreateSut();
        _history.GetPageAsync(3, SampleQuery).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await sut.GetHistory(3, SampleRequest);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ErrorWhileReadingChemicalHistory"));
    }

    [Test]
    public async Task GetHistory_TypedOutcomes_AreNotSentToSentry_ButUnexpectedOnesAre()
    {
        var sut = CreateSut();
        _history.GetPageAsync(3, SampleQuery).ThrowsAsync(new ChemicalNotFoundException("gone"));
        Assert.That(await ControllerTestKit.CountSentryEventsAsync(() => sut.GetHistory(3, SampleRequest)), Is.Zero);

        _history.GetPageAsync(3, SampleQuery).ThrowsAsync(new InvalidOperationException("boom"));
        Assert.That(await ControllerTestKit.CountSentryEventsAsync(() => sut.GetHistory(3, SampleRequest)), Is.EqualTo(1));
    }

    // ---- downloads ----

    [Test]
    public async Task ExportHistory_ChecksViewOnTheProperty_AndIsAnXlsxFile()
    {
        var sut = CreateSut();
        _exports.HistoryXlsxAsync(3, SampleQuery).Returns(new ChemicalExportFile([1], "h.xlsx", ChemicalExportService.XlsxContentType));

        var result = (FileContentResult)await sut.ExportHistory(3, SampleRequest);

        Assert.That(result.ContentType, Is.EqualTo(ChemicalExportService.XlsxContentType));
        await _permissions.Received(1).RequireAsync(ChemicalCaller.Web(UserId), 3, ChemicalPermission.View);
    }

    [Test]
    public async Task ExportHistory_WithoutViewPermission_Is403AndNeverReachesTheService()
    {
        var sut = CreateSut();
        _permissions.RequireAsync(ChemicalCaller.Web(UserId), 3, ChemicalPermission.View)
            .ThrowsAsync(new ChemicalPermissionDeniedException("No View permission on property 3."));

        AssertStatus(await sut.ExportHistory(3, SampleRequest), StatusCodes.Status403Forbidden);
        await _exports.DidNotReceiveWithAnyArgs().HistoryXlsxAsync(default, default);
    }

    [TestCase("2026-09-01T00:00:00Z")]
    [TestCase("2026-9-1")]
    public async Task ExportHistory_AFromThatIsNotExactlyADate_Is400AndNeverReachesTheService(string from)
    {
        var sut = CreateSut();

        AssertStatus(await sut.ExportHistory(3, SampleRequest with { From = from }), StatusCodes.Status400BadRequest);
        await _exports.DidNotReceiveWithAnyArgs().HistoryXlsxAsync(default, default);
    }

    [Test]
    public async Task ExportHistory_MissingBody_Is400()
    {
        var sut = CreateSut();

        AssertStatus(await sut.ExportHistory(3, null), StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task ExportAuthorityList_SetsContentDispositionWithUtf8Name()
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
    public async Task ExportInventory_PassesTheCallerAndTheVisibleIds()
    {
        var sut = CreateSut();
        _exports.InventoryXlsxAsync(UserId, 3, Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new ChemicalExportFile([1], "x.xlsx", ChemicalExportService.XlsxContentType));

        await sut.ExportInventory(3, new ChemicalInventoryExportBody([7, 8]));

        await _exports.Received(1).InventoryXlsxAsync(UserId, 3, Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 7, 8 })));
    }

    [Test]
    public async Task ExportInventory_WithoutBody_ExportsEveryOpenPlacement()
    {
        var sut = CreateSut();
        _exports.InventoryXlsxAsync(UserId, 3, null).Returns(new ChemicalExportFile([1], "x.xlsx", ChemicalExportService.XlsxContentType));

        Assert.That(await sut.ExportInventory(3, null), Is.InstanceOf<FileContentResult>());
    }

    [Test]
    public async Task ExportRemovedPlacements_UnknownProperty_IsA404LikeTheOtherFileRoutes()
    {
        var sut = CreateSut();
        _exports.RemovedPlacementsXlsxAsync(UserId, 3).ThrowsAsync(new ChemicalNotFoundException("Property 3 not found."));

        Assert.That(await sut.ExportRemovedPlacements(3), Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task ExportAuthorityList_PermissionDenied_Is403WithTheReason()
    {
        var sut = CreateSut();
        _exports.AuthorityListPdfAsync(UserId, 3).ThrowsAsync(new ChemicalPermissionDeniedException("No View permission on property 3."));

        var result = await sut.ExportAuthorityList(3);

        AssertStatus(result, StatusCodes.Status403Forbidden);
        Assert.That(((ObjectResult)result).Value, Is.EqualTo("No View permission on property 3."));
    }

    [Test]
    public async Task ExportRemovedPlacements_UnexpectedFailure_Is500WithoutTheRawMessage()
    {
        var sut = CreateSut();
        _exports.RemovedPlacementsXlsxAsync(UserId, 3).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await sut.ExportRemovedPlacements(3);

        AssertStatus(result, StatusCodes.Status500InternalServerError);
        Assert.That(((ObjectResult)result).Value?.ToString(), Does.Not.Contain("boom"));
    }

    [Test]
    public async Task Downloads_TypedOutcomes_AreNotSentToSentry_ButUnexpectedOnesAre()
    {
        var sut = CreateSut();
        _exports.RemovedPlacementsXlsxAsync(UserId, 3).ThrowsAsync(new ChemicalNotFoundException("gone"));
        Assert.That(await ControllerTestKit.CountSentryEventsAsync(() => sut.ExportRemovedPlacements(3)), Is.Zero);

        _exports.RemovedPlacementsXlsxAsync(UserId, 3).ThrowsAsync(new InvalidOperationException("boom"));
        Assert.That(await ControllerTestKit.CountSentryEventsAsync(() => sut.ExportRemovedPlacements(3)), Is.EqualTo(1));
    }

    // ---- TODO(chemistry-GA): remove with the [FirstUserOnly] on the controller, like ChemicalsControllerFirstUserGateTests ----

    [Test]
    public Task OnlyTheFirstUser_PassesTheGate_OnEveryRoute() =>
        FirstUserGate.AssertGateOnEveryRouteAsync(typeof(ChemicalReportsController), ExpectedRoutes);
}
