using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

[TestFixture]
public class PropertiesGrpcServiceTests
{
    [Test]
    public async Task GetCommonDictionary_ServiceFails_ReturnsNoItems()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.GetAccessiblePropertyIdsAsync(7).Returns([1]);
        var propSvc = Substitute.For<IBackendConfigurationPropertiesService>();
        propSvc.GetCommonDictionary(false)
            .Returns(new OperationDataResult<List<CommonDictionaryModel>>(false, "fail"));

        var sut = new PropertiesGrpcService(propSvc, access, resolver);
        var response = await sut.GetCommonDictionary(
            new GetCommonDictionaryRequest { FullNames = false },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Success, Is.False);
        Assert.That(response.Items, Is.Empty);
    }

    [Test]
    public async Task GetCommonDictionary_FiltersInaccessibleProperties()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.GetAccessiblePropertyIdsAsync(7).Returns([1, 3]);
        var propSvc = Substitute.For<IBackendConfigurationPropertiesService>();
        propSvc.GetCommonDictionary(false).Returns(new OperationDataResult<List<CommonDictionaryModel>>(true,
        [
            new CommonDictionaryModel { Id = 1, Name = "Prop A" },
            new CommonDictionaryModel { Id = 2, Name = "Prop B" },
            new CommonDictionaryModel { Id = 3, Name = "Prop C" }
        ]));

        var sut = new PropertiesGrpcService(propSvc, access, resolver);
        var response = await sut.GetCommonDictionary(
            new GetCommonDictionaryRequest { FullNames = false },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Success, Is.True);
        Assert.That(response.Items, Has.Count.EqualTo(2));
        Assert.That(response.Items[0].Name, Is.EqualTo("Prop A"));
        Assert.That(response.Items[1].Name, Is.EqualTo("Prop C"));
    }

    [Test]
    public async Task GetCommonDictionary_NullId_ItemSkipped()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.GetAccessiblePropertyIdsAsync(7).Returns([1]);
        var propSvc = Substitute.For<IBackendConfigurationPropertiesService>();
        propSvc.GetCommonDictionary(false).Returns(new OperationDataResult<List<CommonDictionaryModel>>(true,
            [new CommonDictionaryModel { Id = null, Name = "Orphan" }]));

        var sut = new PropertiesGrpcService(propSvc, access, resolver);
        var response = await sut.GetCommonDictionary(
            new GetCommonDictionaryRequest { FullNames = false },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Items, Is.Empty);
    }

    [Test]
    public async Task GetCommonDictionary_AllAccessible_AllReturned()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.GetAccessiblePropertyIdsAsync(7).Returns([1, 2]);
        var propSvc = Substitute.For<IBackendConfigurationPropertiesService>();
        propSvc.GetCommonDictionary(true).Returns(new OperationDataResult<List<CommonDictionaryModel>>(true,
        [
            new CommonDictionaryModel { Id = 1, Name = "A" },
            new CommonDictionaryModel { Id = 2, Name = "B" }
        ]));

        var sut = new PropertiesGrpcService(propSvc, access, resolver);
        var response = await sut.GetCommonDictionary(
            new GetCommonDictionaryRequest { FullNames = true },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Items, Has.Count.EqualTo(2));
    }
}
