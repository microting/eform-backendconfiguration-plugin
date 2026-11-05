using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BackendConfiguration.Pn.Services.ChemicalReporting;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// The GHS diamonds of the web admin and the authority list PDF: the official UNECE artwork (public domain),
/// byte for byte the files flutter-chemistry pins in tool/fetch_ghs_pictograms.sh.
/// </summary>
[TestFixture]
public class ChemicalGhsPictogramsTests
{
    /// <summary>SHA-1 of each vendored SVG, as pinned by flutter-chemistry's fetch script.</summary>
    private static readonly Dictionary<string, string> PinnedSha1 = new()
    {
        ["GHS01"] = "49deeaed978e3dea8a4f74b4309a5c4a7671cad8",
        ["GHS02"] = "47a26281017faf09f2c0c05ac03d9711ec32ffc6",
        ["GHS03"] = "a09ecfa77ba52b79ca037f99e316963ed46c4eeb",
        ["GHS04"] = "5bb4e2ce36a676db86ab4e9552b9fe0b35de14bd",
        ["GHS05"] = "8bcb729fdedca2dceb74631cd7cd059315bf91cf",
        ["GHS06"] = "c1eac17decdf00e8b7515b57fe5006cb9be6b8d7",
        ["GHS07"] = "a6e245132c742b95295e7194e79a5299b7de91b2",
        ["GHS08"] = "12a0d5263a9dec1ee9f256fbabd7fba46725ad49",
        ["GHS09"] = "f38551d20f5c72ac5a8e9eb708f92b647c60905a",
    };

    private static IEnumerable<string> Codes => PinnedSha1.Keys;

    [TestCaseSource(nameof(Codes))]
    public void Svg_IsThePinnedOfficialArtwork(string code)
    {
        var svg = ChemicalGhsPictograms.Svg(code);

        Assert.That(svg, Is.Not.Null, $"{code} has no embedded SVG");
        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(svg))).ToLowerInvariant(), Is.EqualTo(PinnedSha1[code]));
    }

    [TestCase("ghs07")]
    [TestCase(" GHS07 ")]
    public void Svg_ReadsTheCodeLeniently(string code) =>
        Assert.That(ChemicalGhsPictograms.Svg(code), Is.EqualTo(ChemicalGhsPictograms.Svg("GHS07")).And.Not.Null);

    [TestCase("GHS10")]
    [TestCase("GHS00")]
    [TestCase("H302")]
    [TestCase("")]
    [TestCase(null)]
    public void Svg_UnknownCode_IsNull(string code) => Assert.That(ChemicalGhsPictograms.Svg(code), Is.Null);

    /// <summary>
    /// The web admin cannot load files from the plugin (CI copies only the Angular module), so its pictograms are
    /// base64 strings in a TypeScript module. They must stay the very same artwork as the embedded SVGs.
    /// </summary>
    [TestCaseSource(nameof(Codes))]
    public void WebModule_CarriesTheSameArtwork(string code)
    {
        var module = File.ReadAllText(Path.Combine(RepositoryRoot(), "eform-client", "src", "app", "plugins", "modules",
            "backend-configuration-pn", "modules", "chemicals", "helpers", "chemical-ghs-pictograms.ts"));
        var svg = ChemicalGhsPictograms.Svg(code);
        Assert.That(svg, Is.Not.Null);

        Assert.That(module, Does.Contain($"{code}: '{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}'"));
    }

    /// <summary>The checkout root: the first parent of the test binaries that holds eform-client.</summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "eform-client")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "no eform-client folder above the test binaries");
        return dir!.FullName;
    }
}
