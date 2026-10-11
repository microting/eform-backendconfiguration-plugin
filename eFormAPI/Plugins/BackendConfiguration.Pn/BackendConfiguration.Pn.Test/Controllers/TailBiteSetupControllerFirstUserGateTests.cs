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
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// TODO(#1425): remove with the [FirstUserOnly] on the two TailBiteSetupController management actions once the
/// owner opens tail-bite manager setup to other admins. Only these two are gated: the worker lists and the rest of
/// the tail-bite routes are used by the outbreak pages.
/// </summary>
[TestFixture]
public class TailBiteSetupControllerFirstUserGateTests
{
    private const int FirstUserId = 1;
    private const int OtherAdminId = 7;

    private static IEnumerable<string> GatedActions() => typeof(TailBiteSetupController).GetMethods()
        .Where(m => m.GetCustomAttribute<FirstUserOnlyAttribute>() != null)
        .Select(m => m.Name)
        .OrderBy(n => n, StringComparer.Ordinal);

    [Test]
    public void TheGatedSetIsExactlyEnableAndSetManager()
    {
        Assert.That(GatedActions(), Is.EquivalentTo(new[]
        {
            nameof(TailBiteSetupController.Enable),
            nameof(TailBiteSetupController.SetManager),
        }), "adding or removing a gate needs a deliberate change here");
    }

    [TestCaseSource(nameof(GatedActions))]
    public async Task AnAdminWhoIsNotTheFirstUser_IsForbidden(string actionName)
    {
        var result = await FirstUserGate.AuthorizeAsync(typeof(TailBiteSetupController), actionName,
            FirstUserGate.Caller(OtherAdminId, FirstUserId));

        FirstUserGate.AssertForbidden(result);
    }

    [TestCaseSource(nameof(GatedActions))]
    public async Task TheFirstUser_PassesTheGate(string actionName)
    {
        var result = await FirstUserGate.AuthorizeAsync(typeof(TailBiteSetupController), actionName,
            FirstUserGate.Caller(FirstUserId, FirstUserId));

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AReadRoute_IsNotGated()
    {
        var result = await FirstUserGate.AuthorizeAsync(typeof(TailBiteSetupController),
            nameof(TailBiteSetupController.GetTree), FirstUserGate.Caller(OtherAdminId, FirstUserId));

        Assert.That(result, Is.Null);
    }
}
