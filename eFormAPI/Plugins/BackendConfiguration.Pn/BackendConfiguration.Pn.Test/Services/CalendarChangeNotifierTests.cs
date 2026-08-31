using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.PushNotificationService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// The volume half of the calendar-change push. The payload shape is cheap to
/// get right and cheap to see when it is wrong; what is neither is the send
/// COUNT. A reassignment of a weekly event replans the same (worker, event)
/// delta for every future occurrence, and the worker-tag fan-out multiplies
/// that by every event carrying the tag - so the difference between a correct
/// hook and an incident is entirely in the deduping and the cap, which no
/// phone and no log line shows until an admin clicks save on a busy tag.
///
/// Everything here drives the REAL <see cref="CalendarChangeNotifier"/> through
/// a real <see cref="IServiceScopeFactory"/> and asserts the sends a substitute
/// <see cref="IPushNotificationService"/> actually received: the count, the
/// recipients and the payload. No test asserts that the notifier calls itself.
/// </summary>
[TestFixture]
public class CalendarChangeNotifierTests
{
    private sealed record SentPush(int SiteId, string Title, string Body, Dictionary<string, string> Data);

    private static (CalendarChangeNotifier Notifier, List<SentPush> Sent) BuildNotifier(
        IPushNotificationService? push = null)
    {
        var sent = new List<SentPush>();
        push ??= Substitute.For<IPushNotificationService>();
        push.WhenForAnyArgs(x => x.SendToSiteAsync(0, null!, null!, null))
            .Do(ci => sent.Add(new SentPush(
                ci.ArgAt<int>(0),
                ci.ArgAt<string>(1),
                ci.ArgAt<string>(2),
                ci.ArgAt<Dictionary<string, string>>(3))));

        var services = new ServiceCollection();
        services.AddSingleton(push);

        return (CreateNotifier(services.BuildServiceProvider()), sent);
    }

    private static CalendarChangeNotifier CreateNotifier(IServiceProvider provider)
    {
        return new CalendarChangeNotifier(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CalendarChangeNotifier>.Instance);
    }

    /// <summary>
    /// The payload contract shared with flutter-eform (PR #905):
    /// data-only, type=calendar_changed, event_id = the AreaRulePlanning id.
    /// Empty title and body are what make the sender omit the Notification
    /// block - a non-empty one would put a stray banner on the worker's phone
    /// for an event they never saw.
    /// </summary>
    [Test]
    public async Task Dispatch_SendsOneSilentDataPushPerPair()
    {
        var (notifier, sent) = BuildNotifier();

        await notifier.DispatchAsync(new[]
        {
            new CalendarChangePair(10, 5),
            new CalendarChangePair(11, 5)
        });

        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(sent.Select(x => x.SiteId), Is.EquivalentTo(new[] { 10, 11 }));
        Assert.Multiple(() =>
        {
            foreach (var push in sent)
            {
                Assert.That(push.Title, Is.Empty, "a calendar-change push is silent - no title");
                Assert.That(push.Body, Is.Empty, "a calendar-change push is silent - no body");
                Assert.That(push.Data["type"], Is.EqualTo("calendar_changed"));
                Assert.That(push.Data["event_id"], Is.EqualTo("5"));
            }
        });
    }

    /// <summary>
    /// The incident this design exists to prevent. One reassignment of a weekly
    /// event replans the identical delta for every future occurrence; without
    /// the batch that is one push per occurrence per worker.
    /// </summary>
    [Test]
    public async Task NotifyInBackground_OneEventReplannedManyTimes_SendsOnePushPerWorker()
    {
        var (notifier, sent) = BuildNotifier();

        var batch = new CalendarChangeBatch();
        for (var occurrence = 0; occurrence < 40; occurrence++)
        {
            batch.Add(10, 5);
            batch.Add(11, 5);
        }

        notifier.NotifyInBackground(batch);
        await notifier.LastDispatch!;

        Assert.That(sent, Has.Count.EqualTo(2),
            "40 future occurrences of one event must not become 40 pushes per worker");
        Assert.That(sent.Select(x => x.SiteId), Is.EquivalentTo(new[] { 10, 11 }));
    }

    /// <summary>
    /// A worker who both lost one event and gained another still learns about
    /// both - the dedupe key is (worker, event), not the worker.
    /// </summary>
    [Test]
    public async Task Dispatch_SameWorkerDifferentEvents_SendsBoth()
    {
        var (notifier, sent) = BuildNotifier();

        await notifier.DispatchAsync(new[]
        {
            new CalendarChangePair(10, 5),
            new CalendarChangePair(10, 6)
        });

        Assert.That(sent.Select(x => x.Data["event_id"]), Is.EquivalentTo(new[] { "5", "6" }));
    }

    [Test]
    public async Task Dispatch_AtTheCap_StillSendsThePerEventSignal()
    {
        var (notifier, sent) = BuildNotifier();

        var pairs = Enumerable
            .Range(1, CalendarChangeNotifier.MaxDistinctPairsPerOperation)
            .Select(siteId => new CalendarChangePair(siteId, 7))
            .ToList();

        await notifier.DispatchAsync(pairs);

        Assert.That(sent, Has.Count.EqualTo(CalendarChangeNotifier.MaxDistinctPairsPerOperation));
        Assert.That(sent.All(x => x.Data.ContainsKey("event_id")), Is.True,
            "at the cap the per-event signal is still affordable and must be kept");
    }

