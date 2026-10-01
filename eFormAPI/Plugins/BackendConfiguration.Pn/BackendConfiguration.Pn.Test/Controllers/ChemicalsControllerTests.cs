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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
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

/// <summary>
/// The Kemi web REST façade (flutter-chemistry Plan A Task 15): the published
/// route table, the class-level policy, the web-admin caller identity, route
/// id + body assembly, failure mapping and the photo upload pre-checks.
/// </summary>
[TestFixture]
public class ChemicalsControllerTests
{
    private const int UserId = 5;

    private IChemicalInventoryService _inventory;

    private ChemicalsController CreateSut()
    {
        _inventory = Substitute.For<IChemicalInventoryService>();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(UserId);
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new ChemicalsController(_inventory, userService, localization);
    }

    private static readonly ChemicalCaller Web = ChemicalCaller.Web(UserId);

    [Test]
    public void Controller_RequiresTheBackendConfigurationPluginPolicy()
    {
        var attribute = typeof(ChemicalsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(attribute?.Policy, Is.EqualTo(BackendConfigurationClaims.AccessBackendConfigurationPlugin));
    }

    [Test]
    public void Routes_MatchThePublishedContract()
    {
        var prefix = typeof(ChemicalsController).GetCustomAttribute<RouteAttribute>()!.Template;
        var routes = typeof(ChemicalsController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()
                .Select(a => $"{a.HttpMethods.Single()} {prefix}/{a.Template}"))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.That(routes, Is.EqualTo(new[]
        {
            "GET api/backend-configuration-pn/chemicals/locations/{locationId:int}/photo",
            "GET api/backend-configuration-pn/chemicals/properties/{propertyId:int}/inventory",
            "GET api/backend-configuration-pn/chemicals/properties/{propertyId:int}/permissions",
            "GET api/backend-configuration-pn/chemicals/properties/{propertyId:int}/settings",
            "GET api/backend-configuration-pn/chemicals/register/barcode/{barcode}",
            "GET api/backend-configuration-pn/chemicals/register/search",
            "GET api/backend-configuration-pn/chemicals/sds/{fileName}",
            "POST api/backend-configuration-pn/chemicals/locations",
            "POST api/backend-configuration-pn/chemicals/locations/{locationId:int}/archive",
            "POST api/backend-configuration-pn/chemicals/locations/{locationId:int}/photo",
            "POST api/backend-configuration-pn/chemicals/placements",
            "POST api/backend-configuration-pn/chemicals/placements/{placementId:int}/move",
            "POST api/backend-configuration-pn/chemicals/placements/{placementId:int}/remove",
            "POST api/backend-configuration-pn/chemicals/placements/{placementId:int}/stock-entries",
            "PUT api/backend-configuration-pn/chemicals/locations/{locationId:int}",
            "PUT api/backend-configuration-pn/chemicals/placements/{placementId:int}/note",
            "PUT api/backend-configuration-pn/chemicals/properties/{propertyId:int}/locations/order",
            "PUT api/backend-configuration-pn/chemicals/properties/{propertyId:int}/permissions",
            "PUT api/backend-configuration-pn/chemicals/properties/{propertyId:int}/settings",
        }));
    }

    [Test]
    public async Task EveryCall_ActsAsAWebAdminWithTheEformUserId()
    {
        var sut = CreateSut();
        _inventory.GetPropertyInventoryAsync(Web, 1)
            .Returns(new ChemicalInventoryModel([], [], [], [], [], "", true));

        var result = await sut.GetPropertyInventory(1);

        Assert.That(result.Success, Is.True);
        await _inventory.Received(1).GetPropertyInventoryAsync(Arg.Is<ChemicalCaller>(c => c.IsWebAdmin && c.UserId == UserId), 1);
    }

    [Test]
    public async Task Move_CombinesRouteIdAndBody()
    {
        var sut = CreateSut();
        _inventory.MovePlacementAsync(Web, Arg.Any<ChemicalMovePlacementCommand>())
            .Returns(new ChemicalPlacementChangeModel([], [], []));

        await sut.MovePlacement(7, new ChemicalMovePlacementBody(12, "Hylde 1", 0.5m));

        await _inventory.Received(1).MovePlacementAsync(Web, new ChemicalMovePlacementCommand(7, 12, "Hylde 1", 0.5m));
    }

    [Test]
    public async Task Failures_BecomeUnsuccessfulResults_WithTheReason()
    {
        var sut = CreateSut();
        _inventory.ArchiveLocationAsync(Web, 9).ThrowsAsync(new ChemicalPreconditionException("open placements"));

        var result = await sut.ArchiveLocation(9);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("open placements"));
    }

