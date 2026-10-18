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
using System.Linq;
using Infrastructure.Models.Chemicals;

/// <summary>Id lookups over one inventory, shared by the PDF and the Excel exports so the "#id" fallback is the same everywhere.</summary>
public sealed class ChemicalInventoryLookup(ChemicalInventoryModel model)
{
    private readonly Dictionary<int, ChemicalLocationModel> locations = model.Locations.ToDictionary(l => l.Id);
    private readonly Dictionary<int, ChemicalRegisterEntryModel> entries = model.RegisterEntries.ToDictionary(e => e.ChemicalId);

    public ChemicalLocationModel Location(int locationId) => locations.GetValueOrDefault(locationId);

    public ChemicalRegisterEntryModel Entry(int chemicalId) => entries.GetValueOrDefault(chemicalId);

    /// <summary>The register name, or "#id" when the chemical is no longer in the register.</summary>
    public string ProductName(int chemicalId) => Entry(chemicalId)?.Name ?? FallbackProductName(chemicalId);

    /// <summary>A chemical missing from the register keeps its row under its id (W1 exports and W6 alike).</summary>
    public static string FallbackProductName(int chemicalId) => $"#{chemicalId}";

    public string PropertyName(int propertyId) => model.Properties.FirstOrDefault(p => p.PropertyId == propertyId)?.Name ?? string.Empty;
}
