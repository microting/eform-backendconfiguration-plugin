#nullable enable
using System.Reflection;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>D7 on the wire: a disabled caller gets 403 {"error":"account_disabled"} on every archive call; an
/// unreadable or missing document is a 404. No database.</summary>
[TestFixture]
public class ArchiveAccessFilterTests
{
    private static ActionContext ActionContext(IArchiveAccessService access) =>
        new(new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(access).BuildServiceProvider()
        }, new RouteData(), new ActionDescriptor());

    private static IArchiveAccessService Access(bool disabled)
    {
        var access = Substitute.For<IArchiveAccessService>();
        access.GetContextAsync().Returns(disabled ? ArchiveAccessContext.Disabled : ArchiveTestData.User());
        return access;
    }

    private static async Task<(ActionExecutingContext Executing, ActionExecutedContext? Executed, bool ActionRan)> Run(
        IArchiveAccessService access, Exception? thrownByAction = null)
    {
        var actionContext = ActionContext(access);
        var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(),
            new Dictionary<string, object?>(), new object());
        ActionExecutedContext? executed = null;
        var ran = false;
        await new ArchiveAccessFilterAttribute().OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            executed = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object())
            {
                Exception = thrownByAction
            };
            return Task.FromResult(executed);
        });
        return (executing, executed, ran);
    }

    [Test]
    public async Task DisabledAccount_Is403WithAccountDisabledBody_AndTheActionDoesNotRun()
    {
        var (executing, _, ran) = await Run(Access(disabled: true));

        Assert.That(ran, Is.False);
        var result = executing.Result as ObjectResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That(result.Value, Is.EqualTo(new Dictionary<string, string> { ["error"] = "account_disabled" }));
    }

    [Test]
    public async Task EnabledAccount_RunsTheAction()
    {
        var (executing, executed, ran) = await Run(Access(disabled: false));

        Assert.That(ran, Is.True);
        Assert.That(executing.Result, Is.Null);
        Assert.That(executed!.ExceptionHandled, Is.False);
    }

    [Test]
    public async Task NotFoundFromTheService_Is404()
    {
        var (_, executed, _) = await Run(Access(disabled: false), new ArchiveNotFoundException());

        Assert.That(executed!.ExceptionHandled, Is.True);
        Assert.That(executed.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task DisabledFromTheService_Is403()
    {
        var (_, executed, _) = await Run(Access(disabled: false), new ArchiveAccountDisabledException());

        Assert.That(executed!.ExceptionHandled, Is.True);
        Assert.That((executed.Result as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task OtherExceptions_AreLeftAlone()
    {
        var (_, executed, _) = await Run(Access(disabled: false), new InvalidOperationException("x"));

        Assert.That(executed!.ExceptionHandled, Is.False);
    }

    [TestCaseSource(typeof(ArchiveTestData), nameof(ArchiveTestData.ArchiveControllers))]
    public void ArchiveController_CarriesTheFilter(Type controller) =>
        Assert.That(controller.GetCustomAttribute<ArchiveAccessFilterAttribute>(), Is.Not.Null,
            $"{controller.Name} serves archive data: D7 requires the account_disabled 403 on every action.");
}
