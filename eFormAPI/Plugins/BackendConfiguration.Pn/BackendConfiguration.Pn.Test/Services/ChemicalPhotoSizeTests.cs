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

using System;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// The one photo size rule shared by ChemicalInventoryService, ChemicalsGrpcService
/// (while the upload stream is read) and ChemicalsController (before the form file is read).
/// </summary>
[TestFixture]
public class ChemicalPhotoSizeTests
{
    [TestCase(1)]
    [TestCase(ChemicalInventoryService.MaxPhotoBytes)]
    public void EnsurePhotoSize_AcceptsOneByteUpToTheLimit(long length) =>
        Assert.DoesNotThrow(() => ChemicalInventoryService.EnsurePhotoSize(length));

    [TestCase(0)]
    [TestCase(-1)]
    public void EnsurePhotoSize_RefusesAnEmptyPhoto(long length) =>
        Assert.That(() => ChemicalInventoryService.EnsurePhotoSize(length),
            Throws.InstanceOf<ArgumentException>().With.Message.EqualTo("The photo is empty."));

    [Test]
    public void EnsurePhotoSize_RefusesAPhotoOverTheLimit() =>
        Assert.That(() => ChemicalInventoryService.EnsurePhotoSize(ChemicalInventoryService.MaxPhotoBytes + 1L),
            Throws.InstanceOf<ArgumentException>().With.Message.EqualTo("The photo exceeds 20 MB."));

    [Test]
    public void EnsurePhotoWithinLimit_AllowsAnEmptyPrefix_ButNotOneByteOver()
    {
        Assert.DoesNotThrow(() => ChemicalInventoryService.EnsurePhotoWithinLimit(0));
        Assert.That(() => ChemicalInventoryService.EnsurePhotoWithinLimit(ChemicalInventoryService.MaxPhotoBytes + 1L),
            Throws.InstanceOf<ArgumentException>().With.Message.EqualTo("The photo exceeds 20 MB."));
    }
}
