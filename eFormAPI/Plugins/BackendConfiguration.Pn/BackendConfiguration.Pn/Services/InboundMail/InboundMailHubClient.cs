#nullable enable
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using Microsoft.Extensions.Options;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface IInboundMailHubClient
{
    /// <summary>HubUrl and TenantSigningKey are both set.</summary>
    bool IsConfigured { get; }

    /// <exception cref="InboundMailHubException">The hub did not accept the registration.</exception>
    Task RegisterAddressAsync(string tokenHash, string? previousTokenHash, DateTime? graceUntil);

    /// <exception cref="InboundMailHubException">The hub did not accept the decision.</exception>
    Task SenderDecisionAsync(string hubDocumentId, bool approve);
}

public enum InboundMailHubFailure
{
    /// <summary>HubUrl, TenantSigningKey or the customer number is missing.</summary>
    NotConfigured,
    /// <summary>The hub answered 409: it is busy with the document; the caller may try again shortly.</summary>
    Conflict,
    /// <summary>The hub could not be reached, timed out, or answered anything else than success.</summary>
    Unavailable
}

public sealed class InboundMailHubException(InboundMailHubFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public InboundMailHubFailure Failure { get; } = failure;
}

/// <summary>Signed calls from the tenant to the central inbound mail service (same scheme as inbound).</summary>
public class InboundMailHubClient(HttpClient http, IOptions<InboundMailHubOptions> options,
    ICustomerNoProvider customerNo) : IInboundMailHubClient
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.HubUrl) && !string.IsNullOrEmpty(options.Value.TenantSigningKey);

    public async Task RegisterAddressAsync(string tokenHash, string? previousTokenHash, DateTime? graceUntil)
    {
        var n = await CustomerNoAsync();
        // No tenant display name exists in the SDK or plugin settings; the hub shows the customer number.
        await SendAsync(HttpMethod.Put, $"/api/tenants/{n}/address", n,
            new { tokenHash, previousTokenHash, graceUntil, tenantName = $"Kunde {n}" });
    }

    public async Task SenderDecisionAsync(string hubDocumentId, bool approve)
    {
        var n = await CustomerNoAsync();
        await SendAsync(HttpMethod.Post,
            $"/api/tenants/{n}/documents/{Uri.EscapeDataString(hubDocumentId)}/sender-decision", n,
            new { decision = approve ? "approve" : "reject" });
    }

    private async Task<int> CustomerNoAsync()
    {
        if (!IsConfigured)
            throw new InboundMailHubException(InboundMailHubFailure.NotConfigured,
                "InboundMailHub:HubUrl or InboundMailHub:TenantSigningKey is not configured");
        var n = await customerNo.GetAsync();
        if (n == 0)
            throw new InboundMailHubException(InboundMailHubFailure.NotConfigured, "The SDK customer number is unknown");
        return n;
    }

    private async Task SendAsync(HttpMethod method, string path, int n, object payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, HubJson.Options);
        var date = DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var requestId = Guid.NewGuid().ToString();
        var sig = InboundMailSignature.Sign(options.Value.TenantSigningKey,
            InboundMailSignature.Canonical(method.Method, path, n, requestId, date, InboundMailSignature.BodyHash(body)));

        using var req = new HttpRequestMessage(method, options.Value.HubUrl.TrimEnd('/') + path);
        req.Content = new ByteArrayContent(body);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        req.Headers.TryAddWithoutValidation("Authorization", InboundMailSignature.Scheme + sig);
        req.Headers.TryAddWithoutValidation("Date", date);
        req.Headers.TryAddWithoutValidation("X-Request-Id", requestId);
        req.Headers.TryAddWithoutValidation("X-Customer-No", n.ToString(CultureInfo.InvariantCulture));

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new InboundMailHubException(InboundMailHubFailure.Unavailable, $"{method} {path} did not reach the hub", e);
        }

        using (res)
        {
            if (res.StatusCode == HttpStatusCode.Conflict)
                throw new InboundMailHubException(InboundMailHubFailure.Conflict, $"{method} {path}: hub answered 409");
            if (!res.IsSuccessStatusCode)
                throw new InboundMailHubException(InboundMailHubFailure.Unavailable,
                    $"{method} {path}: hub answered {(int)res.StatusCode}");
        }
    }
}
