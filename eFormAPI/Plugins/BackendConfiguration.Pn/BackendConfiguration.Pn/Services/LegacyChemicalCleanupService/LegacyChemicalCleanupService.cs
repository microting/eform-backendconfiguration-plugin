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

namespace BackendConfiguration.Pn.Services.LegacyChemicalCleanupService;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;

public sealed record LegacyChemicalCleanupResult(int AreaProperties, int Cases, int EntityGroups);

public class LegacyChemicalCleanupService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    ILegacyChemicalSdkOperations sdkOperations,
    ILogger<LegacyChemicalCleanupService> logger)
{
    public const string MarkerName = "BackendConfigurationBaseSettings:LegacyChemicalFlowRemoved";

    public Task RunIfNeededAsync() => throw new NotImplementedException();

    public Task<LegacyChemicalCleanupResult> CleanupAsync() => throw new NotImplementedException();
}
