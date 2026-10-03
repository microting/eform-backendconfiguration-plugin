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
    /// <summary>Digits only, 6–14 long (UPC-E, EAN-8, UPC-A, EAN-13, GTIN-14); surrounding whitespace is trimmed.</summary>
    public static string Normalize(string raw)
    {
        var code = (raw ?? string.Empty).Trim();
        if (code.Length is < 6 or > 14 || !code.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("A barcode is 6 to 14 digits.");
        }

        return code;
    }

    /// <summary>
    /// Every spelling the same GTIN may be curated under: a 12-digit UPC-A is
    /// also its 13-digit EAN form with a leading 0, and vice versa.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string raw)
    {
        var code = Normalize(raw);
        var candidates = new List<string> { code };
        if (code.Length == 12)
        {
            candidates.Add("0" + code);
        }

        if (code.Length == 13 && code[0] == '0')
        {
            candidates.Add(code[1..]);
        }

        return candidates;
    }

    /// <summary>Stub (red phase).</summary>
    public static string NormalizeSearchQuery(string query) => query;
}
