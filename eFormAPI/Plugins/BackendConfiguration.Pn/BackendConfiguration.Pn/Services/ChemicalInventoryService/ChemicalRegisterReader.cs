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
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ChemicalsBase.Infrastructure;
using ChemicalsBase.Infrastructure.Data.Entities;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using BmdTexts = Microting.EformBackendConfigurationBase.Infrastructure.Const.Constants;

public class ChemicalRegisterReader(ChemicalsDbContext chemicalsDbContext) : IChemicalRegisterReader
{
    /// <summary>
    /// md5 of zero bytes. eform-angular-chemical-plugin's UploadPdf hashes the
    /// stream without rewinding it, so this name identifies no document.
    /// </summary>
    internal const string EmptyContentMd5 = "d41d8cd98f00b204e9800998ecf8427e";

    private const string Removed = Constants.WorkflowStates.Removed;
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 50;

    // BMD texts end in "(H302)", "(H360Fd)" or "(EUH 001)".
    private static readonly Regex HazardCode = new(@"\s*\(((?:EU)?H\s?[0-9A-Za-z+]+)\)\s*$", RegexOptions.Compiled);

    private static readonly Expression<Func<Chemical, bool>> IsActiveChemical = c => c.WorkflowState != Removed;

    private static readonly Expression<Func<Product, bool>> IsActiveProduct = p => p.WorkflowState != Removed;

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
        var withBarcode = ChemicalIdsWithBarcode(ChemicalBarcode.Candidates(barcode));
        return await LoadAsync(WithDetails().Where(IsActiveChemical).Where(c => withBarcode.Contains(c.Id)))
            .ConfigureAwait(false);
    }

    public async Task<ChemicalRegisterPageModel> SearchAsync(string query, int page, int pageSize)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length < 2)
        {
            throw new ArgumentException("Search for at least two characters.");
        }

        var size = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        if (page < 0 || page > int.MaxValue / size)
        {
            throw new ArgumentException($"page must be between 0 and {int.MaxValue / size}.");
        }

        var withBarcode = ChemicalIdsWithBarcode(
            text.Length is >= 6 and <= 14 && text.All(char.IsAsciiDigit) ? ChemicalBarcode.Candidates(text) : []);

        var matches = chemicalsDbContext.Chemicals.AsNoTracking()
            .Where(IsActiveChemical)
            .Where(c => c.Name.Contains(text) || c.RegistrationNo.Contains(text) || withBarcode.Contains(c.Id));

        var total = await matches.CountAsync().ConfigureAwait(false);
        var ids = await ByName(matches)
            .Skip(page * size).Take(size)
            .Select(c => c.Id)
            .ToListAsync().ConfigureAwait(false);
        var entries = await LoadAsync(WithDetails().Where(c => ids.Contains(c.Id))).ConfigureAwait(false);

        return new ChemicalRegisterPageModel(entries, total);
    }

    public async Task<ChemicalProductRef> FindProductAsync(int chemicalId, int? productId)
    {
        var chemical = await chemicalsDbContext.Chemicals.AsNoTracking()
                           .Where(IsActiveChemical)
                           .Where(c => c.Id == chemicalId)
                           .Select(c => new { c.Id, c.Status })
                           .FirstOrDefaultAsync().ConfigureAwait(false)
                       ?? throw new ChemicalNotFoundException($"Chemical {chemicalId} is not in the register.");

        if (productId is not null
            && !await ActiveProducts().AnyAsync(p => p.Id == productId && p.ChemicalId == chemicalId).ConfigureAwait(false))
        {
            throw new ArgumentException($"Product {productId} does not belong to chemical {chemicalId}.");
        }

        return new ChemicalProductRef(chemical.Id, chemical.Status, productId);
    }

    public async Task<string> FindSdsFileNameAsync(string fileName)
    {
        var sdsFileName = SdsFileName(fileName);
        if (sdsFileName == null)
        {
            return null;
        }

        var stored = await ActiveProducts()
            .Where(p => p.FileName == sdsFileName)
            .Select(p => p.FileName)
            .FirstOrDefaultAsync().ConfigureAwait(false);
        return SdsFileName(stored);
    }

    private IQueryable<Product> ActiveProducts() => chemicalsDbContext.Products.AsNoTracking().Where(IsActiveProduct);

    /// <summary>Chemicals with an active product carrying one of the barcode spellings (a subquery, not materialised).</summary>
    private IQueryable<int> ChemicalIdsWithBarcode(IReadOnlyCollection<string> barcodes)
    {
        var candidates = barcodes.ToArray();
        return ActiveProducts()
            .Where(p => p.Barcode != null && candidates.Contains(p.Barcode))
            .Select(p => p.ChemicalId);
    }

    private IQueryable<Chemical> WithDetails() =>
        chemicalsDbContext.Chemicals.AsNoTracking()
            .Include(c => c.Products)
            .Include(c => c.ActiveSubstances)
            .Include(c => c.AuthorisationHolder)
            .Include(c => c.ClassificationAndLabeling).ThenInclude(cl => cl.CLP).ThenInclude(clp => clp.HazardStatements)
            .AsSplitQuery();

    /// <summary>The one ordering of register lists, applied in SQL so pages and their contents agree with the collation.</summary>
    private static IOrderedQueryable<Chemical> ByName(IQueryable<Chemical> query) =>
        query.OrderBy(c => c.Name).ThenBy(c => c.Id);

    private static async Task<IReadOnlyList<ChemicalRegisterEntryModel>> LoadAsync(IQueryable<Chemical> query)
    {
        var chemicals = await ByName(query).ToListAsync().ConfigureAwait(false);
        return chemicals.Select(Map).ToList();
    }

    private static bool IsActive(BaseEntity entity) => entity.WorkflowState != Removed;

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
                .Where(h => IsActive(h) && h.Statement is { } s && BmdTexts.HazardStatement.ContainsKey(s))
                .Select(h => SplitHazardStatement(BmdTexts.HazardStatement[h.Statement!.Value]))
                .Distinct()
                .OrderBy(h => h.Code, StringComparer.Ordinal)
                .ToList(),
            (chemical.ActiveSubstances ?? [])
                .Where(IsActive)
                .OrderBy(a => a.Name)
                .Select(a => new ChemicalActiveSubstanceModel(a.Name, a.CASNo ?? string.Empty, Concentration(a)))
                .ToList(),
            chemical.AuthorisationHolder?.Name ?? string.Empty,
            (chemical.Products ?? [])
                .Where(IsActive)
                .OrderBy(p => p.Id)
                .Select(MapProduct)
                .ToList(),
            DateTime.SpecifyKind(chemical.UpdatedAt, DateTimeKind.Utc));
    }

    private static ChemicalProductModel MapProduct(Product product)
    {
        // The SDS file name is the md5 of the document, so it doubles as the
        // version key the app compares: SdsFileName and SdsChecksum are deliberately equal.
        var sds = SdsFileName(product.FileName) ?? string.Empty;
        return new ChemicalProductModel(product.Id, product.Name ?? string.Empty, product.Barcode ?? string.Empty, sds, sds);
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

    /// <summary>The trimmed SDS file name, or null when there is none (blank, or the md5 of an empty upload).</summary>
    private static string SdsFileName(string fileName)
    {
        var name = fileName?.Trim();
        return string.IsNullOrEmpty(name) || string.Equals(name, EmptyContentMd5, StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    private static string Text(IReadOnlyDictionary<int, string> table, int? key) =>
        key is { } k && table.TryGetValue(k, out var text) ? text : string.Empty;

    private static DateTime? Utc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
