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


namespace BackendConfiguration.Pn.Services.ChemicalInventoryService;

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class ChemicalInventoryServiceCollectionExtensions
{
    /// <summary>
    /// Everything ChemicalsGrpcService and ChemicalsController need. The host
    /// provides BaseDbContext, IEFormCoreService and the DbContexts; the plugin
    /// registers IAdhocPhotoStorage itself.
    /// </summary>
    public static IServiceCollection AddChemicalInventory(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<IChemicalPermissionService, ChemicalPermissionService>();
        services.AddTransient<IChemicalRegisterReader, ChemicalRegisterReader>();
        services.AddTransient<IChemicalNameDirectory, ChemicalNameDirectory>();
        services.AddTransient<IChemicalInventoryService, ChemicalInventoryService>();
        services.AddHttpClient<IChemicalBaseClient, ChemicalBaseClient>((provider, http) =>
        {
            // ChemicalBaseClient bounds each SDS download itself (SdsDownloadTimeout)
            // and builds its URLs from the options; this is only a backstop.
            var options = provider.GetRequiredService<IOptions<ChemicalBaseOptions>>().Value;
            http.Timeout = options.SdsDownloadTimeout + TimeSpan.FromSeconds(30);
        });
        return services;
    }

    /// <summary>Binds ChemicalBase:BaseUrl (env ChemicalBase__BaseUrl).</summary>
    public static IServiceCollection AddChemicalBaseOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChemicalBaseOptions>().Bind(configuration.GetSection(ChemicalBaseOptions.SectionName));
        return services;
    }
}
