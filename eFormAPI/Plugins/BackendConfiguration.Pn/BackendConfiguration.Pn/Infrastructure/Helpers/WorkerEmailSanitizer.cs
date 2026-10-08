#nullable enable
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

using System.Globalization;
using System.Linq;

/// <summary>
/// Removes characters a person cannot see from a worker's e-mail address before it
/// is stored or used as a login name. Addresses copied from mail clients, phones or
/// web pages often carry invisible formatting marks such as U+200E (left-to-right
/// mark), U+200B (zero-width space) or U+FEFF; Identity then refuses the address as
/// a user name, so the worker silently never gets a login and "Set password" has
/// nothing to act on.
/// Only format (Cf) and control (Cc) characters are removed, and surrounding
/// white space (including non-breaking spaces) is trimmed. Visible characters,
/// letters such as æ, ø and å included, are never changed.
/// </summary>
public static class WorkerEmailSanitizer
{
    public static string? Clean(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return email;
        }

        var visible = new string(email.Where(c =>
        {
            var category = char.GetUnicodeCategory(c);
            return category != UnicodeCategory.Format && category != UnicodeCategory.Control;
        }).ToArray());

        return visible.Trim();
    }
}