    [Test]
    public async Task Sds_UnknownFileIs404_KnownIsAPdf()
    {
        var sut = CreateSut();
        _inventory.GetSdsPdfAsync(Web, "missing").ThrowsAsync(new ChemicalNotFoundException("no"));
        _inventory.GetSdsPdfAsync(Web, "abc").Returns(new byte[] { 37, 80, 68, 70 });

        Assert.That(await sut.GetSds("missing"), Is.InstanceOf<NotFoundResult>());
        var file = (FileContentResult)await sut.GetSds("abc");
        Assert.That(file.ContentType, Is.EqualTo("application/pdf"));
    }

    [Test]
    public async Task SavePermissions_ForwardsTheChangedWorkers()
    {
        var sut = CreateSut();
        var changes = new List<ChemicalSetWorkerPermissionCommand>
        {
            new(8, ChemicalPermissionFlagsModel.None with { View = true }),
        };
        _inventory.SetWorkerPermissionsAsync(Web, 1, changes).Returns([]);

        var result = await sut.SavePermissions(1, changes);

        Assert.That(result.Success, Is.True);
        await _inventory.Received(1).SetWorkerPermissionsAsync(Web, 1, changes);
    }

    // ---- photo upload: refuse before the bytes are read (as the gRPC upload does) ----

    [Test]
    public async Task UploadPhoto_ForwardsTheBytesAndContentType_AfterThePreCheck()
    {
        var sut = CreateSut();
        var bytes = new byte[] { 1, 2, 3 };
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "shelf.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg",
        };

        var result = await sut.UploadLocationPhoto(4, file);

        Assert.That(result.Success, Is.True);
        Received.InOrder(() =>
        {
            _inventory.RequireCanManageLocationAsync(Web, 4);
            _inventory.SaveLocationPhotoAsync(Web, 4, Arg.Is<byte[]>(b => b.SequenceEqual(bytes)), "image/jpeg");
        });
    }

    [Test]
    public async Task UploadPhoto_CallerWhoCannotManageTheLocation_IsRefusedBeforeTheBytesAreRead()
    {
        var sut = CreateSut();
        _inventory.RequireCanManageLocationAsync(Web, 4).ThrowsAsync(new ChemicalPreconditionException("archived"));
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(3);

        var result = await sut.UploadLocationPhoto(4, file);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("archived"));
        await file.DidNotReceiveWithAnyArgs().CopyToAsync(default, default);
        await _inventory.DidNotReceiveWithAnyArgs().SaveLocationPhotoAsync(default, default, default, default);
    }

    [Test]
    public async Task UploadPhoto_OverTheSizeLimit_IsRefusedBeforeTheBytesAreRead()
    {
        var sut = CreateSut();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(ChemicalInventoryService.MaxPhotoBytes + 1L);

        var result = await sut.UploadLocationPhoto(4, file);

        Assert.That(result.Success, Is.False);
        await file.DidNotReceiveWithAnyArgs().CopyToAsync(default, default);
        await _inventory.DidNotReceiveWithAnyArgs().SaveLocationPhotoAsync(default, default, default, default);
    }

    [Test]
    public async Task UploadPhoto_EmptyFile_IsRefused()
    {
        var sut = CreateSut();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(0);

        var result = await sut.UploadLocationPhoto(4, file);

        Assert.That(result.Success, Is.False);
        await _inventory.DidNotReceiveWithAnyArgs().SaveLocationPhotoAsync(default, default, default, default);
    }
}
