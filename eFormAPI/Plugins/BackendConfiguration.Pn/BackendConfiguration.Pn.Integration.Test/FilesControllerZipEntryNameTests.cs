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

namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Controllers;

/// <summary>Archive zip downloads: a user-editable file name never becomes a path outside the extract folder.</summary>
[TestFixture]
public class FilesControllerZipEntryNameTests
{
    [TestCase("Servicerapport VA-02", "pdf", "Servicerapport VA-02.pdf")]
    [TestCase("../../etc/passwd", "pdf", "passwd.pdf")]
    [TestCase("..\\..\\Windows\\evil", "pdf", "evil.pdf")]
    [TestCase("a/b/c", "png", "c.png")]
    [TestCase("..", "pdf", "file.pdf")]
    [TestCase("folder/", "pdf", "file.pdf")]
    [TestCase("", "pdf", "file.pdf")]
    [TestCase(null, "pdf", "file.pdf")]
    [TestCase("C:evil", "pdf", "Cevil.pdf")]
    [TestCase("report", "a\\..\\..\\evil", "report.aevil")]
    [TestCase("report", "../x", "report.x")]
    [TestCase("report", "", "report")]
    [TestCase("a:b*c?d\"e<f>g|h", "pdf", "abcdefgh.pdf")]
    public void ZipEntryName_KeepsOnlyABareFileName(string? fileName, string extension, string expected)
    {
        Assert.That(FilesController.ZipEntryName(fileName!, extension), Is.EqualTo(expected));
    }
}
