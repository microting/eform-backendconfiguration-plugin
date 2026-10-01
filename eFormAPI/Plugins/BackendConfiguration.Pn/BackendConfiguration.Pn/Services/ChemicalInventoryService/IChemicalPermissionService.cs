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

/// <param name="AccessChangedAt">
/// Latest UpdatedAt of the permission row and the PropertyWorker row. A sync
/// token older than this means the property may be newly visible and must be
/// sent in full.
/// </param>
public sealed record ChemicalPropertyAccessRow(ChemicalPropertyAccessModel Access, DateTime AccessChangedAt);

public interface IChemicalPermissionService
{
    /// <summary>Properties the caller may see (View or Admin), ordered by name. Web admins see every active property.</summary>
    Task<IReadOnlyList<ChemicalPropertyAccessRow>> ListVisiblePropertiesAsync(ChemicalCaller caller);

    /// <summary>
    /// Throws <see cref="ChemicalNotFoundException"/> for a missing or removed
    /// property and <see cref="ChemicalPermissionDeniedException"/> when the
    /// caller lacks the flag (Admin implies all; web admins always pass).
    /// </summary>
    Task RequireAsync(ChemicalCaller caller, int propertyId, ChemicalPermission permission);

    /// <summary>For register-wide actions (search, lookup, SDS): View on at least one property.</summary>
    Task RequireViewOnAnyPropertyAsync(ChemicalCaller caller);
}
