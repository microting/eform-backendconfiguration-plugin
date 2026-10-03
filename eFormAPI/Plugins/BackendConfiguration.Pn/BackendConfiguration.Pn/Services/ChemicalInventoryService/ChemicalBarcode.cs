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

namespace BackendConfiguration.Pn.Services.ChemicalInventoryService;

using System;
using System.Collections.Generic;
using System.Linq;

public static class ChemicalBarcode
{
    /// <summary>
    /// The GTIN a scan carries (plain EAN-8/UPC-A/EAN-13/GTIN-14, GS1 Digital Link
    /// or element string; see <see cref="Gs1.TryExtractGtin"/>).
    /// Throws ArgumentException when it carries none.
    /// </summary>
    public static string Normalize(string raw) =>
        Gs1.TryExtractGtin(raw, out var gtin) ? gtin : throw NotAGtin();

    /// <summary>
    /// Every spelling the same GTIN may be curated under: its zero-padded 14, 13,
    /// 12 and 8 digit forms, so GTIN-14, EAN-13 and UPC-A variants all match.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string raw)
    {
        var gtin14 = Gs1.ToGtin14(Normalize(raw));
        return new[] { 14, 13, 12, 8 }
            .Where(length => gtin14[..(14 - length)].All(c => c == '0'))
            .Select(length => gtin14[(14 - length)..])
            .ToList();
    }

    /// <summary>
    /// A register search query: a scan is searched as its GTIN; free text passes
    /// through trimmed; a scan carrying no valid GTIN (a junk QR) throws ArgumentException.
    /// </summary>
    public static string NormalizeSearchQuery(string query)
    {
        var text = query?.Trim();
        if (Gs1.TryExtractGtin(text, out var gtin))
        {
            return gtin;
        }

        return Gs1.LooksLikeScan(text) ? throw NotAGtin() : text;
    }

    private static ArgumentException NotAGtin() =>
        new("The scan does not contain a valid GTIN (8, 12, 13 or 14 digits with a valid check digit).");
}
