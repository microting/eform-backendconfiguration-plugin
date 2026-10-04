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
*/

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// #1372 — which of several live <c>FieldValues</c> rows for one <c>(CaseId, FieldId)</c>
/// holds the current answer.
///
/// <para>A case can carry duplicate rows for one field: reading a case inserts a
/// missing <c>FieldValue</c> without a lock (eform-sdk-dotnet#4904), so two reads insert
/// two rows in the same second, one of them never edited. The case editor saves into
/// whichever row the SDK returns first — usually the LOWEST Id — so "highest Id" is not
/// "most recently written" and the report kept showing the untouched empty row.</para>
///
/// <para>The current row is the last one written: latest <c>UpdatedAt</c> first (an
/// edit bumps it; the column is second-precision, so rows inserted together tie), then a
/// row that has a value over an empty one, then the highest Id. Picture, audio and
/// movie fields are genuinely multi-valued and must not be collapsed by this rule.</para>
/// </summary>
public static class CurrentFieldValueRule
{
    public static IOrderedEnumerable<T> OrderCurrentFirst<T>(
        this IEnumerable<T> rows,
        Func<T, DateTime?> updatedAt,
        Func<T, string> value,
        Func<T, int> id)
        => rows
            .OrderByDescending(updatedAt)
            .ThenByDescending(r => !string.IsNullOrEmpty(value(r)))
            .ThenByDescending(id);
}
