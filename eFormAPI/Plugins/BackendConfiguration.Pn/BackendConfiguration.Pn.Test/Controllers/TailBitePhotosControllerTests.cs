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
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Services.TailBite;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

public class TailBitePhotosControllerTests
{
    private static readonly Guid PhotoUuid = Guid.NewGuid();

    private static ITailBiteAccess AccessWithSite(int siteId = 7)
    {
        var access = Substitute.For<ITailBiteAccess>();
        access.RequireCallerSiteAsync().Returns(siteId);
        return access;
    }

    private static TailBitePhotosController Sut(ITailBiteAccess access, ITailBiteOutbreakPhotoService photos) => new(access, photos)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Test]
    public async Task Get_NotFoundForbiddenAndUnauthenticated_All404WithoutBody()
    {
        var notFound = Substitute.For<ITailBiteOutbreakPhotoService>();
        notFound.OpenForManagerAsync(7, PhotoUuid).Throws(TailBiteNotFoundException.PhotoNotFound());
        var forbidden = Substitute.For<ITailBiteOutbreakPhotoService>();
        forbidden.OpenForManagerAsync(7, PhotoUuid).Throws(TailBiteForbiddenException.NoAccess());
        var noCaller = Substitute.For<ITailBiteAccess>();
        noCaller.RequireCallerSiteAsync().Throws(new TailBiteForbiddenException("Caller has no resolvable worker identity."));
        var disabled = Substitute.For<ITailBiteAccess>();
        disabled.RequireCallerSiteAsync().Throws(new RpcException(new Status(StatusCode.Unauthenticated, "account disabled")));
        var unused = Substitute.For<ITailBiteOutbreakPhotoService>();

        foreach (var (name, sut) in new[]
                 {
                     ("not found", Sut(AccessWithSite(), notFound)), ("forbidden", Sut(AccessWithSite(), forbidden)),
                     ("no caller", Sut(noCaller, unused)), ("disabled account", Sut(disabled, unused)),
                 })
        {
            var result = await sut.Get(PhotoUuid);
            Assert.That(result, Is.TypeOf<NotFoundResult>(), name);
            Assert.That(sut.Response.Headers.ContainsKey("Content-Disposition"), Is.False, name);
        }
        await unused.DidNotReceiveWithAnyArgs().OpenForManagerAsync(default, default);
    }

    [Test]
    public async Task Get_Success_FileWithInlineDisposition()
    {
        var photos = Substitute.For<ITailBiteOutbreakPhotoService>();
        var content = new MemoryStream([1, 2, 3]);
        photos.OpenForManagerAsync(7, PhotoUuid).Returns((content, "image/jpeg"));
        var sut = Sut(AccessWithSite(), photos);

        var result = await sut.Get(PhotoUuid);

        var file = result as FileStreamResult;
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.FileStream, Is.SameAs(content));
        Assert.That(file.ContentType, Is.EqualTo("image/jpeg"));
        Assert.That(file.FileDownloadName, Is.Empty);
        Assert.That(sut.Response.Headers["Content-Disposition"].ToString(), Is.EqualTo("inline"));
    }

    [Test]
    public void Controller_IsAuthorized_AndRoutedUnderTheTailBitePhotosPrefix()
    {
        Assert.That(typeof(TailBitePhotosController).GetCustomAttributes<AuthorizeAttribute>(inherit: false), Is.Not.Empty);
        Assert.That(ControllerTestKit.RoutesOf(typeof(TailBitePhotosController)),
            Is.EqualTo(new[] { "GET api/backend-configuration-pn/tail-bite/photos/{photoUuid:guid}" }));
    }
}
