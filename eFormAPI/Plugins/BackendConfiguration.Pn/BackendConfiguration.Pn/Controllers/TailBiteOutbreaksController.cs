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

#nullable enable

namespace BackendConfiguration.Pn.Controllers;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Services.TailBite;
using static TailBiteRunner;

/// <summary>
/// Tail-bite (halebid) outbreak handling for the web: list, detail, risk assessment, follow-up actions, close and
/// registration cancel. The manager check is the service's.
/// </summary>
[Authorize]
[Route("api/backend-configuration-pn/tail-bite")]
public class TailBiteOutbreaksController(ITailBiteAccess access, ITailBiteOutbreakService outbreaks) : Controller
{
    [HttpGet("properties/{propertyId:int}/outbreaks")]
    public Task<OperationDataResult<IReadOnlyList<OutbreakSummary>>> List(int propertyId, [FromQuery] bool openOnly = true)
        => Run(access, site => outbreaks.ListAsync(site, propertyId, openOnly));

    [HttpGet("outbreaks/{id:int}")]
    public Task<OperationDataResult<OutbreakDetail>> Get(int id) => Run(access, site => outbreaks.GetAsync(site, id));

    [HttpPut("outbreaks/{id:int}/assessment")]
    public Task<OperationResult> SaveAssessment(int id, [FromBody] SaveAssessmentRequest request)
        => Run(access, site => outbreaks.SaveAssessmentAsync(site, id, request.Answers, request.NewActions ?? []));

    [HttpPut("actions/{id:int}/done")]
    public Task<OperationResult> SetActionDone(int id, [FromBody] ActionDoneRequest request)
        => request.Done is { } done
            ? Run(access, site => outbreaks.SetActionDoneAsync(site, id, done))
            : Task.FromResult(new OperationResult(false, "done is required"));

    [HttpPut("actions/{id:int}/withdraw")]
    public Task<OperationResult> WithdrawAction(int id, [FromBody] ReasonRequest request)
        => Run(access, site => outbreaks.WithdrawActionAsync(site, id, request.Reason));

    [HttpPut("actions/{id:int}/reassign")]
    public Task<OperationResult> ReassignAction(int id, [FromBody] ReassignRequest request)
        => Run(access, site => outbreaks.ReassignActionAsync(site, id, request.ResponsibleSiteId));

    [HttpPut("outbreaks/{id:int}/close")]
    public Task<OperationResult> Close(int id) => Run(access, site => outbreaks.CloseAsync(site, id));

    [HttpPut("registrations/{id:int}/cancel")]
    public Task<OperationResult> CancelRegistration(int id, [FromBody] ReasonRequest request)
        => Run(access, site => outbreaks.CancelRegistrationAsync(site, id, request.Reason));
}
