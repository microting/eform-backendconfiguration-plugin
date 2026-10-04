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
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Reduces a scan to its canonical GTIN (GS1 "Sunrise 2027": packs move from linear
/// EAN/UPC to GS1 Digital Link QR and GS1 DataMatrix). Implements the binding
/// cross-repo GS1 grammar ruling (2026-10-04) shared with the app and chemicalbase:
/// they must accept and reject the same inputs. Pure string parsing: a Digital Link
/// URI is never fetched, and bad input never throws.
/// </summary>
public static class Gs1
{
    /// <summary>Longer than any real scan; refused before parsing.</summary>
    internal const int MaxInputLength = 2048;

    private const char Fnc1 = '\u001d';

    /// <summary>The AIM symbology identifiers stripped before parsing (case-sensitive): EAN-13/UPC-A, EAN-8, GS1 DataMatrix, GS1 QR, GS1-128.</summary>
    private static readonly string[] AimSymbologyIdentifiers = ["]E0", "]E4", "]d2", "]Q3", "]C1"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Any AIM-style symbology identifier: marks typed search text as a scan.
    private static readonly Regex SymbologyIdentifier = new(@"^\][A-Za-z][0-9A-Za-z]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ElementStringStart = new(@"^\([0-9]{2,4}\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The canonical GTIN in <paramref name="input"/>, or false. After trimming, refusing
    /// control characters other than GS and inputs over 2048 characters, stripping one AIM
    /// identifier (<c>]E0 ]E4 ]d2 ]Q3 ]C1</c>; any other <c>]</c> prefix is refused) and one
    /// leading GS, the input must be one of:
    /// plain digits (8, 12, 13 or 14); an element string STARTING with AI 01
    /// (<c>(01)</c> or <c>01</c> + exactly 14 digits; further AIs ignored); or an http(s)
    /// GS1 Digital Link whose percent-decoded path has a segment <c>01</c> or <c>gtin</c>
    /// immediately followed by a segment of 8, 12, 13 or 14 digits (first match wins).
    /// The check digit must be valid and the GTIN not all zeros. Canonical form: pad to
    /// GTIN-14; <c>000000…</c> → GTIN-8, else <c>0…</c> → GTIN-13, else GTIN-14
    /// (so a UPC-A becomes its 13-digit EAN form).
    /// </summary>
    public static bool TryExtractGtin(string input, out string gtin)
    {
        gtin = null;
        var text = Preprocess(input);
        var digits = text == null ? null : FromPlainDigits(text) ?? FromElementString(text) ?? FromDigitalLink(text);
        if (digits == null || IsAllZeros(digits) || !HasValidCheckDigit(digits))
        {
            return false;
        }

        gtin = Canonical(ToGtin14(digits));
        return true;
    }

    /// <summary>
    /// True when <paramref name="input"/> is unmistakably a scanned GS1 symbol
    /// (http(s) URL, element string, symbology identifier or FNC1) rather than typed text.
    /// </summary>
    public static bool LooksLikeScan(string input)
    {
        var text = Trim(input) ?? string.Empty;
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

    private static bool IsGtin(string text) =>
        text.Length is 8 or 12 or 13 or 14 && text.All(char.IsAsciiDigit);

    private static string Canonical(string gtin14) =>
        gtin14.StartsWith("000000", StringComparison.Ordinal) ? gtin14[6..]
        : gtin14[0] == '0' ? gtin14[1..]
        : gtin14;

    /// <summary>Trimmed, without one AIM identifier and one leading GS; null when the input is refused outright.</summary>
    private static string Preprocess(string input)
    {
        var text = Trim(input);
        if (string.IsNullOrEmpty(text) || text.Length > MaxInputLength || text.Any(c => char.IsControl(c) && c != Fnc1))
        {
            return null;
        }

        if (text[0] == ']')
        {
            var prefix = AimSymbologyIdentifiers.FirstOrDefault(p => text.StartsWith(p, StringComparison.Ordinal));
            if (prefix == null)
            {
                return null;
            }

            text = text[prefix.Length..];
        }

        return text.Length > 0 && text[0] == Fnc1 ? text[1..] : text;
    }

    /// <summary>Trims Unicode White_Space (char.IsWhiteSpace) and the BOM U+FEFF, which string.Trim keeps.</summary>
    internal static string Trim(string input)
    {
        if (input == null)
        {
            return null;
        }

        static bool IsTrimmed(char c) => char.IsWhiteSpace(c) || c == '\uFEFF';
        int start = 0, end = input.Length;
        while (start < end && IsTrimmed(input[start]))
        {
            start++;
        }

        while (end > start && IsTrimmed(input[end - 1]))
        {
            end--;
        }

        return input[start..end];
    }

    private static string FromPlainDigits(string text) => IsGtin(text) ? text : null;

    /// <summary>The 14 digits after a leading AI 01: <c>(01)</c> + 14 digits not followed by a digit, or <c>01</c> + 14 digits.</summary>
    private static string FromElementString(string text)
    {
        if (text.StartsWith("(01)", StringComparison.Ordinal))
        {
            return text.Length >= 18 && text[4..18].All(char.IsAsciiDigit) && (text.Length == 18 || !char.IsAsciiDigit(text[18]))
                ? text[4..18]
                : null;
        }

        return text.Length >= 16 && text.StartsWith("01", StringComparison.Ordinal) && text[2..16].All(char.IsAsciiDigit)
            ? text[2..16]
            : null;
    }

    /// <summary>The first 8/12/13/14-digit path segment right after a <c>01</c> or <c>gtin</c> segment of an http(s) URI.</summary>
    private static string FromDigitalLink(string text)
    {
        string rest;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            rest = text[7..];
        }
        else if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            rest = text[8..];
        }
        else
        {
            return null;
        }

        var end = rest.IndexOfAny(['?', '#']);
        var hostAndPath = end < 0 ? rest : rest[..end];
        var slash = hostAndPath.IndexOf('/');
        if (slash <= 0)
        {
            // No path, or an empty host ("https:///01/…"): refused, as in the app and chemicalbase.
            return null;
        }

        var segments = new List<string>();
        foreach (var raw in hostAndPath[(slash + 1)..].Split('/'))
        {
            var segment = PercentDecode(raw);
            if (segment == null)
            {
                return null; // invalid percent-encoding refuses the whole URI
            }

            segments.Add(segment);
        }

        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (segments[i] is "01" or "gtin" && IsGtin(segments[i + 1]))
            {
                return segments[i + 1];
            }
        }

        return null;
    }

    /// <summary>Strict percent-decoding of one path segment: null on a malformed escape or invalid UTF-8 (never throws).</summary>
    private static string PercentDecode(string segment)
    {
        if (!segment.Contains('%'))
        {
            return segment;
        }

        var bytes = new List<byte>(segment.Length);
        for (var i = 0; i < segment.Length; i++)
        {
            if (segment[i] != '%')
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(segment[i].ToString()));
                continue;
            }

            if (i + 2 >= segment.Length || !Uri.IsHexDigit(segment[i + 1]) || !Uri.IsHexDigit(segment[i + 2]))
            {
                return null;
            }

            bytes.Add(Convert.ToByte(segment.Substring(i + 1, 2), 16));
            i += 2;
        }

        try
        {
            return StrictUtf8.GetString(bytes.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