    /// <summary>
    /// Above the cap the operation collapses to one COARSE push per distinct
    /// worker: same type, no event_id. That is still correct - the client
    /// refreshes its whole calendar window on this push and does not narrow by
    /// event_id - and it bounds the send count by the number of workers touched
    /// rather than by workers x events.
    /// </summary>
    [Test]
    public async Task Dispatch_AboveTheCap_CollapsesToOneCoarsePushPerWorker()
    {
        var (notifier, sent) = BuildNotifier();

        var pairs = new List<CalendarChangePair>();
        var eventId = 0;
        while (pairs.Count <= CalendarChangeNotifier.MaxDistinctPairsPerOperation)
        {
            eventId++;
            pairs.Add(new CalendarChangePair(10, eventId));
            pairs.Add(new CalendarChangePair(11, eventId));
            pairs.Add(new CalendarChangePair(12, eventId));
        }

        await notifier.DispatchAsync(pairs);

        Assert.That(sent, Has.Count.EqualTo(3),
            "above the cap the send count must be bounded by distinct workers, not pairs");
        Assert.That(sent.Select(x => x.SiteId), Is.EquivalentTo(new[] { 10, 11, 12 }));
        Assert.Multiple(() =>
        {
            foreach (var push in sent)
            {
                Assert.That(push.Data["type"], Is.EqualTo("calendar_changed"));
                Assert.That(push.Data.ContainsKey("event_id"), Is.False,
                    "a collapsed push names no single event");
            }
        });
    }

    /// <summary>
    /// The per-operation cap is spread across every worker, so it cannot bound
    /// what ONE device receives: a worker tag on 60 events with a single member
    /// is 60 pairs, well under the cap, and 60 wake-ups for that one phone. The
    /// per-worker cap is what stops that, and only for the worker that trips
    /// it - everyone else keeps their event_id.
    /// </summary>
    [Test]
    public async Task Dispatch_ManyEventsForOneWorker_CollapsesOnlyThatWorker()
    {
        var (notifier, sent) = BuildNotifier();

        var pairs = Enumerable
            .Range(1, CalendarChangeNotifier.MaxEventsPerSitePerOperation + 1)
            .Select(eventId => new CalendarChangePair(10, eventId))
            .Append(new CalendarChangePair(11, 99))
            .ToList();

        await notifier.DispatchAsync(pairs);

        Assert.That(sent.Count(x => x.SiteId == 10), Is.EqualTo(1),
            "the flooded worker gets one push, not one per event");
        Assert.That(sent.Single(x => x.SiteId == 10).Data.ContainsKey("event_id"), Is.False);

        var untouched = sent.Single(x => x.SiteId == 11);
        Assert.That(untouched.Data["event_id"], Is.EqualTo("99"),
            "one worker over the cap must not cost every other worker their event_id");
    }

    [Test]
    public async Task Dispatch_AtThePerWorkerCap_StillSendsThePerEventSignal()
    {
        var (notifier, sent) = BuildNotifier();

        var pairs = Enumerable
            .Range(1, CalendarChangeNotifier.MaxEventsPerSitePerOperation)
            .Select(eventId => new CalendarChangePair(10, eventId))
            .ToList();

        await notifier.DispatchAsync(pairs);

        Assert.That(sent, Has.Count.EqualTo(CalendarChangeNotifier.MaxEventsPerSitePerOperation));
        Assert.That(sent.All(x => x.Data.ContainsKey("event_id")), Is.True);
    }

    [Test]
    public void NotifyInBackground_EmptyBatch_SendsNothing()
    {
        var (notifier, sent) = BuildNotifier();

        notifier.NotifyInBackground(new CalendarChangeBatch());

        Assert.That(notifier.LastDispatch, Is.Null,
            "an operation that reassigned nobody must not touch Firebase at all");
        Assert.That(sent, Is.Empty);
    }

    /// <summary>
    /// A push is a courtesy on top of the calendar edit that triggered it. The
    /// edit has already been saved by the time this runs, so a sender that
    /// throws must die here rather than surface anywhere.
    /// </summary>
    [Test]
    public void Dispatch_WhenTheSenderThrows_DoesNotPropagate()
    {
        var push = Substitute.For<IPushNotificationService>();
        push.SendToSiteAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>())
            .Returns(_ => Task.FromException(new InvalidOperationException("firebase is down")));
        var (notifier, _) = BuildNotifier(push);

        Assert.DoesNotThrowAsync(() =>
            notifier.DispatchAsync(new[] { new CalendarChangePair(10, 5) }));
    }

    /// <summary>
    /// Same boundary one layer out: creating the scope and resolving the sender
    /// happen OUTSIDE <c>SendToSiteAsync</c>'s own catch, so they need this
    /// guard of their own.
    /// </summary>
    [Test]
    public void Dispatch_WhenTheSenderCannotBeResolved_DoesNotPropagate()
    {
        // Nothing registered: resolving IPushNotificationService throws.
        var notifier = CreateNotifier(new ServiceCollection().BuildServiceProvider());

        Assert.DoesNotThrowAsync(() =>
            notifier.DispatchAsync(new[] { new CalendarChangePair(10, 5) }));
    }
}
