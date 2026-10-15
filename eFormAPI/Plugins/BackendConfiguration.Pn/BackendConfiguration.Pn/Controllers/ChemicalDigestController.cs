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

namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Services.BackendConfigurationLocalizationService;
using Services.ChemicalReporting;

/// <summary>W5 "Eksempel på næste oversigt" (spec §11). Same prefix and policy as ChemicalsController.</summary>
[Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
// TODO(chemistry-GA): remove this gate (and its tests in ChemicalDigestControllerTests) with the one on ChemicalsController.
[FirstUserOnly]
[Route("api/backend-configuration-pn/chemicals")]
public class ChemicalDigestController(
    IChemicalDigestPreviewBuilder previewBuilder,
    IBackendConfigurationLocalizationService localizationService,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("properties/{propertyId:int}/digest-preview")]
    public async Task<OperationDataResult<ChemicalDigestModel>> GetDigestPreview(int propertyId)
    {
        try
        {
            return new OperationDataResult<ChemicalDigestModel>(true,
                await previewBuilder.BuildAsync(propertyId, timeProvider.GetUtcNow().UtcDateTime).ConfigureAwait(false));
        }
        catch (Exception e)
        {
            return ChemicalsController.FailureResult<ChemicalDigestModel>(
                e, localizationService, ChemicalLocalizationKeys.ErrorWhileReadingChemicalDigest);
        }
    }
}
