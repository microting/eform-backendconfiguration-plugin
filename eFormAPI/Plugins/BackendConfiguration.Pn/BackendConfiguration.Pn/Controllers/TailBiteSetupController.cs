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
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Services.TailBite;
using static TailBiteRunner;

/// <summary>
/// Tail-bite (halebid) setup for the web: location tree, occupancy, action types, rules and the manager toggle.
/// Authorization beyond being signed in is the service's (worker or manager on the entity's property); only
/// enabling a property and the manager toggle are plugin-admin routes.
/// </summary>
[Authorize]
[Route("api/backend-configuration-pn/tail-bite")]
public class TailBiteSetupController(ITailBiteAccess access, ITailBiteSetupService setup) : Controller
{
    // The rule dry run replays the last 90 days (the service default); the web page does not choose the window.
    private const int PreviewDays = 90;

    [HttpPost("properties/{propertyId:int}/enable")]
    [Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
    public Task<OperationResult> Enable(int propertyId) => Run(() => setup.EnableAsync(0, propertyId));

    [HttpPut("property-workers/{id:int}/manager")]
    [Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
    public Task<OperationResult> SetManager(int id, [FromBody] SetManagerRequest request)
        => request.IsManager is { } isManager
            ? Run(() => setup.SetManagerAsync(id, isManager))
            : Task.FromResult(new OperationResult(false, "isManager is required"));

    [HttpGet("properties/{propertyId:int}/tree")]
    public Task<OperationDataResult<LocationTree>> GetTree(int propertyId)
        => Run(access, site => setup.GetTreeAsync(site, propertyId));

    [HttpPost("locations")]
    public Task<OperationDataResult<int>> CreateLocation([FromBody] CreateLocationRequest request)
        => Run(access, site => setup.CreateLocationAsync(site, request.ParentId, request.Name));

    [HttpPost("locations/range")]
    public Task<OperationDataResult<IReadOnlyList<int>>> CreatePenRange([FromBody] CreatePenRangeRequest request)
        => Run(access, site => setup.CreatePenRangeAsync(site, request.ParentId, request.Prefix, request.From, request.To));

    [HttpPut("locations/{id:int}")]
    public Task<OperationResult> RenameLocation(int id, [FromBody] NameRequest request)
        => Run(access, site => setup.RenameLocationAsync(site, id, request.Name));

    [HttpPut("locations/{id:int}/move")]
    public Task<OperationResult> MoveLocation(int id, [FromBody] MoveLocationRequest request)
        => Run(access, site => setup.MoveLocationAsync(site, id, request.NewParentId));

    [HttpDelete("locations/{id:int}")]
    public Task<OperationResult> DeleteLocation(int id) => Run(access, site => setup.DeleteLocationAsync(site, id));

    [HttpPut("locations/{id:int}/occupancy")]
    public Task<OperationResult> SetOccupancy(int id, [FromBody] OccupancyRequest request)
        => Run(access, site => setup.SetOccupancyAsync(site, id, request.PigCount, request.ValidFromUtc));

    [HttpGet("properties/{propertyId:int}/action-types")]
    public Task<OperationDataResult<IReadOnlyList<ActionTypeDto>>> ListActionTypes(int propertyId)
        => Run(access, site => setup.ListActionTypesAsync(site, propertyId));

    [HttpPost("properties/{propertyId:int}/action-types")]
    public Task<OperationDataResult<int>> CreateActionType(int propertyId, [FromBody] NameRequest request)
        => Run(access, site => setup.CreateActionTypeAsync(site, propertyId, request.Name));

    [HttpPut("action-types/{id:int}")]
    public Task<OperationResult> RenameActionType(int id, [FromBody] NameRequest request)
        => Run(access, site => setup.RenameActionTypeAsync(site, id, request.Name));

    [HttpDelete("action-types/{id:int}")]
    public Task<OperationResult> DeleteActionType(int id) => Run(access, site => setup.DeleteActionTypeAsync(site, id));

    [HttpPost("rules")]
    public Task<OperationDataResult<int>> CreateRule([FromBody] RuleInput input)
        => Run(access, site => setup.CreateRuleAsync(site, input));

    [HttpPut("rules/{id:int}")]
    public Task<OperationResult> UpdateRule(int id, [FromBody] RuleInput input)
        => Run(access, site => setup.UpdateRuleAsync(site, id, input));

    [HttpDelete("rules/{id:int}")]
    public Task<OperationResult> DeleteRule(int id) => Run(access, site => setup.DeleteRuleAsync(site, id));

    [HttpPost("rules/preview")]
    public Task<OperationDataResult<RulePreview>> PreviewRule([FromBody] RuleInput input)
        => Run(access, site => setup.PreviewRuleAsync(site, input, PreviewDays));
}
