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
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// The Indbakke address is fixed: only the tenant's first user (lowest AspNetUsers Id) may rotate it
/// ("Lav ny adresse"). Every other inbox route stays open to everyone the class policy lets in.
/// </summary>
[TestFixture]
public class InboxControllerFirstUserGateTests
{
    private const int FirstUserId = 1;
    private const int OtherAdminId = 7;

    private static IEnumerable<string> UngatedActions() => typeof(InboxController).GetMethods()
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any() && m.Name != nameof(InboxController.Rotate))
        .Select(m => m.Name)
        .OrderBy(n => n, StringComparer.Ordinal);

    private static IUserService Caller(int userId, int firstUserId = FirstUserId)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(userId);
        userService.GetFirstUserIdInDb().Returns(firstUserId);
        return userService;
    }

    /// <summary>
    /// Runs the class- and action-level authorization filters the way MVC does and
    /// returns the short-circuit result, or null when the request may reach the action.
    /// </summary>
    private static async Task<IActionResult> AuthorizeAsync(string actionName, IUserService userService)
    {
        var action = typeof(InboxController).GetMethod(actionName)!;
        var services = new ServiceCollection().AddSingleton(userService).BuildServiceProvider();
        var descriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(InboxController).GetTypeInfo(),
            MethodInfo = action,
            ActionName = action.Name,
            ControllerName = "Inbox",
        };
        var context = new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), descriptor),
            new List<IFilterMetadata>());

        var filters = typeof(InboxController).GetCustomAttributes(true)
            .Concat(action.GetCustomAttributes(true))
            .OfType<IAsyncAuthorizationFilter>();

        foreach (var filter in filters)
        {
            await filter.OnAuthorizationAsync(context);
            if (context.Result != null)
            {
                break;
            }
        }

        return context.Result;
    }

    private static void AssertForbidden(IActionResult result) =>
        Assert.That(result, Is.TypeOf<StatusCodeResult>()
            .With.Property(nameof(StatusCodeResult.StatusCode)).EqualTo(StatusCodes.Status403Forbidden));

    [Test]
    public async Task Rotate_AnAdminWhoIsNotTheFirstUser_IsForbidden()
    {
        AssertForbidden(await AuthorizeAsync(nameof(InboxController.Rotate), Caller(OtherAdminId)));
    }

    [Test]
    public async Task Rotate_ACallerWithoutAUserId_IsForbiddenEvenWhenTheUsersTableIsEmpty()
    {
        AssertForbidden(await AuthorizeAsync(nameof(InboxController.Rotate), Caller(0, firstUserId: 0)));
    }

    [Test]
    public async Task Rotate_TheFirstUser_PassesTheGateAndRotates()
    {
        var userService = Caller(FirstUserId);

        var gate = await AuthorizeAsync(nameof(InboxController.Rotate), userService);

        Assert.That(gate, Is.Null);

        var settings = Substitute.For<IInboxSettingsService>();
        settings.RotateAddressAsync(FirstUserId).Returns(
            new OperationDataResult<InboxSettingsModel>(true, new InboxSettingsModel { Address = "4711-abc@example.net" }));
        var sut = new InboxController(Substitute.For<IInboxService>(), settings, userService,
            NullLogger<InboxController>.Instance);

        var result = await sut.Rotate();

        Assert.That(result.Success, Is.True);
        await settings.Received(1).RotateAddressAsync(FirstUserId);
    }

    [TestCaseSource(nameof(UngatedActions))]
    public async Task EveryOtherRoute_IsOpenToAnAdminWhoIsNotTheFirstUser(string actionName)
    {
        Assert.That(await AuthorizeAsync(actionName, Caller(OtherAdminId)), Is.Null);
    }
}
