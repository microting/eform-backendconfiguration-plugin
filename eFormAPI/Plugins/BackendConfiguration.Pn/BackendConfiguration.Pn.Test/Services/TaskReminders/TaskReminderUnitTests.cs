#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.LiveChanges;
using BackendConfiguration.Pn.Services.TaskReminders;
using BackendConfiguration.Pn.Test.Services.LiveChanges;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services.TaskReminders;

/// <summary>Time, text and selection rules of task reminders (package 12), without a database.</summary>
[TestFixture]
public class TaskReminderUnitTests
{
    private static readonly TimeZoneInfo Copenhagen = PropertyTimeZones.Resolve(null);

    // ------------------------------------------------------------------ time

    [TestCase("2027-03-27", 9.0, "2027-03-27T08:00:00Z")] // CET (+1)
    [TestCase("2027-03-28", 9.0, "2027-03-28T07:00:00Z")] // the spring-forward day, CEST (+2)
    [TestCase("2027-10-31", 9.0, "2027-10-31T08:00:00Z")] // the fall-back day, CET again
    [TestCase("2027-03-28", 9.5, "2027-03-28T07:30:00Z")] // half hours
    [TestCase("2027-03-28", 2.5, "2027-03-28T01:30:00Z")] // 02:30 does not exist that night: moved on by the gap, 03:30 CEST
    [TestCase("2027-10-31", 2.5, "2027-10-31T00:30:00Z")] // 02:30 happens twice: the first one (CEST)
    public void LocalStart_ToUtc_IsDstSafe(string day, double hour, string expectedUtc)
    {
        var utc = TaskReminderTime.ToUtc(DateOnly.Parse(day), hour, Copenhagen);

        Assert.That(utc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(utc, Is.EqualTo(DateTime.Parse(expectedUtc).ToUniversalTime()));
    }

    [Test]
    public void LocalToday_IsThePropertysCalendarDate()
    {
        // 23:30 UTC on 27 March is already 28 March in Copenhagen.
        Assert.That(TaskReminderTime.LocalToday(new DateTime(2027, 3, 27, 23, 30, 0, DateTimeKind.Utc), Copenhagen),
            Is.EqualTo(new DateOnly(2027, 3, 28)));
    }

    // ------------------------------------------------------------------ text

    [Test]
    public void Text_IsInTheWorkersLanguage_WithEnglishFallback()
    {
        var da = TaskReminderText.Render("da", "Fodring", new TimeOnly(7, 0), "Gården");
        var en = TaskReminderText.Render("zz-ZZ", "Feeding", new TimeOnly(7, 5), "Farm");
        var deDe = TaskReminderText.Render("de-DE", "Fütterung", new TimeOnly(13, 30), "Hof");

        Assert.That(da, Is.EqualTo(("Fodring", "Starter kl. 07:00 · Gården")));
        Assert.That(en, Is.EqualTo(("Feeding", "Starts at 07:05 · Farm")), "an unknown language falls back to English");
        Assert.That(deDe, Is.EqualTo(("Fütterung", "Beginnt um 13:30 · Hof")), "a regional code matches its language");
    }

    [Test]
    public void Text_HasAFallbackTitle_AndNoDanglingSeparatorWithoutAPropertyName()
    {
        Assert.That(TaskReminderText.Render("en-US", "  ", new TimeOnly(7, 0), null), Is.EqualTo(("Task", "Starts at 07:00")));
    }

    [Test]
    public void Text_IsCutToTheColumns_OnACharacterBoundary()
    {
        var longTitle = new string('a', 199) + "😀😀"; // a surrogate pair would straddle 200
        var (title, body) = TaskReminderText.Render("en-US", longTitle, new TimeOnly(7, 0), new string('p', 500));

        Assert.That(title.Length, Is.LessThanOrEqualTo(TaskReminderText.TitleMax));
        Assert.That(char.IsHighSurrogate(title[^1]), Is.False, "never half a character");
        Assert.That(body.Length, Is.LessThanOrEqualTo(TaskReminderText.BodyMax));
        Assert.That(TaskReminderText.Fit("😀😀", 3), Is.EqualTo("😀"));
    }

    // ------------------------------------------------------------------ selection

    private static CalendarTaskResponseModel Task(int id, string day, double hour, bool allDay = false, bool completed = false,
        int? complianceId = null, string title = "T", bool active = true) =>
        new()
        {
            Id = id, TaskDate = day, StartHour = hour, IsAllDay = allDay, Completed = completed, ComplianceId = complianceId,
            Title = title, Status = active
        };

    [Test]
    public void Candidates_ExcludeInactiveTasks_LikeTheAppsList()
    {
        // GetTasksForWeek also returns compliance-backed rows of a deactivated series; ListEvents and
        // ListChanges do not serve them (Status false), so no reminder either.
        var tasks = new[] { Task(1, "2027-03-28", 9.0, active: false), Task(2, "2027-03-28", 10.0) };

        Assert.That(TaskReminderPlanner.Candidates(tasks, new DateOnly(2027, 3, 28), Copenhagen).Select(c => c.EventId),
            Is.EqualTo(new[] { 2 }));
        Assert.That(BackendConfiguration.Pn.Services.GrpcServices.EventsGrpcService.IsOnWorkerList(tasks[0]), Is.False);
        Assert.That(BackendConfiguration.Pn.Services.GrpcServices.EventsGrpcService.IsOnWorkerList(tasks[1]), Is.True);
    }

    [Test]
    public void Candidates_AreTheOpenTimedTasksOfTheDay()
    {
        var tasks = new[]
        {
            Task(1, "2027-03-28", 9.0, complianceId: 5),
            Task(2, "2027-03-28", 12.0),
            Task(3, "2027-03-28", 8.0),                     // started or not: the planner decides with the clock
            Task(4, "2027-03-28", 0, allDay: true),         // no start time
            Task(5, "2027-03-28", 10.0, completed: true),   // finished
            Task(6, "2027-03-29", 9.0),                     // another day
        };

        var candidates = TaskReminderPlanner.Candidates(tasks, new DateOnly(2027, 3, 28), Copenhagen);

        Assert.That(candidates.Select(d => d.EventId), Is.EqualTo(new[] { 3, 1, 2 }), "by start time");
        Assert.That(candidates[1], Is.EqualTo(new ReminderCandidate(1, 5,
            new DateTime(2027, 3, 28, 7, 0, 0, DateTimeKind.Utc), "T", new TimeOnly(9, 0))));
    }

    // ------------------------------------------------------------------ dirty set

    [Test]
    public void Signals_CoalesceAndDrainBoundedly()
    {
        var signals = new TaskReminderSignals();
        for (var i = 1; i <= 30; i++) signals.MarkProperty(i % 10 + 1);
        signals.MarkSite(7);
        signals.MarkSite(7);

        Assert.That(signals.DirtyCount, Is.EqualTo(10));
        Assert.That(signals.DrainProperties(4), Has.Count.EqualTo(4));
        Assert.That(signals.DirtyCount, Is.EqualTo(6));
        Assert.That(signals.DrainSites(), Is.EqualTo(new[] { 7 }));
        Assert.That(signals.DrainSites(), Is.Empty);
    }

    // ------------------------------------------------------------------ the capture gate in the publisher

    [Test]
    public async Task WithNoAudience_AndAnOptedInDevice_ChangesMarkTheirPropertiesDirty_AndNothingIsSent()
    {
        var notifier = new RecordingLiveNotifier();
        var resolver = Substitute.For<ILiveChangeResolver>();
        resolver.LoadAsync(default!, default).ReturnsForAnyArgs(new LiveChangeLookups([new LiveEventRef(8, 80, 42)]));
        var signals = new TaskReminderSignals { AnyOptedIn = true };
        var publisher = new LiveChangePublisher(notifier, resolver, new LiveConnectionRegistry(TimeProvider.System),
            new LiveChangeWarnings(), NullLogger<LiveChangePublisher>.Instance, signals);

        Assert.That(publisher.WantsChanges, Is.True);
        var rest = publisher.ApplyRevocations([LiveChange.ForRevokeProperty(11, 43), LiveChange.ForTask(41, 7), LiveChange.ForEvent(8),
            LiveChange.ForAssignments(12)]);
        await publisher.PublishAsync(rest);

        Assert.That(notifier.Calls, Is.Empty, "no hub audience: nothing is sent");
        Assert.That(signals.IsDirty(41) && signals.IsDirty(42) && signals.IsDirty(43), Is.True,
            "the task's, the resolved event's and the revoked property's");
        Assert.That(signals.DrainSites(), Is.EqualTo(new[] { 12 }));
    }

    [Test]
    public async Task WithNoAudience_AndNoOptedInDevice_NothingIsDoneAtAll()
    {
        var resolver = Substitute.For<ILiveChangeResolver>();
        var signals = new TaskReminderSignals { AnyOptedIn = false };
        var publisher = new LiveChangePublisher(new RecordingLiveNotifier(), resolver,
            new LiveConnectionRegistry(TimeProvider.System), new LiveChangeWarnings(), NullLogger<LiveChangePublisher>.Instance, signals);

        Assert.That(publisher.WantsChanges, Is.False);
        Assert.That(publisher.ApplyRevocations([LiveChange.ForTask(41, 7)]), Is.Empty);
        await publisher.PublishAsync([LiveChange.ForEvent(8)]);
        await resolver.DidNotReceiveWithAnyArgs().LoadAsync(default!, default);
        Assert.That(signals.DirtyCount, Is.Zero);
    }
}
