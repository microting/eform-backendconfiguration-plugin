#nullable enable
using System.Linq;
using BackendConfiguration.Pn.Services.LiveFeed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services.LiveFeed;

/// <summary>The feed reader is a hosted service of the plugin's own registrations, and keeps a caller's options.</summary>
[TestFixture]
public class LiveFeedRegistrationTests
{
    [Test]
    public void TheReader_IsRegisteredOnce_AsAHostedService()
    {
        var services = new ServiceCollection();
        services.AddBackendConfigurationLiveFeed();
        services.AddBackendConfigurationLiveFeed();

        Assert.That(services.Count(d => d.ServiceType == typeof(IHostedService)
                                        && d.ImplementationType == typeof(LiveFeedReader)), Is.EqualTo(1));
        Assert.That(services.Count(d => d.ServiceType == typeof(LiveFeedOptions)), Is.EqualTo(1));
    }

    [Test]
    public void OptionsRegisteredFirst_AreKept()
    {
        var mine = new LiveFeedOptions { MaxRowsPerTick = 3 };
        var services = new ServiceCollection();
        services.AddSingleton(mine);
        services.AddBackendConfigurationLiveFeed();

        Assert.That(services.Single(d => d.ServiceType == typeof(LiveFeedOptions)).ImplementationInstance, Is.SameAs(mine));
    }
}
