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

namespace BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;

using System;

/// <summary>
/// Thrown when a caller asks for an <c>AdhocTag</c> that does not exist or is
/// soft-deleted (<c>WorkflowState == Removed</c>). Distinct from
/// <see cref="AdhocTaskNotFoundException"/> so façades (B5 gRPC / B6 REST) -
/// and callers reading the exception message - can tell a missing tag apart
/// from a missing task; both still map to <c>NotFound</c>.
/// </summary>
public class AdhocTagNotFoundException : Exception
{
    public int TagId { get; }

    public AdhocTagNotFoundException(int tagId)
        : base($"Adhoc tag {tagId} was not found.")
    {
        TagId = tagId;
    }
}
