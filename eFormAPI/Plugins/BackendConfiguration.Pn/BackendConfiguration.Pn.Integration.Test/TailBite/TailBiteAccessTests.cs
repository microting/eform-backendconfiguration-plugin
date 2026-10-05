using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TailBite;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteAccessTests : TailBiteTestBase
{
    private TailBiteAccess Sut(int resolvedSite, int workersWithEmail = 1)
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(resolvedSite);
        var counter = Substitute.For<ITailBiteWorkerEmailCounter>();
        counter.CountWorkersWithCallerEmailAsync().Returns(workersWithEmail);
        return new TailBiteAccess(BackendConfigurationPnDbContext!, resolver, counter);
    }

    [Test] public async Task SiteZero_Forbidden() => await Assert.ThrowsAsync<TailBiteForbiddenException>(() => Sut(0).RequireCallerSiteAsync());
    [Test] public async Task AmbiguousEmail_Forbidden() => await Assert.ThrowsAsync<TailBiteForbiddenException>(() => Sut(7, 2).RequireCallerSiteAsync());

    [Test]
    public async Task NonWorker_Forbidden_Worker_Allowed_ManagerRequiresFlag()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(7);
        var sut = Sut(7);
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.RequireWorkerAsync(8, PropertyId));
        await Assert.DoesNotThrowAsync(() => sut.RequireWorkerAsync(7, PropertyId));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.RequireManagerAsync(7, PropertyId));
    }
}
