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
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;

/// <summary>Register-wide reads: any caller with View on at least one property (Plan A decision 2).</summary>
public partial class ChemicalInventoryService
{
    private const int MaxSdsFileNameLength = 100;

    public async Task<IReadOnlyList<ChemicalRegisterEntryModel>> LookupBarcodeAsync(ChemicalCaller caller, string barcode)
    {
        await permissions.RequireViewOnAnyPropertyAsync(caller).ConfigureAwait(false);
        return await register.LookupBarcodeAsync(barcode).ConfigureAwait(false);
    }

    public async Task<ChemicalRegisterPageModel> SearchRegisterAsync(ChemicalCaller caller, string query, int page, int pageSize)
    {
        await permissions.RequireViewOnAnyPropertyAsync(caller).ConfigureAwait(false);
        return await register.SearchAsync(query, page, pageSize).ConfigureAwait(false);
    }

    public async Task<byte[]> GetSdsPdfAsync(ChemicalCaller caller, string fileName)
    {
        await permissions.RequireViewOnAnyPropertyAsync(caller).ConfigureAwait(false);
        var name = fileName?.Trim() ?? string.Empty;
        if (name.Length > MaxSdsFileNameLength
            || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("That is not an SDS file name.");
        }

        // Only names the register knows: the backend is not an open proxy to chemicalbase.
        // A blank name or the md5 of an empty upload means "no SDS" and is never known.
        if (!await register.SdsFileExistsAsync(name).ConfigureAwait(false))
        {
            throw new ChemicalNotFoundException($"No product in the register has the SDS '{name}'.");
        }

        // null = 404 at chemicalbase; an empty 200 body is no PDF either.
        var pdf = await chemicalBase.DownloadSdsAsync(name).ConfigureAwait(false);
        return pdf is { Length: > 0 }
            ? pdf
            : throw new ChemicalNotFoundException($"chemicalbase has no SDS named '{name}'.");
    }
}
