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

namespace BackendConfiguration.Pn.Services.ChemicalReporting;

using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// The nine official GHS hazard pictograms (UNECE artwork, public domain; the same files flutter-chemistry pins in
/// tool/fetch_ghs_pictograms.sh), embedded as SVG resources (Resources/Ghs) for the authority list PDF. The web admin
/// carries the same files base64-encoded in chemical-ghs-pictograms.ts.
/// </summary>
public static class ChemicalGhsPictograms
{
    private static readonly IReadOnlyDictionary<string, string> Svgs = Enumerable.Range(1, 9)
        .Select(n => $"GHS{n:00}")
        .ToDictionary(code => code, Load);

    /// <summary>The SVG of a GHS code such as "GHS07", or null for a code without official artwork.</summary>
    public static string Svg(string code) =>
        Svgs.GetValueOrDefault(code?.Trim().ToUpperInvariant() ?? string.Empty);

    private static string Load(string code)
    {
        var name = $"BackendConfiguration.Pn.Resources.Ghs.{code}.svg";
        using var stream = typeof(ChemicalGhsPictograms).Assembly.GetManifestResourceStream(name)
                           ?? throw new FileNotFoundException($"Embedded GHS pictogram {name} is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
