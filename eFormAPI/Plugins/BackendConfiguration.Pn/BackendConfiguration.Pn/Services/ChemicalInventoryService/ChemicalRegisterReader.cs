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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ChemicalsBase.Infrastructure;
using ChemicalsBase.Infrastructure.Data.Entities;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using BmdTexts = Microting.EformBackendConfigurationBase.Infrastructure.Const.Constants;

public class ChemicalRegisterReader(ChemicalsDbContext chemicalsDbContext) : IChemicalRegisterReader
{
    /// <summary>
    /// md5 of zero bytes. eform-angular-chemical-plugin's UploadPdf hashes the
    /// stream without rewinding it, so this name identifies no document.
    /// </summary>
    internal const string EmptyContentMd5 = "d41d8cd98f00b204e9800998ecf8427e";

    private const string Removed = "removed";
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 50;

    // BMD texts end in "(H302)", "(H360Fd)" or "(EUH 001)".
    private static readonly Regex HazardCode = new(@"\s*\(((?:EU)?H\s?[0-9A-Za-z+]+)\)\s*$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<ChemicalRegisterEntryModel>> GetByIdsAsync(IReadOnlyCollection<int> chemicalIds)
    {
        if (chemicalIds.Count == 0)
        {
            return [];
        }

        var ids = chemicalIds.Distinct().ToArray();
        return await LoadAsync(WithDetails().Where(c => ids.Contains(c.Id))).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> ChangedSinceAsync(IReadOnlyCollection<int> chemicalIds, DateTime sinceUtc)
    {
        if (chemicalIds.Count == 0)
        {
            return [];
        }

        var ids = chemicalIds.Distinct().ToArray();
        return await chemicalsDbContext.Chemicals.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && (c.UpdatedAt > sinceUtc || c.Products.Any(p => p.UpdatedAt > sinceUtc)))
            .Select(c => c.Id)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChemicalRegisterEntryModel>> LookupBarcodeAsync(string barcode)
    {
        // Every spelling of the GTIN: the register may hold a UPC-A as 12 digits or as its 13-digit EAN form.
        var candidates = ChemicalBarcode.Candidates(barcode).ToArray();
        return await LoadAsync(WithDetails().Where(c => c.WorkflowState != Removed
                && c.Products.Any(p => p.WorkflowState != Removed && p.Barcode != null && candidates.Contains(p.Barcode))))
            .ConfigureAwait(false);
    }

    public async Task<ChemicalRegisterPageModel> SearchAsync(string query, int page, int pageSize)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length < 2)
        {
            throw new ArgumentException("Search for at least two characters.");
        }

        if (page < 0)
        {
            throw new ArgumentException("page must be 0 or greater.");
        }

        var size = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var barcodes = text.Length is >= 6 and <= 14 && text.All(char.IsAsciiDigit)
            ? ChemicalBarcode.Candidates(text).ToArray()
            : [];

        var matches = chemicalsDbContext.Chemicals.AsNoTracking()
            .Where(c => c.WorkflowState != Removed)
            .Where(c => c.Name.Contains(text)
                        || c.RegistrationNo.Contains(text)
                        || c.Products.Any(p => p.WorkflowState != Removed && p.Barcode != null && barcodes.Contains(p.Barcode)));

        var total = await matches.CountAsync().ConfigureAwait(false);
        var ids = await matches.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip(page * size).Take(size)
            .Select(c => c.Id)
            .ToListAsync().ConfigureAwait(false);
        var entries = await LoadAsync(WithDetails().Where(c => ids.Contains(c.Id))).ConfigureAwait(false);

        return new ChemicalRegisterPageModel(entries, total);
    }

    public async Task<ChemicalProductRef> FindProductAsync(int chemicalId, int? productId)
    {
        var chemical = await chemicalsDbContext.Chemicals.AsNoTracking()
                           .Where(c => c.Id == chemicalId && c.WorkflowState != Removed)
                           .Select(c => new { c.Id, c.RemoteId, c.RegistrationNo, c.Status })
                           .FirstOrDefaultAsync().ConfigureAwait(false)
                       ?? throw new ChemicalNotFoundException($"Chemical {chemicalId} is not in the register.");

        if (productId is null)
        {
            return new ChemicalProductRef(chemical.Id, chemical.RemoteId, chemical.RegistrationNo, chemical.Status, null, null, null);
        }

        var product = await chemicalsDbContext.Products.AsNoTracking()
                          .Where(p => p.Id == productId && p.ChemicalId == chemicalId && p.WorkflowState != Removed)
                          .Select(p => new { p.Id, p.Name, p.FileName })
                          .FirstOrDefaultAsync().ConfigureAwait(false)
                      ?? throw new ArgumentException($"Product {productId} does not belong to chemical {chemicalId}.");

        return new ChemicalProductRef(chemical.Id, chemical.RemoteId, chemical.RegistrationNo, chemical.Status,
            product.Id, product.Name, SdsName(product.FileName));
    }

