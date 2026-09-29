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
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// flutter-chemistry spec §12: the legacy eForm chemical flow is gone. Its
/// ChemicalController served api/chemicals-pn/chemicals/index, which collides
/// with the chemical plugin's own route; new chemical routes live under
/// api/backend-configuration-pn/chemicals/.
/// </summary>
[TestFixture]
public class LegacyChemicalRouteTests
{
    private static readonly Assembly PluginAssembly = typeof(EformBackendConfigurationPlugin).Assembly;

    [Test]
    public void NoControllerServesTheChemicalPluginRoutePrefix()
    {
        var templates = PluginAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>(true).Select(a => a.Template)
                .Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes<RouteAttribute>(true).Select(a => a.Template))))
            .ToList();

        Assert.That(templates.Where(t => t.StartsWith("api/chemicals-pn", StringComparison.OrdinalIgnoreCase)), Is.Empty);
    }

    [Test]
    public void LegacyChemicalTypesAreGone()
    {
        var names = PluginAssembly.GetTypes().Select(t => t.FullName).ToList();

        Assert.That(names, Has.None.EqualTo("BackendConfiguration.Pn.Controllers.ChemicalController"));
        Assert.That(names, Has.None.EqualTo("BackendConfiguration.Pn.Services.ChemicalService.ChemicalService"));
        Assert.That(names, Has.None.EqualTo("BackendConfiguration.Pn.Messages.ChemicalAreaCreated"));
        Assert.That(names, Has.None.EqualTo("BackendConfiguration.Pn.Infrastructure.Helpers.ChemicalDbContextHelper"));
    }

    [Test]
    public void LegacyChemicalEformTemplatesAreNotShipped()
    {
        var resources = PluginAssembly.GetManifestResourceNames();

        Assert.That(resources, Has.None.Contains("25.01 Registrer produkter"));
        Assert.That(resources, Has.None.Contains("25.02 Vis kemisk produkt"));
    }
}
