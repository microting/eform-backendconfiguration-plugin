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
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

/// <summary>
/// #1302 — the task list opens to the eForm `user` role, but ONLY its inline
/// rename and the Manage-tags orphan purge may be called by one; every batch
/// action stays admin-only.
///
/// ASP.NET Core COMBINES authorization attributes (all must pass), so the two
/// user endpoints cannot be opened with a method-level [Authorize] under the
/// admin controller's class-level role — they live in
/// <see cref="TaskListUserController"/> under the same route prefix. These
/// tests evaluate the EFFECTIVE policy of every action (class + method
/// attributes, combined exactly as MVC's authorization filter does) against a
/// `user` and an `admin` principal, and pin the complete set of controllers and
/// actions under the task-list route prefix — so an endpoint added later, to
/// either controller or to a third one, cannot silently open to users.
///
/// No database: pure reflection + the real authorization service.
/// </summary>
[TestFixture]
public class TaskListControllerAuthorizationTests
{
    private const string TaskListPrefix = "api/backend-configuration-pn/task-list";

    private static readonly string[] UserRoutes = ["rename", "purge-orphan-tags"];

    private IAuthorizationService _authorizationService = null!;
    private IAuthorizationPolicyProvider _policyProvider = null!;
    private ServiceProvider _serviceProvider = null!;

    [OneTimeSetUp]
    public void BuildAuthorization()
    {
        _serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddAuthorizationCore()
            .BuildServiceProvider();
        _authorizationService = _serviceProvider.GetRequiredService<IAuthorizationService>();
        _policyProvider = new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions()));
    }

    [OneTimeTearDown]
    public void DisposeAuthorization() => _serviceProvider.Dispose();

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, $"{role}@test.com"), new Claim(ClaimTypes.Role, role)],
            authenticationType: "Test"));

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static string RouteOf(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>().Single().Template ?? string.Empty;

    private static IEnumerable<Type> TaskListControllers() =>
        typeof(TaskListController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttributes<RouteAttribute>()
                .Any(r => string.Equals(r.Template?.TrimEnd('/'), TaskListPrefix, StringComparison.OrdinalIgnoreCase)));

    private async Task<bool> IsAllowed(Type controller, MethodInfo action, string role)
    {
        // What MVC's AuthorizeFilter evaluates: every IAuthorizeData on the
        // controller AND the action, combined into one policy.
        var authorizeData = controller.GetCustomAttributes(true).OfType<IAuthorizeData>()
            .Concat(action.GetCustomAttributes(true).OfType<IAuthorizeData>())
            .ToList();
        Assert.That(authorizeData, Is.Not.Empty,
            $"{controller.Name}.{action.Name} has no [Authorize] at all — it would be anonymous.");
        var policy = await AuthorizationPolicy.CombineAsync(_policyProvider, authorizeData);
        Assert.That(policy, Is.Not.Null);
        var result = await _authorizationService.AuthorizeAsync(Principal(role), null, policy!);
        return result.Succeeded;
    }

    private static IEnumerable<TestCaseData> AdminActionCases() =>
        Actions(typeof(TaskListController))
            .Select(a => new TestCaseData(a.Name).SetName($"AdminOnly_{a.Name}"));

    private static IEnumerable<TestCaseData> UserActionCases() =>
        Actions(typeof(TaskListUserController))
            .Select(a => new TestCaseData(a.Name).SetName($"OpenToUser_{a.Name}"));

    [Test]
    public void OnlyTheTwoKnownControllersServeTheTaskListPrefix()
    {
        Assert.That(TaskListControllers().Select(t => t.Name).OrderBy(n => n),
            Is.EqualTo(new[] { nameof(TaskListController), nameof(TaskListUserController) }));
    }

    [Test]
    public void AdminController_KeepsTheClassLevelAdminRole()
    {
        var authorize = typeof(TaskListController).GetCustomAttributes<AuthorizeAttribute>().ToList();
        Assert.That(authorize.Select(a => a.Roles), Is.EqualTo(new[] { EformRole.Admin }));
    }

    [Test]
    public void UserController_ExposesExactlyRenameAndPurgeOrphanTags()
    {
        Assert.That(Actions(typeof(TaskListUserController)).Select(RouteOf).OrderBy(r => r),
            Is.EqualTo(UserRoutes.OrderBy(r => r)));
    }

    [Test]
    public void AdminController_NoLongerServesTheUserRoutes()
    {
        // A route on both controllers would be an ambiguous match at runtime.
        var adminRoutes = Actions(typeof(TaskListController)).Select(RouteOf).ToList();
        Assert.That(adminRoutes, Is.Not.Empty);
        Assert.That(adminRoutes.Intersect(UserRoutes), Is.Empty);
    }

    [Test]
    public void NoTaskListActionAllowsAnonymous()
    {
        foreach (var controller in TaskListControllers())
        {
            Assert.That(controller.GetCustomAttributes<AllowAnonymousAttribute>(), Is.Empty, controller.Name);
            foreach (var action in Actions(controller))
            {
                Assert.That(action.GetCustomAttributes<AllowAnonymousAttribute>(), Is.Empty,
                    $"{controller.Name}.{action.Name}");
            }
        }
    }

    [TestCaseSource(nameof(UserActionCases))]
    public async Task UserControllerAction_AcceptsUserAndAdmin(string actionName)
    {
        var action = typeof(TaskListUserController).GetMethod(actionName)!;
        Assert.That(await IsAllowed(typeof(TaskListUserController), action, EformRole.User), Is.True);
        Assert.That(await IsAllowed(typeof(TaskListUserController), action, EformRole.Admin), Is.True);
    }

    [TestCaseSource(nameof(AdminActionCases))]
    public async Task AdminControllerAction_RejectsUser_AcceptsAdmin(string actionName)
    {
        var action = typeof(TaskListController).GetMethod(actionName)!;
        Assert.That(await IsAllowed(typeof(TaskListController), action, EformRole.User), Is.False,
            $"{actionName} must stay admin-only for the `user` role.");
        Assert.That(await IsAllowed(typeof(TaskListController), action, EformRole.Admin), Is.True);
    }

    /// <summary>
    /// Pins the ASP.NET Core rule the controller split relies on: a
    /// method-level [Authorize] does NOT relax a class-level role. If this ever
    /// changed, the split would still be correct — but the reasoning in the
    /// controllers' docs would not.
    /// </summary>
    [Test]
    public async Task MethodLevelAuthorize_CannotRelaxAClassLevelRole()
    {
        var combined = await AuthorizationPolicy.CombineAsync(_policyProvider,
            [new AuthorizeAttribute { Roles = EformRole.Admin }, new AuthorizeAttribute()]);
        var result = await _authorizationService.AuthorizeAsync(Principal(EformRole.User), null, combined!);
        Assert.That(result.Succeeded, Is.False);
    }
}
