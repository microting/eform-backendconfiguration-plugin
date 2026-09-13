using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackendConfiguration.Pn.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microting.EformAngularFrontendBase.Infrastructure.Const;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// Authorization tests for <see cref="AssignmentWorkerController"/>. The class
/// requires plugin access. Each action that creates, changes or removes a
/// worker or its property assignments also requires the core device-user
/// policy the Angular client already checks before offering that action.
/// Reads stay at plugin access because compliance, calendar and task pages
/// open to every plugin user load them.
///
/// <see cref="EveryPublicAction_HasAnExplicitPolicyDecision"/> fails when an
/// action is added without a decision recorded in <see cref="ExpectedPolicies"/>.
/// </summary>
[TestFixture]
public class AssignmentWorkerControllerTests
{
    // Keyed by "<method name>(<first parameter type name>)": Create is overloaded.
    // A null value means the action relies on the class-level policy alone.
    private static readonly Dictionary<string, string?> ExpectedPolicies = new()
    {
        ["GetPropertiesAssignment(List`1)"] = null,
        ["GetSimplePropertiesAssignment(List`1)"] = null,
        ["Create(PropertyAssignWorkersModel)"] = AuthConsts.EformPolicies.DeviceUsers.Create,
        ["Update(PropertyAssignWorkersModel)"] = AuthConsts.EformPolicies.DeviceUsers.Update,
        ["Delete(Int32)"] = AuthConsts.EformPolicies.DeviceUsers.Delete,
        ["Index(PropertyWorkersFiltrationModel)"] = null,
        ["UpdateDeviceUser(DeviceUserModel)"] = AuthConsts.EformPolicies.DeviceUsers.Update,
        ["Create(DeviceUserModel)"] = AuthConsts.EformPolicies.DeviceUsers.Create,
        ["UpdateSimplifiedDeviceUser(SimpleDeviceUserModel)"] = AuthConsts.EformPolicies.DeviceUsers.Update,
    };

    private static IEnumerable<MethodInfo> PublicActions() =>
        typeof(AssignmentWorkerController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static string Key(MethodInfo method) =>
        $"{method.Name}({method.GetParameters().First().ParameterType.Name})";

    [Test]
    public void Controller_RequiresBackendConfigurationPluginAccessPolicy()
    {
        var authorize = typeof(AssignmentWorkerController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.That(authorize.Policy,
            Is.EqualTo(BackendConfigurationClaims.AccessBackendConfigurationPlugin));
    }

    [Test]
    public void EveryPublicAction_HasAnExplicitPolicyDecision()
    {
        Assert.That(PublicActions().Select(Key), Is.EquivalentTo(ExpectedPolicies.Keys));
    }

    [TestCaseSource(nameof(ExpectedPolicyCases))]
    public void Action_CarriesExactlyItsExpectedPolicy(string key, string? expectedPolicy)
    {
        var method = PublicActions().Single(m => Key(m) == key);
        var policies = method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .Select(a => a.Policy)
            .ToList();

        if (expectedPolicy == null)
        {
            Assert.That(policies, Is.Empty, $"{key} must rely on the class-level policy alone");
        }
        else
        {
            Assert.That(policies, Is.EqualTo(new[] { expectedPolicy }), key);
        }
    }

    [Test]
    public void NothingIsAnonymous()
    {
        Assert.That(typeof(AssignmentWorkerController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true),
            Is.Empty);
        Assert.That(PublicActions().Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
            .Select(m => m.Name), Is.Empty);
    }

    private static IEnumerable<TestCaseData> ExpectedPolicyCases() =>
        ExpectedPolicies.Select(p => new TestCaseData(p.Key, p.Value).SetName($"Action_CarriesExactlyItsExpectedPolicy({p.Key})"));
}
