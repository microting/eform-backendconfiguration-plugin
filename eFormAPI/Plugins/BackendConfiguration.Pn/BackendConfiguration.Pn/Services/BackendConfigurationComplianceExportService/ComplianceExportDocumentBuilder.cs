using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// Maps each of the three compliance view models onto the format-agnostic
/// <see cref="ComplianceExportDocument"/> (#1169 §2).
///
/// <para>
/// PURE — no database, no clock, no I/O. Every input is a read model that
/// <c>BackendConfigurationComplianceReportService</c> already produced, so nothing
/// here re-queries or re-aggregates: the weighted totals, the null-not-zero
/// compliance percentage and the strictly-before-today overdue count are TAKEN
/// FROM <c>Overview</c>'s output, never recomputed. That is deliberate — #1162
/// ran server-side precisely so the export could render the same numbers as the
/// screen.
/// </para>
///
/// <para>
/// Being pure is also what makes the column sets, the empty-cell glyph, the
/// totals-row marking and the date typing testable without a container.
/// </para>
/// </summary>
public static class ComplianceExportDocumentBuilder
{
    /// <summary>
    /// Oversigt (#1169 §2). Three columns — property name, overdue count,
    /// compliance percentage — followed by the weighted totals row appended as the
    /// LAST DATA ROW (the prototype's shape,
    /// <c>compliance-overview.js:222-241</c>), marked
    /// <see cref="ComplianceExportRow.IsTotal"/> so every renderer can set it apart.
    ///
    /// <para>
    /// <c>CompliancePct</c> is <c>null</c> — never 0 — for a property whose work has
    /// not fallen due. It renders as the en dash, not as a red 0 %.
    /// </para>
    ///
    /// <para>
    /// The first column's header comes from the existing <c>Property</c> key
    /// ("Ejendom"), not from a new "Virksomhed" one: the prototype's two views
    /// label the same PropertyId differently, and one term across the whole
    /// document is less confusing than two.
    /// </para>
    /// </summary>
    public static ComplianceExportDocument BuildOverview(
        ComplianceReportOverviewModel model,
        string period,
        IBackendConfigurationLocalizationService localizationService)
    {
        var document = new ComplianceExportDocument
        {
            Title = localizationService.GetString("ComplianceOverview"),
            Period = period
        };

        var table = new ComplianceExportTable
        {
            Columns =
            [
                new ComplianceExportColumn { Header = localizationService.GetString("Property") },
                new ComplianceExportColumn
                {
                    Header = localizationService.GetString("Overdue"),
                    Type = ComplianceExportCellType.Number
                },
                new ComplianceExportColumn
                {
                    Header = localizationService.GetString("CompliancePercentage"),
                    Type = ComplianceExportCellType.Number
                }
            ]
        };

        foreach (var row in model?.Rows ?? [])
        {
            table.Rows.Add(new ComplianceExportRow
            {
                Cells =
                [
                    ComplianceExportCell.FromText(row.PropertyName),
                    ComplianceExportCell.FromNumber(row.Overdue),
                    ComplianceExportCell.FromNumber(row.CompliancePct)
                ]
            });
        }

        // The totals row is ALWAYS emitted, including for an empty result: the
        // service guarantees a non-null Totals with all-zero counters and a null
        // percentage, and a reader who sees no "I alt" line cannot tell an empty
        // report from a truncated one.
        var totals = model?.Totals ?? new ComplianceReportOverviewRowModel();
        table.Rows.Add(new ComplianceExportRow
        {
            IsTotal = true,
            Cells =
            [
                ComplianceExportCell.FromText(localizationService.GetString("Total")),
                ComplianceExportCell.FromNumber(totals.Overdue),
                ComplianceExportCell.FromNumber(totals.CompliancePct)
            ]
        });

        document.Tables.Add(table);
        return document;
    }

