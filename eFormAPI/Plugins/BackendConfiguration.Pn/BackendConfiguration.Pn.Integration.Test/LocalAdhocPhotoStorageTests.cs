using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Pure file-system tests for <see cref="LocalAdhocPhotoStorage"/> - no
/// database, so no <see cref="TestBaseSetup"/>. Each test gets its own
/// throwaway root under the temp dir, removed again in <c>TearDown</c>.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LocalAdhocPhotoStorageTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"adhoc-photos-test-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task PutThenGet_RoundTripsBytes_CreatingTheRootDirectory()
    {
        var sut = new LocalAdhocPhotoStorage(_root);
        var bytes = Encoding.UTF8.GetBytes("local-round-trip-bytes");
        Assert.That(Directory.Exists(_root), Is.False);

        using (var input = new MemoryStream(bytes))
        {
            await sut.PutAsync("1_abc.png", input);
        }

        await using var content = await sut.GetAsync("1_abc.png");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public void Get_Throws_FileNotFound_ForMissingFile()
    {
        var sut = new LocalAdhocPhotoStorage(_root);

        Assert.ThrowsAsync<FileNotFoundException>(async () => await sut.GetAsync("missing.png"));
    }

    [TestCase("../x.jpg")]
    [TestCase("a/b.jpg")]
    [TestCase("a\\b.jpg")]
    [TestCase("/abs.jpg")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(".")]
    [TestCase("..")]
    public void PutAndGet_Reject_NonBareFileNames(string fileName)
    {
        var sut = new LocalAdhocPhotoStorage(_root);

        Assert.ThrowsAsync<ArgumentException>(async () => await sut.PutAsync(fileName, new MemoryStream([1])));
        Assert.ThrowsAsync<ArgumentException>(async () => await sut.GetAsync(fileName));
    }
}
