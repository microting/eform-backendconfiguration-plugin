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

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Services.BackendConfigurationWorkerTagsService;

/// <summary>
/// Worker groups ("teams"): the SDK tags that actually have worker members, as opposed
/// to the eForm/template tags that share the same <c>Tags</c> table. Separate from
/// <see cref="FileTagsController"/> (document tags) and from the core
/// <c>/api/tags/index</c> (all tags, template tagging included).
/// </summary>
[Authorize]
[Route("api/backend-configuration-pn/worker-tags")]
public class WorkerTagsController(IBackendConfigurationWorkerTagsService workerTagsService) : Controller
{
    [HttpGet]
    public async Task<OperationDataResult<List<CommonDictionaryModel>>> Index()
    {
        return await workerTagsService.GetWorkerTags();
    }
}