    /// <summary>
    /// Detaljer (#1169 §2): Dato / Ejendom / Kalender / Kl. / Opgave / Medarbejder
    /// / Tags / Status, over the FULL filtered set.
    ///
    /// <para>
    /// The date column is TYPED (<see cref="ComplianceExportCellType.Date"/>), which
    /// is what gives the prototype's three renderings from one source: Excel writes
    /// a real date cell, CSV writes ISO <c>yyyy-MM-dd</c> and Word/PDF write
    /// <c>dd.MM.yyyy</c>. The row model carries <c>TaskDate</c> as a STRING
    /// formatted by <c>Index</c> with the CURRENT culture, so it is parsed back with
    /// <c>yyyy-MM-dd</c> + InvariantCulture; a value that will not parse (possible
    /// only under a non-Gregorian server culture) degrades to a text cell carrying
    /// the original string rather than being dropped.
    /// </para>
    /// </summary>
    public static ComplianceExportDocument BuildDetails(
        List<ComplianceReportRowModel> rows,
        string period,
        IBackendConfigurationLocalizationService localizationService)
    {
        var document = new ComplianceExportDocument
        {
            Title = localizationService.GetString("ComplianceDetails"),
            Period = period
        };

        var table = new ComplianceExportTable
        {
            Columns =
            [
                new ComplianceExportColumn
                {
                    Header = localizationService.GetString("Date"),
                    Type = ComplianceExportCellType.Date
                },
                new ComplianceExportColumn { Header = localizationService.GetString("Property") },
                new ComplianceExportColumn { Header = localizationService.GetString("CalendarBoard") },
                new ComplianceExportColumn { Header = localizationService.GetString("StartTime") },
                new ComplianceExportColumn { Header = localizationService.GetString("Task") },
                new ComplianceExportColumn { Header = localizationService.GetString("Worker") },
                new ComplianceExportColumn { Header = localizationService.GetString("Tags") },
                new ComplianceExportColumn { Header = localizationService.GetString("Status") }
            ]
        };

        var doneLabel = localizationService.GetString("Done");
        var notDoneLabel = localizationService.GetString("NotDone");

        foreach (var row in rows ?? [])
        {
            table.Rows.Add(new ComplianceExportRow
            {
                Cells =
                [
                    DateCellFromIsoString(row.TaskDate),
                    ComplianceExportCell.FromText(row.PropertyName),
                    ComplianceExportCell.FromText(row.BoardName),
                    // An all-day occurrence has no clock time; the prototype shows
                    // nothing there, which normalises to the en dash.
                    row.IsAllDay
                        ? new ComplianceExportCell()
                        : ComplianceExportCell.FromText(FormatStartHour(row.StartHour)),
                    ComplianceExportCell.FromText(row.Title),
                    ComplianceExportCell.FromText(JoinNames(row.WorkerNames)),
                    ComplianceExportCell.FromText(JoinNames(row.Tags)),
                    ComplianceExportCell.FromText(row.Completed ? doneLabel : notDoneLabel)
                ]
            });
        }

        document.Tables.Add(table);
        return document;
    }

