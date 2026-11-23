#nullable enable
using System;
using System.Linq;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveChanges;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services.LiveChanges;

/// <summary>
/// The DI wiring as the plugin registers it: everything resolves, the
/// collector is one per scope (and is the scope's <see cref="ILiveChanges"/>),
/// and a POOLED context carries the two singleton interceptors.
/// </summary>
[TestFixture]
public class LiveChangeRegistrationTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBackendConfigurationLiveHub();
        services.AddBackendConfigurationLiveChanges();
        services.AddSingleton(Substitute.For<IEFormCoreService>());
        services.AddDbContextPool<BackendConfigurationPnDbContext>((sp, o) =>
            o.UseMySql("Server=localhost;Database=none;User=none;Password=none",
                new MariaDbServerVersion(new Version(11, 2))).AddLiveChangeInterceptors(sp));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Test]
    public void OneCollectorPerScope_ThatIsTheScopesILiveChanges()
    {
        using var provider = Build();
        using var a = provider.CreateScope();
        using var b = provider.CreateScope();

        var collector = a.ServiceProvider.GetRequiredService<LiveChangeCollector>();
        Assert.That(a.ServiceProvider.GetRequiredService<ILiveChanges>(), Is.SameAs(collector));
        Assert.That(b.ServiceProvider.GetRequiredService<LiveChangeCollector>(), Is.Not.SameAs(collector));
    }

    [Test]
    public void APooledContext_CarriesTheSingletonInterceptors_AndBelongsToItsScopesCollector()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<BackendConfigurationPnDbContext>();
        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.ToList();

        Assert.That(interceptors, Does.Contain(provider.GetRequiredService<LiveChangeInterceptor>()));
        Assert.That(interceptors, Does.Contain(provider.GetRequiredService<LiveChangeTransactionInterceptor>()));
        Assert.That(scope.ServiceProvider.GetRequiredService<LiveChangeCollector>().Owns(context), Is.True);
        using var other = provider.CreateScope();
        Assert.That(other.ServiceProvider.GetRequiredService<LiveChangeCollector>().Owns(context), Is.False);
    }
}
