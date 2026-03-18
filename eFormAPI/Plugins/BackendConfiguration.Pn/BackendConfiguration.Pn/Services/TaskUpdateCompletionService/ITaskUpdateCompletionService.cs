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
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.TaskUpdateCompletionService;

public interface ITaskUpdateCompletionService
{
    /// <summary>
    /// Registers a pending update for the given user. Must be called before sending the bus message.
    /// </summary>
    void Register(int userId);

    /// <summary>
    /// Signals that the update for the given user has been fully committed to the database.
    /// </summary>
    void Complete(int userId);

    /// <summary>
    /// Waits until the pending update for the given user completes, or until the timeout elapses.
    /// Returns immediately if no pending update is registered for the user.
    /// </summary>
    Task WaitForCompletionAsync(int userId, TimeSpan timeout);
}
