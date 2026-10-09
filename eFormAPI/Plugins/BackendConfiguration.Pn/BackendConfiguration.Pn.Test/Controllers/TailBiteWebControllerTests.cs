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

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.EformAngularFrontendBase.Infrastructure.Const;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

public class TailBiteWebControllerTests
{
    private static ITailBiteAccess AccessWithSite(int siteId = 7)
    {
        var access = Substitute.For<ITailBiteAccess>();
        access.RequireCallerSiteAsync().Returns(siteId);
        return access;
    }

    [Test]
    public async Task ListProperties_And_ListWorkers_DoNotResolveACaller()
    {
        var access = Substitute.For<ITailBiteAccess>();
        var queries = Substitute.For<ITailBiteWebQueryService>();
        queries.ListPropertiesAsync().Returns(new List<TailBitePropertyStatus> { new(3, "Ejendom Nord", true) });
        queries.ListWorkersAsync(3).Returns(new List<TailBiteWorker> { new(7, "Jane Doe", true, new[] { 31 }) });
        var sut = new TailBiteWebController(access, queries);

        var properties = await sut.ListProperties();
        var workers = await sut.ListWorkers(3);

        Assert.That(properties.Model, Is.EqualTo(new[] { new TailBitePropertyStatus(3, "Ejendom Nord", true) }));
        Assert.That(workers.Model!.Single().PropertyWorkerIds, Is.EqualTo(new[] { 31 }));
        await access.DidNotReceive().RequireCallerSiteAsync();
    }

    [Test]
    public async Task CallerChecked_Routes_PassTheResolvedSite()
    {
        var queries = Substitute.For<ITailBiteWebQueryService>();
        var sut = new TailBiteWebController(AccessWithSite(), queries);

        await sut.MyProperties();
        await sut.AssignableWorkers(3);
        await sut.ListRules(3);
        await sut.RuleHistory(9);
        await sut.CurrentOccupancy(3);
        await sut.OutbreakRegistrations(5);

        await queries.Received().ListCallerPropertiesAsync(7);
        await queries.Received().ListAssignableWorkersAsync(7, 3);
        await queries.Received().ListRulesAsync(7, 3);
        await queries.Received().RuleHistoryAsync(7, 9);
        await queries.Received().CurrentOccupancyAsync(7, 3);
        await queries.Received().OutbreakRegistrationsAsync(7, 5);
    }

    [Test]
    public async Task Refusal_IsAFailedResultWithTheMessage()
    {
        var queries = Substitute.For<ITailBiteWebQueryService>();
        queries.OutbreakRegistrationsAsync(7, 5).Throws(TailBiteForbiddenException.NoAccess());

        var res = await new TailBiteWebController(AccessWithSite(), queries).OutbreakRegistrations(5);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo(TailBiteForbiddenException.NotFoundOrNoAccess));
    }

    [Test]
    public async Task UnresolvedCaller_IsRefused_BeforeTheQueryRuns()
    {
        var access = Substitute.For<ITailBiteAccess>();
        access.RequireCallerSiteAsync().Throws(new TailBiteForbiddenException("Caller has no resolvable worker identity."));
        var queries = Substitute.For<ITailBiteWebQueryService>();

        var res = await new TailBiteWebController(access, queries).ListRules(3);

        Assert.That(res.Success, Is.False);
        await queries.DidNotReceiveWithAnyArgs().ListRulesAsync(default, default);
    }

    [Test]
    // Converse of TailBiteWebQueryServiceTests' reflection test: no other route carries the policy.
    public void OnlyThePropertyAndWorkerLists_RequireThePluginAdminPolicy()
    {
        var withPolicy = typeof(TailBiteWebController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == BackendConfigurationClaims.AccessBackendConfigurationPlugin))
            .Select(m => m.Name).OrderBy(n => n);
        Assert.That(withPolicy, Is.EqualTo(new[] { "ListProperties", "ListWorkers" }));
    }

    [Test]
    // The managers dialog's lists expose every property and the PropertyWorker ids the manager toggle takes.
    public void OnlyThePropertyAndWorkerLists_RequireTheWorkerUpdatePolicy()
    {
        var withPolicy = typeof(TailBiteWebController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == AuthConsts.EformPolicies.DeviceUsers.Update))
            .Select(m => m.Name).OrderBy(n => n);
        Assert.That(withPolicy, Is.EqualTo(new[] { "ListProperties", "ListWorkers" }));
    }

    [Test]
    // Regression guard: a tail-bite manager need not hold DeviceUsers.Update, and the outbreak page needs this list.
    public void AssignableWorkers_CarriesNoPolicy_TheServiceChecksForAManager()
    {
        var action = typeof(TailBiteWebController).GetMethod(nameof(TailBiteWebController.AssignableWorkers))!;
        Assert.That(action.GetCustomAttributes<AuthorizeAttribute>(), Is.Empty);
    }

    [Test]
    public void Controller_IsAuthorized_AndRoutedUnderTheTailBitePrefix()
    {
        Assert.That(typeof(TailBiteWebController).GetCustomAttributes<AuthorizeAttribute>(inherit: false), Is.Not.Empty);
        Assert.That(typeof(TailBiteWebController).GetCustomAttribute<RouteAttribute>()!.Template,
            Is.EqualTo("api/backend-configuration-pn/tail-bite"));
    }
}