    /// <summary>
    /// Rapport (#1169 §2): ONE TABLE PER TAG GROUP PER TEMPLATE, mirroring
    /// <c>ReportEformModel.GroupTagName</c> → <c>ReportEformGroupModel.CheckListName</c>
    /// (#1160 decision 5).
    ///
    /// <para>
    /// <b>The "Delrapport" question, decided:</b> ONE COMPOSITE column,
    /// <c>{tag} – {template}</c>, not two. The same string is the table title and
    /// the Excel sheet name, so the section survives every flattening — a CSV
    /// reader who concatenates the sheets still sees which sub-report each row came
    /// from, and a pivot over the column reproduces the sections exactly. Two
    /// columns would carry the same information at the cost of a wider table and a
    /// second header to translate.
    /// </para>
    ///
    /// <para>
    /// Fixed columns, then the template's REAL answer columns:
    /// Delrapport / ID / Ejendom / Udført af / Udført dato / Område / Billeder,
    /// then one column per <c>ComplianceReportColumnModel</c>. <c>Handlinger</c> is
    /// absent (buttons are not data) and the placeholders <c>Note</c>,
    /// <c>Option 1</c> and <c>Option 2</c> appear nowhere — the answer headers come
    /// from the template schema.
    /// </para>
    ///
    /// <para>
    /// Cells are addressed by <c>ComplianceReportColumnModel.Key</c> against
    /// <c>ComplianceReportCaseModel.Cells</c>. A missing key means UNANSWERED and
    /// renders as the en dash. Because the loop walks the COLUMN list and looks up
    /// by key — rather than zipping a header list against a value list — the
    /// #1160-finding-3 desync cannot occur here.
    /// </para>
    ///
    /// <para>
    /// <c>Billeder</c> is the image COUNT, not the images
    /// (<c>compliance.js:1773-1790</c>). The images themselves only ever reach the
    /// optional Word/PDF appendix.
    /// </para>
    ///
    /// <para>
    /// <b>The appendix is emitted once per CASE, not once per tag group.</b> The
    /// report service shares ONE <c>ComplianceReportCaseModel</c> BY REFERENCE
    /// across every tag group a row belongs to
    /// (<c>BackendConfigurationComplianceReportService.cs:1150-1152</c>): the ROW
    /// duplication is deliberate — a case carrying three tags belongs in three
    /// sections — but its photographs are the same photographs. Without the
    /// <c>SdkCaseId</c> de-duplication below, a three-tag case contributes three
    /// identical appendix blocks, and <see cref="MaxAppendixImagesPerCase"/> bounds
    /// a BLOCK rather than the document. The images are also the only unbounded
    /// thing in the pipeline — <c>ComplianceExportWordWriter</c> accumulates every
    /// base64 payload into one string and then copies it twice more — so the
    /// document-wide <see cref="MaxAppendixImages"/> ceiling below is what actually
    /// bounds it, and the document states it wherever it bit.
    /// </para>
    /// </summary>
    public static ComplianceExportDocument BuildReport(
        List<ComplianceReportTagGroupModel> tagGroups,
        string period,
        bool includeImageAppendix,
        IBackendConfigurationLocalizationService localizationService)
    {
        var document = new ComplianceExportDocument
        {
            Title = localizationService.GetString("ComplianceReport"),
            Period = period
        };

        var untaggedLabel = localizationService.GetString("WithoutTag");
        var columnsUnavailableLabel = localizationService.GetString("ColumnsUnavailable");

        // De-duplication of the image appendix across tag groups, and the
        // document-wide image budget. Both are per-DOCUMENT, so they live outside
        // the tag-group loop.
        var casesWithAnAppendixBlock = new HashSet<int>();

        foreach (var tagGroup in tagGroups ?? [])
        {
            var tagName = TagGroupLabel(tagGroup, untaggedLabel);

            foreach (var templateGroup in tagGroup.Templates ?? [])
            {
                var templateName = string.IsNullOrWhiteSpace(templateGroup.CheckListName)
                    ? $"#{templateGroup.CheckListId}"
                    : templateGroup.CheckListName;

                var sectionLabel = $"{tagName} – {templateName}";

                var table = new ComplianceExportTable
                {
                    // A template whose schema could not be derived says so in its
                    // own heading — an empty column set otherwise looks like "this
                    // template has no answerable fields", which is a different fact.
                    Title = templateGroup.SchemaUnavailable
                        ? $"{sectionLabel} ({columnsUnavailableLabel})"
                        : sectionLabel,
                    Columns =
                    [
                        new ComplianceExportColumn { Header = localizationService.GetString("SubReport") },
                        new ComplianceExportColumn
                        {
                            Header = localizationService.GetString("CaseId"),
                            Type = ComplianceExportCellType.Number
                        },
                        new ComplianceExportColumn { Header = localizationService.GetString("Property") },
                        new ComplianceExportColumn { Header = localizationService.GetString("DoneBy") },
                        new ComplianceExportColumn
                        {
                            Header = localizationService.GetString("CompletedDate"),
                            Type = ComplianceExportCellType.Date
                        },
                        new ComplianceExportColumn { Header = localizationService.GetString("Area") },
                        new ComplianceExportColumn
                        {
                            Header = localizationService.GetString("Images"),
                            Type = ComplianceExportCellType.Number
                        }
                    ]
                };

                var answerColumns = templateGroup.Columns ?? [];
                foreach (var column in answerColumns)
                {
                    table.Columns.Add(new ComplianceExportColumn
                    {
                        Header = string.IsNullOrWhiteSpace(column.Label) ? column.Key : column.Label
                    });
                }

                foreach (var caseModel in templateGroup.Cases ?? [])
                {
                    var row = new ComplianceExportRow
                    {
                        Cells =
                        [
                            ComplianceExportCell.FromText(sectionLabel),
                            ComplianceExportCell.FromNumber(caseModel.SdkCaseId),
                            ComplianceExportCell.FromText(caseModel.PropertyName),
                            ComplianceExportCell.FromText(JoinNames(caseModel.WorkerNames)),
                            // Case METADATA, never an answer field (#1160 finding 7).
                            ComplianceExportCell.FromDate(caseModel.DoneAt),
                            ComplianceExportCell.FromText(caseModel.Title),
                            ComplianceExportCell.FromNumber(caseModel.ImagesCount)
                        ]
                    };

                    foreach (var column in answerColumns)
                    {
                        // Keyed lookup, never positional: a column with no matching
                        // key is UNANSWERED and gets the en dash, and no later
                        // column shifts.
                        var answered = caseModel.Cells != null
                                       && caseModel.Cells.TryGetValue(column.Key, out var value)
                            ? value
                            : null;
                        row.Cells.Add(ComplianceExportCell.FromText(answered));
                    }

                    table.Rows.Add(row);

                    if (!includeImageAppendix) continue;

                    // ONE block per case, no matter how many tag groups the case
                    // appears in — the row above is duplicated per section on
                    // purpose, the photographs are not. The block lands under the
                    // FIRST section the case appears in, and its caption names that
                    // section.
                    if (!casesWithAnAppendixBlock.Add(caseModel.SdkCaseId)) continue;

                    var (block, wanted) = BuildImageBlock(
                        caseModel, sectionLabel,
                        MaxAppendixImages - document.AppendixImagesEmbedded);

                    document.AppendixImagesRequested += wanted;
                    if (block == null) continue;

                    document.AppendixImagesEmbedded += block.ImageNames.Count;
                    table.ImageBlocks.Add(block);
                }

                document.Tables.Add(table);
            }
        }

        return document;
    }

