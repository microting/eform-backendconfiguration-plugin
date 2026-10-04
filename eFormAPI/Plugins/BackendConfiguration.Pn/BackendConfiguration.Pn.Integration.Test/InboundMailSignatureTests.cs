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

#nullable enable

using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BackendConfiguration.Pn.Integration.Test;

[TestFixture]
public class InboundMailSignatureTests
{
    private const string TenantKey = "c25608a9e5271a2bafd480022de09ea60a11c7970566d3dca57034c662d7c767";
    private const string HubPath = "/api/backend-configuration-pn/inbox/hub/arrived";
    private const string DateHeader = "Sun, 04 Oct 2026 10:00:00 GMT";
    private const string RequestId = "00000000-0000-0000-0000-000000000001";
    private const string ValidSignature = "be3548b960e7fd4b4dfdd72518c1f33d5d642a0312a36d5b4d7eb1cf2d833b1a";
    private static readonly DateTime Now = new(2026, 10, 4, 10, 0, 30, DateTimeKind.Utc);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"a\":1}");

    [Test]
    public void TestVector_MatchesContract()
    {
        var bodyHash = InboundMailSignature.BodyHash(Body);
        var canonical = InboundMailSignature.Canonical("POST", HubPath, 4711, RequestId, DateHeader, bodyHash);

        Assert.That(bodyHash, Is.EqualTo("015abd7f5cc57a2dd94b7590f04ad8084273905ee33ec5cebeae62276a97f862"));
        Assert.That(InboundMailSignature.Sign(TenantKey, canonical), Is.EqualTo(ValidSignature));
    }

    private static InboundMailRequestVerifier Verifier(string key = TenantKey) =>
        new(Options.Create(new InboundMailHubOptions { TenantSigningKey = key }));

    private static HeaderDictionary Headers(string requestId = RequestId, string customerNo = "4711", string sig = ValidSignature) => new()
    {
        ["Authorization"] = InboundMailSignature.Scheme + sig,
        ["Date"] = DateHeader,
        ["X-Request-Id"] = requestId,
        ["X-Customer-No"] = customerNo
    };

    [Test]
    public void Verify_ValidRequest_True() =>
        Assert.That(Verifier().Verify("POST", HubPath, Headers(), Body, 4711, Now), Is.True);

    [Test]
    public void Verify_WrongCustomerNo_False() =>
        Assert.That(Verifier().Verify("POST", HubPath, Headers(customerNo: "4712"), Body, 4711, Now), Is.False);

    [Test]
    public void Verify_TamperedBody_False() =>
        Assert.That(Verifier().Verify("POST", HubPath, Headers(), Encoding.UTF8.GetBytes("{\"a\":2}"), 4711, Now), Is.False);

    [Test]
    public void Verify_OtherPath_False() =>
        Assert.That(Verifier().Verify("POST", "/api/backend-configuration-pn/inbox/hub/deliver", Headers(), Body, 4711, Now), Is.False);

    [Test]
    public void Verify_StaleDate_False() =>
        Assert.That(Verifier().Verify("POST", HubPath, Headers(), Body, 4711, Now.AddMinutes(3)), Is.False);

    [Test]
    public void Verify_DateThreeMinutesInFuture_False() =>
        Assert.That(Verifier().Verify("POST", HubPath, Headers(), Body, 4711, Now.AddMinutes(-3).AddSeconds(-30)), Is.False);

    [Test]
    public void Verify_DateExactlyAtSkewLimit_True() =>
        Assert.That(Verifier().Verify("POST", HubPath, Headers(), Body, 4711, new DateTime(2026, 10, 4, 10, 2, 0, DateTimeKind.Utc)), Is.True);

    [Test]
    public void Verify_DateExactlyAtSkewLimitInFuture_True() =>
        Assert.That(Verifier().Verify("POST", HubPath, Headers(), Body, 4711, new DateTime(2026, 10, 4, 9, 58, 0, DateTimeKind.Utc)), Is.True);

    [TestCase("", false)]
    [TestCase("N/A", false)]
    [TestCase("0", false)]
    [TestCase("-5", false)]
    [TestCase(null, false)]
    [TestCase("4711", true)]
    public void CustomerNoProvider_TryParse(string? raw, bool ok)
    {
        Assert.That(CustomerNoProvider.TryParse(raw, out var n), Is.EqualTo(ok));
        if (ok) Assert.That(n, Is.EqualTo(4711));
    }

    [Test]
    public void Verify_ReplayedRequestId_False()
    {
        var v = Verifier();
        Assert.That(v.Verify("POST", HubPath, Headers(), Body, 4711, Now), Is.True);
        Assert.That(v.Verify("POST", HubPath, Headers(), Body, 4711, Now), Is.False);
    }

    [Test]
    public void Verify_SameRequestIdForOtherCustomerNo_IsNotAReplay()
    {
        var v = Verifier();
        var bodyHash = InboundMailSignature.BodyHash(Body);
        string SignFor(int customerNo) => InboundMailSignature.Sign(TenantKey, InboundMailSignature.Canonical(
            "POST", HubPath, customerNo, RequestId, DateHeader, bodyHash));

        Assert.That(v.Verify("POST", HubPath, Headers(customerNo: "4711", sig: SignFor(4711)), Body, 4711, Now), Is.True);
        Assert.That(v.Verify("POST", HubPath, Headers(customerNo: "4712", sig: SignFor(4712)), Body, 4712, Now), Is.True);
        Assert.That(v.Verify("POST", HubPath, Headers(customerNo: "4712", sig: SignFor(4712)), Body, 4712, Now), Is.False);
    }

    [Test]
    public void Verify_NoKeyConfigured_False() =>
        Assert.That(Verifier(key: "").Verify("POST", HubPath, Headers(), Body, 4711, Now), Is.False);
}
