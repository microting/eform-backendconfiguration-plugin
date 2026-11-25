#nullable enable

namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TaskReminders;
using global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
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

    private SettingsGrpcService ServiceFor(int site, BackendConfigurationPnDbContext db)
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(site);
        return new SettingsGrpcService(db, resolver, TestContextLogger<SettingsGrpcService>.Instance, _signals);
    }

    private BackendConfigurationPnDbContext ContextWith(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var connection = BackendConfigurationPnDbContext!.Database.GetConnectionString()!;
        return new Microting.EformBackendConfigurationBase.Infrastructure.Data.BackendConfigurationPnDbContext(
            new DbContextOptionsBuilder<Microting.EformBackendConfigurationBase.Infrastructure.Data.BackendConfigurationPnDbContext>()
                .UseMySql(connection, ServerVersion.AutoDetect(connection)).AddInterceptors(interceptors).Options);
    }

    /// <summary>Runs an action once, right after the first query whose SQL mentions DeviceTokens.</summary>
    private sealed class AfterTokenRead(Func<Task> action) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int _done;

        public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData, System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DeviceTokens") && Interlocked.Exchange(ref _done, 1) == 0)
            {
                // The row is read (TaskReminders false); the overlapping opt-in (its own context and
                // connection) commits before this request saves.
                await action();
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task AnOptOutOverlappingAnOptIn_StillTurnsRemindersOff()
    {
        // Copilot (PR #1530): both requests read TaskReminders = false; the opt-in saves first, then
        // the opt-out assigns the value it already saw, and EF would skip the unchanged column.
        await Service().RegisterPushToken(Request(false), Substitute.For<ServerCallContext>());
        await using var optOutDb = ContextWith(new AfterTokenRead(async () =>
            await Service().RegisterPushToken(Request(true), Substitute.For<ServerCallContext>())));

        await ServiceFor(Site, optOutDb).RegisterPushToken(Request(false), Substitute.For<ServerCallContext>());

        Assert.That(await StoredAsync(), Is.False, "the later explicit opt-out wins");
    }

    [Test]
    public async Task AnOptedInInstall_TakenOverWithoutTheField_SignalsBothWorkers()
    {
        // Copilot (PR #1530): an older registration path (no task_reminders) moves an opted-in install
        // to another worker; both must be planned at once, and the gate must be open.
        await Service().RegisterPushToken(Request(true), Substitute.For<ServerCallContext>());
        _signals.DrainSites();
        _signals.AnyOptedIn = false;
        const int otherSite = 4712;

        await ServiceFor(otherSite, CreateFreshBackendConfigurationDbContext())
            .RegisterPushToken(Request(null), Substitute.For<ServerCallContext>());

        Assert.That(_signals.DrainSites(), Is.EquivalentTo(new[] { Site, otherSite }));
        Assert.That(_signals.AnyOptedIn, Is.True);
    }

    [Test]
    public async Task ARevivedOptedInInstall_WithoutTheField_SignalsItsWorker()
    {
        await Service().RegisterPushToken(Request(true), Substitute.For<ServerCallContext>());
        await using (var db = CreateFreshBackendConfigurationDbContext())
        {
            await db.DeviceTokens.ExecuteUpdateAsync(u => u.SetProperty(t => t.WorkflowState, "removed")); // logged out
        }

        _signals.DrainSites();
        await Service().RegisterPushToken(Request(null), Substitute.For<ServerCallContext>());

        Assert.That(_signals.DrainSites(), Is.EqualTo(new[] { Site }));
    }

    [Test]
    public async Task AnOldAppsRepeatedRegistration_StaysIdle()
    {
        await Service().RegisterPushToken(Request(null), Substitute.For<ServerCallContext>());
        await ServiceFor(4712, CreateFreshBackendConfigurationDbContext())
            .RegisterPushToken(Request(null), Substitute.For<ServerCallContext>());

        Assert.That(_signals.DrainSites(), Is.Empty);
        Assert.That(_signals.AnyOptedIn, Is.False);
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
