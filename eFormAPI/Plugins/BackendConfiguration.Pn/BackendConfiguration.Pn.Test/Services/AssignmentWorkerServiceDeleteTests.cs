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
/// Only the first user (lowest AspNetUsers Id) may delete a property worker;
/// everyone else, admins and callers without an id included, is refused by
/// <see cref="BackendConfigurationAssignmentWorkerService.Delete"/> before it
/// reads or writes anything.
///
/// Every DbContext and the UserManager are passed as <c>null</c>: touching any
/// of them throws, and Delete's catch turns that into
/// <c>ErrorWhilDeleteAssignmentsProperties</c> rather than the refusal, so an
/// exact refusal message proves nothing was touched first. The localization
/// substitute echoes the key, so the message names the rule that fired.
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
    public async Task Delete_RefusesAnAdminWhoIsNotTheFirstUser_AndTouchesNothing()
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
