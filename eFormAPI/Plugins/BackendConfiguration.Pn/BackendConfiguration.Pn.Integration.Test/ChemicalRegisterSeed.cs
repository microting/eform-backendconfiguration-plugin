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

using ChemicalsBase.Infrastructure;
using ChemicalsBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

public sealed record SeededChemical(int ChemicalId, int ProductId, string RemoteId);

/// <summary>
/// Minimal but complete register rows (the ChemicalsBase model has required
/// navigations and JSON-converted collections that must not be null).
/// </summary>
public static class ChemicalRegisterSeed
{
    public static async Task<SeededChemical> AddChemicalAsync(
        ChemicalsDbContext db, string name, string registrationNo, int status = 5,
        DateTime? salesDeadline = null, DateTime? useAndPossessionDeadline = null,
        string? barcode = null, string? sdsFileName = null)
    {
        var chemical = new Chemical
        {
            Name = name,
            RegistrationNo = registrationNo,
            RemoteId = Guid.NewGuid().ToString("N"),
            Status = status,
            SalesDeadline = salesDeadline,
            UseAndPossesionDeadline = useAndPossessionDeadline,
            AuthorisationHolder = new AuthorisationHolder
            {
                RemoteId = Guid.NewGuid().ToString("N"), Name = "Holder A/S", Address = new Address { Country = 45 },
            },
            ClassificationAndLabeling = new ClassificationAndLabeling
            {
                // HazardStatement key 1 = "Ustabilt eksplosiv (H200)"; pictograms 2 and 7 = GHS02, GHS07; signal word 1 = "Fare".
                CLP = new CLP
                {
                    HazardPictograms = new List<int> { 7, 2 }, SignalWord = 1,
                    HazardStatements = new List<HazardStatement> { new() { Statement = 1 } },
                },
                DPD = new DPD { RiskPhrases = new List<int>() },
            },
            BiocideUser = new List<int>(),
            PesticideProductGroup = new List<int>(),
            BiocideProductType = new List<int>(),
            PesticidePossibleUse = new List<int>(),
            BiocidePossibleUse = new List<int>(),
            BiocideSpecialUse = new List<int>(),
            BarcodeValue = new List<string>(),
            ActiveSubstances = new List<ActiveSubstance>
            {
                // Unit 2 = "g/l".
                new() { RemoteId = Guid.NewGuid().ToString("N"), Name = "Glyphosat", CASNo = "1071-83-6", Concentration = 360, Unit = 2 },
            },
            Products = new List<Product>
            {
                new()
                {
                    Name = $"{name} 1 L", Barcode = barcode, FileName = sdsFileName ?? string.Empty,
                    Checksum = string.Empty, IsActive = true, IsValid = true, Verified = true,
                },
            },
        };
        db.Chemicals.Add(chemical);
        await db.SaveChangesAsync();
        return new SeededChemical(chemical.Id, chemical.Products.Single().Id, chemical.RemoteId);
    }

    /// <summary>A random GTIN of <paramref name="length"/> digits (8, 12, 13 or 14) with a valid GS1 check digit.</summary>
    public static string RandomGtin(int length)
    {
        var payload = RandomDigits(length - 1);
        var sum = payload.Reverse().Select((c, i) => (c - '0') * (i % 2 == 0 ? 3 : 1)).Sum();
        return payload + (char)('0' + (10 - sum % 10) % 10);
    }

    public static string RandomDigits(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(i => i == 0 ? Random.Shared.Next(1, 10) : Random.Shared.Next(0, 10)));
}
