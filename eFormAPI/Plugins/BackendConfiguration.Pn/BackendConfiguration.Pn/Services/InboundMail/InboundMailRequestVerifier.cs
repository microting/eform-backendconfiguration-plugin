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

namespace BackendConfiguration.Pn.Services.InboundMail;

using System;
using System.Collections.Concurrent;
using System.Globalization;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

/// <summary>
/// Verifies the HMAC request signature of inbound hub calls. Register as a singleton: the
/// replay store lives on the instance and must be shared across requests.
/// </summary>
public class InboundMailRequestVerifier(IOptions<InboundMailHubOptions> options)
{
    private static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(10);

    // "{customerNo}:{requestId}" -> expiry. TryAdd makes the replay check atomic.
    private readonly ConcurrentDictionary<string, DateTime> _seen = new();

    /// <summary>
    /// The checks that need no body: a signing key is configured, the headers are present and well-formed,
    /// the customer number matches and the Date is within the allowed skew. Lets the controller refuse an
    /// unsigned request before buffering its body; <see cref="Verify"/> repeats them.
    /// </summary>
    public bool HeadersPlausible(IHeaderDictionary headers, int customerNo, DateTime nowUtc) =>
        HeadersPlausible(headers, customerNo.ToString(CultureInfo.InvariantCulture), nowUtc);

    private bool HeadersPlausible(IHeaderDictionary headers, string customerNoText, DateTime nowUtc) =>
        !string.IsNullOrEmpty(options.Value.TenantSigningKey)
        && headers["Authorization"].ToString().StartsWith(InboundMailSignature.Scheme, StringComparison.Ordinal)
        && Guid.TryParse(headers["X-Request-Id"].ToString(), out _)
        && headers["X-Customer-No"].ToString() == customerNoText
        && DateTime.TryParseExact(headers["Date"].ToString(), "R", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var sent)
        && (nowUtc - sent).Duration() <= MaxSkew;

    public bool Verify(string method, string path, IHeaderDictionary headers, byte[] body, int customerNo, DateTime nowUtc)
    {
        var customerNoText = customerNo.ToString(CultureInfo.InvariantCulture);
        if (!HeadersPlausible(headers, customerNoText, nowUtc))
            return false;

        var key = options.Value.TenantSigningKey;
        var auth = headers["Authorization"].ToString();
        var date = headers["Date"].ToString();
        var requestId = headers["X-Request-Id"].ToString();
        var expected = InboundMailSignature.Sign(key, InboundMailSignature.Canonical(method, path, customerNo,
            requestId, date, InboundMailSignature.BodyHash(body)));
        if (!InboundMailSignature.FixedTimeEquals(expected, auth[InboundMailSignature.Scheme.Length..].Trim()))
            return false;

        // Only signature-valid requests reach here, so the store cannot be filled by unauthenticated callers.
        Prune(nowUtc);
        return _seen.TryAdd($"{customerNoText}:{requestId}", nowUtc + ReplayWindow);
    }

    private void Prune(DateTime nowUtc)
    {
        foreach (var entry in _seen)
        {
            if (entry.Value <= nowUtc)
                _seen.TryRemove(entry);
        }
    }
}
