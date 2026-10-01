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
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

// Request bodies for routes that carry the id in the path (ChemicalsController).
// ToCommand joins the route id and the body into the service command.

public sealed record ChemicalMovePlacementBody(int TargetLocationId, string TargetPlacementNote, decimal? Amount)
{
    public ChemicalMovePlacementCommand ToCommand(int placementId) =>
        new(placementId, TargetLocationId, TargetPlacementNote, Amount);
}

public sealed record ChemicalRemovePlacementBody(ChemicalRemovalReasonEnum Reason, DateTime? RemovedAt, string Note)
{
    public ChemicalRemovePlacementCommand ToCommand(int placementId) => new(placementId, Reason, RemovedAt, Note);
}

public sealed record ChemicalPlacementNoteBody(string PlacementNote);

public sealed record ChemicalStockEntryBody(ChemicalStockEntryKindEnum Kind, ChemicalStockAmountModel Amount)
{
    public ChemicalAddStockEntryCommand ToCommand(int placementId) => new(placementId, Kind, Amount);
}

public sealed record ChemicalUpdateLocationBody(string Name, string Description, int? SortOrder)
{
    public ChemicalUpdateLocationCommand ToCommand(int locationId) => new(locationId, Name, Description, SortOrder);
}

/// <summary>DigestRecipients null (absent from the JSON) = no recipients.</summary>
public sealed record ChemicalSettingsBody(bool StockEnabled, List<string> DigestRecipients)
{
    public ChemicalSetSettingsCommand ToCommand(int propertyId) => new(propertyId, StockEnabled, DigestRecipients ?? []);
}
