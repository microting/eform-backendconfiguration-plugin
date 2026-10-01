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
/// Editing and deleting a property, changing its control areas and deleting a
/// compliance are admin-only. The controllers keep their class-level
/// [Authorize] (any signed-in account, which includes the `user` role), so the
/// admin role sits on each action. Every case below evaluates the EFFECTIVE
/// policy of an action — class and method attributes combined, exactly as
/// MVC's authorization filter does — against a `user` and an `admin`
/// principal. Same approach as <see cref="TaskListControllerAuthorizationTests"/>.
///
/// No database: pure reflection + the real authorization service.
/// </summary>
[TestFixture]
public class PropertyAdminActionsAuthorizationTests
{
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

    /// <summary>The single public action named <paramref name="name"/> that carries <typeparamref name="TVerb"/>.</summary>
    private static MethodInfo Action<TVerb>(Type controller, string name) where TVerb : HttpMethodAttribute =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == name && m.GetCustomAttributes<TVerb>().Any());

    private async Task<bool> IsAllowed(Type controller, MethodInfo action, string role)
    {
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

    private static IEnumerable<TestCaseData> AdminOnlyActions()
    {
        yield return new TestCaseData(typeof(PropertiesController), Action<HttpPutAttribute>(typeof(PropertiesController), nameof(PropertiesController.Update)))
            .SetName("AdminOnly_PUT_properties");
        yield return new TestCaseData(typeof(PropertiesController), Action<HttpDeleteAttribute>(typeof(PropertiesController), nameof(PropertiesController.Delete)))
            .SetName("AdminOnly_DELETE_properties");
        yield return new TestCaseData(typeof(PropertyAreasController), Action<HttpPutAttribute>(typeof(PropertyAreasController), nameof(PropertyAreasController.Update)))
            .SetName("AdminOnly_PUT_property-areas");
        yield return new TestCaseData(typeof(CompliancesController), Action<HttpDeleteAttribute>(typeof(CompliancesController), nameof(CompliancesController.Delete)))
            .SetName("AdminOnly_DELETE_compliances-delete-id");
    }

    /// <summary>
    /// Reads and the compliance case endpoints the worker-facing pages use stay
    /// open to the `user` role — the admin role is on the four actions, not on
    /// the controllers.
    /// </summary>
    private static IEnumerable<TestCaseData> UserActions()
    {
        yield return new TestCaseData(typeof(PropertiesController), Action<HttpPostAttribute>(typeof(PropertiesController), nameof(PropertiesController.Index)))
            .SetName("OpenToUser_POST_properties-index");
        yield return new TestCaseData(typeof(PropertiesController), Action<HttpGetAttribute>(typeof(PropertiesController), nameof(PropertiesController.Read)))
            .SetName("OpenToUser_GET_properties");
        yield return new TestCaseData(typeof(PropertyAreasController), Action<HttpGetAttribute>(typeof(PropertyAreasController), nameof(PropertyAreasController.Read)))
            .SetName("OpenToUser_GET_property-areas");
        yield return new TestCaseData(typeof(CompliancesController), Action<HttpPostAttribute>(typeof(CompliancesController), nameof(CompliancesController.Index)))
            .SetName("OpenToUser_POST_compliances-index");
        yield return new TestCaseData(typeof(CompliancesController), Action<HttpPutAttribute>(typeof(CompliancesController), nameof(CompliancesController.Update)))
            .SetName("OpenToUser_PUT_compliances-cases");
    }

    [TestCaseSource(nameof(AdminOnlyActions))]
    public async Task AdminOnlyAction_RejectsUser_AcceptsAdmin(Type controller, MethodInfo action)
    {
        Assert.That(action.GetCustomAttributes<AllowAnonymousAttribute>(), Is.Empty,
            $"{controller.Name}.{action.Name} must not allow anonymous access.");
        Assert.That(await IsAllowed(controller, action, EformRole.User), Is.False,
            $"{controller.Name}.{action.Name} must be admin-only.");
        Assert.That(await IsAllowed(controller, action, EformRole.Admin), Is.True);
    }

    [TestCaseSource(nameof(UserActions))]
    public async Task UserAction_AcceptsUserAndAdmin(Type controller, MethodInfo action)
    {
        Assert.That(await IsAllowed(controller, action, EformRole.User), Is.True,
            $"{controller.Name}.{action.Name} must stay open to the user role.");
        Assert.That(await IsAllowed(controller, action, EformRole.Admin), Is.True);
    }

    /// <summary>
    /// Pins the PUT/DELETE set on PropertiesController: adding one fails here
    /// until it is reviewed, listed and admin-only.
    /// </summary>
    [Test]
    public async Task EveryPutAndDeleteOnPropertiesController_IsAdminOnly()
    {
        var actions = typeof(PropertiesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpPutAttribute>().Any() || m.GetCustomAttributes<HttpDeleteAttribute>().Any())
            .ToList();
        Assert.That(actions.Select(a => a.Name).OrderBy(n => n),
            Is.EqualTo(new[] { nameof(PropertiesController.Delete), nameof(PropertiesController.Update) }));
        foreach (var action in actions)
        {
            Assert.That(await IsAllowed(typeof(PropertiesController), action, EformRole.User), Is.False,
                $"PropertiesController.{action.Name} must be admin-only.");
        }
    }
}
