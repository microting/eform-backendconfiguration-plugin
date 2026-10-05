using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.FileArchive;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Pure file-system tests for <see cref="LocalArchiveStorage"/>, the archive storage of installs with
/// s3Enabled=false (CI, self-hosted) - no database. Each test gets its own throwaway root under the temp dir.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LocalArchiveStorageTests
{
    private string _root = null!;
    private string _source = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"archive-files-test-{Guid.NewGuid():N}");
        _source = Path.Combine(Path.GetTempPath(), $"archive-source-{Guid.NewGuid():N}.pdf");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        if (File.Exists(_source))
        {
            File.Delete(_source);
        }
    }

    [Test]
    public async Task PutThenGet_RoundTripsBytes_CreatingTheRootDirectory()
    {
        var sut = new LocalArchiveStorage(_root);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 local-round-trip");
        await File.WriteAllBytesAsync(_source, bytes);
        Assert.That(Directory.Exists(_root), Is.False);

        await sut.PutAsync(_source, "0123abcd.pdf");

        await using var content = await sut.GetAsync("0123abcd.pdf");
        Assert.That(content, Is.Not.Null);
        using var ms = new MemoryStream();
        await content!.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public async Task Put_OverwritesAnExistingObject()
    {
        var sut = new LocalArchiveStorage(_root);
        await File.WriteAllBytesAsync(_source, [1, 2, 3]);
        await sut.PutAsync(_source, "same.pdf");
        await File.WriteAllBytesAsync(_source, [4, 5]);

        await sut.PutAsync(_source, "same.pdf");

        await using var content = await sut.GetAsync("same.pdf");
        using var ms = new MemoryStream();
        await content!.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
    }

    [Test]
    public async Task Put_ReplacesAnObjectWhileItIsOpenForReading()
    {
        var sut = new LocalArchiveStorage(_root);
        await File.WriteAllBytesAsync(_source, Encoding.UTF8.GetBytes("%PDF-1.4 first"));
        await sut.PutAsync(_source, "0123abcd.pdf");
        await File.WriteAllBytesAsync(_source, Encoding.UTF8.GetBytes("%PDF-1.4 second"));

        // A download still holds the stored file open.
        await using var reader = await sut.GetAsync("0123abcd.pdf");
        await sut.PutAsync(_source, "0123abcd.pdf");

        await using var content = await sut.GetAsync("0123abcd.pdf");
        using var ms = new MemoryStream();
        await content!.CopyToAsync(ms);
        Assert.That(Encoding.UTF8.GetString(ms.ToArray()), Is.EqualTo("%PDF-1.4 second"));
        Assert.That(Directory.GetFiles(_root), Has.Length.EqualTo(1), "no temp file is left behind");
    }

    [Test]
    public async Task Get_ReturnsNull_ForMissingObject()
    {
        var sut = new LocalArchiveStorage(_root);

        Assert.That(await sut.GetAsync("missing.pdf"), Is.Null);
    }

    [TestCase("../x.pdf")]
    [TestCase("a/b.pdf")]
    [TestCase("a\\b.pdf")]
    [TestCase("/abs.pdf")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(".")]
    [TestCase("..")]
    public async Task PutAndGet_Reject_NonBareObjectNames(string objectName)
    {
        var sut = new LocalArchiveStorage(_root);
        await File.WriteAllBytesAsync(_source, [1]);

        await Assert.ThrowsAsync<ArgumentException>(async () => await sut.PutAsync(_source, objectName));
        await Assert.ThrowsAsync<ArgumentException>(async () => await sut.GetAsync(objectName));
    }
}
