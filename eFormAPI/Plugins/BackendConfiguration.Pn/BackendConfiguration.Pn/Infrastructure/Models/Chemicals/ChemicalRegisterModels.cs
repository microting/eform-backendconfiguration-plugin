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

using System;
using System.Collections.Generic;

public sealed record ChemicalHazardStatementModel(string Code, string Text);

public sealed record ChemicalActiveSubstanceModel(string Name, string CasNo, string Concentration);

public sealed record ChemicalProductModel(int ProductId, string Name, string Barcode, string SdsFileName, string SdsChecksum);

/// <summary>Register (BMD) details of one chemical as the app and web show them.</summary>
public sealed record ChemicalRegisterEntryModel(
    int ChemicalId, string Name, string RegistrationNo, int? Status, string StatusText,
    DateTime? SalesDeadline, DateTime? UseAndPossessionDeadline,
    DateTime? AuthorisationDate, DateTime? AuthorisationExpirationDate, DateTime? AuthorisationTerminationDate,
    IReadOnlyList<string> HazardPictograms, int? SignalWord, string SignalWordText,
    IReadOnlyList<ChemicalHazardStatementModel> HazardStatements,
    IReadOnlyList<ChemicalActiveSubstanceModel> ActiveSubstances,
    string AuthorisationHolder, IReadOnlyList<ChemicalProductModel> Products, DateTime UpdatedAt);

public sealed record ChemicalRegisterPageModel(IReadOnlyList<ChemicalRegisterEntryModel> Entries, int Total);

/// <summary>The identifiers of a chemical/product the writes need (validation, suggestions).</summary>
public sealed record ChemicalProductRef(
    int ChemicalId, string ChemicalRemoteId, string RegistrationNo, int? Status,
    int? ProductId, string ProductName, string ProductFileName);
