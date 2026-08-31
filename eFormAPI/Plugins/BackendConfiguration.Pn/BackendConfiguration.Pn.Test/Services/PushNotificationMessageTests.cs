using System.Collections.Generic;
using BackendConfiguration.Pn.Services.PushNotificationService;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// Message shape for the flutter-eform sender. The first caller pushes
/// data-only wake-ups, which are the half of the contract that cannot be seen
/// on a phone when it is wrong: a silent push that quietly carries a
/// Notification block shows a stray banner, and one missing APNs
/// content-available is simply never delivered to a backgrounded iOS app.
///
/// Pure - <see cref="PushNotificationService.BuildMessage"/> touches neither
/// the database nor Firebase, so this lives in the unit suite rather than
/// paying for a MariaDB container.
/// </summary>
[TestFixture]
public class PushNotificationMessageTests
{
    [Test]
    public void BuildMessage_DataOnly_OmitsNotificationAndSetsContentAvailable()
    {
        var message = PushNotificationService.BuildMessage(
            "tok",
            "",
            "",
            new Dictionary<string, string> { { "type", "events_changed" } });

        Assert.Multiple(() =>
        {
            Assert.That(message.Notification, Is.Null,
                "a data-only push must not attach a visible Notification block");
            Assert.That(message.Apns, Is.Not.Null);
            Assert.That(message.Apns.Aps.ContentAvailable, Is.True,
                "iOS needs content-available to wake the app for a silent data push");
            Assert.That(message.Data["type"], Is.EqualTo("events_changed"));
#pragma warning disable CS0618 // Token is the FCM registration token; Fid is a
            // different identifier and would break every send. See the comment
            // on BuildMessage's Token assignment.
            Assert.That(message.Token, Is.EqualTo("tok"));
#pragma warning restore CS0618
        });
    }

    [Test]
    public void BuildMessage_WithTitleOrBody_SetsNotificationBlock()
    {
        var message = PushNotificationService.BuildMessage("tok", "Hello", "World", null);

        Assert.Multiple(() =>
        {
            Assert.That(message.Notification, Is.Not.Null);
            Assert.That(message.Notification.Title, Is.EqualTo("Hello"));
            Assert.That(message.Notification.Body, Is.EqualTo("World"));
        });
    }

    // Either half on its own still means "show something", so neither may fall
    // through to the silent branch.
    [TestCase("Title", "")]
    [TestCase("", "Body")]
    public void BuildMessage_WithOnlyOneOfTitleOrBody_IsStillVisible(string title, string body)
    {
        var message = PushNotificationService.BuildMessage("tok", title, body, null);

        Assert.That(message.Notification, Is.Not.Null,
            "a push carrying a title or a body is a visible notification, not a silent one");
    }
}
