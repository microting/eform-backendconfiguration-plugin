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

/// <summary>The six chemical permission flags of spec §8.</summary>
public enum ChemicalPermission
{
    View,
    Register,
    Remove,
    Stock,
    ManageLocations,
    Admin
}

/// <summary>Flags of one worker on one property, as stored (Admin not expanded).</summary>
public sealed record ChemicalPermissionFlagsModel(
    bool View, bool Register, bool Remove, bool Stock, bool ManageLocations, bool Admin)
{
    public static readonly ChemicalPermissionFlagsModel None = new(false, false, false, false, false, false);

    public static readonly ChemicalPermissionFlagsModel All = new(true, true, true, true, true, true);

    /// <summary>Admin implies every flag (spec §8).</summary>
    public ChemicalPermissionFlagsModel Effective() => Admin ? All : this;

    public bool Allows(ChemicalPermission permission)
    {
        var effective = Effective();
        return permission switch
        {
            ChemicalPermission.View => effective.View,
            ChemicalPermission.Register => effective.Register,
            ChemicalPermission.Remove => effective.Remove,
            ChemicalPermission.Stock => effective.Stock,
            ChemicalPermission.ManageLocations => effective.ManageLocations,
            ChemicalPermission.Admin => effective.Admin,
            _ => false
        };
    }
}

/// <summary>One worker's stored flags on a property, for the permission screens (app 8, web W4).</summary>
public sealed record ChemicalWorkerPermissionModel(int WorkerId, string WorkerName, ChemicalPermissionFlagsModel Flags);
