using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BackendConfiguration.Pn.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Test;

/// <summary>
/// Covers the PlanTimer sheet header mapping PushToGoogleSheet plans its
/// appends from. Every header row starts with the three non-worker columns the
/// import skips, so the first worker column is index 3 (column D).
/// </summary>
[TestFixture]
public class PlanTimerSheetColumnsTests
{
    private static List<object> Headers(params string[] workerHeaders) =>
        new object[] { "Dato", "Uge", "Ugedag" }.Concat(workerHeaders).ToList();

    private static void AssertColumns(PlanTimerSheetColumns.Layout layout, string key, int? hours, int? text)
    {
        var worker = layout.Workers.Single(x => x.Key == key);
        Assert.That((worker.HoursColumn, worker.TextColumn), Is.EqualTo((hours, text)), key);
    }

    [Test]
    public void Map_CanonicalPairs_MapsHoursThenText()
    {
        var layout = PlanTimerSheetColumns.Map(Headers(
            "Albert Doba - timer", "Albert Doba - tekst",
            "Phien Van Le - timer", "Phien Van Le - tekst"));

        Assert.That(layout.Problems, Is.Empty);
        Assert.That(layout.Workers, Is.EqualTo(new[]
        {
            new PlanTimerSheetColumns.WorkerColumns("albertdoba", "Albert Doba", 3, 4),
            new PlanTimerSheetColumns.WorkerColumns("phienvanle", "Phien Van Le", 5, 6)
        }));
    }

    /// <summary>
    /// Tenant 1063: two workers' columns had been inserted between another
    /// worker's timer and tekst columns, so the old fixed stride read a
    /// neighbouring header as the site name and dropped those workers.
    /// </summary>
    [Test]
    public void Map_PairsSplitByInsertedColumns_StillPairsEachWorkerByName()
    {
        var layout = PlanTimerSheetColumns.Map(Headers(
            "Malaika Luna Jørgensen - timer",
            "Valentin - - timer", "Valentin - - tekst",
            "Phien Van Le - timer", "Phien Van Le - tekst",
            "Malaika Luna Jørgensen - tekst",
            "Serhii Vashchuk - timer", "Serhii Vashchuk - tekst"));

        Assert.That(layout.Problems, Is.Empty);
        AssertColumns(layout, "malaikalunajørgensen", 3, 8);
        AssertColumns(layout, "valentin", 4, 5);
        AssertColumns(layout, "phienvanle", 6, 7);
        AssertColumns(layout, "serhiivashchuk", 9, 10);
    }

    [Test]
    public void Map_SuffixCaseSpacingAndDashes_AreIgnored()
    {
        var layout = PlanTimerSheetColumns.Map(Headers(
            "Phien Van Le -Timer", "Phien Van Le  -  TEKST ",
            "Ngoan Tran – timer", "Ngoan Tran—tekst"));

        Assert.That(layout.Problems, Is.Empty);
        AssertColumns(layout, "phienvanle", 3, 4);
        AssertColumns(layout, "ngoantran", 5, 6);
    }

    [Test]
    public void Map_LegacyHeaderWithoutSuffix_ReadsTheNextColumnAsText()
    {
        var layout = PlanTimerSheetColumns.Map(Headers(
            "Albert Doba", "", "Svend Gammelgård", "Tekst", "Ngoan Tran - timer", "Ngoan Tran - tekst"));

        Assert.That(layout.Problems, Is.Empty);
        Assert.That(layout.Workers.Select(x => x.Key),
            Is.EqualTo(new[] { "albertdoba", "svendgammelgård", "ngoantran" }));
        AssertColumns(layout, "albertdoba", 3, 4);
        AssertColumns(layout, "svendgammelgård", 5, 6);
        AssertColumns(layout, "ngoantran", 7, 8);
    }

    [Test]
    public void Map_LegacyHeaderLastInRow_UsesTheColumnTheApiTrimmedForText()
    {
        AssertColumns(PlanTimerSheetColumns.Map(Headers("Albert Doba")), "albertdoba", 3, 4);
    }

