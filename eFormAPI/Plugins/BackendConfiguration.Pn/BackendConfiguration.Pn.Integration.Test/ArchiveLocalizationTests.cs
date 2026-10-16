#nullable enable
using System.Text;
using System.Text.Json;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The archive refusal keys exist in the embedded Resources/localization.json in every shipped locale, and the
/// Danish texts are exactly the contract's. The services' tests assert keys only (the test localisation stub
/// echoes them), so this is the one check that a customer sees Danish and not "ArchiveMaxTwoLevels".
/// No database: same shape as ExportLocalizationCompletenessTests.
/// </summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ArchiveLocalizationTests
{
    private const string ReferenceKey = "BackendConfiguration";

    private static readonly Dictionary<string, string> Danish = new()
    {
        ["ArchiveMaxTwoLevels"] = "Højst 2 niveauer",
        ["ArchiveFolderNameExistsIn"] = "Navnet findes allerede i {0}",
        ["ArchiveFolderNameExistsTopLevel"] = "Navnet findes allerede på øverste niveau",
        ["ArchiveFolderNotEmpty"] = "Kun tomme mapper kan slettes ({0} dokumenter, {1} undermapper)",
        ["ArchiveDefaultFolderCannotDelete"] = "Standardmappen kan ikke slettes",
        ["ArchiveDefaultFolderCannotMove"] = "Standardmappen kan ikke flyttes",
        ["ArchiveDefaultFolderCannotRestrict"] = "Standardmappen kan ikke have begrænset læseadgang",
        ["ArchiveChooseFolder"] = "Vælg en mappe",
        ["ArchiveMoveChangesAccess"] =
            "Flytningen ændrer, hvem der kan læse dokumenterne. Kun en administrator kan gøre det.",
        ["ArchiveFolderNameInvalid"] = "Skriv et navn på højst 250 tegn",
        ["ArchiveFolderNotFound"] = "Mappen findes ikke",
        ["ArchiveUnknownGroup"] = "En valgt medarbejdergruppe findes ikke længere"
    };

    private static IEnumerable<string> Keys() => Danish.Keys;

    [TestCaseSource(nameof(Keys))]
    public void Key_ResolvesInEveryShippedLocale_AndDanishIsExact(string key)
    {
        var entries = Entries();
        var locales = entries.First(e => e.Key == ReferenceKey).Values.Keys.ToList();
        Assert.That(locales, Has.Count.GreaterThanOrEqualTo(20), "reference entry is broken");

        var matches = entries.Where(e => e.Key == key).ToList();
        Assert.That(matches, Has.Count.EqualTo(1), $"'{key}' must exist exactly once in localization.json");
        var values = matches.Single().Values;
        var missing = locales.Where(l => !values.TryGetValue(l, out var v) || string.IsNullOrWhiteSpace(v)).ToList();
        Assert.That(missing, Is.Empty, $"'{key}' has no value in: {string.Join(", ", missing)}");
        Assert.That(values.Keys.Except(locales), Is.Empty, $"'{key}' carries locales the file does not ship");
        Assert.That(values["da"], Is.EqualTo(Danish[key]));
    }

    private static List<(string Key, Dictionary<string, string> Values)> Entries()
    {
        var assembly = typeof(EformBackendConfigurationPlugin).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.Resources.localization.json");
        Assert.That(stream, Is.Not.Null);
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());
        return json.RootElement.EnumerateArray()
            .Select(e => (e.GetProperty("Key").GetString()!,
                e.GetProperty("LocalizedValue").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "")))
            .ToList();
    }
}
