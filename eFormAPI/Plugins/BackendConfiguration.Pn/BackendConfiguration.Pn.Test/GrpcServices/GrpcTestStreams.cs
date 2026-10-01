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


using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace BackendConfiguration.Pn.Test.GrpcServices;

// In-memory gRPC streams shared by the gRPC service unit fixtures.

/// <summary>A client stream that yields the given messages, then ends.</summary>
internal sealed class FakeAsyncStreamReader<T>(IEnumerable<T> items) : IAsyncStreamReader<T> where T : class
{
    private readonly Queue<T> _items = new(items);

    public T Current { get; private set; }

    /// <summary>Messages not read yet.</summary>
    public int Remaining => _items.Count;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (_items.Count == 0)
        {
            Current = null;
            return Task.FromResult(false);
        }

        Current = _items.Dequeue();
        return Task.FromResult(true);
    }
}

/// <summary>A server stream that records every message written to it.</summary>
internal sealed class FakeServerStreamWriter<T> : IServerStreamWriter<T>
{
    public List<T> Written { get; } = [];

    public WriteOptions WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        Written.Add(message);
        return Task.CompletedTask;
    }

    public Task WriteAsync(T message, CancellationToken cancellationToken) => WriteAsync(message);
}
