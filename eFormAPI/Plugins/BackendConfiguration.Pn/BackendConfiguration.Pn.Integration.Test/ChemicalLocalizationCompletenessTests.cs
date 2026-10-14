namespace BackendConfiguration.Pn.Integration.Test;

using System.Text;
using System.Text.Json;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;

/// <summary>
/// Every key the chemical controllers and report services read exists in
/// Resources/localization.json. JsonStringLocalizer returns the raw key for a
/// missing entry, so a gap would put "ErrorWhileSavingChemicalPlacement" or
/// "ChemicalProduct" in front of a customer. Per-locale completeness of every
/// entry is already pinned file-wide by ExportLocalizationCompletenessTests
/// (TheFileHasEveryLocaleOnEveryKey, TheFileHasNoBlankValues); this fixture only
/// ties the code's key registry to the file. DB-free, like that fixture.
/// </summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ChemicalLocalizationCompletenessTests
{
    [Test]
    public void TheRegistryIsNotEmpty() =>
        Assert.That(ChemicalLocalizationKeys.All, Has.Count.GreaterThanOrEqualTo(9));

    [Test]
    public void EveryChemicalKeyExistsInLocalizationJson()
    {
        var keys = Entries().Select(e => e.Key).ToHashSet();

        var missing = ChemicalLocalizationKeys.All.Where(k => !keys.Contains(k)).ToList();

        Assert.That(missing, Is.Empty, "Chemical keys absent from Resources/localization.json: " + string.Join(", ", missing));
    }

    [Test]
    public void NoChemicalKeyIsTranslatedAsItsOwnIdentifier()
    {
        var offenders = (from entry in Entries()
                where ChemicalLocalizationKeys.All.Contains(entry.Key)
                from locale in new[] { "en-US", "da" }
                where entry.Values.TryGetValue(locale, out var value) && value == entry.Key
                select $"{entry.Key} / {locale}")
            .ToList();

        Assert.That(offenders, Is.Empty, "en-US and da are written by hand, never the key: " + string.Join(", ", offenders));
    }

    private static List<(string Key, Dictionary<string, string> Values)> Entries()
    {
        var assembly = typeof(EformBackendConfigurationPlugin).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Resources.localization.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.That(stream, Is.Not.Null, $"embedded resource '{resourceName}' was not found");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());
        return json.RootElement.EnumerateArray()
            .Select(element => (
                Key: element.GetProperty("Key").GetString()!,
                Values: element.GetProperty("LocalizedValue").EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty)))
            .ToList();
    }
}
