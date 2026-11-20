#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.TailBite;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// The Halebid (tail-bite) section of the Rapport export (#1517), appended after
/// the eForm sections by <see cref="ComplianceExportDocumentBuilder.BuildReport"/>.
/// PURE, like that builder: everything comes from the
/// <see cref="TailBiteComplianceReport"/> the report service already built.
///
/// <para>
/// <b>Shape.</b> Up to three tables, the first carrying the section's caption
/// (<c>Halebid</c>), title, totals sentence (<see cref="ComplianceExportTable.Lead"/>),
/// appendix label and every appendix block, and starting on a new page:
/// <list type="number">
/// <item><c>Udbrud</c>, one row per outbreak — the only table the CSV carries.
/// Its PDF and CSV columns differ (rulings 1 and 7): the CSV-only columns
/// reproduce today's leading Rapport columns (<c>Delrapport … Billeder</c>,
/// same keys) so the CSV header starts the same with or without eForm tables,
/// and the PDF-only ones (<c>Lokation</c>, <c>Regel</c>, <c>Ja-faktorer</c>,
/// <c>Billeder</c> last) are the mock-up's.</item>
/// <item><c>Handlinger fra risikovurderingerne</c> (PDF only), omitted when no
/// outbreak has an action.</item>
/// <item><c>Registreringer bag udbruddene</c> (PDF only), omitted when there
/// are none.</item>
/// </list>
/// With no outbreak in the period the section is the first table alone: PDF
/// only, no columns, and "Ingen udbrud i perioden" as its lead.
/// </para>
///
/// <para>
/// <b>Times.</b> Every timestamp on the report model is UTC; what is printed is
/// Copenhagen wall-clock time (<see cref="ComplianceFutureTaskGuard.CopenhagenTimeOf"/>)
/// and dates are Copenhagen dates.
/// </para>
///
/// <para>
/// <b>Appendix.</b> One block per registration with photos, in outbreak then
/// registration order, a registration behind two outbreaks only once. The
/// per-block cap and the document-wide ceiling are the eForm appendix's own
/// (<see cref="ComplianceExportDocumentBuilder.MaxAppendixImagesPerCase"/>,
/// <see cref="ComplianceExportDocumentBuilder.MaxAppendixImages"/>), counted on
/// the same two document counters, so the eForm sections and Halebid share one
/// budget.
/// </para>
/// </summary>
public static class ComplianceExportTailBiteBuilder
{
    /// <summary>How a Copenhagen date and time is printed on the page and in appendix captions.</summary>
    private const string DisplayDateTimeFormat = "dd.MM.yyyy HH:mm";

    /// <summary>The six risk factors in the assessment's order, with their label keys.</summary>
    private static readonly (TailBiteFactor Factor, string LabelKey, Func<FactorAnswers, bool> Answer)[] Factors =
    [
        (TailBiteFactor.Water, "TailBiteWater", a => a.Water),
        (TailBiteFactor.Feed, "TailBiteFeed", a => a.Feed),
        (TailBiteFactor.ActivityMaterial, "TailBiteActivityMaterial", a => a.ActivityMaterial),
        (TailBiteFactor.Climate, "TailBiteClimate", a => a.Climate),
        (TailBiteFactor.Health, "TailBiteHealth", a => a.Health),
        (TailBiteFactor.Management, "TailBiteManagement", a => a.Management)
    ];

    /// <summary>
    /// Appends the Halebid section to <paramref name="document"/>. A null
    /// <paramref name="tailBite"/> (the caller sees no Halebid section) leaves
    /// the document untouched.
    /// </summary>
    public static void Append(
        ComplianceExportDocument document,
        TailBiteComplianceReport? tailBite,
        bool includeImageAppendix,
        IBackendConfigurationLocalizationService localizationService)
    {
        if (tailBite == null) return;

        var sectionLabel = localizationService.GetString("TailBite");
        var outbreaksTable = new ComplianceExportTable
        {
            Caption = sectionLabel,
            Title = localizationService.GetString("TailBiteReportTitle"),
            AppendixLabel = sectionLabel,
            StartsOnNewPage = true
        };
        document.Tables.Add(outbreaksTable);

        var outbreaks = tailBite.Outbreaks ?? [];
        if (outbreaks.Count == 0)
        {
            // Headings and the "no outbreaks" line only: nothing for the CSV.
            outbreaksTable.PdfOnly = true;
            outbreaksTable.Lead = localizationService.GetString("TailBiteNoOutbreaks");
            return;
        }

        var totals = tailBite.Totals;
        outbreaksTable.Lead = localizationService.GetString("TailBiteTotals",
            totals.Outbreaks, totals.ClosedInPeriod, totals.BittenPigs, totals.SeverePigs,
            totals.ActionsDone, totals.ActionsTotal);
        outbreaksTable.Subtitle = localizationService.GetString("TailBiteOutbreaks");

        // Each column with the cell it takes from an outbreak, so the header and
        // the cells cannot drift apart.
        var outbreakColumns = OutbreakColumns(localizationService, sectionLabel);
        outbreaksTable.Columns = outbreakColumns.Select(c => c.Column).ToList();
        foreach (var outbreak in outbreaks)
        {
            outbreaksTable.Rows.Add(new ComplianceExportRow
            {
                IsDone = outbreak.ClosedAt != null,
                Cells = outbreakColumns.Select(c => c.Cell(outbreak)).ToList()
            });
        }

        var actionsTable = ActionsTable(outbreaks, localizationService);
        if (actionsTable.Rows.Count > 0) document.Tables.Add(actionsTable);

        var registrationsTable = RegistrationsTable(outbreaks, localizationService);
        if (registrationsTable.Rows.Count > 0) document.Tables.Add(registrationsTable);

        if (includeImageAppendix)
        {
            AppendImageBlocks(document, outbreaksTable, outbreaks, localizationService.GetString("TailBiteOutbreak"));
        }
    }

