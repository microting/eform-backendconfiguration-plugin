#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.AspNetCore.Authorization;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

public class TailBiteControllersTests
{
    private static ITailBiteAccess AccessWithSite(int siteId = 7)
    {
        var access = Substitute.For<ITailBiteAccess>(); access.RequireCallerSiteAsync().Returns(siteId);
        return access;
    }

    [Test]
    public async Task Close_Conflict_ReturnsFailedResultWithMessage()
    {
        var access = AccessWithSite();
        var svc = Substitute.For<ITailBiteOutbreakService>();
        svc.CloseAsync(7, 5).Throws(new TailBiteConflictException("2 follow-up action(s) are not done or withdrawn."));
        var res = await new TailBiteOutbreaksController(access, svc).Close(5);
        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Does.Contain("not done"));
    }

    [Test]
    public async Task Enable_CallsServiceWithoutSite()
    {
        var access = Substitute.For<ITailBiteAccess>();
        var setup = Substitute.For<ITailBiteSetupService>();
        var res = await new TailBiteSetupController(access, setup).Enable(1);
        Assert.That(res.Success, Is.True);
        await setup.Received().EnableAsync(0, 1);
        await access.DidNotReceive().RequireCallerSiteAsync();
    }

    [Test]
    public async Task SetManager_CallsServiceWithoutSite()
    {
        var access = Substitute.For<ITailBiteAccess>();
        var setup = Substitute.For<ITailBiteSetupService>();
        var res = await new TailBiteSetupController(access, setup).SetManager(4, new SetManagerRequest(true));
        Assert.That(res.Success, Is.True);
        await setup.Received().SetManagerAsync(4, true);
        await access.DidNotReceive().RequireCallerSiteAsync();
    }

    [Test]
    public async Task Forbidden_ReturnsFailedResult_NotException()
    {
        var access = Substitute.For<ITailBiteAccess>(); access.RequireCallerSiteAsync().Throws(new TailBiteForbiddenException("no worker"));
        var res = await new TailBiteOutbreaksController(access, Substitute.For<ITailBiteOutbreakService>()).Close(5);
        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("no worker"));
    }

    [Test]
    public async Task NonTailBiteException_Propagates()
    {
        var access = AccessWithSite();
        var svc = Substitute.For<ITailBiteOutbreakService>();
        svc.CloseAsync(7, 5).Throws(new InvalidOperationException("boom"));
        await Assert.ThatAsync(async () => await new TailBiteOutbreaksController(access, svc).Close(5), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task GetTree_PassesCallerSiteAndWrapsData()
    {
        var access = AccessWithSite();
        var setup = Substitute.For<ITailBiteSetupService>();
        var tree = new LocationTree(3, 1, [], []);
        setup.GetTreeAsync(7, 3).Returns(tree);
        var res = await new TailBiteSetupController(access, setup).GetTree(3);
        Assert.That(res.Success, Is.True);
        Assert.That(res.Model, Is.SameAs(tree));
    }

    [Test]
    public async Task ListActionTypes_ReturnsServiceRecords()
    {
        var access = AccessWithSite();
        var setup = Substitute.For<ITailBiteSetupService>();
        setup.ListActionTypesAsync(7, 3).Returns(new List<ActionTypeDto> { new(1, "HALM", "Halm", 2) });
        var res = await new TailBiteSetupController(access, setup).ListActionTypes(3);
        Assert.That(res.Model, Is.EqualTo(new[] { new ActionTypeDto(1, "HALM", "Halm", 2) }));
    }

    [Test]
    public async Task CreateActionType_ForwardsNameAndReturnsId()
    {
        var access = AccessWithSite();
        var setup = Substitute.For<ITailBiteSetupService>();
        setup.CreateActionTypeAsync(7, 3, "Ny type").Returns(42);
        var res = await new TailBiteSetupController(access, setup).CreateActionType(3, new NameRequest("Ny type"));
        Assert.That(res.Model, Is.EqualTo(42));
    }

    [Test]
    public async Task SaveAssessment_NullActions_PassesEmptyList()
    {
        var access = AccessWithSite();
        var svc = Substitute.For<ITailBiteOutbreakService>();
        var answers = new FactorAnswers(true, false, false, false, false, false);
        var res = await new TailBiteOutbreaksController(access, svc).SaveAssessment(5, new SaveAssessmentRequest(answers, null));
        Assert.That(res.Success, Is.True);
        await svc.Received().SaveAssessmentAsync(7, 5, answers, Arg.Is<IReadOnlyList<ActionInput>>(l => l.Count == 0));
    }

    [Test]
    public async Task Withdraw_And_Cancel_ForwardReason()
    {
        var access = AccessWithSite();
        var svc = Substitute.For<ITailBiteOutbreakService>();
        var c = new TailBiteOutbreaksController(access, svc);
        await c.WithdrawAction(8, new ReasonRequest("not needed"));
        await c.CancelRegistration(9, new ReasonRequest("typo"));
        await svc.Received().WithdrawActionAsync(7, 8, "not needed");
        await svc.Received().CancelRegistrationAsync(7, 9, "typo");
    }

    [Test]
    public void OnlyEnableAndSetManager_RequireThePluginAdminPolicy()
    {
        var withPolicy = typeof(TailBiteSetupController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == BackendConfigurationClaims.AccessBackendConfigurationPlugin))
            .Select(m => m.Name).OrderBy(n => n).ToList();
        Assert.That(withPolicy, Is.EqualTo(new[] { "Enable", "SetManager" }));
    }

    [Test]
    public async Task SetManager_NullIsManager_ReturnsFailedResult()
    {
        var setup = Substitute.For<ITailBiteSetupService>();
        var res = await new TailBiteSetupController(AccessWithSite(), setup).SetManager(4, new SetManagerRequest(null));
        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("isManager is required"));
        await setup.DidNotReceiveWithAnyArgs().SetManagerAsync(default, default);
    }

    [Test]
    public async Task SetActionDone_NullDone_ReturnsFailedResult()
    {
        var svc = Substitute.For<ITailBiteOutbreakService>();
        var res = await new TailBiteOutbreaksController(AccessWithSite(), svc).SetActionDone(8, new ActionDoneRequest(null));
        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("done is required"));
        await svc.DidNotReceiveWithAnyArgs().SetActionDoneAsync(default, default, default);
    }

    [Test]
    public async Task SetActionDone_False_ForwardsFalse()
    {
        var svc = Substitute.For<ITailBiteOutbreakService>();
        var res = await new TailBiteOutbreaksController(AccessWithSite(), svc).SetActionDone(8, new ActionDoneRequest(false));
        Assert.That(res.Success, Is.True);
        await svc.Received().SetActionDoneAsync(7, 8, false);
    }

    [TestCase(typeof(TailBiteSetupController))]
    [TestCase(typeof(TailBiteOutbreaksController))]
    public void Controllers_CarryClassLevelAuthorize(Type controller)
        => Assert.That(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: false), Is.Not.Empty);
}
