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

namespace BackendConfiguration.Pn.Services.LegacyChemicalCleanupService;

using System.Threading.Tasks;

/// <summary>
/// The SDK calls the cleanup makes that talk to the Microting cloud. Core is
/// concrete with non-virtual members, so this seam is what lets the cleanup be
/// tested without cloud credentials. It covers the cleanup's deletes of cases
/// (deployed into the legacy folders, and the planned cases that
/// BackendConfigurationPropertyAreasServiceHelper.DeleteAreaPropertyAsync is
/// handed this seam for) and of the legacy entity lists. The rest of
/// DeleteAreaPropertyAsync (folders, entity group) still goes through the real Core.
/// </summary>
public interface ILegacyChemicalSdkOperations
{
    /// <summary>Throws when the case was not deleted or the cloud did not answer in time.</summary>
    Task DeleteCaseAsync(int microtingUid);

    Task DeleteEntityGroupAsync(string entityGroupMicrotingUid);
}
