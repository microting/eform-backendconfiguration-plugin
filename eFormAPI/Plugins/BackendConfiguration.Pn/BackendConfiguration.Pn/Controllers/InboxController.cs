#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

namespace BackendConfiguration.Pn.Controllers;

[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/inbox")]
public class InboxController(IInboxService inbox, IInboxSettingsService settings, IUserService userService,
    ILogger<InboxController> logger) : Controller
{
    [HttpGet]
    public Task<OperationDataResult<List<InboxListItem>>> List([FromQuery] int? status, [FromQuery] string? search) =>
        inbox.ListAsync(status, search);

    [HttpGet("{id:int}")]
    public Task<OperationDataResult<InboxListItem>> Get(int id) => inbox.GetAsync(id);

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> Pdf(int id)
    {
        try
        {
            var stream = await inbox.GetPdfAsync(id);
            return stream == null ? NotFound() : File(stream, "application/pdf");
        }
        catch (Exception e)
        {
            // Storage misconfigured or unreachable: still a 500, but with the cause in the log.
            logger.LogError(e, "Inbox PDF {Id}: the stored file could not be read", id);
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The PDF could not be read.");
        }
    }

    [HttpPost("{id:int}/file")]
    public Task<OperationResult> FileDocument(int id, [FromBody] FileInboxDocumentRequest req) =>
        inbox.FileAsync(id, req, userService.UserId);

    [HttpPost("{id:int}/undo")]
    public Task<OperationResult> Undo(int id) => inbox.UndoAsync(id, userService.UserId);

    /// <summary><paramref name="block"/> also adds a Block rule for the sender, so later mail from it is refused.</summary>
    [HttpPost("{id:int}/reject")]
    public Task<OperationResult> Reject(int id, [FromQuery] bool block = false) =>
        inbox.RejectAsync(id, block, userService.UserId);

    [HttpGet("settings")]
    public Task<OperationDataResult<InboxSettingsModel>> GetSettings() => settings.GetAsync(userService.UserId);

    [HttpPut("settings")]
    public Task<OperationResult> PutSettings([FromBody] InboxSettingsModel model) =>
        settings.UpdateAsync(model, userService.UserId);

    /// <summary>The address is fixed: only the tenant's first user may replace it.</summary>
    [FirstUserOnly]
    [HttpPost("settings/rotate-address")]
    public Task<OperationDataResult<InboxSettingsModel>> Rotate() => settings.RotateAddressAsync(userService.UserId);
}
