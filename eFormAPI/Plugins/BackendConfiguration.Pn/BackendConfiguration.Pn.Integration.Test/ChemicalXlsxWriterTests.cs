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

using BackendConfiguration.Pn.Services.ChemicalReporting;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>DB-free (no TestBaseSetup): the workbook the Kemi exports share.</summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ChemicalXlsxWriterTests
{
    [Test]
    public void Write_HeadersThenRows_InOneSheet()
    {
        var bytes = ChemicalXlsxWriter.Write("Kemikalier", ["Produkt", "Lager"],
            [new object[] { "Karate 2,5 WG", 3.5m }, new object[] { "Virkon S", 10 }]);

        var (sheet, rows) = XlsxTestReader.Read(bytes);
        Assert.That(sheet, Is.EqualTo("Kemikalier"));
        Assert.That(rows, Is.EqualTo(new[]
        {
            new List<string> { "Produkt", "Lager" },
            new List<string> { "Karate 2,5 WG", "3.5" },
            new List<string> { "Virkon S", "10" },
        }));
        Assert.That(XlsxTestReader.CellType(bytes, 1, 1), Is.EqualTo("n"));
        Assert.That(XlsxTestReader.CellType(bytes, 0, 0), Is.EqualTo("inlineStr"));
    }

    [Test]
    public void FormulaLookingText_StaysLiteralText()
    {
        var bytes = ChemicalXlsxWriter.Write("x", ["Note"],
            [new object[] { "=HYPERLINK(\"http://evil\")" }, new object[] { "Æblegården \"øst\" å" }]);

        var (_, rows) = XlsxTestReader.Read(bytes);
        Assert.That(rows[1][0], Is.EqualTo("=HYPERLINK(\"http://evil\")"));
        Assert.That(XlsxTestReader.CellType(bytes, 1, 0), Is.EqualTo("inlineStr"));
        Assert.That(rows[2][0], Is.EqualTo("Æblegården \"øst\" å"));
    }

    [Test]
    public void NullsAndControlCharacters_BecomeCleanText()
    {
        var bytes = ChemicalXlsxWriter.Write("x", ["A", "B"], [new object[] { null!, "a\u0001b\nc" }]);

        var (_, rows) = XlsxTestReader.Read(bytes);
        Assert.That(rows[1], Is.EqualTo(new List<string> { string.Empty, "ab\nc" }));
    }

    [Test]
    public void HeaderOnly_IsAValidWorkbook()
    {
        var (_, rows) = XlsxTestReader.Read(ChemicalXlsxWriter.Write("x", ["A"], []));

        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [TestCase("Fjernede placeringer 2025/2026", "Fjernede placeringer 20252026")]
    [TestCase("[Historik]: *?", "Historik")]
    [TestCase("", "Sheet1")]
    [TestCase("Kemikalieliste til Arbejdstilsynet Hovedgården", "Kemikalieliste til Arbejdstilsy")]
    public void SheetNames_AreMadeValid(string input, string expected) =>
        Assert.That(ChemicalXlsxWriter.SheetName(input), Is.EqualTo(expected));
}