    [Test]
    public void Map_LegacyHeaderWithSuffixedText_PairsByName()
    {
        var layout = PlanTimerSheetColumns.Map(Headers("Albert Doba", "Albert Doba - tekst"));

        Assert.That(layout.Problems, Is.Empty);
        AssertColumns(layout, "albertdoba", 3, 4);
    }

    [Test]
    public void Map_TextOnlyWorker_IsKept()
    {
        var layout = PlanTimerSheetColumns.Map(Headers("Phien Van Le - tekst"));

        Assert.That(layout.Problems, Is.Empty);
        AssertColumns(layout, "phienvanle", null, 3);
    }

    [Test]
    public void Map_HoursOnlyWorker_IsKept()
    {
        var layout = PlanTimerSheetColumns.Map(Headers("Phien Van Le - timer", "Ngoan Tran - timer"));

        Assert.That(layout.Problems, Is.Empty);
        AssertColumns(layout, "phienvanle", 3, null);
        AssertColumns(layout, "ngoantran", 4, null);
    }

    [Test]
    public void Map_DuplicateHeader_KeepsTheFirstAndNamesBothColumns()
    {
        var layout = PlanTimerSheetColumns.Map(Headers(
            "Phien Van Le - timer", "Phien Van Le - tekst", "Phien Van Le - timer"));

        AssertColumns(layout, "phienvanle", 3, 4);
        Assert.That(layout.Problems, Is.EqualTo(new[]
        {
            "column F header \"Phien Van Le - timer\" duplicates column D, which is used instead"
        }));
    }

    [Test]
    public void Map_BlankAndNamelessHeaders_AreNotWorkers()
    {
        var layout = PlanTimerSheetColumns.Map(Headers("", "   ", " - timer", "-"));

        Assert.That(layout.Workers, Is.Empty);
        Assert.That(layout.Problems, Is.EqualTo(new[]
        {
            "column F header \"- timer\" names no worker",
            "column G header \"-\" names no worker"
        }));
    }

    [Test]
    public void PlanAppends_SiteWithBothColumns_AppendsNothing()
    {
        var appends = PlanTimerSheetColumns.PlanAppends(
            Headers("Albert Doba - timer", "Albert Doba - tekst"),
            ["Albert Doba"]);

        Assert.That(appends.Headers, Is.Empty);
        Assert.That(appends.Problems, Is.Empty);
    }

    [Test]
    public void PlanAppends_NewSite_AppendsThePairInOrder()
    {
        var appends = PlanTimerSheetColumns.PlanAppends(
            Headers("Albert Doba - timer", "Albert Doba - tekst"),
            ["Albert Doba", "Phien Van Le"]);

        Assert.That(appends.Headers, Is.EqualTo(new[] { "Phien Van Le - timer", "Phien Van Le - tekst" }));
        Assert.That(appends.Problems, Is.Empty);
    }

    /// <summary>
    /// A header retyped with other spacing, capitals or a different dash used to
    /// be treated as absent, so every push appended another column for the same
    /// worker.
    /// </summary>
    [TestCase("phien  van le  -  TIMER")]
    [TestCase("Phien Van Le – timer")]
    public void PlanAppends_HeaderVariantOfTheSameName_IsNotDuplicated(string timerHeader)
    {
        var appends = PlanTimerSheetColumns.PlanAppends(
            Headers(timerHeader, "Phien Van Le - tekst"),
            ["Phien Van Le"]);

        Assert.That(appends.Headers, Is.Empty);
    }

    /// <summary>
    /// Tenant 1063's Malaika: her "- tekst" column had gone missing, and the
    /// half appended on its own landed after two other workers' pairs.
    /// </summary>
    [Test]
    public void PlanAppends_SiteWithHalfAPair_AppendsAWholePairAndNamesTheOldColumn()
    {
        var appends = PlanTimerSheetColumns.PlanAppends(
            Headers("Malaika Luna Jørgensen - timer", "Phien Van Le - timer", "Phien Van Le - tekst"),
            ["Malaika Luna Jørgensen", "Phien Van Le"]);

        Assert.That(appends.Headers,
            Is.EqualTo(new[] { "Malaika Luna Jørgensen - timer", "Malaika Luna Jørgensen - tekst" }));
        Assert.That(appends.Problems.Single(),
            Does.Contain("Malaika Luna Jørgensen").And.Contain("only one of its two columns (D)")
                .And.Contain("column D"));
    }

