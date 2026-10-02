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
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.LegacyChemicalCleanupService;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Core.CaseDelete reports a refused delete as false, and its "parsing in
/// progress" retry loop can run for hours. The legacy cleanup must see both as
/// a failed item instead of a deleted one. Needs no database; it lives here so
/// it runs in the same CI step as LegacyChemicalCleanupServiceTests.
/// </summary>
[TestFixture]
public class LegacyChemicalSdkOperationsTests
{
    [Test]
    public void EnsureCaseDeleted_FalseResult_Throws() =>
        Assert.That(async () => await LegacyChemicalSdkOperations.EnsureCaseDeletedAsync(
                Task.FromResult(false), 42, TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("42"));

    [Test]
    public async Task EnsureCaseDeleted_NoAnswerWithinTheTimeout_Throws()
    {
        var call = LegacyChemicalSdkOperations.EnsureCaseDeletedAsync(
            new TaskCompletionSource<bool>().Task, 42, TimeSpan.FromMilliseconds(50));

        // Bounds the test itself: an unbounded wait would hang here forever.
        var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.That(finished, Is.SameAs(call), "EnsureCaseDeletedAsync did not give up within 10 s");
        Assert.That(async () => await call, Throws.InstanceOf<TimeoutException>());
    }

    [Test]
    public void EnsureCaseDeleted_TrueResult_Completes() =>
        Assert.That(async () => await LegacyChemicalSdkOperations.EnsureCaseDeletedAsync(
                Task.FromResult(true), 42, TimeSpan.FromSeconds(5)),
            Throws.Nothing);

    [Test]
    public void CaseDeleteTimeout_IsTwoMinutes() =>
        Assert.That(LegacyChemicalSdkOperations.CaseDeleteTimeout, Is.EqualTo(TimeSpan.FromMinutes(2)));
}
