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

using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalNameDirectoryTests : TestBaseSetup
{
    private async Task<ChemicalNameDirectory> CreateSut()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new ChemicalNameDirectory(BaseDbContext, coreHelper);
    }

    private async Task<EformUser> AddUserAsync(string first, string last)
    {
        var email = $"{Guid.NewGuid():N}@chemicals.test";
        var user = new EformUser
        {
            Email = email, UserName = email, FirstName = first, LastName = last, Locale = "da",
            EmailConfirmed = true, TimeZone = "Europe/Copenhagen", Formats = "de-DE",
        };
        BaseDbContext.Users.Add(user);
        await BaseDbContext.SaveChangesAsync();
        return user;
    }

    [Test]
    public async Task UserNames_ResolvesKnownUsers_SkipsUnknownAndZero()
    {
        var anna = await AddUserAsync("Anna", "Hansen");
        var sut = await CreateSut();

        var names = await sut.UserNamesAsync([anna.Id, 0, int.MaxValue]);

        Assert.That(names, Is.EquivalentTo(new Dictionary<int, string> { [anna.Id] = "Anna Hansen" }));
    }

    [Test]
    public async Task WorkerNames_ResolvesSdkSiteNames()
    {
        var site = new Site { Name = $"Worker {Guid.NewGuid():N}" };
        await site.Create(MicrotingDbContext!);
        var sut = await CreateSut();

        var names = await sut.WorkerNamesAsync([site.Id]);

        Assert.That(names[site.Id], Is.EqualTo(site.Name));
    }
}
