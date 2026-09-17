using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

/// <summary>
/// Maps the PlanTimer sheet's header row to each worker's hours and text
/// columns by header NAME, never by position. The sheet is edited by hand and
/// PushToGoogleSheet appends a missing "- timer" or "- tekst" header on its
/// own, so a fixed two-column stride drifts: one stray column makes every later
/// worker read a neighbouring header as its site name and silently miss the
/// site lookup.
///
/// Copy of TimePlanning.Pn.Infrastructure.Helpers.PlanTimerSheetColumns in
/// eform-angular-timeplanning-plugin, itself a copy of the scheduled import's
/// helper in eform-service-timeplanning-plugin. Kept in sync by hand: the three
/// repos share no code, and the rules must agree or the same sheet is read and
/// written differently through each path. This copy exists because this plugin
/// writes the sheet's headers when a worker is created or edited. Consolidating
/// the three into Microting.TimePlanningBase is the real fix and needs a base
/// release.
/// </summary>
public static class PlanTimerSheetColumns
{
    /// <summary>Columns before this one hold the date and other non-worker data.</summary>
    public const int FirstWorkerColumn = 3;

    /// <summary>
    /// Hyphen plus the en and em dashes a spreadsheet's autocorrect types. The
    /// hyphen must stay first: SuffixedHeader puts this in a character class.
    /// </summary>
    private const string Dashes = "-–—";