    /// <summary>
    /// The label for a tag group, discriminating on the TAG ID and never on the
    /// name.
    ///
    /// <para>
    /// <c>BackendConfigurationComplianceReportService.cs:1061-1073</c> deliberately
    /// does NOT drop a tag id whose NAME could not be resolved: tag ids live in the
    /// BC database and tag names in the items-planning one with no foreign key
    /// between them, so a row keeps the tag it actually carries and the group's
    /// <c>TagName</c> is simply null. Discriminating on the name would file such a
    /// group under "Uden tag" — indistinguishable from the genuinely untagged group
    /// that the service sorts last precisely to keep the two apart. That would
    /// silently merge two different sections under one <c>Delrapport</c> value
    /// (whose whole justification is that a pivot over it reproduces the sections
    /// exactly), collide two Excel sheet names, and make the export disagree with
    /// the screen.
    /// </para>
    ///
    /// <para>
    /// A named group with no resolvable name is therefore labelled <c>#{TagId}</c>
    /// — the same neutral form this method already uses for a template with no
    /// name (<c>#{CheckListId}</c>). It is visibly not a tag NAME, so it cannot be
    /// mistaken for one, it stays distinct from every other group, and it names the
    /// id a reader can look the tag up by. Only a group with NO tag id at all gets
    /// the localised "Uden tag".
    /// </para>
    /// </summary>
    private static string TagGroupLabel(ComplianceReportTagGroupModel tagGroup, string untaggedLabel)
    {
        if (!tagGroup.TagId.HasValue) return untaggedLabel;

        return string.IsNullOrWhiteSpace(tagGroup.TagName)
            ? $"#{tagGroup.TagId.Value}"
            : tagGroup.TagName;
    }

    /// <summary>
    /// Per-case appendix block, capped at <see cref="MaxAppendixImagesPerCase"/>
    /// and further clipped to what is left of the document-wide
    /// <see cref="MaxAppendixImages"/> budget. Images whose display name the
    /// projector could not derive (the <c>UploadedData.FileName</c> existence check
    /// failed) are dropped: there is no file to read for them.
    ///
    /// <para>
    /// Returns the block (<c>null</c> when nothing can be embedded for this case)
    /// AND how many images the per-case cap would have embedded had the document
    /// budget been unlimited. The caller adds that second number up so the document
    /// can state the ceiling's own truncation — including for the cases that got no
    /// block at all.
    /// </para>
    /// </summary>
    private static (ComplianceExportImageBlock Block, int Wanted) BuildImageBlock(
        ComplianceReportCaseModel caseModel, string sectionLabel, int documentBudget)
    {
        var usable = (caseModel.Images ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.FileName))
            .ToList();