    private static List<(ComplianceExportColumn Column, Func<TailBiteReportOutbreak, ComplianceExportCell> Cell)>
        OutbreakColumns(IBackendConfigurationLocalizationService localizationService, string sectionLabel)
    {
        var yes = localizationService.GetString("Yes");
        var no = localizationService.GetString("No");
        var notAssessed = localizationService.GetString("TailBiteNotAssessed");
        var noYesFactors = localizationService.GetString("TailBiteNoYesFactors");

        ComplianceExportColumn Column(string key, string headerKey,
            ComplianceExportCellType type = ComplianceExportCellType.Text,
            bool csvOnly = false, bool pdfOnly = false) => new()
        {
            Key = key,
            Header = localizationService.GetString(headerKey),
            Type = type,
            CsvOnly = csvOnly,
            PdfOnly = pdfOnly
        };

        var photosCell = (Func<TailBiteReportOutbreak, ComplianceExportCell>)(o =>
            ComplianceExportDocumentBuilder.ImagesCell(PhotoCount(o), localizationService));

        var columns = new List<(ComplianceExportColumn, Func<TailBiteReportOutbreak, ComplianceExportCell>)>
        {
            // Today's leading Rapport columns, same keys, so the CSV header starts
            // the same with or without eForm tables (ruling 7).
            (Column("SubReport", "SubReport", csvOnly: true), _ => ComplianceExportCell.FromText(sectionLabel)),
            (Column("CaseId", "CaseId", ComplianceExportCellType.Number), o => ComplianceExportCell.FromNumber(o.Id)),
            (Column("Property", "Property", csvOnly: true), o => ComplianceExportCell.FromText(o.PropertyName)),
            (Column("DoneBy", "DoneBy", csvOnly: true), _ => new ComplianceExportCell()),
            (Column("CompletedDate", "CompletedDate", ComplianceExportCellType.Date, csvOnly: true),
                _ => new ComplianceExportCell()),
            (Column("Area", "Area", csvOnly: true), o => ComplianceExportCell.FromText(o.LocationPath)),
            (Column("Images", "Images", ComplianceExportCellType.Number, csvOnly: true), photosCell),

            (Column("TailBiteLocation", "Location", pdfOnly: true), o => ComplianceExportCell.FromText(o.LocationPath)),
            (Column("TailBiteRuleVersion", "TailBiteRule", pdfOnly: true),
                o => ComplianceExportCell.FromText($"v{o.RuleVersion}")),
            (Column("TailBiteStarted", "TailBiteStarted"), o => DateTimeCell(o.OpenedAt, isoForCsv: true)),
            (Column("TailBiteAssessed", "TailBiteAssessed", ComplianceExportCellType.Date), o => LocalDateCell(o.AssessedAt)),
            (Column("TailBiteClosed", "TailBiteClosed", ComplianceExportCellType.Date), o => LocalDateCell(o.ClosedAt)),
            (Column("TailBiteBitten", "TailBiteBitten", ComplianceExportCellType.Number),
                o => ComplianceExportCell.FromNumber(o.BittenPigs)),
            (Column("TailBiteSevere", "TailBiteSevere", ComplianceExportCellType.Number),
                o => ComplianceExportCell.FromNumber(o.SeverePigs)),
            (Column("TailBiteYesFactors", "TailBiteYesFactors", pdfOnly: true),
                o => ComplianceExportCell.FromText(YesFactors(o.Answers, localizationService, notAssessed, noYesFactors)))
        };

        foreach (var factor in Factors)
        {
            // Ja/Nej per factor; blank while the outbreak is not assessed.
            columns.Add((Column(factor.LabelKey, factor.LabelKey, csvOnly: true),
                o => o.Answers == null
                    ? new ComplianceExportCell()
                    : ComplianceExportCell.FromText(factor.Answer(o.Answers) ? yes : no)));
        }

        columns.Add((Column("TailBiteActions", "TailBiteActions", ComplianceExportCellType.Number), o =>
        {
            // The CSV writes the total (0 included); the PDF "d af t udført", or
            // the en dash when there is nothing to do.
            var total = o.Actions.Count;
            var done = o.Actions.Count(a => a.DoneAt != null);
            return ComplianceExportCell.FromNumber(total, total == 0
                ? ComplianceExportCell.EmptyGlyph
                : localizationService.GetString("TailBiteActionsOf", done, total));
        }));
        columns.Add((Column("TailBiteActionsDone", "TailBiteActionsDone", ComplianceExportCellType.Number, csvOnly: true),
            o => ComplianceExportCell.FromNumber(o.Actions.Count(a => a.DoneAt != null))));
        columns.Add((Column("TailBitePhotos", "Images", ComplianceExportCellType.Number, pdfOnly: true), photosCell));

        return columns;
    }

