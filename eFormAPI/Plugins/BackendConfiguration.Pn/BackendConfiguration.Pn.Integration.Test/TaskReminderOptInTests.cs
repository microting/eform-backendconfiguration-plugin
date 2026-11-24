#nullable enable

namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TaskReminders;
using global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.Settings.V1;
using NSubstitute;

/// <summary>
/// The task-reminder opt-in on <c>RegisterPushToken</c> (package 12): stored in
/// <c>DeviceToken.TaskReminders</c>, absent = the stored choice is kept (false for a new row, so
/// old apps never get reminders), and a registration re-plans the worker.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskReminderOptInTests : TestBaseSetup
{
    private const int Site = 4711;
    private TaskReminderSignals _signals = null!;

    [SetUp]
    public async Task Clean()
    {
        _signals = new TaskReminderSignals();
        BackendConfigurationPnDbContext!.DeviceTokenVersions.RemoveRange(BackendConfigurationPnDbContext.DeviceTokenVersions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.DeviceTokens.RemoveRange(BackendConfigurationPnDbContext.DeviceTokens);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private SettingsGrpcService Service()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(Site);
        return new SettingsGrpcService(CreateFreshBackendConfigurationDbContext(), resolver,
            TestContextLogger<SettingsGrpcService>.Instance, _signals);
    }

    private static RegisterPushTokenRequest Request(bool? taskReminders, string install = "install-1")
    {
        var request = new RegisterPushTokenRequest
        {
            FcmToken = Guid.NewGuid().ToString("N"), Platform = "android", AppId = "eform", InstallationId = install
        };
        if (taskReminders is { } value) request.TaskReminders = value;
        return request;
    }

    private async Task<bool> StoredAsync(string install = "install-1")
    {
        await using var db = CreateFreshBackendConfigurationDbContext();
        return (await db.DeviceTokens.AsNoTracking().SingleAsync(t => t.InstallationId == install)).TaskReminders;
    }

    [Test]
    public async Task AnOldRequest_StoresNoOptIn_AndAnswersAsBefore()
    {
        var reply = await Service().RegisterPushToken(Request(null), Substitute.For<ServerCallContext>());

        Assert.That(reply, Is.EqualTo(new RegisterPushTokenResponse()));
        Assert.That(await StoredAsync(), Is.False);
        Assert.That(_signals.AnyOptedIn, Is.False);
        Assert.That(_signals.DrainSites(), Is.Empty, "an old app's registration re-plans nothing");
    }

    [Test]
    public async Task TheOptIn_IsStored_KeptWhenAbsent_AndCanBeTurnedOff()
    {
        await Service().RegisterPushToken(Request(true), Substitute.For<ServerCallContext>());
        Assert.That(await StoredAsync(), Is.True);
        Assert.That(_signals.AnyOptedIn, Is.True, "the capture gate opens at once");
        Assert.That(_signals.DrainSites(), Is.EqualTo(new[] { Site }), "the worker is planned");

        await Service().RegisterPushToken(Request(null), Substitute.For<ServerCallContext>());
        Assert.That(await StoredAsync(), Is.True, "absent keeps the stored choice");

        await Service().RegisterPushToken(Request(false), Substitute.For<ServerCallContext>());
        Assert.That(await StoredAsync(), Is.False);
        Assert.That(_signals.DrainSites(), Is.EqualTo(new[] { Site }), "turning it off re-plans too (its reminders are retracted)");
    }
}
