using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteOutbreakNotifierTests : TailBiteTestBase
{
    private async Task<TailBiteOutbreak> SeedOutbreakAsync()
    {
        var o = new TailBiteOutbreak { PropertyId = PropertyId, LocationId = SectionId, RuleId = RuleId, RuleVersion = 1,
            OpenedAt = Clock.GetUtcNow().UtcDateTime, OpenedByRegistrationId = 1 };
        await o.Create(BackendConfigurationPnDbContext!);
        return o;
    }

    [Test]
    public async Task Opened_PushesEveryManagerOnce_WithDeepLink()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(7, manager: true);
        await SeedWorkerAsync(8, manager: true);
        await SeedWorkerAsync(9);
        var o = await SeedOutbreakAsync();
        var push = Substitute.For<ITailBitePushSender>();
        var sut = new TailBiteOutbreakNotifier(BackendConfigurationPnDbContext!, push);

        await sut.NotifyOpenedAsync(PropertyId, [new OutbreakOutcome(o.Id, true), new OutbreakOutcome(999, false)]);

        await push.Received(1).SendToSiteAsync(7, Arg.Any<string>(), Arg.Is<string>(b => b.Contains("Sektion 4")),
            Arg.Is<Dictionary<string, string>>(d => d["type"] == "tailbite_outbreak" && d["outbreak_id"] == o.Id.ToString()));
        await push.Received(1).SendToSiteAsync(8, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, string>>());
        await push.DidNotReceive().SendToSiteAsync(9, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, string>>());
    }

    [Test]
    public async Task Opened_RemovedManagerNeverPushed_DuplicateManagerRowsPushedOnce()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(7, manager: true);
        await SeedWorkerAsync(8, manager: true);
        await SeedWorkerAsync(8, manager: true);
        var removed = await SeedWorkerAsync(10, manager: true);
        await removed.Delete(BackendConfigurationPnDbContext!);
        var o = await SeedOutbreakAsync();
        var push = Substitute.For<ITailBitePushSender>();
        var sut = new TailBiteOutbreakNotifier(BackendConfigurationPnDbContext!, push);

        await sut.NotifyOpenedAsync(PropertyId, [new OutbreakOutcome(o.Id, true)]);

        await push.Received(1).SendToSiteAsync(7, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, string>>());
        await push.Received(1).SendToSiteAsync(8, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, string>>());
        await push.DidNotReceive().SendToSiteAsync(10, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, string>>());
    }

    [Test]
    public async Task JoinedOnlyOutcomes_PushNothing()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(7, manager: true);
        var o = await SeedOutbreakAsync();
        var push = Substitute.For<ITailBitePushSender>();
        var sut = new TailBiteOutbreakNotifier(BackendConfigurationPnDbContext!, push);

        await sut.NotifyOpenedAsync(PropertyId, [new OutbreakOutcome(o.Id, false)]);

        await push.DidNotReceiveWithAnyArgs().SendToSiteAsync(default, default!, default!, default!);
    }
}
