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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// The binding cross-repo GS1 grammar (controller ruling 2026-10-04): the app, this backend
/// and chemicalbase must give the same answer for every vector in TestData/gs1-vectors.json,
/// which is a verbatim copy of the shared file. gtin null = reject.
/// </summary>
[TestFixture]
public class Gs1VectorTests
{
    private static IEnumerable<TestCaseData> Vectors()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "gs1-vectors.json")));
        var index = 0;
        foreach (var vector in json.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.TryGetProperty("repeat", out var repeat)
                ? string.Concat(Enumerable.Repeat(repeat.GetProperty("text").GetString(), repeat.GetProperty("count").GetInt32()))
                : vector.GetProperty("input").GetString();
            var gtin = vector.GetProperty("gtin").ValueKind == JsonValueKind.Null ? null : vector.GetProperty("gtin").GetString();
            yield return new TestCaseData(input, gtin).SetName($"TryExtractGtin_MatchesTheSharedVector_{index++:00}");
        }
    }

    [Test]
    public void TheVectorFileIsPresentAndComplete()
    {
        Assert.That(Vectors().Count(), Is.EqualTo(26));
    }

    [TestCaseSource(nameof(Vectors))]
    public void TryExtractGtin_MatchesTheSharedVector(string input, string expected)
    {
        var accepted = false;
        string gtin = null;
        Assert.That(() => accepted = Gs1.TryExtractGtin(input, out gtin), Throws.Nothing);
        Assert.That(accepted, Is.EqualTo(expected != null), input);
        Assert.That(gtin, Is.EqualTo(expected));
    }
}
