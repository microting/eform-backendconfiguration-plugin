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
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.TailBite;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// The Halebid section of the export mock-ups (#1517, <c>ExportPdfHalebid</c> /
/// <c>ExportCsvHalebid</c>), shared by the builder, writer and image fixtures.
/// Fictional data only. Every timestamp is UTC; November is CET (UTC+1), so
/// 05:10 UTC prints as 06:10.
/// </summary>
internal static class TailBiteExportTestData
{
    public const string Farm = "Eksempelgården I/S";

    public static DateTime Utc(int month, int day, int hour = 0, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    public static List<RegistrationPhoto> Photos(int registrationId, params int[] uploadedDataIds) =>
        uploadedDataIds.Select(id => new RegistrationPhoto(Guid.NewGuid(), registrationId, id)).ToList();

    /// <summary>
    /// Outbreaks 41 (closed, all three actions done, two registrations of two
    /// photos), 42 (open, one of three actions done, one registration of two
    /// photos) and 43 (not assessed, no actions, registrations of three and two
    /// photos) — the mock-up's three rows.
    /// </summary>
    public static TailBiteComplianceReport MockupReport()
    {
        var outbreak41 = new TailBiteReportOutbreak(41, 1, Farm, "Stald 3 › Sektion 2", 1,
            Utc(11, 2, 5, 10), Utc(11, 3, 9), Utc(11, 14, 10), 6, 2,
            new FactorAnswers(false, true, true, false, false, false),
            [
                new TailBiteReportAction(TailBiteFactor.Feed, "Kontrollér foderautomaten", "Jonas Skov",
                    Utc(11, 6), Utc(11, 6, 10), "Jonas Skov"),
                new TailBiteReportAction(TailBiteFactor.ActivityMaterial, "Ekstra reb og halm", "Mette Holm",
                    Utc(11, 4), Utc(11, 4, 10), "Mette Holm"),
                new TailBiteReportAction(TailBiteFactor.ActivityMaterial, "Opfølgning på legemateriale", "Mette Holm",
                    Utc(11, 13), Utc(11, 13, 10), "Mette Holm")
            ],
            [
                new TailBiteReportRegistration(701, Utc(11, 2, 5, 10), "Sti 207", 1, 1, "Reb", "Mette Holm",
                    Photos(701, 9001, 9002)),
                new TailBiteReportRegistration(702, Utc(11, 2, 5, 14), "Sti 208", 3, 1, "Flyttet grise", "Mette Holm",
                    Photos(702, 9003, 9004))
            ]);

        var outbreak42 = new TailBiteReportOutbreak(42, 1, Farm, "Stald 5 › Sektion 1", 1,
            Utc(11, 12, 4, 52), Utc(11, 13, 9), null, 5, 1,
            new FactorAnswers(true, false, false, true, true, false),
            [
                new TailBiteReportAction(TailBiteFactor.Water, "Rens drikkenipler", "Jonas Skov",
                    Utc(11, 14), Utc(11, 14, 10), "Jonas Skov"),
                new TailBiteReportAction(TailBiteFactor.Climate, "Få ventil 4 tjekket", "Jonas Skov",
                    Utc(11, 22), null, null),
                new TailBiteReportAction(TailBiteFactor.Health, "Dyrlægen ser på holdet", "Jonas Skov",
                    Utc(11, 26), null, null)
            ],
            [
                new TailBiteReportRegistration(703, Utc(11, 12, 4, 52), "Sti 501", 4, 1, "Halm", "Jonas Skov",
                    Photos(703, 9005, 9006))
            ]);

        var outbreak43 = new TailBiteReportOutbreak(43, 1, Farm, "Stald 3 › Sektion 4", 1,
            Utc(11, 17, 7, 44), null, null, 6, 2, null,
            [],
            [
                new TailBiteReportRegistration(704, Utc(11, 17, 7, 44), "Sti 302", 2, 1, "", "Mette Holm",
                    Photos(704, 9007, 9008, 9009)),
                new TailBiteReportRegistration(705, Utc(11, 17, 8, 2), "Sti 303, Sti 304", 2, 1, "", "Mette Holm",
                    Photos(705, 9010, 9011))
            ]);

        return new TailBiteComplianceReport([outbreak41, outbreak42, outbreak43],
            new TailBiteReportTotals(3, 1, 17, 5, 4, 6));
    }

    /// <summary>A section the caller can see, with no outbreak in the period.</summary>
    public static TailBiteComplianceReport EmptyReport() =>
        new([], new TailBiteReportTotals(0, 0, 0, 0, 0, 0));

    /// <summary>
    /// The Danish values of the REAL <c>Resources/localization.json</c>, read
    /// from the plugin assembly the way <c>JsonStringLocalizer</c> reads it, so
    /// an assertion on "Halebid" or "Bidte grise" pins what a Danish customer
    /// gets. A key the file lacks comes back as itself.
    /// </summary>
    public static readonly IBackendConfigurationLocalizationService Danish = new DanishFileLocalizer();

    private sealed class DanishFileLocalizer : IBackendConfigurationLocalizationService
    {
        private static readonly Lazy<Dictionary<string, string>> Values = new(Load);

        public string GetString(string key) => Values.Value.TryGetValue(key, out var value) ? value : key;

        public string GetString(string format, params object[] args) => string.Format(GetString(format), args);

        public string GetStringWithFormat(string format, params object[] args)
        {
            var value = GetString(format);
            return args?.Length > 0 ? string.Format(value, args) : value;
        }

        private static Dictionary<string, string> Load()
        {
            var assembly = typeof(EformBackendConfigurationPlugin).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                                   $"{assembly.GetName().Name}.Resources.localization.json")
                               ?? throw new InvalidOperationException("localization.json is not embedded");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var json = JsonDocument.Parse(reader.ReadToEnd());

            var values = new Dictionary<string, string>();
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                if (entry.GetProperty("LocalizedValue").TryGetProperty("da", out var da))
                {
                    values.TryAdd(entry.GetProperty("Key").GetString()!, da.GetString() ?? string.Empty);
                }
            }

            return values;
        }
    }
}
