using System.Linq;
using System.Reflection;
using BackendConfiguration.Pn.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// #1417: the inbox is admin-only. ASP.NET Core combines authorization attributes, so only [AllowAnonymous]
/// (on the class or an action) could relax the class-level admin role.
/// </summary>
[TestFixture]
public class InboxControllerAuthorizationTests
{
    [Test]
    public void InboxController_IsAdminOnly()
    {
        var attributes = typeof(InboxController).GetCustomAttributes<AuthorizeAttribute>().ToList();

        Assert.That(attributes, Has.Count.EqualTo(1));
        Assert.That(attributes[0].Roles, Is.EqualTo(EformRole.Admin));
        Assert.That(attributes[0].Policy, Is.Null.Or.Empty);
        Assert.That(typeof(InboxController).IsDefined(typeof(AllowAnonymousAttribute)), Is.False,
            "[AllowAnonymous] on the controller would override the admin role for every action");
    }

    [Test]
    public void InboxController_NoActionIsAnonymous()
    {
        var anonymousActions = typeof(InboxController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsDefined(typeof(AllowAnonymousAttribute)))
            .Select(m => m.Name);

        // An action-level [Authorize] only adds requirements (they combine with the class's), so it is allowed;
        // [AllowAnonymous] would skip the admin role.
        Assert.That(anonymousActions, Is.Empty);
    }
}
