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

#nullable enable

namespace BackendConfiguration.Pn.Services.InboundMail;

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microting.eForm.Dto;
using Microting.eFormApi.BasePn.Abstractions;

public interface ICustomerNoProvider
{
    /// <summary>The customer number, or 0 when the SDK setting is missing or not a positive number.</summary>
    Task<int> GetAsync();
}

/// <summary>The tenant's customer number, from the SDK setting the host writes at startup.</summary>
public class CustomerNoProvider(IEFormCoreService coreService, ILogger<CustomerNoProvider> logger) : ICustomerNoProvider
{
    private int _cached; // 0 = not cached

    public async Task<int> GetAsync()
    {
        var cached = Volatile.Read(ref _cached);
        if (cached > 0) return cached;

        var core = await coreService.GetCore();
        var raw = await core.GetSdkSetting(Settings.customerNo);
        if (!TryParse(raw, out var customerNo))
        {
            logger.LogWarning("SDK setting customerNo is not a positive number; inbound mail requests will be refused");
            return 0;
        }

        Volatile.Write(ref _cached, customerNo);
        return customerNo;
    }

    public static bool TryParse(string? raw, out int customerNo) =>
        int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out customerNo) && customerNo > 0;
}
