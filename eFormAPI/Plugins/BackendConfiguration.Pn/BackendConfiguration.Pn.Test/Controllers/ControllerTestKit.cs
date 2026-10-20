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
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using Sentry;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>Reflection and substitute helpers the Kemi controller fixtures share.</summary>
internal static class ControllerTestKit
{
    /// <summary>A localization service that answers with the key itself.</summary>
    public static IBackendConfigurationLocalizationService EchoLocalization()
    {
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return localization;
    }

    public static IEnumerable<string> RoutedActions(Type controller) => controller.GetMethods()
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .Select(m => m.Name)
        .OrderBy(n => n, StringComparer.Ordinal);

    /// <summary>Every "VERB full/route" of the controller, sorted.</summary>
    public static IReadOnlyList<string> RoutesOf(Type controller)
    {
        var prefix = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        return controller.GetMethods()
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => $"{a.HttpMethods.Single()} {prefix}/{a.Template}"))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Runs <paramref name="act"/> against a Sentry hub that counts events and sends none. The hub
    /// is process-wide, so the calling fixture must be [NonParallelizable].
    /// Only explicit captures are counted: the default integrations also report
    /// TaskScheduler.UnobservedTaskException and AppDomain.UnhandledException, which fire from the
    /// finalizer thread whenever the GC happens to collect a faulted task that something earlier in
    /// the test run left unobserved. That made the count 2 instead of 1 at random.
    /// </summary>
    public static async Task<int> CountSentryEventsAsync(Func<Task> act)
    {
        var captured = 0;
        using (SentrySdk.Init(o =>
               {
                   o.Dsn = "https://0123456789abcdef0123456789abcdef@example.com/0";
                   o.AutoSessionTracking = false;
                   o.DisableUnobservedTaskExceptionCapture();
                   o.DisableAppDomainUnhandledExceptionCapture();
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
}
