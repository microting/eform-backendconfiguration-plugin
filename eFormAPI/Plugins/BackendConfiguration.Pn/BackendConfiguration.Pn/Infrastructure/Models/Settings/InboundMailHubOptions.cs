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

/// <summary>
/// Connection to the central inbound mail service. Empty TenantSigningKey: every
/// inbound hub call is refused (401). Empty HubUrl (or TenantSigningKey): outbound calls
/// fail and the settings endpoints answer InboxNotConfigured.
/// </summary>
public class InboundMailHubOptions
{
    /// <summary>
    /// Scheme and host, optionally with a port (e.g. <c>https://hub.example.com:8443</c>), with no path:
    /// request signatures cover only the API path, so a path prefix here would break verification at the hub.
    /// </summary>
    public string HubUrl { get; set; } = "";
    public string TenantSigningKey { get; set; } = "";
    public string MailDomain { get; set; } = "indbakke.microting.dk";
}
