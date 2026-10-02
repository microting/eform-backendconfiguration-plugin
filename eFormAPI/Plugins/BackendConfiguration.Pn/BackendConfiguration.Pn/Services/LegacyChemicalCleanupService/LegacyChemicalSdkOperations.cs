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

using System;
using System.Threading.Tasks;
using Microting.eFormApi.BasePn.Abstractions;

public class LegacyChemicalSdkOperations(IEFormCoreService coreHelper) : ILegacyChemicalSdkOperations
{
    public async Task DeleteCaseAsync(int microtingUid)
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await EnsureCaseDeletedAsync(core.CaseDelete(microtingUid), microtingUid, CaseDeleteTimeout)
            .ConfigureAwait(false);
    }

    internal static readonly TimeSpan CaseDeleteTimeout = TimeSpan.FromMinutes(2);

    // Red-phase seam: still ignores the result and waits without a bound.
    internal static async Task EnsureCaseDeletedAsync(Task<bool> caseDelete, int microtingUid, TimeSpan timeout)
    {
        await caseDelete.ConfigureAwait(false);
    }

    public async Task DeleteEntityGroupAsync(string entityGroupMicrotingUid)
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await core.EntityGroupDelete(entityGroupMicrotingUid).ConfigureAwait(false);
    }
}
