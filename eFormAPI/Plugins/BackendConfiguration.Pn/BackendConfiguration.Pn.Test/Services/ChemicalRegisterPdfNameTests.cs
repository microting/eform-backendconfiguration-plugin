using System;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>The user-visible text of the shared register-PDF name check (no database needed before it runs).</summary>
[TestFixture]
public class ChemicalRegisterPdfNameTests
{
    private static ChemicalInventoryService CreateSut() => new(
        null!, Substitute.For<IChemicalPermissionService>(), Substitute.For<IChemicalRegisterReader>(), null!, null!,
        Substitute.For<IChemicalBaseClient>(), TimeProvider.System);

    private static readonly ChemicalCaller Caller = ChemicalCaller.App(3, 42);

    [Test]
    public async Task Label_name_error_reads_a_label()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await CreateSut().GetLabelPdfAsync(Caller, "a.b"));

        Assert.That(ex!.Message, Is.EqualTo("That is not a label file name."));
    }

    [Test]
    public async Task Sds_name_error_reads_an_sds()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await CreateSut().GetSdsPdfAsync(Caller, "a.b"));

        Assert.That(ex!.Message, Is.EqualTo("That is not an SDS file name."));
    }
}
