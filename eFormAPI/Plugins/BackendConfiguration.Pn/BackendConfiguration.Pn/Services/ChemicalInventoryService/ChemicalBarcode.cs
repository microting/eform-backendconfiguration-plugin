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
    /// The lookup key of a scan or typed code: its GTIN (plain EAN-8/UPC-A/EAN-13/GTIN-14,
    /// GS1 Digital Link or element string; see <see cref="Gs1.TryExtractGtin"/>), or else the
    /// literal digits of a 6–14 digit code that is no valid GTIN (a curated typo'd check
    /// digit, a raw UPC-E). Throws ArgumentException otherwise, and for an all-zero code.
    /// </summary>
    public static string Normalize(string raw)
    {
        if (Gs1.TryExtractGtin(raw, out var gtin))
        {
            return gtin;
        }

        var text = Gs1.Trim(raw) ?? string.Empty;
        return IsLiteralBarcode(text) ? text : throw NotAGtin();
    }

    /// <summary>
    /// Every spelling the same code may be curated under. A GTIN yields its zero-padded
    /// 14, 13, 12 and 8 digit forms, so GTIN-14, EAN-13 and UPC-A variants all match;
    /// literal digits (no valid GTIN) match only themselves.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string raw)
    {
        var code = Normalize(raw);
        if (!Gs1.TryExtractGtin(code, out var gtin))
        {
            return [code];
        }

        var gtin14 = Gs1.ToGtin14(gtin);
        return new[] { 14, 13, 12, 8 }
            .Where(length => gtin14[..(14 - length)].All(c => c == '0'))
            .Select(length => gtin14[(14 - length)..])
            .ToList();
    }

    /// <summary>The barcode spellings a register search text may match; empty when the text is no barcode.</summary>
    public static IReadOnlyList<string> SearchCandidates(string text) =>
        Gs1.TryExtractGtin(text, out _) || IsLiteralBarcode(Gs1.Trim(text) ?? string.Empty) ? Candidates(text) : [];

    /// <summary>
    /// A register search query: a scan is searched as its GTIN; free text passes
    /// through trimmed; a scan carrying no valid GTIN (a junk QR) throws ArgumentException.
    /// </summary>
    public static string NormalizeSearchQuery(string query)
    {
        var text = Gs1.Trim(query);
        if (Gs1.TryExtractGtin(text, out var gtin))
        {
            return gtin;
        }

        return Gs1.LooksLikeScan(text) ? throw NotAGtin() : text;
    }

    private static bool IsLiteralBarcode(string text) =>
        text.Length is >= 6 and <= 14 && text.All(char.IsAsciiDigit) && !Gs1.IsAllZeros(text);

    private static ArgumentException NotAGtin() =>
        new("The scan does not contain a valid GTIN (8, 12, 13 or 14 digits with a valid check digit).");
}
