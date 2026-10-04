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
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Reduces a scan to its GTIN (GS1 "Sunrise 2027": packs move from linear EAN/UPC
/// to GS1 Digital Link QR and GS1 DataMatrix). Pure string parsing: a Digital Link
/// URI is never fetched.
/// </summary>
public static class Gs1
{
    /// <summary>Longer than any real scan; refused before parsing.</summary>
    internal const int MaxInputLength = 2048;

    private const char Fnc1 = '\u001d';

    /// <summary>
    /// The AIM symbology identifiers stripped before parsing (case-sensitive):
    /// EAN-13/UPC-A, EAN-8, GS1 DataMatrix, GS1 QR and GS1-128.
    /// </summary>
    private static readonly string[] AimSymbologyIdentifiers = ["]E0", "]E4", "]d2", "]Q3", "]C1"];

    // "(01)" + 14 digits anywhere in a human-readable element string.
    private static readonly Regex ElementStringGtin = new(@"\(01\)([0-9]{14})(?![0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Any AIM-style symbology identifier: marks the input as a scan, even when it is not one we strip.
    private static readonly Regex SymbologyIdentifier = new(@"^\][A-Za-z][0-9A-Za-z]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ElementStringStart = new(@"^\([0-9]{2,4}\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The GTIN in <paramref name="input"/>, check digit verified: a plain EAN-8,
    /// UPC-A, EAN-13 or GTIN-14; a GS1 Digital Link URI (any http(s) domain, path
    /// segment <c>01</c> or <c>gtin</c> followed by 8, 12, 13 or 14 digits); or a
    /// GS1 element string, either <c>(01)…</c> or raw / FNC1-separated <c>01…</c>.
    /// A leading AIM symbology identifier (<c>]E0 ]E4 ]d2 ]Q3 ]C1</c>) is stripped first.
    /// An all-zero GTIN is refused. A GTIN-14 with a leading 0 is returned as its
    /// GTIN-13; other lengths as given.
    /// </summary>
    public static bool TryExtractGtin(string input, out string gtin)
    {
        gtin = null;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxInputLength)
        {
            return false;
        }

        text = WithoutSymbologyIdentifier(text);
        var candidate = FromDigitalLink(text) ?? FromElementString(text) ?? text;
        if (!IsGtinLength(candidate.Length) || !candidate.All(char.IsAsciiDigit) || IsAllZeros(candidate)
            || !HasValidCheckDigit(candidate))
        {
            return false;
        }

        gtin = candidate.Length == 14 && candidate[0] == '0' ? candidate[1..] : candidate;
        return true;
    }

    /// <summary>
    /// True when <paramref name="input"/> is unmistakably a scanned GS1 symbol
    /// (http(s) URL, element string, symbology identifier or FNC1) rather than typed text.
    /// </summary>
    public static bool LooksLikeScan(string input)
    {
        var text = input?.Trim() ?? string.Empty;
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || ElementStringStart.IsMatch(text)
               || SymbologyIdentifier.IsMatch(text)
               || text.Contains(Fnc1);
    }

    /// <summary>GS1 forbids an all-zero GTIN; register placeholder rows use one.</summary>
    internal static bool IsAllZeros(string digits) => digits.All(c => c == '0');

    /// <summary>The 14-digit form of a GTIN (left-padded with zeros).</summary>
    internal static string ToGtin14(string gtin) => gtin.PadLeft(14, '0');

    internal static bool IsGtinLength(int length) => length is 8 or 12 or 13 or 14;

    /// <summary>GS1 mod-10: weights 3,1,3,… from the digit left of the check digit.</summary>
    internal static bool HasValidCheckDigit(string digits)
    {
        var sum = 0;
        for (var i = digits.Length - 2; i >= 0; i--)
        {
            sum += (digits[i] - '0') * ((digits.Length - 2 - i) % 2 == 0 ? 3 : 1);
        }

        return (10 - sum % 10) % 10 == digits[^1] - '0';
    }

    // FromDigitalLink / FromElementString: null = the input is not that syntax;
    // "" = it is, but carries no usable GTIN (so the input is refused, not read as a plain GTIN).

    private static string WithoutSymbologyIdentifier(string text)
    {
        var prefix = AimSymbologyIdentifiers.FirstOrDefault(p => text.StartsWith(p, StringComparison.Ordinal));
        return prefix == null ? text : text[prefix.Length..];
    }

    /// <summary>The value after the first <c>01</c> / <c>gtin</c> path segment of an http(s) URI.</summary>
    private static string FromDigitalLink(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i] == "01" || segments[i].Equals("gtin", StringComparison.OrdinalIgnoreCase))
            {
                return segments[i + 1];
            }
        }

        return string.Empty;
    }

    /// <summary>The 14 digits of AI 01 in a bracketed, raw or FNC1-prefixed element string.</summary>
    private static string FromElementString(string text)
    {
        if (ElementStringStart.IsMatch(text))
        {
            var match = ElementStringGtin.Match(text);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        var raw = text.TrimStart(Fnc1);
        var fnc1Prefixed = raw.Length != text.Length;
        if (raw.Length < 16 || !raw.StartsWith("01", StringComparison.Ordinal) || !raw[2..16].All(char.IsAsciiDigit))
        {
            // Unprefixed: maybe a plain GTIN (an unprefixed 16+ character string then fails the length check).
            return fnc1Prefixed ? string.Empty : null;
        }

        return raw[2..16];
    }
}