    /// <summary>
    /// The PDF's <c>Ja-faktorer</c> cell (ruling 6): the yes factors joined
    /// <c>", "</c> in the assessment's order, "Mangler vurdering" when not
    /// assessed, "Ingen" when assessed with no yes.
    /// </summary>
    private static string YesFactors(FactorAnswers? answers, IBackendConfigurationLocalizationService localizationService,
        string notAssessed, string noYesFactors)
    {
        if (answers == null) return notAssessed;
        var yes = Factors.Where(f => f.Answer(answers)).Select(f => localizationService.GetString(f.LabelKey)).ToList();
        return yes.Count == 0 ? noYesFactors : string.Join(", ", yes);
    }

    /// <summary>"Handlinger fra risikovurderingerne": every live action, outbreak by outbreak.</summary>
    private static ComplianceExportTable ActionsTable(
        IReadOnlyList<TailBiteReportOutbreak> outbreaks, IBackendConfigurationLocalizationService localizationService)
    {
        var table = new ComplianceExportTable
        {
            PdfOnly = true,
            Subtitle = localizationService.GetString("TailBiteActionsTable"),
            Columns =
            [
                PdfColumn("TailBiteOutbreak", localizationService, ComplianceExportCellType.Number),
                PdfColumn("Location", localizationService),
                PdfColumn("TailBiteFactor", localizationService),
                PdfColumn("TailBiteAction", localizationService),
                PdfColumn("TailBiteResponsible", localizationService),
                PdfColumn("TailBiteFollowUp", localizationService, ComplianceExportCellType.Date),
                PdfColumn("Done", localizationService, ComplianceExportCellType.Date),
                PdfColumn("DoneBy", localizationService)
            ]
        };

        foreach (var outbreak in outbreaks)
        {
            foreach (var action in outbreak.Actions)
            {
                table.Rows.Add(new ComplianceExportRow
                {
                    IsDone = action.DoneAt != null,
                    Cells =
                    [
                        ComplianceExportCell.FromNumber(outbreak.Id),
                        ComplianceExportCell.FromText(outbreak.LocationPath),
                        ComplianceExportCell.FromText(FactorLabel(action.Factor, localizationService)),
                        ComplianceExportCell.FromText(action.Description),
                        ComplianceExportCell.FromText(action.ResponsibleName),
                        LocalDateCell(action.FollowUpDate),
                        LocalDateCell(action.DoneAt),
                        ComplianceExportCell.FromText(action.DoneByName)
                    ]
                });
            }
        }

        return table;
    }

