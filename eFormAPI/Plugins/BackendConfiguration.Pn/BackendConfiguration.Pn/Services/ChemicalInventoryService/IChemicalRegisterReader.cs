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
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;

/// <summary>Read-only access to the customer's chemical-base copy (spec §4: never written).</summary>
public interface IChemicalRegisterReader
{
    /// <summary>Details by id, INCLUDING chemicals the nightly sync removed (they may still be placed).</summary>
    Task<IReadOnlyList<ChemicalRegisterEntryModel>> GetByIdsAsync(IReadOnlyCollection<int> chemicalIds);

    /// <summary>The ids among <paramref name="chemicalIds"/> whose chemical or products changed after <paramref name="sinceUtc"/>.</summary>
    Task<IReadOnlyList<int>> ChangedSinceAsync(IReadOnlyCollection<int> chemicalIds, DateTime sinceUtc);

    /// <summary>Active chemicals with a product carrying the barcode (UPC-A/EAN-13 forms match). Throws ArgumentException on a non-barcode.</summary>
    Task<IReadOnlyList<ChemicalRegisterEntryModel>> LookupBarcodeAsync(string barcode);

    /// <summary>Active chemicals whose name or reg.nr contains the query, or whose product barcode equals it; ordered by name.</summary>
    Task<ChemicalRegisterPageModel> SearchAsync(string query, int page, int pageSize);

    /// <summary>
    /// The active chemical (NotFound otherwise) and, when given, its product
    /// (ArgumentException when the product belongs to another chemical).
    /// </summary>
    Task<ChemicalProductRef> FindProductAsync(int chemicalId, int? productId);

    /// <summary>
    /// The stored SDS file name of an active product matching <paramref name="fileName"/>
    /// (by the register's collation), or null when there is none or it means "no SDS".
    /// </summary>
    Task<string> FindSdsFileNameAsync(string fileName);
}