        var wanted = Math.Min(usable.Count, MaxAppendixImagesPerCase);
        if (wanted == 0) return (null, 0);

        var allowed = Math.Min(wanted, Math.Max(documentBudget, 0));
        // The document ceiling is already spent. The case still counts towards the
        // requested total — that is what makes the document's "n of m" honest — but
        // it gets no block, because a captioned block with no image in it is a page
        // break carrying nothing.
        if (allowed == 0) return (null, wanted);

        var block = new ComplianceExportImageBlock
        {
            Caption = $"{sectionLabel} · {caseModel.SdkCaseId} · {caseModel.Title} · {caseModel.TaskDate}",
            // The case's OWN image count, NOT the post-drop count. An image whose
            // name could not be derived is dropped above, and the Billeder COLUMN
            // still prints caseModel.ImagesCount — so taking the total from the
            // filtered list would make the caption agree with itself while
            // disagreeing with the table, and would hide the very drop this filter
            // performed.
            TotalImages = caseModel.ImagesCount
        };

        foreach (var image in usable.Take(allowed))
        {
            block.ImageNames.Add(image.FileName);
            block.GeoLinks.Add(image.GeoLink);
        }

        return (block, wanted);
    }

    /// <summary>
    /// Hard per-case ceiling on appendix images. The measured worst case was 111
    /// appendix sheets out of 135 for one quarter of completed work, so the
    /// appendix is opt-in AND capped; the document states the cap wherever it bit.
    /// </summary>
    public const int MaxAppendixImagesPerCase = 4;

    /// <summary>
    /// Hard DOCUMENT-wide ceiling on appendix images, on top of the per-case cap.
    ///
    /// <para>
    /// The per-case cap bounds a block, not the file. Nothing in the Word path
    /// streams: <c>ComplianceExportWordWriter.WriteAsync</c> accumulates every
    /// <c>data:image/png;base64,…</c> payload into one <see cref="System.Text.StringBuilder"/>,
    /// then materialises it with <c>ToString()</c> and again with the
    /// <c>{%Content%}</c> replace — three live copies of the same string before
    /// HtmlToOpenXml sees it. At the ~230 KB a 600px-wide resized photograph
    /// base64-encodes to, 200 images is ~46 MB of UTF-16 per copy, so a worst-case
    /// export peaks at roughly 140 MB of large-object-heap string rather than the
    /// effectively unbounded figure the 5000-row ceiling would otherwise permit.
    /// </para>
    ///
    /// <para>
    /// 200 was chosen as the largest round number that keeps that peak inside a
    /// normal container budget while still carrying an ordinary quarter's appendix
    /// whole — the measured worst case of 111 blocks only reaches 200 images if
    /// most of those cases carry the full four photographs.
    /// </para>
    /// </summary>
    public const int MaxAppendixImages = 200;

    /// <summary>
    /// <c>yyyy-MM-dd</c> → a typed date cell. Anything else degrades to a text cell
    /// carrying the original string — never dropped, never guessed at with another
    /// format.
    /// </summary>
    private static ComplianceExportCell DateCellFromIsoString(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return ComplianceExportCell.FromDate(parsed);
        }

        return ComplianceExportCell.FromText(value);
    }

    /// <summary>
    /// A fractional hour-of-day (9.5) as <c>HH:mm</c> (09:30). Values outside
    /// [0, 24) are clamped rather than producing "25:00": <c>StartHour</c> is a
    /// free <c>double</c> on the occurrence-exception row and nothing constrains it
    /// at the database.
    /// </summary>
    public static string FormatStartHour(double startHour)
    {
        var totalMinutes = (int)Math.Round(startHour * 60d, MidpointRounding.AwayFromZero);
        totalMinutes = Math.Clamp(totalMinutes, 0, 24 * 60 - 1);
        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }

    private static string JoinNames(List<string> values) =>
        values == null || values.Count == 0
            ? null
            : string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
}
