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
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// GS1 "Sunrise 2027": products move from linear EAN/UPC to GS1 Digital Link QR
/// and GS1 DataMatrix. Every spelling of a scan reduces to one check-digit-valid GTIN.
/// </summary>
[TestFixture]
public class Gs1Tests
{
    private const char Fnc1 = '\u001d';

    // 5701234567899 is a valid EAN-13; 15701234567896 a valid GTIN-14 (indicator 1).
    private static readonly object[] Valid =
    [
        // plain GTINs
        new object[] { "5701234567899", "5701234567899" },
        new object[] { " 5701234567899\n", "5701234567899" },
        new object[] { "\uFEFF5701234567899\u00A0", "5701234567899" },   // BOM and Unicode White_Space are trimmed
        new object[] { "\u30005701234567899\u2028", "5701234567899" },
        new object[] { "96385074", "96385074" },
        new object[] { "012345678905", "0012345678905" },              // UPC-A → its EAN-13 form
        new object[] { "05701234567899", "5701234567899" },
        new object[] { "15701234567896", "15701234567896" },
        // GS1 Digital Link, any domain, 01 or gtin segment, extra AIs and query ignored
        new object[] { "https://id.gs1.org/01/05701234567899", "5701234567899" },
        new object[] { "https://example.com/01/05701234567899/10/ABC123?17=261231", "5701234567899" },
        new object[] { "http://brand.example.dk/products/01/5701234567899", "5701234567899" },
        new object[] { "HTTPS://SHOP.EXAMPLE.DK/gtin/05701234567899", "5701234567899" },
        new object[] { "https://id.gs1.org/01/012345678905", "0012345678905" },
        new object[] { "https://id.gs1.org/01/96385074", "96385074" },
        new object[] { "https://id.gs1.org/01/15701234567896/21/XYZ", "15701234567896" },
        new object[] { "https://brand.dk/produkter/rengøring/01/05711111111114", "5711111111114" }, // non-ASCII path kept as-is
        // GS1 element strings (human-readable)
        new object[] { "(01)05701234567899", "5701234567899" },
        new object[] { "(01)05701234567899junk", "5701234567899" },      // content after AI 01 + 14 digits is ignored
        new object[] { "(01)05701234567899(10)ABC(17)261231", "5701234567899" },
        // raw / FNC1-separated element strings (DataMatrix, GS1 QR)
        new object[] { "0105701234567899", "5701234567899" },
        new object[] { "0105701234567899" + "10ABC" + Fnc1 + "17261231", "5701234567899" },
        new object[] { Fnc1 + "0105701234567899" + Fnc1 + "10ABC", "5701234567899" },
        new object[] { "]d20105701234567899", "5701234567899" },
        new object[] { "]Q30115701234567896", "15701234567896" },
        new object[] { "]C10105701234567899" + Fnc1 + "10ABC", "5701234567899" },
        // AIM symbology identifiers a hardware scanner prefixes to a plain EAN/UPC
        new object[] { "]E05701234567899", "5701234567899" },
        new object[] { "]E0012345678905", "0012345678905" },
        new object[] { "]E496385074", "96385074" },
    ];

    [TestCaseSource(nameof(Valid))]
    public void TryExtractGtin_AcceptsGs1Spellings(string input, string expected)
    {
        Assert.That(Gs1.TryExtractGtin(input, out var gtin), Is.True, input);
        Assert.That(gtin, Is.EqualTo(expected));
    }

    private static readonly object[] Invalid =
    [
        new object[] { null },
        "",
        "   ",
        "5701234567892",                                  // bad check digit
        "05701234567892",
        "1234567",                                        // 7 digits: not a GTIN length
        "12345",
        "123456789012345",                                // 15 digits
        "57012A4567899",
        "5701 234567899",
        "https://example.com/hello",                      // junk QR
        "https://id.gs1.org/01/05701234567892",           // bad check digit
        "https://id.gs1.org/01/123",                      // wrong length
        "https://id.gs1.org/01/05701234567899x",
        "https://id.gs1.org/10/05701234567899",           // not the GTIN AI
        "ftp://id.gs1.org/01/05701234567899",             // not http(s)
        "id.gs1.org/01/05701234567899",                   // not an absolute URI
        "(01)0570123456789",                              // 13 digits after (01)
        "(01)05701234567892",
        "(10)ABC",
        "0105701234567892",
        "01057012345678",                                 // truncated element string
        "WIFI:S:guest;T:WPA;P:secret;;",
        // grammar ruling: AI 01 only at the start, one GS stripped, exact segments, no control characters
        "(17)261231(01)05701234567899",
        "\u001d\u001d0105701234567899",
        "https://id.gs1.org/01//05701234567899",
        "https://shop.example.dk/GTIN/05701234567899",
        "https:///01/05711111111114",                     // empty host (parity with app + chemicalbase)
        "http:///01/05711111111114",
        "https://example.com/%EF%BB%BF01/05711111111114", // a percent-encoded BOM is not stripped
        "5701234567899\u0000",
        "(01)05701234567899\u0007",
        // all-zero GTINs (GS1 forbids them; register placeholders must not match)
        "00000000",
        "000000000000",
        "0000000000000",
        "00000000000000",
        "https://id.gs1.org/01/00000000000000",
        "(01)00000000000000",
        "]E00000000000000",
        // symbology identifiers: unknown ones, or a known one with a bad payload
        "]X05701234567899",
        "]e05701234567899",
        "]E05701234567892",
        "]E4",
        "]d2",
        new string('1', 5000),
    ];

