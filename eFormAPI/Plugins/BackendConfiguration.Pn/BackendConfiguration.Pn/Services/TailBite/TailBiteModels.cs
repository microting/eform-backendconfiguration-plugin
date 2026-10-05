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

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Security.Cryptography;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

public static class TailBiteDefaults
{
    // The proposed seeded default rule (Global Constraints): root, 5 bitten pigs or 1 severe within 7 days, summed per stable.
    public static TailBiteRule DefaultRule(int rootLocationId) => new()
    {
        LocationId = rootLocationId, MinBittenPigs = 5, MinSevere = 1, WindowDays = 7, CountDepth = 1
    };

    public static readonly (string Code, string Name)[] ActionTypes =
    [
        ("FLYTTET", "Flyttet grise"), ("TAGET_UD", "Taget ud"), ("BEHANDLET", "Behandlet"), ("REB", "Reb / rodemateriale"),
        ("HALM", "Halm"), ("PULVER", "Pulver på hale"), ("ANDET", "Andet")
    ];

    // 16 random bytes, base64url, no padding: 22 characters.
    public static string NewQrCode()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
