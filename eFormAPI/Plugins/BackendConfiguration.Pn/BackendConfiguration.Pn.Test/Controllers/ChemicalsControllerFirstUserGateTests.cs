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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// Until chemistry goes GA every Kemi web route is reachable only by the tenant's
/// first user (lowest AspNetUsers Id, the eForm host's IsFirstUser convention).
/// The table runs the controller's authorization filters for every routed action,
/// so a new route that escapes the gate fails here. Remove with the gate at GA.
/// </summary>
[TestFixture]
public class ChemicalsControllerFirstUserGateTests
{
    private const int FirstUserId = 1;
    private const int OtherAdminId = 7;

    private static IEnumerable<string> RoutedActions() => typeof(ChemicalsController).GetMethods()
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
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
        var action = typeof(ChemicalsController).GetMethod(actionName)!;
        var services = new ServiceCollection().AddSingleton(userService).BuildServiceProvider();
        var descriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(ChemicalsController).GetTypeInfo(),
            MethodInfo = action,
            ActionName = action.Name,
            ControllerName = "Chemicals",
        };
        var context = new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), descriptor),
            new List<IFilterMetadata>());

        var filters = typeof(ChemicalsController).GetCustomAttributes(true)
            .Concat(action.GetCustomAttributes(true))
            .OfType<IFilterMetadata>()
            .Select(f => f is IFilterFactory factory ? factory.CreateInstance(services) : f);

        foreach (var filter in filters)
        {
            switch (filter)
            {
                case IAsyncAuthorizationFilter asyncFilter:
                    await asyncFilter.OnAuthorizationAsync(context);
                    break;
                case IAuthorizationFilter syncFilter:
                    syncFilter.OnAuthorization(context);
                    break;
            }

            if (context.Result != null)
            {
                break;
            }
        }

        return context.Result;
    }

    [Test]
    public void TheTableCoversEveryRoute()
    {
        Assert.That(RoutedActions().Count(), Is.EqualTo(19));
    }

    [TestCaseSource(nameof(RoutedActions))]
    public async Task AnAdminWhoIsNotTheFirstUser_IsForbiddenOnEveryRoute(string actionName)
    {
        var result = await AuthorizeAsync(actionName, Caller(OtherAdminId));

        Assert.That(result, Is.InstanceOf<IStatusCodeActionResult>());
        Assert.That(((IStatusCodeActionResult)result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task ACallerWithoutAUserId_IsForbiddenEvenWhenTheUsersTableIsEmpty()
    {
        var result = await AuthorizeAsync(nameof(ChemicalsController.GetPropertyInventory), Caller(0, firstUserId: 0));

        Assert.That(result, Is.InstanceOf<IStatusCodeActionResult>());
        Assert.That(((IStatusCodeActionResult)result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task TheFirstUser_PassesTheGateAndGetsTheInventory()
    {
        var userService = Caller(FirstUserId);

        var gate = await AuthorizeAsync(nameof(ChemicalsController.GetPropertyInventory), userService);

        Assert.That(gate, Is.Null);

        var inventory = Substitute.For<IChemicalInventoryService>();
        inventory.GetPropertyInventoryAsync(ChemicalCaller.Web(FirstUserId), 1)
            .Returns(new ChemicalInventoryModel([], [], [], [], [], "", true));
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        var sut = new ChemicalsController(inventory, userService, localization);

        var result = await sut.GetPropertyInventory(1);

        Assert.That(result.Success, Is.True);
    }
}
