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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;

public static class UserNameComparerHelper
{
    /// <summary>
    /// Case-insensitive name comparer for the CURRENT user's UI language, so
    /// alphabetical lists collate per that language's rules — Danish ('da')
    /// puts æ/ø/å after z (a…zæøå) while English keeps plain a–z order.
    /// Falls back to ordinal for unauthenticated callers (gRPC/background)
    /// or when the user's language cannot be resolved.
    /// </summary>
    public static async Task<StringComparer> GetForCurrentUser(IUserService userService, ILogger logger)
    {
        try
        {
            if (userService.UserId < 1)
            {
                // No authenticated user (gRPC/background callers) — skip the
                // lookup entirely; GetCurrentUserLanguage would throw.
                return StringComparer.OrdinalIgnoreCase;
            }

            var languageCode = (await userService.GetCurrentUserLanguage())?.LanguageCode;
            if (!string.IsNullOrWhiteSpace(languageCode))
            {
                return StringComparer.Create(CultureInfo.GetCultureInfo(languageCode), true);
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e,
                "Could not resolve the current user's language for name sorting; falling back to ordinal");
        }

        return StringComparer.OrdinalIgnoreCase;
    }
}
