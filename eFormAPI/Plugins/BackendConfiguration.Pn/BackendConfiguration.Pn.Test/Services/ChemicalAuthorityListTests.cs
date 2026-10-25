using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalReporting;
using Microsoft.Extensions.DependencyInjection;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>"Kemikalieliste til Arbejdstilsynet" (spec §2 decision 19): all active placements with hazards, deadlines and SDS link.</summary>
[TestFixture]
public class ChemicalAuthorityListTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    private static string Text(string key) => key;

    /// <summary>The licence is set once by plugin start-up (AddChemicalReporting), never per render.</summary>
    [SetUp]
    public void StartUp() => new ServiceCollection().AddChemicalReporting();

    private static ChemicalRegisterEntryModel Entry(int id, string name, string sds = "") => new(
        id, name, $"{id}-1", 6, "Produkt afmeldt",
        new DateTime(2026, 3, 1), new DateTime(2027, 3, 1), null, null, null,
        ["GHS07", "GHS09"], 2, "Advarsel",
        [new ChemicalHazardStatementModel("H302", "Farlig ved indtagelse."), new ChemicalHazardStatementModel("H410", "Meget giftig for vandlevende organismer.")],
        [], "Syngenta", [new ChemicalProductModel(id * 10, $"{name} 1 L", "", sds, sds)], Now);

    private static ChemicalPlacementModel Placement(int id, int locationId, int chemicalId, DateTime? removedAt = null, decimal balance = 0, ChemicalStockUnitEnum? unit = null, int propertyId = 1) => new(
        id, locationId, propertyId, chemicalId, null, $"Hylde {id}", 5, "Jens Hansen", Now.AddDays(-30),
        removedAt.HasValue ? 5 : null, removedAt.HasValue ? "Jens Hansen" : null, removedAt, removedAt.HasValue ? ChemicalRemovalReasonEnum.Used : null,
        null, null, balance, unit, Now, null);

    private static ChemicalInventoryModel Inventory(bool stock, IReadOnlyList<ChemicalPlacementModel> placements, IReadOnlyList<ChemicalRegisterEntryModel> entries) => new(
        [new ChemicalPropertyAccessModel(1, "Hovedgården", ChemicalPermissionFlagsModel.All, stock, null)],
        [new ChemicalLocationModel(10, 1, "Værksted", "", null, 2, false, Now), new ChemicalLocationModel(11, 1, "Lade", "", null, 1, false, Now)],
        placements, [], entries, "", true);

    [Test]
    public void Build_ListsOpenPlacementsOnly_InLocationOrder()
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(false,
            [Placement(1, 10, 100), Placement(2, 11, 200), Placement(3, 11, 100, removedAt: Now.AddDays(-1))],
            [Entry(100, "Karate 2,5 WG"), Entry(200, "Hussar OD")]), 1, Now, "https://chemicalbase.example/", Text);

        Assert.That(doc.Rows.Select(r => (r.Location, r.ProductName)), Is.EqualTo(new[] { ("Lade", "Hussar OD"), ("Værksted", "Karate 2,5 WG") }));
        Assert.That(doc.Title, Is.EqualTo("ChemicalAuthorityListTitle"));
        Assert.That(doc.Subtitle, Does.Contain("Hovedgården"));
    }

    [Test]
    public void Build_Subtitle_PrintsTheCopenhagenTime()
    {
        // 2026-09-29 23:30 UTC is already 2026-09-30 01:30 in Copenhagen (CEST).
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(false, [], []), 1,
            new DateTime(2026, 9, 29, 23, 30, 0, DateTimeKind.Utc), "https://x", Text);

        Assert.That(doc.Subtitle, Does.EndWith("ChemicalGeneratedLabel: 30-09-2026 01:30"));
    }

    [Test]
    public void Build_Row_CarriesHazardsDeadlinesAndThePublicSdsLink()
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(false, [Placement(1, 11, 100)], [Entry(100, "Karate 2,5 WG", "ab12 cd")]),
            1, Now, "https://chemicalbase.example/", Text);

        var row = doc.Rows.Single();
        Assert.That(row.Status, Is.EqualTo("Produkt afmeldt"));
        Assert.That(row.SignalWord, Is.EqualTo("Advarsel"));
        Assert.That(row.Pictograms, Is.EqualTo("GHS07, GHS09"));
        Assert.That(row.HazardStatements, Is.EqualTo("H302 Farlig ved indtagelse.\nH410 Meget giftig for vandlevende organismer."));
        Assert.That(row.Deadlines, Is.EqualTo("ChemicalDeadlineSales: 01-03-2026\nChemicalDeadlineUseAndPossession: 01-03-2027"));
        Assert.That(row.Placement, Is.EqualTo("Hylde 1"));
        Assert.That(row.SdsUrl, Is.EqualTo("https://chemicalbase.example/api/chemicals-pn/get-pdf-file?fileName=ab12%20cd"));
    }

    [Test]
    public void Build_QuantityColumn_OnlyWhenStockIsEnabled()
    {
        var placements = new[] { Placement(1, 11, 100, balance: 3.5m, unit: ChemicalStockUnitEnum.Kg) };
        var entries = new[] { Entry(100, "Karate") };

        var withStock = ChemicalAuthorityListBuilder.Build(Inventory(true, placements, entries), 1, Now, "https://x", Text);
        var withoutStock = ChemicalAuthorityListBuilder.Build(Inventory(false, placements, entries), 1, Now, "https://x", Text);

        Assert.That(withStock.Rows.Single().Quantity, Is.EqualTo("3.5 kg"));
        Assert.That(withStock.Headers, Does.Contain("ChemicalColumnQuantity"));
        Assert.That(withoutStock.Headers, Does.Not.Contain("ChemicalColumnQuantity"));
        Assert.That(withoutStock.Rows.Single().Quantity, Is.Empty);
    }

    [Test]
    public void Build_QuantityKeepsThreeDecimals()
    {
        var doc = ChemicalAuthorityListBuilder.Build(
            Inventory(true, [Placement(1, 11, 100, balance: 0.004m, unit: ChemicalStockUnitEnum.Kg), Placement(2, 11, 100, balance: 0.875m, unit: ChemicalStockUnitEnum.L)], [Entry(100, "Karate")]),
            1, Now, "https://x", Text);

        Assert.That(doc.Rows.Select(r => r.Quantity), Is.EquivalentTo(new[] { "0.004 kg", "0.875 L" }));
    }

    [Test]
    public void Build_ListsOnlyThePlacementsOfTheRequestedProperty()
    {
        var doc = ChemicalAuthorityListBuilder.Build(
            Inventory(false, [Placement(1, 11, 100), Placement(2, 11, 200, propertyId: 2)], [Entry(100, "Karate"), Entry(200, "Hussar OD")]),
            1, Now, "https://x", Text);

        Assert.That(doc.Rows.Select(r => r.ProductName), Is.EqualTo(new[] { "Karate" }));
    }

    [Test]
    public void Build_ChemicalMissingFromRegister_KeepsTheRow()
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(false, [Placement(1, 11, 999)], []), 1, Now, "https://x", Text);

        var row = doc.Rows.Single();
        Assert.That(row.ProductName, Is.EqualTo("#999"));
        Assert.That(row.SdsUrl, Is.Empty);
        Assert.That(row.HazardStatements, Is.Empty);
    }

    [Test]
    public void Build_Row_CarriesThePictogramCodesForTheDiamonds()
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(false, [Placement(1, 11, 100)], [Entry(100, "Karate")]), 1, Now, "https://x", Text);

        Assert.That(doc.Rows.Single().PictogramCodes, Is.EqualTo(new[] { "GHS07", "GHS09" }));
        Assert.That(doc.PictogramColumn, Is.EqualTo(doc.Headers.ToList().IndexOf("ChemicalColumnPictograms")));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Build_GivesEveryColumnAWidth(bool stock)
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(stock, [], []), 1, Now, "https://x", Text);

        Assert.That(doc.ColumnWeights, Has.Count.EqualTo(doc.Headers.Count));
        Assert.That(doc.ColumnWeights, Has.All.GreaterThan(0f));
    }

    /// <summary>The Danish column headers, as the plugin's localization.json prints them.</summary>
    private static readonly Dictionary<string, string> Danish = new()
    {
        ["ChemicalColumnProduct"] = "Produkt", ["ChemicalColumnRegistrationNo"] = "Reg.nr.", ["ChemicalColumnStatus"] = "Status",
        ["ChemicalColumnSignalWord"] = "Signalord", ["ChemicalColumnPictograms"] = "Farepiktogrammer",
        ["ChemicalColumnHazardStatements"] = "Faresætninger", ["ChemicalColumnLocation"] = "Lokation",
        ["ChemicalColumnPlacement"] = "Placering", ["ChemicalColumnDeadlines"] = "Frister", ["ChemicalColumnQuantity"] = "Mængde",
        ["ChemicalColumnSds"] = "Sikkerhedsdatablad",
    };

    /// <summary>
    /// The 855 check found "Farepiktogramme|r" and "Sikkerhedsdatabla|d": a header word wider than its column is cut.
    /// Every Danish header word must fit its column, measured with a generous 0.6 em per bold character.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void Build_EveryDanishHeaderWordFitsItsColumn(bool stock)
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(stock, [], []), 1, Now, "https://x",
            key => Danish.TryGetValue(key, out var da) ? da : key);
        Assert.That(doc.ColumnWeights, Has.Count.EqualTo(doc.Headers.Count));
        var total = doc.ColumnWeights.Sum();

        for (var i = 0; i < doc.Headers.Count; i++)
        {
            var available = doc.ColumnWeights[i] / total * ChemicalAuthorityListPdf.ContentWidthPoints - 2 * ChemicalAuthorityListPdf.CellPadding;
            var longestWord = doc.Headers[i].Split(' ').Max(w => w.Length);
            Assert.That(available, Is.GreaterThanOrEqualTo(longestWord * 0.6f * ChemicalAuthorityListPdf.HeaderFontSize),
                $"\"{doc.Headers[i]}\" does not fit its column");
        }
    }

    /// <summary>A known code is drawn as its diamond (vector artwork), not printed as its code.</summary>
    [Test]
    public void Render_DrawsTheDiamondOfAKnownPictogram() =>
        // GHS01 (exploding bomb) is 17 kB of paths; printed as text it would be five characters.
        Assert.That(PdfWithPictograms("GHS01").Length, Is.GreaterThan(PdfWithPictograms("GHS99").Length + 2000));

    /// <summary>A code without artwork is printed as its code: more than an empty cell, far less than a diamond.</summary>
    [Test]
    public void Render_KeepsAnUnknownPictogramAsText()
    {
        var unknown = PdfWithPictograms("GHS99").Length;

        Assert.That(unknown, Is.GreaterThan(PdfWithPictograms().Length), "the code is printed");
        Assert.That(unknown, Is.LessThan(PdfWithPictograms("GHS01").Length - 2000), "no artwork is drawn for it");
    }

    private static byte[] PdfWithPictograms(params string[] pictograms) => ChemicalAuthorityListPdf.Render(ChemicalAuthorityListBuilder.Build(
        Inventory(false, [Placement(1, 11, 100)], [Entry(100, "Karate") with { HazardPictograms = pictograms }]), 1, Now, "https://x", Text));

    /// <summary>
    /// The SDS column prints only a link, so a paper copy must still say where the safety data sheets are: one line
    /// under the subtitle.
    /// </summary>
    [Test]
    public void Build_SaysWhereTheSafetyDataSheetsAre()
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(false, [], []), 1, Now, "https://x", Text);

        Assert.That(doc.SdsNote, Is.EqualTo("ChemicalSdsNote"));
    }

    [Test]
    public void Render_PrintsTheSdsNote()
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(false, [], []), 1, Now, "https://x", Text);

        Assert.That(ChemicalAuthorityListPdf.Render(doc with { SdsNote = new string('x', 400) }).Length,
            Is.GreaterThan(ChemicalAuthorityListPdf.Render(doc with { SdsNote = "" }).Length));
    }

    [Test]
    public void Render_ProducesAMultiPagePdfWithDanishText()
    {
        var placements = Enumerable.Range(1, 60).Select(i => Placement(i, 11, 100)).ToList();
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(true, placements, [Entry(100, "Æblegårdens Hussar ØÅ")]), 1, Now, "https://x", Text);

        var pdf = ChemicalAuthorityListPdf.Render(doc);

        Assert.That(Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
        Assert.That(PageCount(pdf), Is.GreaterThan(1), "60 rows of hazard text cannot fit on one landscape A4 page");
    }

    [Test]
    public void Render_EmptyListIsOnePage() =>
        Assert.That(PageCount(ChemicalAuthorityListPdf.Render(ChemicalAuthorityListBuilder.Build(Inventory(false, [], []), 1, Now, "https://x", Text))), Is.EqualTo(1));

    /// <summary>Skia writes page dictionaries as plain objects: "/Type /Page" (the page tree is "/Type /Pages").</summary>
    private static int PageCount(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![A-Za-z])").Count;

    [Test]
    public void Render_EmptyList_IsStillAPdf()
    {
        var doc = ChemicalAuthorityListBuilder.Build(Inventory(false, [], []), 1, Now, "https://x", Text);

        Assert.That(Encoding.ASCII.GetString(ChemicalAuthorityListPdf.Render(doc), 0, 5), Is.EqualTo("%PDF-"));
    }
}
