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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>Shared harness for [FirstUserOnly] gate tests: runs a controller's authorization filters the way MVC does.</summary>
internal static class FirstUserGate
{
    public static IUserService Caller(int userId, int firstUserId)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(userId);
        userService.GetFirstUserIdInDb().Returns(firstUserId);
        return userService;
    }

    /// <summary>Runs only the IAsyncAuthorizationFilter attributes (e.g. [FirstUserOnly]) on the controller and action; [Authorize] policies are not evaluated. Returns the short-circuit result, or null when those filters let the request through.</summary>
    public static async Task<IActionResult> AuthorizeAsync(Type controller, string actionName, IUserService userService)
    {
        var action = controller.GetMethod(actionName)!;
        var services = new ServiceCollection().AddSingleton(userService).BuildServiceProvider();
        var descriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = controller.GetTypeInfo(),
            MethodInfo = action,
            ActionName = action.Name,
            ControllerName = controller.Name,
        };
        var context = new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), descriptor),
            new List<IFilterMetadata>());

        var filters = controller.GetCustomAttributes(true)
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

    public static void AssertForbidden(IActionResult result) =>
        Assert.That(result, Is.TypeOf<StatusCodeResult>()
            .With.Property(nameof(StatusCodeResult.StatusCode)).EqualTo(StatusCodes.Status403Forbidden));
}
