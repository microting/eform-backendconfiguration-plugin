using System;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// <see cref="BackendConfigurationAssignmentWorkerService.Delete"/> refuses everyone
/// but the first user (lowest AspNetUsers Id). Every dependency except
/// <see cref="IUserService"/> is null or an inert substitute, so asserting the exact
/// refusal (the localization substitute echoes the key) proves the check ran first.
/// </summary>
[TestFixture]
public class AssignmentWorkerServiceDeleteTests
{
    private const int FirstUserId = 1;
    private const int OtherUserId = 2;
    private const int DeviceUserId = 42;
    private const string Refusal = "OnlyTheFirstUserCanDeleteWorkers";

    private static BackendConfigurationAssignmentWorkerService CreateSut(
        IEFormCoreService coreHelper,
        IUserService userService)
    {
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

        return new BackendConfigurationAssignmentWorkerService(
            coreHelper,
            userManager: null,
            userService,
            backendConfigurationPnDbContext: null,
            localization,
            itemsPlanningPnDbContext: null,
            timePlanningDbContext: null,
            caseTemplatePnDbContext: null,
            baseDbContext: null,
            NullLogger<BackendConfigurationAssignmentWorkerService>.Instance,
            Substitute.For<ICalendarAssignmentReconciliationService>());
    }

    private static IUserService CallerWithId(int userId, int firstUserId = FirstUserId)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(userId);
        userService.GetFirstUserIdInDb().Returns(firstUserId);
        return userService;
    }

    [Test]
    public async Task Delete_AdminWhoIsNotTheFirstUser_IsRefusedBeforeTheSdk()
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        var userService = CallerWithId(OtherUserId);
        userService.IsAdmin().Returns(true);
        userService.Role.Returns("admin");

        var result = await CreateSut(coreHelper, userService).Delete(DeviceUserId);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(Refusal));
        await coreHelper.DidNotReceive().GetCore();
    }

    [Test]
    public async Task Delete_RefusesACallerWithoutAnId_EvenWhenNoUserExists()
    {
        // An anonymous caller (UserId 0) against an empty users table (first id 0)
        // must not pass as 0 == 0.
        var coreHelper = Substitute.For<IEFormCoreService>();

        var result = await CreateSut(coreHelper, CallerWithId(0, firstUserId: 0)).Delete(DeviceUserId);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(Refusal));
        await coreHelper.DidNotReceive().GetCore();
    }

    [Test]
    public async Task Delete_LetsTheFirstUserPastTheCheck()
    {
        // GetCore is the first step after the check; it throws a sentinel so the
        // call stops there instead of reaching the (null) DbContexts.
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromException<eFormCore.Core>(
            new InvalidOperationException("core reached")));

        var result = await CreateSut(coreHelper, CallerWithId(FirstUserId)).Delete(DeviceUserId);

        Assert.That(result.Message, Does.Not.Contain(Refusal));
        Assert.That(result.Message, Does.Contain("core reached"));
        await coreHelper.Received(1).GetCore();
    }
}
