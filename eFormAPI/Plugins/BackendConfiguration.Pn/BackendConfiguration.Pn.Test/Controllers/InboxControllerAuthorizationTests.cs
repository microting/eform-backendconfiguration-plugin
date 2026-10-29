using System.Linq;
using System.Reflection;
using BackendConfiguration.Pn.Controllers;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// #1417, D-L6: the inbox is for admins and Leders: a plain [Authorize] plus [ArchiveManager]. ASP.NET Core combines
/// authorization attributes, so only [AllowAnonymous] (on the class or an action) could skip the manager gate.
/// </summary>
[TestFixture]
public class InboxControllerAuthorizationTests
{
    [Test]
    public void InboxController_IsSignedInOnly_AndGatedByArchiveManager()
    {
        var attributes = typeof(InboxController).GetCustomAttributes<AuthorizeAttribute>().ToList();

        Assert.That(attributes, Has.Count.EqualTo(1));
        Assert.That(attributes[0].Roles, Is.Null.Or.Empty, "D-L6: [ArchiveManager] replaces Roles = Admin");
        Assert.That(attributes[0].Policy, Is.Null.Or.Empty);
        Assert.That(typeof(InboxController).IsDefined(typeof(ArchiveManagerAttribute)), Is.True);
        Assert.That(typeof(InboxController).IsDefined(typeof(AllowAnonymousAttribute)), Is.False,
            "[AllowAnonymous] on the controller would skip the gate for every action");
    }

    [Test]
    public void InboxController_NoActionIsAnonymous()
    {
        var anonymousActions = typeof(InboxController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsDefined(typeof(AllowAnonymousAttribute)))
            .Select(m => m.Name);

        // An action-level [Authorize] only adds requirements (they combine with the class's), so it is allowed;
        // [AllowAnonymous] would skip the manager gate.
        Assert.That(anonymousActions, Is.Empty);
    }
}
