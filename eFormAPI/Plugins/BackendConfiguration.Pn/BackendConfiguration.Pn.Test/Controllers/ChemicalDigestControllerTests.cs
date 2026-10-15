/*
The MIT License (MIT)
Copyright (c) 2007 - 2026 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using BackendConfiguration.Pn.Services.ChemicalReporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Sentry;

namespace BackendConfiguration.Pn.Test.Controllers;

// Sentry's static hub is process-wide: the capture tests below swap it, so this fixture runs alone.
[TestFixture]
[NonParallelizable]
public class ChemicalDigestControllerTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 5, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private IChemicalDigestPreviewBuilder _preview;

    private ChemicalDigestController CreateSut()
    {
        _preview = Substitute.For<IChemicalDigestPreviewBuilder>();
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new ChemicalDigestController(_preview, localization, new FixedClock());
    }

    private static IEnumerable<string> RoutedActions() => typeof(ChemicalDigestController).GetMethods()
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .Select(m => m.Name)
        .OrderBy(n => n, StringComparer.Ordinal);

    [Test]
    public void UsesThePluginPolicyAndTheChemicalsPrefix()
    {
        Assert.That(typeof(ChemicalDigestController).GetCustomAttribute<AuthorizeAttribute>()?.Policy,
            Is.EqualTo(BackendConfigurationClaims.AccessBackendConfigurationPlugin));
        var prefix = typeof(ChemicalDigestController).GetCustomAttribute<RouteAttribute>()!.Template;
        var routes = typeof(ChemicalDigestController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => $"{a.HttpMethods.Single()} {prefix}/{a.Template}"))
            .ToList();
        Assert.That(routes, Is.EqualTo(new[] { "GET api/backend-configuration-pn/chemicals/properties/{propertyId:int}/digest-preview" }));
    }

    [Test]
    public async Task Preview_BuildsForThePropertyAtTheClocksNow()
    {
        var sut = CreateSut();
        var model = new ChemicalDigestModel(3, "Hovedgården", 40, Now, [], [], [], [], []);
        _preview.BuildAsync(3, Now).Returns(model);

        var result = await sut.GetDigestPreview(3);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.SameAs(model));
    }

    [Test]
    public async Task Preview_Failure_IsAnUnsuccessfulResultWithTheLocalizedReason()
    {
        var sut = CreateSut();
        _preview.BuildAsync(9, Now).ThrowsAsync(new ChemicalNotFoundException("Property 9 not found."));

        var result = await sut.GetDigestPreview(9);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.StartWith(ChemicalLocalizationKeys.ErrorWhileReadingChemicalDigest));
        Assert.That(result.Message, Does.Contain("Property 9 not found."));
    }

    // E-12: typed outcomes are answers, not Sentry events; only unexpected failures are captured.
    private static async Task<int> CountSentryEventsAsync(Func<Task> act)
    {
        var captured = 0;
        using (SentrySdk.Init(o =>
               {
                   o.Dsn = "https://0123456789abcdef0123456789abcdef@example.com/0";
                   o.AutoSessionTracking = false;
                   o.SetBeforeSend(_ =>
                   {
                       captured++;
                       return null; // never leaves the process
                   });
               }))
        {
            await act();
        }

        return captured;
    }

    [Test]
    public async Task Preview_TypedOutcomes_AreNotSentToSentry()
    {
        var sut = CreateSut();
        _preview.BuildAsync(9, Now).ThrowsAsync(new ChemicalNotFoundException("gone"));

        var events = await CountSentryEventsAsync(() => sut.GetDigestPreview(9));

        Assert.That(events, Is.Zero);
    }

    [Test]
    public async Task Preview_UnexpectedFailures_AreSentToSentry()
    {
        var sut = CreateSut();
        _preview.BuildAsync(9, Now).ThrowsAsync(new InvalidOperationException("boom"));

        var events = await CountSentryEventsAsync(() => sut.GetDigestPreview(9));

        Assert.That(events, Is.EqualTo(1));
    }

    // ---- TODO(chemistry-GA): remove with the [FirstUserOnly] on the controller, like ChemicalsControllerFirstUserGateTests ----

    private const int FirstUserId = 1;
    private const int OtherAdminId = 7;

    [Test]
    public void TheGateCoversEveryRoute()
    {
        Assert.That(RoutedActions().Count(), Is.EqualTo(1), "gate-review a new route, then bump this");
    }

    [TestCaseSource(nameof(RoutedActions))]
    public async Task AnAdminWhoIsNotTheFirstUser_IsForbiddenOnEveryRoute(string actionName)
    {
        var result = await FirstUserGate.AuthorizeAsync(typeof(ChemicalDigestController), actionName,
            FirstUserGate.Caller(OtherAdminId, FirstUserId));

        FirstUserGate.AssertForbidden(result);
    }

    [TestCaseSource(nameof(RoutedActions))]
    public async Task TheFirstUser_PassesTheGate(string actionName)
    {
        var result = await FirstUserGate.AuthorizeAsync(typeof(ChemicalDigestController), actionName,
            FirstUserGate.Caller(FirstUserId, FirstUserId));

        Assert.That(result, Is.Null);
    }
}
