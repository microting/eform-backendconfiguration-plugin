using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test;

/// <summary>
/// The API host routes this plugin's Sentry events to the plugin's own project, which it finds through
/// <c>[assembly: AssemblyMetadata("SentryDsn", ...)]</c> on the plugin assembly (an
/// <c>AssemblyMetadata</c> item in the csproj). Only the shape is asserted; the value lives in the csproj.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class SentryDsnAssemblyMetadataTests
{
    [Test]
    public void PluginAssembly_DeclaresExactlyOneWellFormedSentryDsn()
    {
        var values = typeof(EformBackendConfigurationPlugin).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(x => x.Key == "SentryDsn")
            .Select(x => x.Value)
            .ToList();

        Assert.That(values, Has.Count.EqualTo(1), "exactly one SentryDsn assembly metadata value");
        Assert.That(Uri.TryCreate(values[0], UriKind.Absolute, out var dsn), Is.True, "absolute URI");
        Assert.That(dsn!.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
        Assert.That(dsn.UserInfo, Is.Not.Empty, "public key in the user info");
        Assert.That(dsn.AbsolutePath.Trim('/'), Does.Match(@"^\d+$"), "numeric project id as the path");
        Assert.That(dsn.Host, Does.Match(@"^o\d+\.ingest(\.[a-z]+)?\.sentry\.io$"), "sentry.io ingest host");
    }
}
