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
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System.Text;
using System.Text.Json;

/// <summary>
/// Pins that every localisation key the compliance-export path reads has a real
/// value in EVERY locale <c>Resources/localization.json</c> ships.
///
/// <para>
/// <b>Why this exists.</b> <c>JsonStringLocalizer.GetString</c> returns THE RAW KEY
/// when the entry has no value for the current culture (or the value is empty) —
/// it does not fall back to English and it does not throw. A locale left out of a
/// future export PR therefore does not fail anything: that customer simply gets a
/// PDF whose page header reads <c>Property:</c>, a column headed <c>Company</c>
/// and a title reading <c>ComplianceOverviewTitle</c>. The recent export work
/// (#1189 – #1192) added seven such keys across 26 locales each, by hand, and
/// nothing in the suite noticed whether all 26 were actually filled in.
/// </para>
///
/// <para>
/// <b>No database and no container.</b> This fixture does NOT derive from
/// <c>TestBaseSetup</c> — every <c>TestBaseSetup</c> subclass starts its own
/// MariaDB testcontainer, and there is nothing here to seed. The file is read
/// straight out of the plugin assembly's manifest resources, exactly as
/// <c>JsonStringLocalizer</c> reads it in production (the resource name is derived
/// the same way, so a rename of the embedded resource fails this fixture too).
/// It is <c>ParallelScope.All</c>, like the other DB-free fixtures in this project.
/// </para>
///
/// <para>
/// <b>Scope.</b> The key list below is the EXPORT path's, derived by reading every
/// <c>GetString</c> / <c>GetStringWithFormat</c> call in the five files under
/// <c>Services/BackendConfigurationComplianceExportService/</c> — the service, the
/// document builder, the Word writer, the CSV writer (which reads none) and the
/// PDF converter (likewise). It is deliberately not "every key in the file": the
/// file has pre-existing locale gaps on keys outside this path, and widening the
/// assertion would make the fixture fail for reasons that have nothing to do with
/// the export.
/// </para>
///
/// <para>
/// <b>Stated gap.</b> Presence is not correctness — a key whose Bulgarian value is
/// a copy of the English one passes here. Nothing automated can tell those apart;
/// this fixture only guarantees the user never sees a bare identifier.
/// </para>
///
/// <para>
/// <b>Stated gap, second order.</b> <c>ExportKeys</c> below is HAND-MAINTAINED: a
/// one-time manual derivation from the export source, correct as of the PR that
/// added it and not re-derived by anything at run time. It will therefore drift.
/// A future PR that adds a <c>GetString("NewKey")</c> to the export path without a
/// <c>localization.json</c> entry passes this fixture in silence — the fixture's own
/// failure mode, one level up from the one it was written to catch. It is not
/// automated on purpose: <c>dotnet test</c> runs from the output directory, where the
/// service sources are not present, so a source scan would be fragile in exactly the
/// way that produces a red build for the wrong reason.
/// </para>
/// <para>
/// <b>Re-deriving the list.</b> Grep <c>GetString</c> and <c>GetStringWithFormat</c>
/// across <c>Services/BackendConfigurationComplianceExportService/</c> and take the
/// string literals. That yields all but one: <c>InvalidExportRequest</c> is passed to
/// <c>Fail(key)</c> through a NON-LITERAL argument, so it does not appear next to a
/// <c>GetString</c> call and a literal-only grep misses it. Do this whenever the
/// export path gains or loses a localised string.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ExportLocalizationCompletenessTests
{
    /// <summary>
    /// The entry whose locale set defines "every locale the plugin ships". The
    /// plugin's own display name is the safest reference available: it is what
    /// <c>EformBackendConfigurationPlugin.GetNavigationMenu</c> reads to label the
    /// plugin in the sidebar, so it is the one key that must exist everywhere for
    /// the plugin to be usable at all.
    /// </summary>
    private const string ReferenceKey = "BackendConfiguration";

    /// <summary>
    /// Every key the export path asks the localizer for.
    ///
    /// <para>
    /// <c>BackendConfigurationComplianceExportService</c>: the two failure messages
    /// (<c>InvalidExportRequest</c> reaches the user through <c>Fail</c>), the
    /// <c>All</c> fallback both labels use, and the three view labels that name the
    /// downloaded file.
    /// </para>
    /// <para>
    /// <c>ComplianceExportDocumentBuilder</c>: the Oversigt title and its three
    /// column headers plus the totals row (#1190), the Detaljer title and its eight
    /// column headers plus the two status words (#1191), and Rapport's title, its
    /// seven fixed column headers and the four labels it composes captions from
    /// (#1188/#1192).
    /// </para>
    /// <para>
    /// <c>ComplianceExportWordWriter</c>: the page header's three labels plus
    /// <c>All</c>, and the appendix heading and its document-limit note.
    /// </para>
    /// <para>
    /// <c>ComplianceExportCsvWriter</c> and <c>ComplianceExportPdfConverter</c> read
    /// no keys at all — the CSV's header row is the document's own column headers,
    /// already localised by the builder.
    /// </para>
    /// </summary>
    private static readonly string[] ExportKeys =
    [
        // BackendConfigurationComplianceExportService
        "All",
        "ComplianceDetails",
        "ComplianceOverview",
        "ComplianceReport",
        "ErrorWhileGeneratingReportFile",
        "InvalidExportRequest",

        // ComplianceExportDocumentBuilder — Oversigt (#1190)
        "ComplianceOverviewTitle",
        "Company",
        "Overdue",
        "CompliancePercentage",
        "Total",

        // ComplianceExportDocumentBuilder — Detaljer (#1191)
        "ComplianceDetailsTitle",
        "Date",
        "Property",
        "CalendarBoard",
        "StartTime",
        "Task",
        "Worker",
        "TagsPlain",
        "Status",
        "Done",
        "NotDone",

        // ComplianceExportDocumentBuilder — Rapport (#1188 / #1192)
        "WithoutReportHeadline",
        "ColumnsUnavailable",
        "Case",
        "SubReport",
        "CaseId",
        "DoneBy",
        "CompletedDate",
        "Area",
        "Images",
        "ImagesCount",
        "ImagesCountOne",

        // ComplianceExportWordWriter — page header and appendix
        "Period",
        "Appendix",
        "ImageAppendixDocumentLimit"
    ];

    /// <summary>
    /// The one export key the builder passes through <c>string.Format</c>
    /// (<c>ImagesCount</c> → <c>"{0} billeder"</c>). A locale whose value lost the
    /// placeholder does not throw — <c>string.Format</c> just drops the argument —
    /// so the cell would read "billeder" with no number in front of it.
    /// </summary>
    private const string FormattedExportKey = "ImagesCount";

    /// <summary>
    /// Guards the fixture against passing vacuously. If the reference entry were
    /// ever reduced to one locale, every completeness assertion below would still
    /// pass while checking nothing.
    /// </summary>
    [Test]
    public void ReferenceKeyCarriesThePluginsFullLocaleSet()
    {
        var locales = Locales();

        Assert.That(locales, Has.Count.GreaterThanOrEqualTo(20),
            $"'{ReferenceKey}' resolved only {locales.Count} locales "
            + "— the reference entry is broken, so every other assertion in this fixture is vacuous.");
        Assert.That(locales, Does.Contain("en-US"));
        Assert.That(locales, Does.Contain("da"));
        Assert.That(locales, Does.Contain("de"));
    }

    /// <summary>
    /// Every key the export path reads EXISTS as an entry. A key that was renamed
    /// in the code but not in the file (or vice versa) shows up here rather than as
    /// a literal identifier in a customer's PDF.
    /// </summary>
    [Test]
    public void EveryExportKeyExistsInLocalizationJson()
    {
        var entries = Entries();

        var missing = ExportKeys
            .Where(key => entries.All(entry => entry.Key != key))
            .ToList();

        Assert.That(missing, Is.Empty,
            "Keys read by the compliance-export path but absent from Resources/localization.json: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// <b>The assertion this fixture exists for.</b> Every export key resolves to a
    /// NON-BLANK value in every locale — resolved exactly as
    /// <c>JsonStringLocalizer.GetString</c> resolves it: the first entry that
    /// carries the culture, then the key match, then the empty check (the file has
    /// duplicate keys, so "the first entry carrying this culture" and "the first
    /// entry with this key" are not the same rule, and the localizer uses the
    /// former).
    ///
    /// <para>
    /// The message names the KEY and the LOCALE for every offender, so a future
    /// failure is a one-line fix in the JSON rather than a debugging session.
    /// </para>
    /// </summary>
    [Test]
    public void EveryExportKeyIsTranslatedInEveryLocale()
    {
        var entries = Entries();
        var locales = Locales();

        var gaps = (from key in ExportKeys
                from locale in locales
                let value = Resolve(entries, key, locale)
                where string.IsNullOrWhiteSpace(value)
                select $"{key} / {locale}")
            .ToList();

        Assert.That(gaps, Is.Empty,
            $"{gaps.Count} compliance-export localisation value(s) are missing or blank. "
            + "JsonStringLocalizer returns the raw key for each of these, so the user sees the "
            + "identifier itself in the exported file. Offenders (key / locale): "
            + string.Join(", ", gaps));
    }

    /// <summary>
    /// The one formatted export key keeps its <c>{0}</c> placeholder in every
    /// locale. Losing it is silent: <c>string.Format</c> discards the surplus
    /// argument, so the image count simply disappears from the cell.
    /// </summary>
    [Test]
    public void TheFormattedExportKeyKeepsItsPlaceholderInEveryLocale()
    {
        var entries = Entries();

        var gaps = (from locale in Locales()
                let value = Resolve(entries, FormattedExportKey, locale)
                where value == null || !value.Contains("{0}")
                select $"{FormattedExportKey} / {locale}")
            .ToList();

        Assert.That(gaps, Is.Empty,
            $"'{FormattedExportKey}' has lost its {{0}} placeholder, so the image count is dropped "
            + "silently from the rendered cell. Offenders (key / locale): " + string.Join(", ", gaps));
    }

    // ==================================================================
    // Reading the file the way production reads it
    // ==================================================================

    /// <summary>
    /// The locales the plugin ships, taken from <see cref="ReferenceKey"/>'s entry.
    /// Derived rather than hard-coded so adding a language is a JSON-only change.
    /// </summary>
    private static List<string> Locales()
    {
        var entry = Entries().FirstOrDefault(e => e.Key == ReferenceKey);

        Assert.That(entry.Key, Is.EqualTo(ReferenceKey),
            $"the reference key '{ReferenceKey}' is not in Resources/localization.json");

        return entry.Values.Keys.ToList();
    }

    /// <summary>
    /// <c>JsonStringLocalizer.GetString</c>, reproduced: the entries carrying this
    /// culture, then the first of those whose key matches, then the empty check.
    /// Returns null where the localizer would hand the caller the key back.
    /// </summary>
    private static string? Resolve(
        List<(string Key, Dictionary<string, string> Values)> entries, string key, string locale)
    {
        var entry = entries
            .Where(e => e.Values.ContainsKey(locale))
            .FirstOrDefault(e => e.Key == key);

        if (entry.Key == null) return null;

        var value = entry.Values[locale];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// The embedded <c>Resources/localization.json</c>, addressed by the SAME name
    /// <c>JsonStringLocalizer</c> builds
    /// (<c>{assemblyName}.Resources.localization.json</c>) — so this fixture also
    /// fails if the resource is renamed or its <c>&lt;EmbeddedResource&gt;</c> entry
    /// is dropped from the csproj, which would otherwise only surface at runtime as
    /// a <c>NullReferenceException</c> on the first localised string.
    /// </summary>
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
