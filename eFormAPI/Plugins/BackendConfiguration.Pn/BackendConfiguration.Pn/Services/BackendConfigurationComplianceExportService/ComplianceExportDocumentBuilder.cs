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
/// Being pure is also what makes the column sets, the empty-cell marking, the
/// totals-row and done-row marking and the date typing testable without a
/// container.
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
    /// <b>The first column's header is "Virksomhed" (key <c>Company</c>), per the
    /// #1190 mock-up, deliberately reversing the #1164 header wording for the
    /// EXPORT ONLY — the screen's Oversigt table and the page-header line above
    /// this table keep "Ejendom".</b> The <c>Property</c> key itself is untouched
    /// because Detaljer and the page header use it.
    /// </para>
    ///
    /// <para>
    /// The document title is "Compliance oversigt" (key
    /// <c>ComplianceOverviewTitle</c>), not the bare view label "Oversigt" (key
    /// <c>ComplianceOverview</c>). The view label is what <c>BuildFileName</c>
    /// prefixes the download with (<c>Oversigt-…</c>), so it stays as it is.
    /// </para>
    ///
    /// <para>
    /// <c>Compliance %</c> is a TEXT column carrying <c>"{pct}%"</c> (#1190), for
    /// the data rows and the totals row alike, so every renderer prints the sign
    /// — a typed Number cell is decorated by no renderer. The value is still taken
    /// verbatim from the read model; only the suffix is added. <c>Overskredet</c>
    /// stays a Number column.
    /// </para>
    /// </summary>
    public static ComplianceExportDocument BuildOverview(
        ComplianceReportOverviewModel model,
        string period,
        IBackendConfigurationLocalizationService localizationService)
    {
        var document = new ComplianceExportDocument
        {
            Title = localizationService.GetString("ComplianceOverviewTitle"),
            Period = period
        };

        var table = new ComplianceExportTable
        {
            Columns =
            [
                // "Virksomhed" — see the type comment: export-only wording, the
                // screen keeps "Ejendom".
                new ComplianceExportColumn { Header = localizationService.GetString("Company") },
                new ComplianceExportColumn
                {
                    Header = localizationService.GetString("Overdue"),
                    Type = ComplianceExportCellType.Number
                },
                // Text, not Number: the cell carries its own "%" suffix.
                new ComplianceExportColumn { Header = localizationService.GetString("CompliancePercentage") }
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
                    PercentCell(row.CompliancePct)
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
                PercentCell(totals.CompliancePct)
            ]
        });

        document.Tables.Add(table);
        return document;
    }

    /// <summary>
    /// The Oversigt percentage as a text cell with the <c>%</c> sign attached
    /// (#1190): <c>25</c> → <c>25%</c>. Invariant formatting — the value is an
    /// integer, but the intent is stated.
    ///
    /// <para>
    /// <c>null</c> — a property whose work has not fallen due — stays the shared
    /// empty cell: the en dash in Word/PDF, a blank field in CSV (#1191's rule,
    /// applied by the CSV writer through <see cref="ComplianceExportCell.IsEmpty"/>),
    /// never <c>0%</c> and never <c>%</c> alone. This builder only emits the
    /// empty cell and does not decide its rendering.
    /// </para>
    /// </summary>
    private static ComplianceExportCell PercentCell(int? pct) =>
        pct.HasValue
            ? ComplianceExportCell.FromText(pct.Value.ToString(CultureInfo.InvariantCulture) + "%")
            : new ComplianceExportCell();

    /// <summary>
    /// Detaljer (#1169 §2, mock-up p8 via #1191): Dato / Ejendom / Kalender / Kl.
    /// / Opgave / Medarbejder / Tags / Status, over the FULL filtered set.
    ///
    /// <para>
    /// The date column is TYPED (<see cref="ComplianceExportCellType.Date"/>), which
    /// is what gives the two renderings from one source: CSV writes ISO
    /// <c>yyyy-MM-dd</c>, while Word/PDF print the cell's
    /// <see cref="ComplianceExportCell.DisplayText"/> — the screen's long weekday
    /// form, <c>Tirsdag 21. juli</c> (#1191, see <see cref="FormatWeekdayDate"/>).
    /// The row model carries <c>TaskDate</c> as a STRING
    /// formatted by <c>Index</c> with the CURRENT culture, so it is parsed back with
    /// <c>yyyy-MM-dd</c> + InvariantCulture; a value that will not parse (possible
    /// only under a non-Gregorian server culture) degrades to a text cell carrying
    /// the original string rather than being dropped — and then has no weekday
    /// text either, because there is no date to derive one from.
    /// </para>
    ///
    /// <para>
    /// <c>Kl.</c> is the RANGE <c>13:00 - 14:00</c> (hyphen-minus with spaces, as
    /// the mock-up; not the en dash used elsewhere), computed from
    /// <c>StartHour</c> + <c>Duration</c> exactly as the screen's
    /// <c>formatComplianceTimeRange</c> does (#1191). An all-day row has no clock
    /// time and gets the empty cell.
    /// </para>
    ///
    /// <para>
    /// The document title is "Compliance" (key <c>ComplianceDetailsTitle</c>),
    /// not the view label "Detaljer" (key <c>ComplianceDetails</c>), which
    /// <c>BuildFileName</c> still prefixes the download with — the same
    /// title-vs-view-label split Oversigt uses. The <c>Tags</c> header reads the
    /// NEW <c>TagsPlain</c> key ("Tags"), not <c>Tags</c> ("Etiketter"): the
    /// export says what the mock-up says, and the <c>Tags</c> key is untouched
    /// because other screens read it.
    /// </para>
    ///
    /// <para>
    /// A completed row is marked <see cref="ComplianceExportRow.IsDone"/> so
    /// Word/PDF can tint it; the <c>Status</c> cell still carries the label, which
    /// is what CSV readers get.
    /// </para>
    /// </summary>
    public static ComplianceExportDocument BuildDetails(
        List<ComplianceReportRowModel> rows,
        string period,
        IBackendConfigurationLocalizationService localizationService)
    {
        var document = new ComplianceExportDocument
        {
            Title = localizationService.GetString("ComplianceDetailsTitle"),
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
                // "Tags", not "Etiketter" — see the method comment.
                new ComplianceExportColumn { Header = localizationService.GetString("TagsPlain") },
                new ComplianceExportColumn { Header = localizationService.GetString("Status") }
            ]
        };

        var doneLabel = localizationService.GetString("Done");
        var notDoneLabel = localizationService.GetString("NotDone");

        foreach (var row in rows ?? [])
        {
            var dateCell = DateCellFromIsoString(row.TaskDate);
            if (dateCell.Date.HasValue)
            {
                dateCell.DisplayText = FormatWeekdayDate(dateCell.Date.Value);
            }

            table.Rows.Add(new ComplianceExportRow
            {
                IsDone = row.Completed,
                Cells =
                [
                    dateCell,
                    ComplianceExportCell.FromText(row.PropertyName),
                    ComplianceExportCell.FromText(row.BoardName),
                    // An all-day occurrence has no clock time; the prototype shows
                    // nothing there, which is the empty cell (en dash in Word/PDF,
                    // blank in CSV).
                    row.IsAllDay
                        ? new ComplianceExportCell()
                        : ComplianceExportCell.FromText(FormatTimeRange(row.StartHour, row.Duration)),
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
    /// Rapport (#1169 §2, regrouped by #1188): ONE TABLE PER REPORT HEADLINE —
    /// the PDF's "Tabel_Rapport" rule ("one table per Rapportoverskrift, not per
    /// tag"), which is also how <c>ReportEformModel</c>'s old Rapport grouped.
    ///
    /// <para>
    /// <b>Reversed decisions.</b> Until #1188 this method produced one table per
    /// tag group per template (#1160 decision 5 / #1167), titled and keyed by the
    /// composite <c>{tag} – {template}</c> label that PR #1178 decided the
    /// <c>Delrapport</c> column would carry ("ONE COMPOSITE column, not two"). All
    /// of that read the prototype's literal <c>Rapportoverskrift</c> heading as a
    /// placeholder. The customer's PDF (pages 4–5, 9–10) shows the actual model:
    /// a small caption of the tags joined <c>" - "</c>, a bold heading that IS
    /// the report headline, and a <c>Delrapport</c> cell carrying the ROW's own
    /// tags — no headline column anywhere. Hence, per table:
    /// <see cref="ComplianceExportTable.Caption"/> = the group's
    /// <c>TagsCaption</c>, <see cref="ComplianceExportTable.Title"/> = the
    /// headline label, and the <c>Delrapport</c> cell = the row's
    /// <c>Tags</c> joined <c>" - "</c> (an untagged row gets the empty cell).
    /// There is deliberately NO "Rapportoverskrift" column (#1188 decision 4: the
    /// mock-up has none; the <c>ReportHeadline</c> localisation key exists for
    /// #1192 should that be revisited).
    /// </para>
    ///
    /// <para>
    /// Fixed columns, then the group's REAL answer columns:
    /// Delrapport / ID / Ejendom / Udført af / Udført dato / Område / Billeder,
    /// then one column per <c>ComplianceReportColumnModel</c> — the service's
    /// UNION over every template answered in the group, keyed <c>f{fieldId}</c>.
    /// <c>Handlinger</c> is absent (buttons are not data) and the placeholders
    /// <c>Note</c>, <c>Option 1</c> and <c>Option 2</c> appear nowhere — the
    /// answer headers come from the template schemas.
    /// </para>
    ///
    /// <para>
    /// Cells are addressed by <c>ComplianceReportColumnModel.Key</c> against
    /// <c>ComplianceReportCaseModel.Cells</c>. A missing key means UNANSWERED and
    /// renders as the en dash — which, with union columns, is also how a case
    /// answered on template A renders under template B's columns, in place.
    /// Because the loop walks the COLUMN list and looks up by key — rather than
    /// zipping a header list against a value list — the #1160-finding-3 desync
    /// cannot occur here.
    /// </para>
    ///
    /// <para>
    /// <c>Billeder</c> is the image COUNT, not the images
    /// (<c>compliance.js:1773-1790</c>). The images themselves only ever reach the
    /// optional Word/PDF appendix, whose block caption leads with the section's
    /// tags caption (<c>Bilag – Miljøtilsyn - Brand …</c>, PDF page 9) — or the
    /// headline label when the group has no tags.
    /// </para>
    ///
    /// <para>
    /// The appendix is emitted once per <c>SdkCaseId</c>. Since #1188 a case is in
    /// exactly one group, so the de-duplication is redundant — but it is kept:
    /// it is the invariant the document-wide <see cref="MaxAppendixImages"/>
    /// ceiling (the only thing bounding the Word writer's base64 accumulation)
    /// was reasoned about with, and it costs a hash set.
    /// </para>
    /// </summary>
    public static ComplianceExportDocument BuildReport(
        List<ComplianceReportHeadlineGroupModel> groups,
        string period,
        bool includeImageAppendix,
        IBackendConfigurationLocalizationService localizationService)
    {
        var document = new ComplianceExportDocument
        {
            Title = localizationService.GetString("ComplianceReport"),
            Period = period
        };

        var withoutHeadlineLabel = localizationService.GetString("WithoutReportHeadline");
        var columnsUnavailableLabel = localizationService.GetString("ColumnsUnavailable");

        // De-duplication of the image appendix, and the document-wide image
        // budget. Both are per-DOCUMENT, so they live outside the group loop.
        var casesWithAnAppendixBlock = new HashSet<int>();

        foreach (var group in groups ?? [])
        {
            var headlineLabel = HeadlineLabel(group, withoutHeadlineLabel);
            var caption = group.TagsCaption ?? string.Empty;

            var table = new ComplianceExportTable
            {
                Caption = caption,
                // A template whose schema could not be derived says so in the
                // heading — an empty column block otherwise looks like "this
                // template has no answerable fields", which is a different fact.
                // With union columns the notice is per template: it names the ids
                // when only some of the group's templates are affected, and is
                // the bare label when every one of them is.
                Title = SchemaUnavailableSuffix(group, columnsUnavailableLabel) is { } suffix
                    ? $"{headlineLabel} ({suffix})"
                    : headlineLabel,
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

            var answerColumns = group.Columns ?? [];
            foreach (var column in answerColumns)
            {
                table.Columns.Add(new ComplianceExportColumn
                {
                    Header = string.IsNullOrWhiteSpace(column.Label) ? column.Key : column.Label
                });
            }

            // The appendix caption leads with the tags caption (PDF page 9), and
            // falls back to the headline label so an untagged group's blocks are
            // still attributable to their section.
            var appendixLabel = string.IsNullOrEmpty(caption) ? headlineLabel : caption;

            foreach (var caseModel in group.Cases ?? [])
            {
                var row = new ComplianceExportRow
                {
                    Cells =
                    [
                        // The ROW's own tags, " - "-joined (PDF page 10); an
                        // untagged row gets the empty cell, not the headline.
                        ComplianceExportCell.FromText(JoinTags(caseModel.Tags)),
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
                    // key is UNANSWERED (or belongs to another template in the
                    // union) and gets the en dash, and no later column shifts.
                    var answered = caseModel.Cells != null
                                   && caseModel.Cells.TryGetValue(column.Key, out var value)
                        ? value
                        : null;
                    row.Cells.Add(ComplianceExportCell.FromText(answered));
                }

                table.Rows.Add(row);

                if (!includeImageAppendix) continue;

                // ONE block per case — see the method comment on why the guard is
                // kept although a case is now in exactly one group.
                if (!casesWithAnAppendixBlock.Add(caseModel.SdkCaseId)) continue;

                var (block, wanted) = BuildImageBlock(
                    caseModel, appendixLabel,
                    MaxAppendixImages - document.AppendixImagesEmbedded);

                document.AppendixImagesRequested += wanted;
                if (block == null) continue;

                document.AppendixImagesEmbedded += block.ImageNames.Count;
                table.ImageBlocks.Add(block);
            }

            document.Tables.Add(table);
        }

        return document;
    }

    /// <summary>
    /// The heading for a headline group, discriminating on the HEADLINE ID and
    /// never on the name.
    ///
    /// <para>
    /// The report service deliberately does NOT drop a headline id whose NAME
    /// could not be resolved: tag ids live in the BC database and tag names in the
    /// items-planning one with no foreign key between them, so a row keeps the
    /// headline it actually carries and the group's <c>HeadlineName</c> is simply
    /// null. Discriminating on the name would file such a group under "Uden
    /// rapportoverskrift" — indistinguishable from the genuinely headline-less
    /// fallback group the service sorts last precisely to keep the two apart —
    /// giving two sections the same title and making the export disagree with the
    /// screen, which renders <c>#{id}</c>.
    /// </para>
    ///
    /// <para>
    /// A named group with no resolvable name is therefore labelled
    /// <c>#{HeadlineTagId}</c> — visibly not a NAME, so it cannot be mistaken for
    /// one, distinct from every other group, and naming the id a reader can look
    /// the tag up by. Only the group with NO headline id at all gets the localised
    /// "Uden rapportoverskrift".
    /// </para>
    /// </summary>
    private static string HeadlineLabel(ComplianceReportHeadlineGroupModel group, string withoutHeadlineLabel)
    {
        if (!group.HeadlineTagId.HasValue) return withoutHeadlineLabel;

        return string.IsNullOrWhiteSpace(group.HeadlineName)
            ? $"#{group.HeadlineTagId.Value}"
            : group.HeadlineName;
    }

    /// <summary>
    /// The "(Kolonner utilgængelige)" heading suffix, or <c>null</c> when every
    /// template in the group has a schema. When only SOME of them lack one the
    /// label names the affected template ids (<c>Kolonner utilgængelige: #509,
    /// #511</c>), because the rest of the table still carries real columns and a
    /// reader must be able to tell which template's answers are missing.
    /// </summary>
    private static string SchemaUnavailableSuffix(
        ComplianceReportHeadlineGroupModel group, string columnsUnavailableLabel)
    {
        var unavailable = group.SchemaUnavailableCheckListIds ?? [];
        if (unavailable.Count == 0) return null;

        var all = group.CheckListIds ?? [];
        var everyTemplateAffected = all.Count == 0 || all.All(unavailable.Contains);
        return everyTemplateAffected
            ? columnsUnavailableLabel
            : $"{columnsUnavailableLabel}: {string.Join(", ", unavailable.Select(id => $"#{id}"))}";
    }

    /// <summary>
    /// The row's tags for the <c>Delrapport</c> cell — hyphen-minus with a space
    /// either side, verbatim from the PDF (page 10), NOT the en dash the empty
    /// cell and the period line use. Null (→ the empty cell) for an untagged row.
    /// </summary>
    private static string JoinTags(List<string> tags) =>
        tags == null || tags.Count == 0
            ? null
            : string.Join(" - ", tags.Where(t => !string.IsNullOrWhiteSpace(t)));

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

    /// <summary>
    /// Detaljer's <c>Kl.</c> range, <c>13:00 - 14:00</c> for start 13.0 and
    /// duration 1.0 — the screen's formula (<c>formatComplianceTimeRange</c> in
    /// <c>compliance-week-grouping.ts</c>), plus <see cref="FormatStartHour"/>'s
    /// 23:59 clamp required by #1191. The separator is a hyphen-minus with a
    /// space either side, verbatim from the mock-up; it is NOT the en dash the
    /// period line and the empty cell use.
    ///
    /// <para>
    /// Both ends go through <see cref="FormatStartHour"/>, so the end is clamped
    /// the same way the start is: 23.5 + 1.0 renders <c>23:30 - 23:59</c> here,
    /// where the screen (which has no clamp and rounds each part on its own)
    /// shows <c>23:30 - 24:30</c>; likewise the export rounds TOTAL minutes
    /// (<c>10:00</c>) whereas the screen rounds hours and minutes separately and
    /// can yield <c>09:60</c>. <c>Duration</c> is a non-nullable <c>double</c>
    /// on the row model, so a row with no duration carries 0 and renders a
    /// zero-width range (<c>13:00 - 13:00</c>) — the screen does the same for a 0
    /// duration, and a fabricated end would be a guess.
    /// </para>
    /// </summary>
    public static string FormatTimeRange(double startHour, double duration) =>
        $"{FormatStartHour(startHour)} - {FormatStartHour(startHour + duration)}";

    /// <summary>
    /// Detaljer's <c>Dato</c> as the screen shows it: <c>dddd d. MMMM</c> with the
    /// first letter upper-cased — <c>Tirsdag 21. juli</c> under a Danish culture
    /// (#1191; the screen's <c>formatComplianceDayLabel</c>). No year: the period
    /// in the page header carries it.
    ///
    /// <para>
    /// Formatted with <see cref="CultureInfo.CurrentCulture"/> — the culture the
    /// JSON <c>IStringLocalizer</c> (<c>JsonStringLocalizer</c>) resolves the column
    /// headers through (it reads <c>CurrentCulture</c>, not <c>CurrentUICulture</c>),
    /// so the weekday and the headers around it are always in the same language.
    /// Under a non-Danish request culture the LANGUAGE of the weekday and month
    /// follows the culture, but the <c>dddd d. MMMM</c> day-dot-month shape is
    /// fixed — unlike the screen, whose <c>toLocaleDateString</c> would give
    /// <c>Tuesday, July 21</c> for en-US. The DATE was parsed with the invariant culture
    /// (<see cref="DateCellFromIsoString"/>); only the rendering is
    /// culture-dependent.
    /// </para>
    /// </summary>
    public static string FormatWeekdayDate(DateTime date)
    {
        var text = date.ToString("dddd d. MMMM", CultureInfo.CurrentCulture);
        return text.Length == 0
            ? text
            : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }

    private static string JoinNames(List<string> values) =>
        values == null || values.Count == 0
            ? null
            : string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
}
