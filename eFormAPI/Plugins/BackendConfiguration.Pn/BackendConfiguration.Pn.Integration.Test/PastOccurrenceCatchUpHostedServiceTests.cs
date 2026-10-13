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

namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Services.PastOccurrenceCatchUp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

[TestFixture]
public class PastOccurrenceCatchUpHostedServiceTests
{
    private static PastOccurrenceCatchUpHostedService MakeHosted(IPastOccurrenceCatchUpService catchUp)
    {
        var services = new ServiceCollection();
        services.AddSingleton(catchUp);
        var sp = services.BuildServiceProvider();
        return new PastOccurrenceCatchUpHostedService(sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PastOccurrenceCatchUpHostedService>.Instance);
    }

    [Test]
    public async Task RunOnce_CatchesUpAllPropertiesBypassingTheThrottle()
    {
        var catchUp = Substitute.For<IPastOccurrenceCatchUpService>();
        catchUp.CatchUpAsync(null, true, Arg.Any<CancellationToken>())
            .Returns(new PastOccurrenceCatchUpResult(3, 1, 0, 2));

        await MakeHosted(catchUp).RunOnceAsync(CancellationToken.None);

        await catchUp.Received(1).CatchUpAsync(null, true, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunOnce_CatchUpThrows_DoesNotThrow()
    {
        var catchUp = Substitute.For<IPastOccurrenceCatchUpService>();
        catchUp.CatchUpAsync(Arg.Any<IReadOnlyCollection<int>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PastOccurrenceCatchUpResult>(new InvalidOperationException("boom")));

        await Assert.DoesNotThrowAsync(() => MakeHosted(catchUp).RunOnceAsync(CancellationToken.None));
    }
}
