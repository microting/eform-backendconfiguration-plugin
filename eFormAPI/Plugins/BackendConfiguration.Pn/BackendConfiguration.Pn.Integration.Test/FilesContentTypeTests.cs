using BackendConfiguration.Pn.Controllers;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>get-file serves an archive file with the content type its extension implies.</summary>
[TestFixture]
public class FilesContentTypeTests
{
    [TestCase("pdf", "application/pdf")]
    [TestCase("PDF", "application/pdf")]
    [TestCase("jpg", "image/jpeg")]
    [TestCase("jpeg", "image/jpeg")]
    [TestCase(".JPG", "image/jpeg")]
    [TestCase("png", "image/png")]
    [TestCase("docx", "application/octet-stream")]
    [TestCase("", "application/octet-stream")]
    [TestCase(null, "application/octet-stream")]
    public void ContentTypeFor_maps_the_extension(string? extension, string expected)
    {
        Assert.That(FilesController.ContentTypeFor(extension), Is.EqualTo(expected));
    }
}