    [Test]
    public void PlanAppends_EmptySheet_AppendsEveryPairOnceEvenIfASiteRepeats()
    {
        var appends = PlanTimerSheetColumns.PlanAppends(
            new List<object>(),
            ["Julius -", "julius  -", ""]);

        Assert.That(appends.Headers, Is.EqualTo(new[] { "Julius - - timer", "Julius - - tekst" }));
        Assert.That(appends.Problems, Is.Empty);
    }

    /// <summary>
    /// Headers written into A/B/C would sit in the date columns the import
    /// skips, so the next push would not see them and would append them again.
    /// </summary>
    [Test]
    public void PlanAppends_ShortOrEmptyHeaderRow_StartsAtTheFirstWorkerColumn()
    {
        Assert.That(PlanTimerSheetColumns.PlanAppends(new List<object>(), ["Albert Doba"]).FirstColumn,
            Is.EqualTo(PlanTimerSheetColumns.FirstWorkerColumn));
        Assert.That(PlanTimerSheetColumns.PlanAppends(new List<object> { "Dato" }, ["Albert Doba"]).FirstColumn,
            Is.EqualTo(PlanTimerSheetColumns.FirstWorkerColumn));
    }

    [Test]
    public void PlanAppends_PopulatedHeaderRow_StartsAfterTheLastHeader()
    {
        var appends = PlanTimerSheetColumns.PlanAppends(
            Headers("Albert Doba - timer", "Albert Doba - tekst"),
            ["Albert Doba", "Phien Van Le"]);

        Assert.That(appends.FirstColumn, Is.EqualTo(5));
        Assert.That(PlanTimerSheetColumns.ColumnLetter(appends.FirstColumn), Is.EqualTo("F"));
    }

    /// <summary>
    /// The site lookup normalizes both sides with this, so a site named
    /// "Julius -" matches its "Julius - - timer" header. The old import
    /// stripped hyphens from the site name only, so those never matched.
    /// </summary>
    [TestCase("Phien Van Le", "phienvanle")]
    [TestCase("  phien  VAN le ", "phienvanle")]
    [TestCase("Julius -", "julius")]
    [TestCase("Phien Van\tLe", "phienvanle")]
    [TestCase("Anne–Marie", "annemarie")]
    [TestCase(null, "")]
    public void NormalizeName_StripsWhitespaceAndDashesAndLowerCases(string name, string expected)
    {
        Assert.That(PlanTimerSheetColumns.NormalizeName(name), Is.EqualTo(expected));
    }

    [TestCase("", 0.0)]
    [TestCase("   ", 0.0)]
    [TestCase("7,5", 7.5)]
    [TestCase(" 7.5 ", 7.5)]
    [TestCase("8", 8.0)]
    [TestCase("abc", null)]
    [TestCase("-1", null)]
    [TestCase("NaN", null)]
    [TestCase("Infinity", null)]
    public void ParseHours_BlankIsZeroAndAnythingButAFiniteNumberIsNull(string cell, double? expected)
    {
        Assert.That(PlanTimerSheetColumns.ParseHours(cell), Is.EqualTo(expected));
    }

    [TestCase(0, "A")]
    [TestCase(3, "D")]
    [TestCase(25, "Z")]
    [TestCase(26, "AA")]
    [TestCase(701, "ZZ")]
    [TestCase(702, "AAA")]
    public void ColumnLetter_MatchesTheSheetsColumnNames(int col, string expected)
    {
        Assert.That(PlanTimerSheetColumns.ColumnLetter(col), Is.EqualTo(expected));
    }

    [Test]
    public void CellAt_ColumnBeyondTheRowOrNull_IsEmpty()
    {
        var row = new List<object> { "15.09.2026", "7,5" };

        Assert.That(PlanTimerSheetColumns.CellAt(row, 1), Is.EqualTo("7,5"));
        Assert.That(PlanTimerSheetColumns.CellAt(row, 5), Is.Empty);
        Assert.That(PlanTimerSheetColumns.CellAt(row, null), Is.Empty);
        Assert.That(PlanTimerSheetColumns.CellAt(new List<object>(), 0), Is.Empty);
    }
}