    private static readonly Regex SuffixedHeader = new(
        $@"^(?<name>.*?)\s*[{Dashes}]\s*(?<kind>timer|tekst)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Bare column labels a legacy sheet may carry; never a worker's name.</summary>
    private static readonly HashSet<string> ColumnLabels = ["timer", "tekst"];

    /// <summary>A null column means the sheet has no such column for the worker.</summary>
    public sealed record WorkerColumns(string Key, string Name, int? HoursColumn, int? TextColumn);

    public sealed record Layout(IReadOnlyList<WorkerColumns> Workers, IReadOnlyList<string> Problems);

    /// <summary>
    /// The comparison key for a worker or site name: lower-case with every
    /// whitespace character (non-breaking spaces included) and dash removed,
    /// so "Phien Van Le", "phien  van le" and "Julius -" match their headers.
    /// </summary>
    public static string NormalizeName(string name) =>
        new string((name ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && !Dashes.Contains(c)).ToArray())
            .ToLowerInvariant();

    public static Layout Map(IList<object> headerRow)
    {
        var byKey = new Dictionary<string, WorkerColumns>();
        var unsuffixed = new List<(int Column, string Header)>();
        var problems = new List<string>();

        for (var col = FirstWorkerColumn; col < headerRow.Count; col++)
        {
            var header = CellAt(headerRow, col).Trim();
            if (header.Length == 0)
            {
                continue;
            }

            var match = SuffixedHeader.Match(header);
            if (!match.Success)
            {
                unsuffixed.Add((col, header));
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            var key = NormalizeName(name);
            if (key.Length == 0)
            {
                problems.Add(NamesNoWorker(col, header));
                continue;
            }

            var isHours = match.Groups["kind"].Value.Equals("timer", StringComparison.OrdinalIgnoreCase);
            var worker = byKey.GetValueOrDefault(key) ?? new WorkerColumns(key, name, null, null);
            if ((isHours ? worker.HoursColumn : worker.TextColumn) is { } used)
            {
                problems.Add(Duplicate(col, header, used));
                continue;
            }

            byKey[key] = isHours ? worker with { HoursColumn = col } : worker with { TextColumn = col };
        }

        // Headers without a suffix are the legacy layout the fixed stride read:
        // the header is the hours column and the next column holds the text,
        // whatever its header says, unless a suffixed header claims it.
        var legacyTextColumns = new HashSet<int>();
        foreach (var (col, header) in unsuffixed)
        {
            var key = NormalizeName(header);
            if (legacyTextColumns.Contains(col) || ColumnLabels.Contains(key))
            {
                continue;
            }

            if (key.Length == 0)
            {
                problems.Add(NamesNoWorker(col, header));
                continue;
            }

            var worker = byKey.GetValueOrDefault(key) ?? new WorkerColumns(key, header, null, null);
            if (worker.HoursColumn is { } used)
            {
                problems.Add(Duplicate(col, header, used));
                continue;
            }

            worker = worker with { HoursColumn = col };
            if (worker.TextColumn == null && !SuffixedHeader.IsMatch(CellAt(headerRow, col + 1).Trim()))
            {
                worker = worker with { TextColumn = col + 1 };
                legacyTextColumns.Add(col + 1);
            }

            byKey[key] = worker;
        }

        return new Layout(byKey.Values.OrderBy(x => x.HoursColumn ?? x.TextColumn).ToList(), problems);
    }

    /// <summary>
    /// <paramref name="FirstColumn"/> is the 0-based column the first appended
    /// header belongs in. It never precedes FirstWorkerColumn: on a sheet whose
    /// header row is empty or short, headers written into A/B/C would sit in the
    /// date columns the import skips, and the next push would append them again.
    /// </summary>
    public sealed record HeaderAppends(
        IReadOnlyList<string> Headers,
        int FirstColumn,
        IReadOnlyList<string> Problems);

    /// <summary>
    /// The headers a push must append so every site has both a "- timer" and a
    /// "- tekst" column. Existing headers are matched the same way the import
    /// matches them -- ignoring case, whitespace and dash style -- so a
    /// hand-edited header is recognized instead of being duplicated.
    ///
    /// Headers are only ever appended. Nothing is moved, renamed or removed, so
    /// no column parts company with the data under it. A site holding only half
    /// a pair therefore gets a complete new pair at the end and keeps its old
    /// column; the import pairs columns by name, so it goes on reading the old
    /// one until someone moves the data over and deletes it. Problems name that
    /// column so it does not sit there unnoticed.
    /// </summary>
    public static HeaderAppends PlanAppends(IList<object> existingHeaders, IEnumerable<string> siteNames)
    {
        var existing = Map(existingHeaders).Workers.ToDictionary(x => x.Key);
        var headers = new List<string>();
        var problems = new List<string>();
        var planned = new HashSet<string>();

        foreach (var siteName in siteNames)
        {
            var key = NormalizeName(siteName);
            if (key.Length == 0 || !planned.Add(key))
            {
                continue;
            }

            if (existing.TryGetValue(key, out var columns))
            {
                if (columns.HoursColumn != null && columns.TextColumn != null)
                {
                    continue;
                }

                var letter = ColumnLetter(columns.HoursColumn ?? columns.TextColumn!.Value);
                problems.Add(
                    $"\"{siteName}\" had only one of its two columns ({letter}); a complete pair was appended. The import keeps reading column {letter} until its data is moved to the new pair and the column is deleted.");
            }

            headers.Add(TimerHeader(siteName));
            headers.Add(TextHeader(siteName));
        }

        return new HeaderAppends(headers, Math.Max(existingHeaders.Count, FirstWorkerColumn), problems);
    }

    public static string TimerHeader(string siteName) => $"{siteName} - timer";

    public static string TextHeader(string siteName) => $"{siteName} - tekst";

    /// <summary>
    /// The cell value, or empty when the column is absent. The Sheets API drops
    /// trailing empty cells, so rows are often shorter than the header row.
    /// </summary>
    public static string CellAt(IList<object> row, int? col) =>
        col is { } c && c < row.Count ? row[c]?.ToString() ?? string.Empty : string.Empty;

    /// <summary>
    /// A blank cell is 0 hours; null means the cell is not a finite number.
    /// "NaN" and "Infinity" parse as doubles but cannot be stored.
    /// </summary>
    public static double? ParseHours(string cell)
    {
        var text = cell.Trim();
        if (text.Length == 0)
        {
            return 0;
        }

        if (!double.TryParse(text.Replace(",", "."), NumberStyles.AllowDecimalPoint,
                NumberFormatInfo.InvariantInfo, out var hours) || !double.IsFinite(hours))
        {
            return null;
        }

        return hours;
    }

    /// <summary>The sheet's column letter for a 0-based index: 0 is A, 26 is AA.</summary>
    public static string ColumnLetter(int col)
    {
        var letters = string.Empty;
        for (var n = col + 1; n > 0; n = (n - 1) / 26)
        {
            letters = (char)('A' + (n - 1) % 26) + letters;
        }

        return letters;
    }

    private static string NamesNoWorker(int col, string header) =>
        $"column {ColumnLetter(col)} header \"{header}\" names no worker";

    private static string Duplicate(int col, string header, int used) =>
        $"column {ColumnLetter(col)} header \"{header}\" duplicates column {ColumnLetter(used)}, which is used instead";
}
