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

public abstract class TailBiteException(string m, Exception? inner = null) : Exception(m, inner);
public class TailBiteNotFoundException(string m) : TailBiteException(m);

public class TailBiteForbiddenException(string m) : TailBiteException(m)
{
    /// <summary>
    /// The one message for a missing id and for an id the caller may not use, so a manager method cannot be used to
    /// probe which ids exist.
    /// </summary>
    public const string NotFoundOrNoAccess = "Not found or no access.";

    public static TailBiteForbiddenException NoAccess() => new(NotFoundOrNoAccess);
}

public class TailBiteValidationException(string m) : TailBiteException(m);

public class TailBiteConflictException : TailBiteException
{
    public TailBiteConflictException(string m) : base(m) { }
    public TailBiteConflictException(string m, Exception inner) : base(m, inner) { }
}
