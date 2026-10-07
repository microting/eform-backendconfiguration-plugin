using System.Linq;
using System.Reflection;
using BackendConfiguration.Pn.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// #1417: the inbox is admin-only. ASP.NET Core combines authorization attributes, so the
/// class-level admin role cannot be relaxed by an action and no other [Authorize] may sit on top of it.
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
    }

    [Test]
    public void InboxController_ActionsDoNotRelaxOrReplaceTheAdminRole()
    {
        var actionsWithOwnAuthorization = typeof(InboxController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsDefined(typeof(AuthorizeAttribute)) || m.IsDefined(typeof(AllowAnonymousAttribute)))
            .Select(m => m.Name);

        Assert.That(actionsWithOwnAuthorization, Is.Empty);
    }
}