    /// <summary>"Registreringer bag udbruddene": every registration, outbreak by outbreak.</summary>
    private static ComplianceExportTable RegistrationsTable(
        IReadOnlyList<TailBiteReportOutbreak> outbreaks, IBackendConfigurationLocalizationService localizationService)
    {
        var table = new ComplianceExportTable
        {
            PdfOnly = true,
            Subtitle = localizationService.GetString("TailBiteRegistrationsTable"),
            Columns =
            [
                PdfColumn("TailBiteOutbreak", localizationService, ComplianceExportCellType.Number),
                PdfColumn("Date", localizationService),
                PdfColumn("Location", localizationService),
                PdfColumn("TailBiteMinor", localizationService, ComplianceExportCellType.Number),
                PdfColumn("TailBiteSevere", localizationService, ComplianceExportCellType.Number),
                PdfColumn("TailBiteActionType", localizationService),
                PdfColumn("TailBiteRegisteredBy", localizationService),
                PdfColumn("Images", localizationService, ComplianceExportCellType.Number)
            ]
        };

        foreach (var outbreak in outbreaks)
        {
            foreach (var registration in outbreak.Registrations)
            {
                table.Rows.Add(new ComplianceExportRow
                {
                    Cells =
                    [
                        ComplianceExportCell.FromNumber(outbreak.Id),
                        DateTimeCell(registration.EffectiveAt, isoForCsv: false),
                        ComplianceExportCell.FromText(registration.Pens),
                        ComplianceExportCell.FromNumber(registration.Minor),
                        ComplianceExportCell.FromNumber(registration.Severe),
                        ComplianceExportCell.FromText(registration.ActionTypes),
                        ComplianceExportCell.FromText(registration.RegisteredByName),
                        ComplianceExportDocumentBuilder.ImagesCell(registration.Photos.Count, localizationService)
                    ]
                });
            }
        }

        return table;
    }

    /// <summary>
    /// The appendix blocks, on the section's first table: one per registration
    /// with photos, accounted by the same
    /// <see cref="ComplianceExportDocumentBuilder.ReserveAppendixImages"/> as an
    /// eForm case; a registration the ceiling leaves nothing for gets no block.
    /// </summary>
    private static void AppendImageBlocks(ComplianceExportDocument document, ComplianceExportTable table,
        IReadOnlyList<TailBiteReportOutbreak> outbreaks, string outbreakLabel)
    {
        var registrationsWithABlock = new HashSet<int>();

        foreach (var outbreak in outbreaks)
        {
            foreach (var registration in outbreak.Registrations)
            {
                // A registration behind two outbreaks is shown once, under the first.
                if (!registrationsWithABlock.Add(registration.Id)) continue;

                var photoIds = registration.Photos
                    .Where(p => p.UploadedDataId > 0)
                    .Select(p => p.UploadedDataId)
                    .ToList();

                var allowed = ComplianceExportDocumentBuilder.ReserveAppendixImages(document, photoIds.Count);
                if (allowed == 0) continue;

                var effectiveAt = LocalTime(registration.EffectiveAt)
                    .ToString(DisplayDateTimeFormat, CultureInfo.InvariantCulture);
                table.ImageBlocks.Add(new ComplianceExportImageBlock
                {
                    Caption = $"{outbreakLabel} {outbreak.Id} · {registration.Pens} · {effectiveAt} · {registration.RegisteredByName}",
                    TotalImages = registration.Photos.Count,
                    TailBitePhotos = photoIds.Take(allowed).ToList()
                });
            }
        }
    }

    private static ComplianceExportColumn PdfColumn(string headerKey,
        IBackendConfigurationLocalizationService localizationService,
        ComplianceExportCellType type = ComplianceExportCellType.Text) => new()
    {
        Key = headerKey,
        Header = localizationService.GetString(headerKey),
        Type = type
    };

    private static string FactorLabel(TailBiteFactor factor, IBackendConfigurationLocalizationService localizationService)
    {
        foreach (var entry in Factors)
        {
            if (entry.Factor == factor) return localizationService.GetString(entry.LabelKey);
        }

        return factor.ToString();
    }

    private static int PhotoCount(TailBiteReportOutbreak outbreak) => outbreak.Registrations.Sum(r => r.Photos.Count);

    private static DateTime LocalTime(DateTime utc) => ComplianceFutureTaskGuard.CopenhagenTimeOf(utc);

    /// <summary>A Copenhagen date as a typed date cell; null is the empty cell.</summary>
    private static ComplianceExportCell LocalDateCell(DateTime? utc) =>
        utc.HasValue
            ? ComplianceExportCell.FromDate(ComplianceFutureTaskGuard.DateInCopenhagen(utc.Value))
            : new ComplianceExportCell();

    /// <summary>
    /// A Copenhagen date and time: <c>dd.MM.yyyy HH:mm</c> on the page. With
    /// <paramref name="isoForCsv"/> the cell's text — what the CSV writes — is
    /// <c>yyyy-MM-dd HH:mm</c> and the page reads the display text instead.
    /// </summary>
    private static ComplianceExportCell DateTimeCell(DateTime utc, bool isoForCsv)
    {
        var local = LocalTime(utc);
        var display = local.ToString(DisplayDateTimeFormat, CultureInfo.InvariantCulture);
        if (!isoForCsv) return ComplianceExportCell.FromText(display);

        var cell = ComplianceExportCell.FromText(local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        cell.DisplayText = display;
        return cell;
    }
}
