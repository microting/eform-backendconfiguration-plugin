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
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

[TestFixture]
public class ChemicalInventoryServiceCollectionExtensionsTests
{
    [Test]
    public void AddChemicalInventory_RegistersEveryChemicalService()
    {
        var services = new ServiceCollection();

        services.AddChemicalInventory();

        (Type Service, Type Implementation)[] expected =
        [
            (typeof(IChemicalPermissionService), typeof(ChemicalPermissionService)),
            (typeof(IChemicalRegisterReader), typeof(ChemicalRegisterReader)),
            (typeof(IChemicalNameDirectory), typeof(ChemicalNameDirectory)),
            (typeof(IChemicalInventoryService), typeof(ChemicalInventoryService)),
        ];
        foreach (var (service, implementation) in expected)
        {
            Assert.That(services.Any(d => d.ServiceType == service && d.ImplementationType == implementation), service.Name);
        }

        Assert.That(services.Any(d => d.ServiceType == typeof(IChemicalBaseClient)), "typed HttpClient");
        Assert.That(services.Single(d => d.ServiceType == typeof(TimeProvider)).Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
    }

    [Test]
    public void AddChemicalBaseOptions_BindsTheChemicalBaseSection()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["ChemicalBase:BaseUrl"] = "https://chemicalbase.example",
        }).Build();

        var options = new ServiceCollection().AddChemicalBaseOptions(configuration).BuildServiceProvider()
            .GetRequiredService<IOptions<ChemicalBaseOptions>>().Value;

        Assert.That(options.BaseUrl, Is.EqualTo("https://chemicalbase.example"));
    }
}
