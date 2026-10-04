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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>Request signature shared with the central inbound mail service (see the tenant-side spec).</summary>
public static class InboundMailSignature
{
    public const string Scheme = "HMAC-SHA256 ";

    public static string BodyHash(byte[] body) => Hex(SHA256.HashData(body));

    public static string Canonical(string method, string path, int customerNo, string requestId, string date, string bodyHash) =>
        $"{method.ToUpperInvariant()}\n{path}\n{customerNo.ToString(CultureInfo.InvariantCulture)}\n{requestId}\n{date}\n{bodyHash}";

    public static string Sign(string tenantKey, string canonical) =>
        Hex(HMACSHA256.HashData(Encoding.UTF8.GetBytes(tenantKey), Encoding.UTF8.GetBytes(canonical)));

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
