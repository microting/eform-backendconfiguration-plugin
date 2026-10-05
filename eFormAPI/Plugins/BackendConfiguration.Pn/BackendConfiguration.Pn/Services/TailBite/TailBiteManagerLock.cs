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

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Threading.Tasks;

/// <summary>
/// The manager-then-lock shape every TailBite manager write shares: check the caller on the property, take the
/// property lock, and check again on committed state before the work runs.
/// </summary>
internal static class TailBiteManagerLock
{
    public static Task RunAsync(ITailBitePropertyLock propertyLock, ITailBiteAccess access, int callerSiteId, int propertyId, Func<Task> work)
        => RunAsync(propertyLock, access, callerSiteId, propertyId, async () => { await work(); return true; });

    public static async Task<T> RunAsync<T>(ITailBitePropertyLock propertyLock, ITailBiteAccess access, int callerSiteId, int propertyId,
        Func<Task<T>> work)
    {
        await access.RequireManagerAsync(callerSiteId, propertyId);
        return await propertyLock.RunLockedAsync(propertyId, async () =>
        {
            // The pre-lock check may be stale by the time the lock is granted; repeat it on committed state.
            await access.RequireManagerAsync(callerSiteId, propertyId);
            return await work();
        });
    }
}
