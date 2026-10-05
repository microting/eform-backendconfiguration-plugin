#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// Called only by the central inbound mail service. Anonymous at the auth layer;
/// every request must carry a valid signature (see the tenant-side spec).
/// </summary>
[AllowAnonymous]
[Route("api/backend-configuration-pn/inbox/hub")]
public class InboxHubController(IInboxHubService service, InboundMailRequestVerifier verifier,
    ICustomerNoProvider customerNo) : Controller
{
    /// <summary>30 MiB. The central service refuses mails over 25 MB, so this is ample for one PDF plus multipart overhead.</summary>
    private const long DeliverSizeLimit = 31_457_280;

    /// <summary>The JSON calls carry a few hundred bytes.</summary>
    private const long JsonSizeLimit = 65_536;

    [HttpPost("arrived")]
    [RequestSizeLimit(JsonSizeLimit)]
    public async Task<IActionResult> Arrived()
    {
        if (await ReadVerifiedBodyAsync() is not { } body) return Unauthorized();
        var req = TryDeserialize<ArrivedRequest>(body);
        if (req == null || !IsValidHubDocumentId(req.HubDocumentId)
            || string.IsNullOrWhiteSpace(req.FromAddress) || string.IsNullOrWhiteSpace(req.FileName))
            return BadRequest();
        return HubJsonResult(await service.ArrivedAsync(req));
    }

    [HttpGet("catalog")]
    [RequestSizeLimit(JsonSizeLimit)]
    public async Task<IActionResult> Catalog()
    {
        if (await ReadVerifiedBodyAsync() is null) return Unauthorized();
        return HubJsonResult(await service.CatalogAsync());
    }

    [HttpPost("deliver")]
    [RequestSizeLimit(DeliverSizeLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = DeliverSizeLimit)]
    public async Task<IActionResult> Deliver()
    {
        // The signature covers the raw multipart body, so it is checked before the form is parsed.
        if (await ReadVerifiedBodyAsync() is null) return Unauthorized();
        if (!Request.HasFormContentType) return BadRequest();

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var metadataJson = form["metadata"].ToString();
        if (string.IsNullOrEmpty(metadataJson) && form.Files.GetFile("metadata") is { } metadataPart)
        {
            // Sent with a file name, the JSON part lands among the files instead of the form values.
            using var reader = new StreamReader(metadataPart.OpenReadStream());
            metadataJson = await reader.ReadToEndAsync();
        }

        var meta = TryDeserialize<DeliverMetadata>(metadataJson);
        if (file == null || meta == null || !IsValidHubDocumentId(meta.HubDocumentId)) return BadRequest();

        await using var stream = file.OpenReadStream();
        await service.DeliverAsync(meta, stream, file.FileName);
        return NoContent();
    }

    [HttpPost("failed")]
    [RequestSizeLimit(JsonSizeLimit)]
    public async Task<IActionResult> Failed()
    {
        if (await ReadVerifiedBodyAsync() is not { } body) return Unauthorized();
        var req = TryDeserialize<FailedRequest>(body);
        if (req == null || !IsValidHubDocumentId(req.HubDocumentId)) return BadRequest();
        await service.FailedAsync(req);
        return NoContent();
    }

    /// <summary>
    /// The whole raw body (no cap of its own: the endpoint's request size limit applies), or null
    /// when the signature headers are missing/stale or the signature does not verify. The body stays readable for form parsing afterwards.
    /// </summary>
    private async Task<byte[]?> ReadVerifiedBodyAsync()
    {
        var tenant = await customerNo.GetAsync();
        if (tenant <= 0) return null; // customer number unknown: nothing can be verified

        // Cheap reject of unsigned or stale requests before buffering up to 30 MiB; Verify repeats these
        // checks and verifies the signature once the body is read.
        if (!verifier.HeadersPlausible(Request.Headers, tenant, DateTime.UtcNow)) return null;

        Request.EnableBuffering();
        // Sized from Content-Length (bounded by the endpoint's size limit) so that, when it is exact, the
        // buffer itself is the body and a deliver avoids a second copy of up to 30 MiB. Verify takes a byte[].
        using var ms = new MemoryStream(Request.ContentLength is { } length and <= int.MaxValue ? (int)length : 0);
        await Request.Body.CopyToAsync(ms);
        Request.Body.Position = 0;
        var body = ms.Length == ms.Capacity ? ms.GetBuffer() : ms.ToArray();
        return verifier.Verify(Request.Method, Request.Path.Value ?? "", Request.Headers, body, tenant,
            DateTime.UtcNow)
            ? body
            : null;
    }

    /// <summary>The DB column is 36 characters; anything else would end as a 500 instead of a 400.</summary>
    private static bool IsValidHubDocumentId(string? id) => id is { Length: <= 36 } && Guid.TryParse(id, out _);

    private static T? TryDeserialize<T>(byte[] json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, HubJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, HubJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ContentResult HubJsonResult(object value) =>
        Content(JsonSerializer.Serialize(value, HubJson.Options), "application/json");
}
