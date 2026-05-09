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
*/

namespace BackendConfiguration.Pn.Infrastructure.Models.Settings;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Configuration for the Google Drive integration. Bound from the
/// "GoogleDrive" configuration section.
/// <list type="bullet">
///   <item><description><c>MicrotingOAuthProxyUrl</c> — base URL of the
///     stateless Microting OAuth proxy (e.g. <c>https://oauth.microting.com</c>).
///     The proxy owns the Google client_id/secret; customer instances never
///     see them.</description></item>
///   <item><description><c>ProxySigningKey</c> — shared HS256 key used to (a)
///     verify the envelope/state/channel JWTs the proxy mints and (b) sign
///     HMACs on outbound calls to <c>/start</c> and <c>/refresh</c>. Same key
///     on every customer instance.</description></item>
///   <item><description><c>RefreshTokenEncryptionKey</c> — base64-encoded
///     32 byte AES-GCM key used at-rest to encrypt the user's Google refresh
///     token before persisting it on <c>GoogleOAuthToken.EncryptedRefreshToken</c>.
///     Per-customer-instance secret. Distinct from <c>ProxySigningKey</c>.</description></item>
/// </list>
///
/// The annotations are validated by the options pipeline
/// (<c>ValidateDataAnnotations()</c>) — a deployment that supplies the
/// section but with a malformed/missing value fails fast at first resolve
/// instead of producing opaque runtime crypto errors.
/// </summary>
public class GoogleDriveOptions
{
    [Required, Url]
    public string MicrotingOAuthProxyUrl { get; set; } = "";

    [Required, MinLength(32)]
    public string ProxySigningKey { get; set; } = "";

    [Required, MinLength(32)]
    public string RefreshTokenEncryptionKey { get; set; } = "";

    /// <summary>
    /// Google Picker API browser key (NOT a sensitive secret — designed to be
    /// embedded in client-side code; restricted by HTTP-Referrer in the GCP
    /// console). Returned to the frontend via the <c>/picker-token</c> endpoint
    /// so the Picker JS SDK can be instantiated. Optional: in many
    /// configurations the Picker works with just an OAuth access token; the
    /// developer key is mainly required for higher-quota loaders.
    /// Empty string is a valid default.
    /// </summary>
    public string PickerDeveloperKey { get; set; } = "";

    /// <summary>
    /// The customer instance's externally-reachable base URL (e.g.
    /// <c>https://acme.microting.com</c>). Embedded into the channel-token
    /// JWT minted in PR-5 so the OAuth proxy knows which customer instance
    /// to fan a Drive change notification back to.
    ///
    /// Looked up by <see cref="GoogleDriveAuthService.EnsureWatchChannelAsync"/>
    /// with this precedence: <c>BackendConfigurationSettings:CustomerInstanceUrl</c>
    /// from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// first, then falling back to this option. We keep both knobs so the
    /// platform-wide value can live alongside the rest of the
    /// <c>BackendConfigurationSettings</c> bundle while a deployment that
    /// wants the value scoped to the GoogleDrive section specifically can
    /// set it here. Empty string disables watch-channel registration; the
    /// auth service will throw rather than mint a JWT with a placeholder URL.
    /// </summary>
    public string CustomerInstanceUrl { get; set; } = "";
}
