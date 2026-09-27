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
using System.Globalization;

/// <summary>
/// The one order for a task's tag names everywhere they are listed — task list,
/// calendar, compliance Detaljer/Rapport and their exports, the team list (#1334).
/// Always Danish (a…z, æ, ø, å), case-insensitive, regardless of the user's UI
/// language, so an export is the same whoever downloads it. Needs ICU at runtime
/// (InvariantGlobalization must stay off in the host).
/// </summary>
public static class TagNameComparer
{
    public static readonly StringComparer Danish =
        StringComparer.Create(CultureInfo.GetCultureInfo("da-DK"), ignoreCase: true);
}
