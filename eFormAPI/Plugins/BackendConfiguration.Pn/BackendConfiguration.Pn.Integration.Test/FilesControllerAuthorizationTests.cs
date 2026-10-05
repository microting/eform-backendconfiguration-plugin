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

namespace BackendConfiguration.Pn.Integration.Test;

using System.Linq;
using System.Reflection;
using BackendConfiguration.Pn.Controllers;
using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Archive files (invoices, reports, mailed-in PDFs) are tenant data: every action of the archive
/// files controller, including the single-file download the web client and the archive app use,
/// requires a signed-in user. No database: pure reflection.
/// </summary>
[TestFixture]
public class FilesControllerAuthorizationTests
{
    [Test]
    public void Controller_RequiresAnAuthenticatedUser()
    {
        Assert.That(typeof(FilesController).GetCustomAttributes<AuthorizeAttribute>(true), Is.Not.Empty);
        // A class-level [AllowAnonymous] would open every action despite the [Authorize] beside it.
        Assert.That(typeof(FilesController).GetCustomAttributes<AllowAnonymousAttribute>(true), Is.Empty);
    }

    [Test]
    public void NoAction_AllowsAnonymousAccess()
    {
        var anonymous = typeof(FilesController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>(true).Any())
            .Select(m => m.Name)
            .ToList();

        Assert.That(anonymous, Is.Empty);
    }
}
