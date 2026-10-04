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
/// inbound hub call is refused (401). Empty HubUrl: outbound calls are skipped.
/// </summary>
public class InboundMailHubOptions
{
    public string HubUrl { get; set; } = "";
    public string TenantSigningKey { get; set; } = "";
    public string MailDomain { get; set; } = "indbakke.microting.dk";
}
