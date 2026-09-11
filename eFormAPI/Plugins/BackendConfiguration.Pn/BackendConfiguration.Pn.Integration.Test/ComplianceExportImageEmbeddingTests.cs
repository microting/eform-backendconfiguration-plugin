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

using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.WordService;
using DocumentFormat.OpenXml.Packaging;
using System.Text;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Dto;
// Aliased for the same reason the writer aliases them: Wordprocessing defines
// Settings, which collides with Microting.eForm.Dto.Settings (the SDK picture
// settings this fixture writes), and the three drawing namespaces each define
// their own Extent/Extents.
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// The one thing <see cref="ComplianceExportWriterTests"/> cannot assert: that a
/// PHOTOGRAPH actually lands inside the exported <c>.docx</c>.
///
/// <para>
/// <b>Why a second fixture.</b> Every Word test in
/// <c>ComplianceExportWriterTests</c> calls
/// <c>WriteAsync(document, core: null)</c>, and that fixture is deliberately
/// database-free — no container, no SDK. But
/// <see cref="ComplianceExportWordWriter.WriteAsync"/> reads the two SDK settings
/// that decide where image bytes come from (<c>s3Enabled</c> and
/// <c>fileLocationPicture</c>) only when <c>core != null</c>, so with a null Core
/// its <c>InsertImage</c> returns at the <c>string.IsNullOrWhiteSpace(basePicturePath)</c>
/// guard and every appendix <c>&lt;td&gt;</c> is emitted EMPTY. Those tests
/// therefore pin the appendix's LAYOUT and can never pin its CONTENT — the
/// appendix could render blank on every customer PDF and all of them would stay
/// green. Moving these two tests into that fixture would make the whole of it
/// container-dependent, which is exactly what its own doc comment refuses; hence
/// a <see cref="TestBaseSetup"/> fixture of its own, for the two tests that need
/// a real <c>Core</c>.
/// </para>
///
/// <para>
/// <b>Boundary.</b> The docx, and nothing past it. No S3 (CI starts no object
/// storage, so the <c>s3Enabled</c> arm is out of reach and is explicitly turned
/// OFF here rather than left to whatever the seed dump holds) and no
/// <c>soffice</c> (not on the CI image — asserting PDF bytes would be asserting
/// the environment). The image itself is generated in-test with ImageMagick, the
/// same library the writer encodes with, so no binary asset is checked in.
/// </para>
///
/// <para>
/// <b>Not covered.</b> That the embedded picture LOOKS like the original — the
/// assertions go as far as "an <see cref="ImagePart"/> exists, the
/// <c>a:blip</c> in the grid cell points at it, and its bytes decode to a
/// 300 px image"; nothing here compares pixels, so a correctly-sized but
/// scrambled photograph would pass. Nor the S3 arm, nor the PDF arm.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceExportImageEmbeddingTests : TestBaseSetup
{
    private const string Period = "01.01.2026 – 31.03.2026";

    /// <summary>The caption <c>ComplianceExportDocumentBuilder</c> derives for the single case below.</summary>
    private const string CaseCaption = "Sag 2185 · El-tavle · 20.05.2026";

    /// <summary>
    /// EMU per inch and the CSS reference DPI. HtmlToOpenXml converts a pixel
    /// dimension as <c>value / 96 * 914400</c> (<c>Unit.ComputeInEmus</c>,
    /// <c>UnitMetric.Pixel</c>), which is what turns the writer's
    /// <see cref="ComplianceExportWordWriter.AppendixImageWidthPx"/> into the
    /// <c>wp:extent/@cx</c> asserted below. Spelled out rather than hard-coded so
    /// the number is derived from the production constant, not from a run.
    /// </summary>
    private const long EmusPerInch = 914400L;

    private const int CssPixelsPerInch = 96;

    /// <summary>The picture directory handed to the SDK as <c>fileLocationPicture</c>.</summary>
    private string? _pictureDirectory;

    /// <summary>
    /// Named so it does not hide <see cref="TestBaseSetup.TearDown"/>; NUnit runs
    /// both. The directory is per-test, so a failed test cannot leak its file into
    /// the next one.
    /// </summary>
    [TearDown]
    public void CleanUpPictureDirectory()
    {
        if (_pictureDirectory == null) return;

        try
        {
            if (Directory.Exists(_pictureDirectory)) Directory.Delete(_pictureDirectory, true);
        }
        catch (IOException)
        {
            // A leaked temp directory must not fail a green test.
        }
        finally
        {
            _pictureDirectory = null;
        }
    }

    /// <summary>
    /// A fresh, per-test picture directory, registered for
    /// <see cref="CleanUpPictureDirectory"/>. Split out from
    /// <see cref="ConfigureLocalPictureStore"/> because
    /// <c>ComplianceExportWordWriter.InsertImage</c> takes the base picture path as
    /// an ARGUMENT — only <c>WriteAsync</c> reads it back out of the SDK — so a test
    /// that calls <c>InsertImage</c> directly needs the directory without the SDK
    /// settings, and therefore without a <c>Core</c>.
    /// </summary>
    private string CreatePictureDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"compliance-export-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _pictureDirectory = directory;
        return directory;
    }

    /// <summary>
    /// The picture store the writer will read from: a fresh temp directory, wired
    /// into the SDK as <c>fileLocationPicture</c>, with S3 explicitly off.
    /// </summary>
    private async Task<string> ConfigureLocalPictureStore(eFormCore.Core core)
    {
        var directory = CreatePictureDirectory();

        await core.SetSdkSetting(Settings.s3Enabled, "false");
        await core.SetSdkSetting(Settings.fileLocationPicture, directory);

        // The writer reads them back through Core.GetSdkSetting, which swallows a
        // read failure into "N/A"; assert the premise rather than debug an empty
        // grid cell later.
        Assert.That(await core.GetSdkSetting(Settings.s3Enabled), Is.EqualTo("false"),
            "premise: the local-file arm is selected");
        Assert.That(await core.GetSdkSetting(Settings.fileLocationPicture), Is.EqualTo(directory),
            "premise: the writer will read from the temp picture store");

        return directory;
    }

    /// <summary>
    /// A real, tiny JPEG — 40x30 solid red — written with the same library the
    /// writer decodes and re-encodes with. Deliberately NOT 300 px wide: the
    /// writer resizes to <see cref="ComplianceExportWordWriter.AppendixImageWidthPx"/>,
    /// and a source that already had that width would let a broken resize pass.
    /// </summary>
    private static void WriteSampleJpeg(string path) => WriteSampleImage(path);

    /// <summary>
    /// The same 40x30 solid red, in whatever format <paramref name="path"/>'s
    /// extension names — ImageMagick picks the coder from it, which is exactly how
    /// the picture store comes by its own <c>.jpg</c>/<c>.png</c> mix. Used by the
    /// content-type test, which needs a source that is genuinely NOT a PNG.
    /// </summary>
    private static void WriteSampleImage(string path)
    {
        using var image = new MagickImage(MagickColors.Red, 40, 30);
        image.Write(path);
    }

    /// <summary>
    /// An HTML body fragment through the writer's OWN conversion path — the
    /// embedded <c>file.docx</c> shell and <see cref="WordProcessor"/>, i.e.
    /// HtmlToOpenXml at the version the plugin ships — into a readable package.
    ///
    /// <para>
    /// Only the width test needs this. Every other assertion here goes through
    /// <see cref="ComplianceExportWordWriter.WriteAsync"/>, but <c>WriteAsync</c>
    /// cannot express "resize the bytes to one width and declare another": it
    /// passes <see cref="ComplianceExportWordWriter.AppendixImageWidthPx"/> for
    /// both. The page shell (#1189) is deliberately not applied — the drawing's
    /// <c>wp:extent</c> is computed from the image alone, with no reference to the
    /// page or its margins, so landscape would change nothing here.
    /// </para>
    /// </summary>
    private static MemoryStream ConvertBodyFragment(string bodyFragment)
    {
        using var template = typeof(ComplianceExportWordWriter).Assembly
                                 .GetManifestResourceStream(ComplianceExportWordWriter.DocxResource)
                             ?? throw new InvalidOperationException(
                                 $"Embedded resource {ComplianceExportWordWriter.DocxResource} is missing");

        // Not disposed: it is the return value, and WordProcessor.Dispose saves the
        // package into it without closing it — the same contract WriteAsync relies on.
        var docxStream = new MemoryStream();
        template.CopyTo(docxStream);
        docxStream.Position = 0;

        var word = new WordProcessor(docxStream);
        word.AddHtml($"<body>{bodyFragment}</body>");
        word.Dispose();

        docxStream.Position = 0;
        return docxStream;
    }

    /// <summary>
    /// The <c>ReportDocument()</c> shape of <see cref="ComplianceExportWriterTests"/>,
    /// cut down to ONE group, ONE case and ONE image, so "exactly one image part"
    /// is an exact count rather than an at-least. Built through the REAL
    /// <see cref="ComplianceExportDocumentBuilder.BuildReport"/> — the appendix
    /// block, its caption and its image-name list are the builder's, not a
    /// hand-made document that happens to match.
    /// </summary>
    private static ComplianceExportDocument BuildReportWithOneImage(string imageFileName)
    {
        var group = new ComplianceReportHeadlineGroupModel
        {
            HeadlineTagId = 8,
            HeadlineName = "Elinstallationer og eftersyn",
            TagsCaption = "Miljøtilsyn - EL",
            CheckListIds = [511],
            Columns =
            [
                new ComplianceReportColumnModel { Key = "f11", Label = "Bemærkning" }
            ],
            Cases =
            [
                new ComplianceReportCaseModel
                {
                    SdkCaseId = 2185, PropertyName = "Ejendom 9", Title = "El-tavle",
                    Tags = ["Miljøtilsyn", "EL"], WorkerNames = ["Bo"],
                    DoneAt = new DateTime(2026, 5, 20, 8, 0, 0), TaskDate = "2026-05-20",
                    ImagesCount = 1,
                    Images = [new ComplianceReportImageModel { FileName = imageFileName }],
                    Cells = new Dictionary<string, string> { ["f11"] = "Fint" }
                }
            ]
        };

        return ComplianceExportDocumentBuilder.BuildReport(
            [group], Period, includeImageAppendix: true, new AppendixLocalizer());
    }

    private static ComplianceExportWordWriter NewWordWriter() =>
        new(new AppendixLocalizer(), TestContextLogger.Instance);

    /// <summary>
    /// The appendix grid is the LAST table in the body: the writer emits every
    /// section table first, then the appendix. With a single group there are
    /// exactly two.
    /// </summary>
    private static W.Table AppendixGrid(W.Body body)
    {
        var tables = body.Descendants<W.Table>().ToList();
        Assert.That(tables, Has.Count.EqualTo(2), "one section table, then the appendix grid");
        return tables[1];
    }

    // ==================================================================
    // Test 1 — the photograph is inside the document
    // ==================================================================

    /// <summary>
    /// A photograph in the picture directory ends up EMBEDDED in the docx, in the
    /// appendix grid cell that captions it.
    ///
    /// <para>
    /// Each assertion closes a different way the appendix could ship blank:
    /// <list type="number">
    /// <item>the package holds exactly one <see cref="ImagePart"/> — the bytes
    /// travelled from the picture directory into the file at all (the template
    /// <c>file.docx</c> carries no image part of its own, so the count is the
    /// photograph's). <b>This is the one that fails if the appendix ever stops
    /// embedding.</b></item>
    /// <item>the <c>a:blip/@r:embed</c> inside the grid's FIRST CELL resolves to
    /// that part's relationship id — the picture is in the cell under the case
    /// caption, not floating somewhere else in the document, and the cell is not
    /// the <c>&amp;nbsp;</c> placeholder the writer falls back to;</item>
    /// <item>the drawing's <c>wp:extent/@cx</c> is
    /// <see cref="ComplianceExportWordWriter.AppendixImageWidthPx"/> converted to
    /// EMU, and the embedded bytes DECODE to that width — the resize in
    /// <c>InsertImage</c> actually ran, rather than a 40 px source being embedded
    /// at its own size.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Test]
    public async Task Word_AppendixEmbedsThePhotographInItsGridCell()
    {
        var core = await GetCore();
        var pictureDirectory = await ConfigureLocalPictureStore(core);

        const string imageFileName = "4_700_a.jpg";
        WriteSampleJpeg(Path.Combine(pictureDirectory, imageFileName));

        var document = BuildReportWithOneImage(imageFileName);
        Assert.That(document.Tables.Single().ImageBlocks.Single().ImageNames,
            Is.EqualTo(new[] { imageFileName }),
            "premise: the builder put exactly this one file name in the appendix block");

        await using var stream = await NewWordWriter().WriteAsync(document, core);
        using var word = WordprocessingDocument.Open(stream, false);

        var mainPart = word.MainDocumentPart!;
        var body = mainPart.Document!.Body!;

        // (1) The bytes are in the package.
        var imageParts = mainPart.ImageParts.ToList();
        Assert.That(imageParts, Has.Count.EqualTo(1),
            "expected exactly one image part: the photograph. This assumes the embedded "
            + "file.docx template ships no image part of its own — if the template ever "
            + "gains a body image, revisit this count before concluding the appendix grid "
            + "is empty");

        // (2) ...and they are in the grid cell, not merely in the package.
        var grid = AppendixGrid(body);
        var rows = grid.Elements<W.TableRow>().ToList();
        Assert.That(rows, Has.Count.EqualTo(1), "one image, so one grid row");

        var cells = rows[0].Elements<W.TableCell>().ToList();
        Assert.That(cells, Has.Count.EqualTo(ComplianceExportWordWriter.AppendixImagesPerRow),
            "the grid row always carries a full set of cells");

        var drawings = cells[0].Descendants<W.Drawing>().ToList();
        Assert.That(drawings, Has.Count.EqualTo(1),
            "the photograph is not in the first grid cell");
        Assert.That(body.Descendants<W.Drawing>().Count(), Is.EqualTo(1),
            "the only drawing in the document is the appendix photograph");
        Assert.That(cells[1].Descendants<W.Drawing>(), Is.Empty,
            "the odd cell of the row stays empty");

        var blip = drawings[0].Descendants<A.Blip>().Single();
        Assert.That(blip.Embed?.Value, Is.EqualTo(mainPart.GetIdOfPart(imageParts[0])),
            "the grid cell's picture does not resolve to the embedded image part");

        // (3) ...laid out at the appendix width.
        // This assertion still cannot discriminate WHERE that width came from:
        // the production call site passes AppendixImageWidthPx as both the resize
        // width and the layout width, so the bytes and the declaration agree and
        // the cx would match whichever one the converter used. Since #1219 the
        // declaration is the one that applies, and
        // Word_ImageIsLaidOutAtTheDeclaredWidthNotTheDecodedOne below is the test
        // that proves it, by making the two widths differ. What this one
        // establishes is that the picture is laid out at the appendix width
        // rather than at some other size — the load-bearing "the resize actually
        // ran" assertion is the decoded-width one below.
        var expectedCx = ComplianceExportWordWriter.AppendixImageWidthPx * EmusPerInch / CssPixelsPerInch;
        var extent = drawings[0].Descendants<DW.Extent>().Single();
        Assert.That(extent.Cx?.Value, Is.EqualTo(expectedCx),
            $"the appendix image is not laid out at {ComplianceExportWordWriter.AppendixImageWidthPx}px");

        // ...and the BYTES themselves were resized, not merely laid out small.
        // The source is 40x30, so an unresized embed would decode to 40 px here.
        // This is also the only assertion that proves the part holds a decodable
        // image rather than an arbitrary blob.
        using (var embeddedBytes = imageParts[0].GetStream())
        {
            using var embedded = new MagickImage(embeddedBytes);
            Assert.That(embedded.Width, Is.EqualTo((uint)ComplianceExportWordWriter.AppendixImageWidthPx),
                "the embedded bytes were not resized to the appendix width");
            // Derived from the constant rather than hardcoded, so the assertion
            // follows AppendixImageWidthPx if it ever changes. The source is
            // 40x30 (4:3), so height = width * 3 / 4 — exact for any multiple
            // of 4, which every sane appendix width is.
            Assert.That(embedded.Height,
                Is.EqualTo((uint)(ComplianceExportWordWriter.AppendixImageWidthPx * 3 / 4)),
                $"the 40x30 source resized to {ComplianceExportWordWriter.AppendixImageWidthPx} "
                + "wide must keep its 4:3 ratio");
        }

        // The caption the picture belongs to is still the builder's.
        Assert.That(body.Descendants<W.Paragraph>().Select(p => p.InnerText.Trim()),
            Has.Some.EqualTo(CaseCaption));
    }

    // ==================================================================
    // Test 2 — a broken photograph does not fail the report
    // ==================================================================

    /// <summary>
    /// A photograph the picture store does not have must cost that ONE picture and
    /// nothing else: the docx still renders, the grid keeps its row and its cells,
    /// and the case is still captioned. "One broken photograph must not fail a
    /// 200-page report" is stated only in a comment on
    /// <c>ComplianceExportWordWriter.InsertImage</c>; this pins it.
    ///
    /// <para>
    /// The picture directory EXISTS and is configured — this is the missing-file
    /// arm (<c>!File.Exists</c>), not the no-picture-directory arm the
    /// null-<c>Core</c> tests already walk, which would prove nothing about a
    /// broken photograph.
    /// </para>
    /// </summary>
    [Test]
    public async Task Word_MissingPhotographLeavesTheReportIntact()
    {
        var core = await GetCore();
        var pictureDirectory = await ConfigureLocalPictureStore(core);

        const string imageFileName = "4_700_missing.jpg";
        Assert.That(File.Exists(Path.Combine(pictureDirectory, imageFileName)), Is.False,
            "premise: the photograph is not in the picture store");

        var document = BuildReportWithOneImage(imageFileName);

        await using var stream = await NewWordWriter().WriteAsync(document, core);
        using var word = WordprocessingDocument.Open(stream, false);

        var mainPart = word.MainDocumentPart!;
        var body = mainPart.Document!.Body!;

        Assert.That(mainPart.ImageParts, Is.Empty, "nothing should have been embedded");
        Assert.That(body.Descendants<W.Drawing>(), Is.Empty);

        // The grid survives whole — the missing photograph costs a blank cell, not
        // a broken row.
        var grid = AppendixGrid(body);
        var rows = grid.Elements<W.TableRow>().ToList();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Elements<W.TableCell>().Count(),
            Is.EqualTo(ComplianceExportWordWriter.AppendixImagesPerRow));

        var paragraphTexts = body.Descendants<W.Paragraph>().Select(p => p.InnerText.Trim()).ToList();

        // ...and the case is still identified, so a reader can see WHICH case lost
        // its photograph.
        Assert.That(paragraphTexts, Has.Some.EqualTo(CaseCaption));

        // The report itself is unharmed: the section table still carries the case.
        var sectionRows = body.Descendants<W.Table>().First().Elements<W.TableRow>().ToList();
        Assert.That(sectionRows.Count, Is.GreaterThanOrEqualTo(2), "a header row and the case row");
        Assert.That(sectionRows.Any(r => r.InnerText.Contains("El-tavle")), Is.True,
            "the case row is missing from the report table");
    }

    // ==================================================================
    // Test 3 — the image part declares the content type of its own bytes (#1219)
    // ==================================================================

    /// <summary>
    /// The embedded <see cref="ImagePart"/>'s content type must describe the bytes
    /// it actually holds.
    ///
    /// <para>
    /// <b>What this catches.</b> <c>InsertImage</c> hardcoded
    /// <c>data:image/png;base64,…</c> while encoding with the parameterless
    /// <c>MagickImage.ToBase64()</c>, which writes in the image's CURRENT format —
    /// JPEG, for the <c>.jpg</c> files the SDK's picture store is full of. And
    /// HtmlToOpenXml picks the OOXML part type from the DECLARED mime
    /// (<c>ImagePrefetcher.ReadDataUri</c> → <c>TryInspectMimeType</c>), never from
    /// the bytes, so the package got a <c>/word/media/imageN.png</c> part, content
    /// type <c>image/png</c>, holding JPEG. Word and LibreOffice sniff and render
    /// it, which is why nothing ever looked wrong; a strict OOXML validator, or a
    /// converter that trusts the declaration, is entitled not to.
    /// </para>
    ///
    /// <para>
    /// The JPEG case is the discriminating one — on pre-#1219 code the content-type
    /// assertion below fails with <c>image/png</c> against JPEG bytes. The PNG case
    /// passed before the fix too and is here as the other half of the pair: the fix
    /// must not have made everything JPEG.
    /// </para>
    /// </summary>
    [TestCase("4_700_a.jpg", MagickFormat.Jpeg, "image/jpeg", ".jpg")]
    [TestCase("4_700_a.png", MagickFormat.Png, "image/png", ".png")]
    public async Task Word_EmbeddedImagePartDeclaresTheContentTypeOfItsBytes(
        string imageFileName, MagickFormat expectedFormat, string expectedContentType, string expectedExtension)
    {
        var core = await GetCore();
        var pictureDirectory = await ConfigureLocalPictureStore(core);

        WriteSampleImage(Path.Combine(pictureDirectory, imageFileName));

        var document = BuildReportWithOneImage(imageFileName);

        await using var stream = await NewWordWriter().WriteAsync(document, core);
        using var word = WordprocessingDocument.Open(stream, false);

        var mainPart = word.MainDocumentPart!;
        var imagePart = mainPart.ImageParts.Single();

        // What the bytes REALLY are. Read first, so a failure below reports the
        // mismatch rather than an assumption about it.
        MagickFormat actualFormat;
        using (var embeddedBytes = imagePart.GetStream())
        {
            using var embedded = new MagickImage(embeddedBytes);
            actualFormat = embedded.Format;
        }

        Assert.That(actualFormat, Is.EqualTo(expectedFormat),
            $"premise: a {expectedExtension} source should still be {expectedFormat} once embedded");

        // The part's declaration. THIS is the assertion that fails on pre-#1219
        // code for the .jpg case: image/png declared over JPEG bytes.
        Assert.That(imagePart.ContentType, Is.EqualTo(expectedContentType),
            $"the image part declares {imagePart.ContentType} but holds {actualFormat} bytes");

        // ...and the part name follows the content type, so the package is
        // internally consistent rather than merely correctly labelled.
        Assert.That(Path.GetExtension(imagePart.Uri.OriginalString), Is.EqualTo(expectedExtension),
            "the media part's extension does not match its content type");
    }

    // ==================================================================
    // Test 4 — the DECLARED layout width is the one that applies (#1219)
    // ==================================================================

    /// <summary>
    /// The layout width <c>InsertImage</c> is given must be the width the drawing
    /// is laid out at, independently of how wide the embedded bytes happen to be.
    ///
    /// <para>
    /// <b>Why the two widths have to differ.</b> The HTML <c>width</c> ATTRIBUTE
    /// only accepts a bare integer: AngleSharp's
    /// <c>IHtmlImageElement.DisplayWidth</c> — which is what HtmlToOpenXml's
    /// <c>ImageExpression</c> reads — parses it with <c>Int32.TryParse</c>, so the
    /// old <c>width="300px"</c> failed to parse and fell back to
    /// <c>OriginalWidth</c>, which is 0 because the converter's AngleSharp context
    /// has no resource loader. The width then came from the decoded bytes instead.
    /// Production never noticed because it passes the same number as both the
    /// resize width and the layout width, so the two answers coincided. They are
    /// separate parameters, so this test drives them apart: bytes resized to
    /// <c>resizeWidthPx</c>, layout declared at <c>layoutWidthPx</c>.
    /// </para>
    ///
    /// <para>
    /// <b>The assertion that fails on pre-#1219 code</b> is the <c>cx</c> one: it
    /// reported <c>resizeWidthPx</c> in EMU (the decoded width) instead of
    /// <c>layoutWidthPx</c>. Everything above it passed before the fix as well.
    /// </para>
    ///
    /// <para>
    /// This is the only test here that calls <c>InsertImage</c> directly rather
    /// than going through <c>WriteAsync</c>; see <see cref="ConvertBodyFragment"/>
    /// for why. It still exercises the real parameter plumbing — that
    /// <c>imageWidth</c>, and not <c>imageSize</c>, is what reaches the attribute.
    /// </para>
    /// </summary>
    [Test]
    public async Task Word_ImageIsLaidOutAtTheDeclaredWidthNotTheDecodedOne()
    {
        // Deliberately unequal, and neither is AppendixImageWidthPx: a fix that
        // accidentally hardcoded the appendix constant would still fail here.
        const int resizeWidthPx = 100;
        const int layoutWidthPx = 250;

        var pictureDirectory = CreatePictureDirectory();

        const string imageFileName = "4_700_widths.jpg";
        WriteSampleImage(Path.Combine(pictureDirectory, imageFileName));

        // No Core: with s3Enabled false, InsertImage reads the local file at
        // basePicturePath and never touches the SDK. (WriteAsync is what needs a
        // Core, to READ that path out of the SDK settings in the first place.)
        var html = new StringBuilder();
        await NewWordWriter().InsertImage(
            imageFileName, html, resizeWidthPx, layoutWidthPx, core: null,
            basePicturePath: pictureDirectory, s3Enabled: false);
        Assert.That(html.ToString(), Does.Contain("<img "),
            "premise: InsertImage resolved the picture and emitted an image");

        await using var stream = ConvertBodyFragment(html.ToString());
        using var word = WordprocessingDocument.Open(stream, false);

        var mainPart = word.MainDocumentPart!;
        var imagePart = mainPart.ImageParts.Single();

        uint decodedWidth;
        uint decodedHeight;
        using (var embeddedBytes = imagePart.GetStream())
        {
            using var embedded = new MagickImage(embeddedBytes);
            decodedWidth = embedded.Width;
            decodedHeight = embedded.Height;
        }

        // premise: the BYTES are the resize width, not the layout width — without
        // this the cx assertion below would prove nothing.
        Assert.That(decodedWidth, Is.EqualTo((uint)resizeWidthPx),
            "premise: the embedded bytes carry the resize width");
        Assert.That(decodedWidth, Is.Not.EqualTo((uint)layoutWidthPx),
            "premise: the two widths must differ for this test to discriminate");

        var extent = mainPart.Document!.Body!.Descendants<DW.Extent>().Single();

        // The load-bearing assertion. Pre-#1219 this was resizeWidthPx * 9525.
        Assert.That(extent.Cx?.Value,
            Is.EqualTo((long)layoutWidthPx * EmusPerInch / CssPixelsPerInch),
            $"the declared layout width ({layoutWidthPx}px) was ignored; the image is laid out at "
            + $"the width of its decoded bytes ({decodedWidth}px) instead");

        // ...and the height follows from the aspect ratio of the bytes, scaled to
        // the declared width — HtmlToOpenXml's ImageHeader.KeepAspectRatio, which
        // does this in integer arithmetic, so the truncation is reproduced here
        // rather than rounded.
        var expectedHeightPx = (long)decodedHeight * layoutWidthPx / decodedWidth;
        Assert.That(extent.Cy?.Value, Is.EqualTo(expectedHeightPx * EmusPerInch / CssPixelsPerInch),
            "the image was scaled to the declared width without keeping its aspect ratio");
    }

    /// <summary>
    /// Only the two keys the appendix assertions read — <c>Appendix</c> ("Bilag")
    /// and <c>Case</c> ("Sag") — are localised; every other key comes back as
    /// itself, like the shared key-returning double. The column headers are not
    /// asserted here; <see cref="ComplianceExportWriterTests"/> owns those.
    /// </summary>
    private sealed class AppendixLocalizer : IBackendConfigurationLocalizationService
    {
        private static readonly Dictionary<string, string> Danish = new()
        {
            ["Appendix"] = "Bilag",
            ["Case"] = "Sag"
        };

        public string GetString(string key) => Danish.TryGetValue(key, out var value) ? value : key;

        public string GetString(string format, params object[] args) => string.Format(GetString(format), args);

        public string GetStringWithFormat(string format, params object[] args)
        {
            var value = GetString(format);
            return args?.Length > 0 ? string.Format(value, args) : value;
        }
    }
}
