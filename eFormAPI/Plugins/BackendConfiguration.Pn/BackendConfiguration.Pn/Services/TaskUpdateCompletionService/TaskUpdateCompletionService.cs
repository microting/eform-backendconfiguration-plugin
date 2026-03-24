/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

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

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.TaskUpdateCompletionService;

public class TaskUpdateCompletionService : ITaskUpdateCompletionService
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _pending = new();

    public void Register(int userId)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[userId] = tcs;
    }

    public void Complete(int userId)
    {
        if (_pending.TryRemove(userId, out var tcs))
        {
            tcs.TrySetResult(true);
        }
    }

    public async Task WaitForCompletionAsync(int userId, TimeSpan timeout)
    {
        if (!_pending.TryGetValue(userId, out var tcs))
        {
            // No pending update for this user — proceed immediately
            return;
        }

        try
        {
            await tcs.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Fallback: the handler took too long; proceed with whatever is in the DB
            _pending.TryRemove(userId, out _);
        }
    }
}