    [TestCaseSource(nameof(Invalid))]
    public void TryExtractGtin_RejectsJunk(string input)
    {
        Assert.That(Gs1.TryExtractGtin(input, out var gtin), Is.False, input);
        Assert.That(gtin, Is.Null);
    }

    // ---- ChemicalBarcode: one product, every spelling ----

    [Test]
    public void Candidates_UpcA_MatchesItsEan13AndGtin14Forms()
    {
        Assert.That(ChemicalBarcode.Candidates("012345678905"),
            Is.EquivalentTo(new[] { "012345678905", "0012345678905", "00012345678905" }));
    }

    [Test]
    public void Candidates_Ean13WithLeadingZero_MatchesItsUpcAAndGtin14Forms()
    {
        Assert.That(ChemicalBarcode.Candidates("0012345678905"),
            Is.EquivalentTo(new[] { "012345678905", "0012345678905", "00012345678905" }));
    }

    [TestCase("5701234567899")]
    [TestCase("05701234567899")]
    [TestCase("https://id.gs1.org/01/05701234567899")]
    [TestCase("(01)05701234567899(10)LOT1")]
    public void Candidates_EveryGs1SpellingOfAnEan13_MatchesTheSameStoredForms(string scanned)
    {
        Assert.That(ChemicalBarcode.Candidates(scanned), Is.EquivalentTo(new[] { "5701234567899", "05701234567899" }));
    }

    [Test]
    public void Candidates_Ean8_MatchesItsZeroPaddedForms()
    {
        Assert.That(ChemicalBarcode.Candidates("96385074"),
            Is.EquivalentTo(new[] { "96385074", "000096385074", "0000096385074", "00000096385074" }));
    }

    [Test]
    public void Candidates_Gtin14WithIndicator_IsOnlyItself()
    {
        Assert.That(ChemicalBarcode.Candidates("15701234567896"), Is.EquivalentTo(new[] { "15701234567896" }));
    }

    [TestCase("https://example.com/hello")]
    [TestCase("(01)05701234567892")]
    [TestCase("abc")]
    [TestCase("12345")]
    [TestCase("0000000000000")]
    [TestCase("000000")]
    public void Candidates_Junk_Throws(string scanned)
    {
        Assert.That(() => ChemicalBarcode.Candidates(scanned), Throws.InstanceOf<ArgumentException>());
    }

    // Curated rows may carry a typo'd check digit or a raw UPC-E: digits-only input that
    // is no valid GTIN is matched literally, never GTIN-normalised or expanded.
    [TestCase("5701234567892")]
    [TestCase("05701234567892")]
    [TestCase("0123456")]
    [TestCase("01234566")]
    [TestCase("123456")]
    public void Candidates_DigitsThatAreNoValidGtin_MatchOnlyLiterally(string stored)
    {
        Assert.That(ChemicalBarcode.Candidates($" {stored} "), Is.EqualTo(new[] { stored }));
        Assert.That(ChemicalBarcode.Normalize(stored), Is.EqualTo(stored));
    }

    [TestCase(" 05701234567899 ", "5701234567899")]
    [TestCase("https://id.gs1.org/01/05701234567899", "5701234567899")]
    public void Normalize_ReturnsTheGtin(string scanned, string expected)
    {
        Assert.That(ChemicalBarcode.Normalize(scanned), Is.EqualTo(expected));
    }

    // ---- search: free text passes through, a scan becomes its GTIN, a junk scan is refused ----

    [TestCase("Roundup", "Roundup")]
    [TestCase("  Roundup ", "Roundup")]
    [TestCase("4-567", "4-567")]
    [TestCase("123456", "123456")]
    [TestCase("5701234567892", "5701234567892")]
    [TestCase("5701234567899", "5701234567899")]
    [TestCase("https://id.gs1.org/01/05701234567899", "5701234567899")]
    [TestCase("(01)05701234567899", "5701234567899")]
    [TestCase("05701234567899", "5701234567899")]
    public void NormalizeSearchQuery_TextPassesThrough_ScansBecomeTheGtin(string query, string expected)
    {
        Assert.That(ChemicalBarcode.NormalizeSearchQuery(query), Is.EqualTo(expected));
    }

    [TestCase("https://example.com/hello")]
    [TestCase("HTTP://id.gs1.org/01/05701234567892")]
    [TestCase("(01)123")]
    [TestCase("]d2junk")]
    public void NormalizeSearchQuery_JunkScan_Throws(string query)
    {
        Assert.That(() => ChemicalBarcode.NormalizeSearchQuery(query), Throws.InstanceOf<ArgumentException>());
    }
}
