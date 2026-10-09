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
using Microting.EformAngularFrontendBase.Infrastructure.Const;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Services.TailBite;
using static TailBiteRunner;

/// <summary>
/// Read endpoints the tail-bite web admin needs beyond the setup and outbreak controllers: the property list with
/// the enabled flag and a property's workers (the managers dialog), the caller's own enabled properties (the area's
/// picker), the workers a manager can make responsible (the outbreak page), and rules, rule history, current pig counts
/// and an outbreak's registration rows (caller-checked by the service).
/// The managers dialog's two lists are management routes like enable and the manager toggle: they expose every property
/// and the PropertyWorker ids the toggle takes, so they need plugin access plus the worker-update permission
/// (DeviceUsers.Update, as for editing a property worker). The outbreak page must not use them: a tail-bite manager need
/// not hold that permission, so it has its own manager-checked list with names only.
/// </summary>
[Authorize]
[Route("api/backend-configuration-pn/tail-bite")]
public class TailBiteWebController(ITailBiteAccess access, ITailBiteWebQueryService queries) : Controller
{
    [HttpGet("properties")]
    [Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
    [Authorize(Policy = AuthConsts.EformPolicies.DeviceUsers.Update)]
    public Task<OperationDataResult<IReadOnlyList<TailBitePropertyStatus>>> ListProperties()
        => Run(() => queries.ListPropertiesAsync());

    // Caller-scoped, for the tail-bite area's property picker: only the enabled properties the caller works on.
    [HttpGet("my-properties")]
    public Task<OperationDataResult<IReadOnlyList<TailBitePropertyStatus>>> MyProperties()
        => Run(access, site => queries.ListCallerPropertiesAsync(site));

    [HttpGet("properties/{propertyId:int}/workers")]
    [Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
    [Authorize(Policy = AuthConsts.EformPolicies.DeviceUsers.Update)]
    public Task<OperationDataResult<IReadOnlyList<TailBiteWorker>>> ListWorkers(int propertyId)
        => Run(() => queries.ListWorkersAsync(propertyId));

    // Caller-checked (a tail-bite manager of the property), for the outbreak page's responsible-person pickers.
    [HttpGet("properties/{propertyId:int}/assignable-workers")]
    public Task<OperationDataResult<IReadOnlyList<TailBiteAssignableWorker>>> AssignableWorkers(int propertyId)
        => Run(access, site => queries.ListAssignableWorkersAsync(site, propertyId));

    [HttpGet("properties/{propertyId:int}/rules")]
    public Task<OperationDataResult<IReadOnlyList<RuleDto>>> ListRules(int propertyId)
        => Run(access, site => queries.ListRulesAsync(site, propertyId));

    [HttpGet("rules/{id:int}/history")]
    public Task<OperationDataResult<IReadOnlyList<RuleVersionDto>>> RuleHistory(int id)
        => Run(access, site => queries.RuleHistoryAsync(site, id));

    [HttpGet("properties/{propertyId:int}/occupancy")]
    public Task<OperationDataResult<IReadOnlyList<OccupancyDto>>> CurrentOccupancy(int propertyId)
        => Run(access, site => queries.CurrentOccupancyAsync(site, propertyId));

    [HttpGet("outbreaks/{id:int}/registrations")]
    public Task<OperationDataResult<OutbreakRegistrations>> OutbreakRegistrations(int id)
        => Run(access, site => queries.OutbreakRegistrationsAsync(site, id));
}
