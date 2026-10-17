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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// E-13 / Task 15 review I-1: the W6 page and its Excel export say the same words. The Excel reads
/// Resources/localization.json (ChemicalHistoryDetailsFormatter and the action/user columns), the page reads the
/// web locale files through HISTORY_TEXT_KEYS (chemical-history.helper.ts). For every mapped key the two values
/// must be equal in every locale the plugin ships, so a fix on one side cannot leave the other behind.
/// </summary>
[TestFixture]
public class ChemicalHistoryWordsParityTests
{
    private const string WebRoot = "eform-client/src/app/plugins/modules/backend-configuration-pn";

    private static readonly string[] Actions =
    [
        "Registered", "Moved", "Removed", "NoteChanged", "StockReceived", "StockConsumed", "StockAdjusted",
        "LocationCreated", "LocationRenamed", "LocationArchived", "LocationPhotoChanged", "PermissionChanged",
        "SettingsChanged", "StatusChanged", "AlertSent",
    ];

    private static readonly Regex TsEntry = new(@"^\s*'((?:[^'\\]|\\.)*)':\s*'((?:[^'\\]|\\.)*)',?\s*$", RegexOptions.Multiline);

    /// <summary>C# key → web key: the formatter's texts (read from the TS map itself), the 15 actions and the System user.</summary>
    private static Dictionary<string, string> Pairs()
    {
        var helper = File.ReadAllText(Path.Combine(RepoRoot(), WebRoot, "modules/chemicals/helpers/chemical-history.helper.ts"));
        var block = Regex.Match(helper, @"HISTORY_TEXT_KEYS[^{]*\{(?<body>[^}]*)\}").Groups["body"].Value;
        var pairs = Regex.Matches(block, @"(Chemical\w+):\s*'([^']+)'")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.That(pairs, Has.Count.EqualTo(20), "HISTORY_TEXT_KEYS has the formatter's 20 texts");
        foreach (var action in Actions)
        {
            pairs[$"ChemicalAction{action}"] = $"Chemicals: Action {action}";
        }

        pairs["ChemicalSystemUser"] = "Chemicals: System";
        return pairs;
    }

    [Test]
    public void ExcelAndPageUseTheSameWords_InEveryLocale()
    {
        var excel = Excel();
        var pairs = Pairs();
        var mismatches = new List<string>();
        foreach (var (locale, values) in LocalesOf(excel))
        {
            var web = Web(locale);
            foreach (var (cs, webKey) in pairs)
            {
                Assert.That(values.ContainsKey(cs), Is.True, $"localization.json has {cs}");
                var excelText = values[cs][locale];
                var webText = web.GetValueOrDefault(webKey);
                if (excelText != webText)
                {
                    mismatches.Add($"{locale} {cs}: Excel \"{excelText}\" vs web \"{webText}\"");
                }
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    [Test]
    public void ThePairsCoverEveryHistoryAction()
    {
        var pairs = Pairs();

        Assert.That(pairs, Has.Count.EqualTo(36));
        Assert.That(Enum.GetNames<Infrastructure.Models.Chemicals.ChemicalHistoryActionEnum>().Select(n => $"ChemicalAction{n}"),
            Is.SubsetOf(pairs.Keys));
    }

    private static IEnumerable<(string Locale, Dictionary<string, Dictionary<string, string>> Values)> LocalesOf(
        Dictionary<string, Dictionary<string, string>> excel)
    {
        var locales = excel["ChemicalSystemUser"].Keys.ToList();
        Assert.That(locales, Has.Count.EqualTo(26));
        return locales.Select(l => (l, excel));
    }

    /// <summary>localization.json locale → web file: "en-US" → enUS, "de" → deDE, "da" → da.</summary>
    private static string WebFile(string locale) => locale switch
    {
        "de" => "deDE",
        "da" => "da",
        _ => locale.Replace("-", string.Empty),
    };

    private static Dictionary<string, string> Web(string locale)
    {
        var path = Path.Combine(RepoRoot(), WebRoot, "i18n", $"{WebFile(locale)}.ts");
        Assert.That(File.Exists(path), Is.True, path);
        var result = new Dictionary<string, string>();
        foreach (Match m in TsEntry.Matches(File.ReadAllText(path, Encoding.UTF8)))
        {
            result[Unescape(m.Groups[1].Value)] = Unescape(m.Groups[2].Value);
        }

        return result;
    }

    private static string Unescape(string ts) => Regex.Replace(ts, @"\\(.)", "$1");

    private static Dictionary<string, Dictionary<string, string>> Excel()
    {
        var assembly = typeof(EformBackendConfigurationPlugin).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.Resources.localization.json");
        Assert.That(stream, Is.Not.Null);
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());
        return json.RootElement.EnumerateArray().ToDictionary(
            e => e.GetProperty("Key").GetString()!,
            e => e.GetProperty("LocalizedValue").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""));
    }

    /// <summary>The repository checkout the test runs in (the web locale files live next to eFormAPI).</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, WebRoot, "i18n")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"no {WebRoot}/i18n above {TestContext.CurrentContext.TestDirectory}");
    }
}