    public Task<bool> SdsFileExistsAsync(string fileName) =>
        SdsName(fileName).Length == 0
            ? Task.FromResult(false)
            : chemicalsDbContext.Products.AsNoTracking()
                .AnyAsync(p => p.FileName == fileName && p.WorkflowState != Removed);

    private IQueryable<Chemical> WithDetails() =>
        chemicalsDbContext.Chemicals.AsNoTracking()
            .Include(c => c.Products)
            .Include(c => c.ActiveSubstances)
            .Include(c => c.AuthorisationHolder)
            .Include(c => c.ClassificationAndLabeling).ThenInclude(cl => cl.CLP).ThenInclude(clp => clp.HazardStatements)
            .AsSplitQuery();

    private static async Task<IReadOnlyList<ChemicalRegisterEntryModel>> LoadAsync(IQueryable<Chemical> query)
    {
        var chemicals = await query.ToListAsync().ConfigureAwait(false);
        return chemicals.OrderBy(c => c.Name).ThenBy(c => c.Id).Select(Map).ToList();
    }

    private static ChemicalRegisterEntryModel Map(Chemical chemical)
    {
        var clp = chemical.ClassificationAndLabeling?.CLP;
        return new ChemicalRegisterEntryModel(
            chemical.Id,
            chemical.Name,
            chemical.RegistrationNo,
            chemical.Status,
            Text(BmdTexts.ProductStatusType, chemical.Status),
            Utc(chemical.SalesDeadline),
            Utc(chemical.UseAndPossesionDeadline),
            Utc(chemical.AuthorisationDate),
            Utc(chemical.AuthorisationExpirationDate),
            Utc(chemical.AuthorisationTerminationDate),
            (clp?.HazardPictograms ?? []).Where(p => p is >= 1 and <= 9).Distinct().Order()
                .Select(p => $"GHS{p:00}").ToList(),
            clp?.SignalWord,
            Text(BmdTexts.SignalWord, clp?.SignalWord),
            (clp?.HazardStatements ?? [])
                .Where(h => h.WorkflowState != Removed && h.Statement is { } s && BmdTexts.HazardStatement.ContainsKey(s))
                .Select(h => SplitHazardStatement(BmdTexts.HazardStatement[h.Statement!.Value]))
                .Distinct()
                .OrderBy(h => h.Code, StringComparer.Ordinal)
                .ToList(),
            (chemical.ActiveSubstances ?? [])
                .Where(a => a.WorkflowState != Removed)
                .OrderBy(a => a.Name)
                .Select(a => new ChemicalActiveSubstanceModel(a.Name, a.CASNo ?? string.Empty, Concentration(a)))
                .ToList(),
            chemical.AuthorisationHolder?.Name ?? string.Empty,
            (chemical.Products ?? [])
                .Where(p => p.WorkflowState != Removed)
                .OrderBy(p => p.Id)
                .Select(p => new ChemicalProductModel(p.Id, p.Name ?? string.Empty, p.Barcode ?? string.Empty,
                    SdsName(p.FileName), SdsName(p.FileName)))
                .ToList(),
            DateTime.SpecifyKind(chemical.UpdatedAt, DateTimeKind.Utc));
    }

    /// <summary>"Farlig ved indtagelse (H302)" → ("H302", "Farlig ved indtagelse"); "(EUH 001)" → "EUH001".</summary>
    internal static ChemicalHazardStatementModel SplitHazardStatement(string text)
    {
        var match = HazardCode.Match(text);
        return match.Success
            ? new ChemicalHazardStatementModel(match.Groups[1].Value.Replace(" ", string.Empty), text[..match.Index].Trim())
            : new ChemicalHazardStatementModel(string.Empty, text.Trim());
    }

    private static string Concentration(ActiveSubstance substance)
    {
        if (substance.Concentration is not { } value)
        {
            return string.Empty;
        }

        var unit = Text(BmdTexts.Unit, substance.Unit);
        var number = value.ToString("0.###", CultureInfo.InvariantCulture);
        return unit.Length == 0 ? number : $"{number} {unit}";
    }

    /// <summary>The SDS file name, or empty when there is none (blank, or the md5 of an empty upload).</summary>
    private static string SdsName(string fileName) =>
        string.IsNullOrWhiteSpace(fileName) || fileName.Trim() == EmptyContentMd5 ? string.Empty : fileName;

    private static string Text(IReadOnlyDictionary<int, string> table, int? key) =>
        key is { } k && table.TryGetValue(k, out var text) ? text : string.Empty;

    private static DateTime? Utc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
