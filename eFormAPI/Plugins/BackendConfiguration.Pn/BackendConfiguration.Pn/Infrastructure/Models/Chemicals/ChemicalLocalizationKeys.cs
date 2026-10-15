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

namespace BackendConfiguration.Pn.Infrastructure.Models.Chemicals;

using System.Collections.Generic;

/// <summary>
/// Every Resources/localization.json key the chemical web code reads
/// (ChemicalsController, ChemicalDigestController, ChemicalReportsController and
/// the ChemicalReporting services). ChemicalLocalizationCompletenessTests ties this
/// list to the file; add a key here in the same change that first reads it.
/// </summary>
public static class ChemicalLocalizationKeys
{
    // Plan A ChemicalsController (flutter-chemistry Plan A Task 15).
    public const string ErrorWhileReadingChemicals = "ErrorWhileReadingChemicals";
    public const string ErrorWhileSearchingChemicalRegister = "ErrorWhileSearchingChemicalRegister";
    public const string ErrorWhileSavingChemicalPlacement = "ErrorWhileSavingChemicalPlacement";
    public const string ErrorWhileSavingChemicalStock = "ErrorWhileSavingChemicalStock";
    public const string ErrorWhileSavingChemicalLocation = "ErrorWhileSavingChemicalLocation";
    public const string ErrorWhileReadingChemicalPermissions = "ErrorWhileReadingChemicalPermissions";
    public const string ErrorWhileSavingChemicalPermissions = "ErrorWhileSavingChemicalPermissions";
    public const string ErrorWhileReadingChemicalSettings = "ErrorWhileReadingChemicalSettings";
    public const string ErrorWhileSavingChemicalSettings = "ErrorWhileSavingChemicalSettings";

    // ChemicalDigestController (Plan E Task 7).
    public const string ErrorWhileReadingChemicalDigest = "ErrorWhileReadingChemicalDigest";

    public static readonly string[] ControllerErrors =
    [
        ErrorWhileReadingChemicals, ErrorWhileSearchingChemicalRegister, ErrorWhileSavingChemicalPlacement,
        ErrorWhileSavingChemicalStock, ErrorWhileSavingChemicalLocation, ErrorWhileReadingChemicalPermissions,
        ErrorWhileSavingChemicalPermissions, ErrorWhileReadingChemicalSettings, ErrorWhileSavingChemicalSettings,
        ErrorWhileReadingChemicalDigest,
    ];

    public static IReadOnlyList<string> All { get; } = [.. ControllerErrors];
}
