using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using BackendConfiguration.Pn.Services.ChemicalReporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalExportServiceTests : ChemicalTestBase
{
    private static readonly ChemicalCaller Web = ChemicalCaller.Web(TestUserId);

    /// <summary>The QuestPDF licence is set by plugin start-up (AddChemicalReporting), not per render.</summary>
    [OneTimeSetUp]
    public void StartUp() => new ServiceCollection().AddChemicalReporting();

    /// <summary>
    /// The sut clock only feeds file names and the "printed" stamp. The arranged data is written with
    /// the real clock (version tables stamp DateTime.UtcNow), so the history query below uses it too.
    /// </summary>
    private ChemicalExportService CreateSut()
    {
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new ChemicalExportService(
            CreateInventoryService(),
            new ChemicalHistoryService(BackendConfigurationPnDbContext!, new ChemicalRegisterReader(ChemicalsDbContext!), Names),
            BackendConfigurationPnDbContext!,
            localization,
            Options.Create(new ChemicalBaseOptions { BaseUrl = "https://chemicalbase.example" }),
            new FixedTimeProvider(new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc)));
    }

    private static ChemicalStockAmountModel Liters(decimal amount) => new(null, null, amount, ChemicalStockUnitEnum.L, null, null, null);

    private async Task<(int PropertyId, string PropertyName, int Open1, int Open2, int Closed)> ArrangeAsync(bool stock, decimal amount = 2m)
    {
        var property = await CreatePropertyAsync();
        var inventory = CreateInventoryService();
        await inventory.SetSettingsAsync(Web, new ChemicalSetSettingsCommand(property.Id, stock, []));
        var lade = await inventory.CreateLocationAsync(Web, new ChemicalCreateLocationCommand(property.Id, "Lade", "", null));
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Karate 2,5 WG", "1-212", status: 6, sdsFileName: "abc123");
        async Task<int> RegisterAsync(string note) => (await inventory.RegisterPlacementAsync(Web,
            new ChemicalRegisterPlacementCommand(lade.Id, chemical.ChemicalId, null, note, stock ? Liters(amount) : null))).Placements.Single().Id;
        var open1 = await RegisterAsync("=HYPERLINK(\"x\")");
        var open2 = await RegisterAsync("Hylde 2");
        var closed = await RegisterAsync("Hylde 3");
        await inventory.RemovePlacementAsync(Web, new ChemicalRemovePlacementCommand(closed, ChemicalRemovalReasonEnum.Disposed, null, "farligt affald"));
        return (property.Id, property.Name, open1, open2, closed);
    }

    [Test]
    public async Task AuthorityListPdf_IsAPdfNamedAfterTheProperty()
    {
        var a = await ArrangeAsync(stock: false);

        var file = await CreateSut().AuthorityListPdfAsync(TestUserId, a.PropertyId);

        Assert.That(Encoding.ASCII.GetString(file.Content, 0, 5), Is.EqualTo("%PDF-"));
        Assert.That(file.ContentType, Is.EqualTo(ChemicalExportService.PdfContentType));
        Assert.That(file.FileName, Is.EqualTo($"ChemicalAuthorityListTitle - {a.PropertyName} - 2026-09-29.pdf"));
    }

    [Test]
    public async Task AuthorityListPdf_ForANonAsciiPropertyName_KeepsTheNameAndHasAnAsciiFallback()
    {
        var a = await ArrangeAsync(stock: false);
        var property = await BackendConfigurationPnDbContext!.Properties.FindAsync(a.PropertyId);
        property!.Name = "Æblegården";
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var file = await CreateSut().AuthorityListPdfAsync(TestUserId, a.PropertyId);

        Assert.That(file.FileName, Is.EqualTo("ChemicalAuthorityListTitle - Æblegården - 2026-09-29.pdf"));
        Assert.That(ComplianceExportFileNaming.MakeAsciiFallback("Æblegården"), Is.EqualTo("_bleg_rden"));
    }

    [Test]
    public async Task InventoryXlsx_ExportsOnlyOpenPlacementsOfTheRequestedProperty()
    {
        var a = await ArrangeAsync(stock: false);
        var other = await ArrangeAsync(stock: false);

        var file = await CreateSut().InventoryXlsxAsync(TestUserId, a.PropertyId, [a.Open1, a.Closed, other.Open1]);

        var (_, rows) = XlsxTestReader.Read(file.Content);
        Assert.That(rows, Has.Count.EqualTo(2), "header + the one open placement of this property");
        Assert.That(rows[1][0], Is.EqualTo("Karate 2,5 WG"));
        Assert.That(rows[1][4], Is.EqualTo("=HYPERLINK(\"x\")"), "notes stay literal text");
        Assert.That(file.ContentType, Is.EqualTo(ChemicalExportService.XlsxContentType));
    }

    [Test]
    public async Task InventoryXlsx_WithoutIds_ExportsEveryOpenPlacement_AndStockOnlyWhenEnabled()
    {
        var withStock = await ArrangeAsync(stock: true);
        var withoutStock = await ArrangeAsync(stock: false);
        var sut = CreateSut();

        var (_, stockRows) = XlsxTestReader.Read((await sut.InventoryXlsxAsync(TestUserId, withStock.PropertyId, null)).Content);
        var (_, plainRows) = XlsxTestReader.Read((await sut.InventoryXlsxAsync(TestUserId, withoutStock.PropertyId, null)).Content);

        Assert.That(stockRows, Has.Count.EqualTo(3));
        Assert.That(stockRows[0], Does.Contain("ChemicalColumnQuantity"));
        Assert.That(stockRows[1][stockRows[0].IndexOf("ChemicalColumnQuantity")], Is.EqualTo("2"));
        Assert.That(stockRows[1][stockRows[0].IndexOf("ChemicalColumnRequiresAction")], Is.EqualTo("ChemicalYes"), "status 6 requires action");
        Assert.That(plainRows[0], Does.Not.Contain("ChemicalColumnQuantity"));
    }

    [Test]
    public async Task InventoryXlsx_Quantity_KeepsRealDecimalsAndDropsOnlyTrailingZeros()
    {
        var a = await ArrangeAsync(stock: true, amount: 2.5m);

        var (_, rows) = XlsxTestReader.Read((await CreateSut().InventoryXlsxAsync(TestUserId, a.PropertyId, null)).Content);

        Assert.That(rows[1][rows[0].IndexOf("ChemicalColumnQuantity")], Is.EqualTo("2.5"));
    }

    [Test]
    public async Task InventoryXlsx_SortsNamesInDanishOrder_RegardlessOfTheHostCulture()
    {
        var property = await CreatePropertyAsync();
        var inventory = CreateInventoryService();
        var lade = await inventory.CreateLocationAsync(Web, new ChemicalCreateLocationCommand(property.Id, "Lade", "", null));
        foreach (var name in new[] { "Åben", "Zebra", "Ægte" })
        {
            var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, name, $"9-{name.Length}{(int)name[0]}");
            await inventory.RegisterPlacementAsync(Web, new ChemicalRegisterPlacementCommand(lade.Id, chemical.ChemicalId, null, "", null));
        }

        var (_, rows) = XlsxTestReader.Read((await CreateSut().InventoryXlsxAsync(TestUserId, property.Id, null)).Content);

        Assert.That(rows.Skip(1).Select(r => r[0]), Is.EqualTo(new[] { "Zebra", "Ægte", "Åben" }), "Danish: z, æ, å");
    }

    [Test]
    public async Task RemovedPlacementsXlsx_ListsClosedPlacementsWithReason()
    {
        var a = await ArrangeAsync(stock: false);

        var (sheet, rows) = XlsxTestReader.Read((await CreateSut().RemovedPlacementsXlsxAsync(TestUserId, a.PropertyId)).Content);

        Assert.That(sheet, Is.EqualTo("ChemicalRemovedPlacementsTitle"));
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[1], Does.Contain("ChemicalReasonDisposed"));
        Assert.That(rows[1], Does.Contain("farligt affald"));
    }

    [Test]
    public async Task HistoryXlsx_OneRowPerEvent()
    {
        var a = await ArrangeAsync(stock: false);
        var today = DateTime.UtcNow.Date;
        var query = new ChemicalHistoryQuery(today.AddDays(-1), today.AddDays(1), null, null, null);
        var events = await new ChemicalHistoryService(BackendConfigurationPnDbContext!, new ChemicalRegisterReader(ChemicalsDbContext!), Names)
            .GetAllAsync(a.PropertyId, query);

        var (_, rows) = XlsxTestReader.Read((await CreateSut().HistoryXlsxAsync(a.PropertyId, query)).Content);

        Assert.That(events, Is.Not.Empty, "the arrange wrote history inside the period");
        Assert.That(rows, Has.Count.EqualTo(events.Count + 1));
        Assert.That(rows.Select(r => r[2]), Does.Contain("ChemicalActionRegistered").And.Contain("ChemicalActionRemoved"));
        var removed = rows.Single(r => r[2] == "ChemicalActionRemoved");
        Assert.That(removed[6], Is.EqualTo("ChemicalColumnRemovalReason: ChemicalReasonDisposed · farligt affald"));
        Assert.That(rows[0], Is.EqualTo(new List<string>
        {
            "ChemicalColumnTime", "ChemicalColumnUser", "ChemicalColumnAction", "ChemicalColumnProduct",
            "ChemicalColumnRegistrationNo", "ChemicalColumnLocation", "ChemicalColumnDetails",
        }));
    }

    /// <summary>
    /// m-2, a regression guard (green against today's code): W6 and its Excel are keyed on the property. A second
    /// property with its own location, placements, stock entries, permission, settings and alert adds nothing to
    /// the first property's history or to its Excel.
    /// </summary>
    [Test]
    public async Task Events_OfAnotherProperty_NeverAppear()
    {
        var first = await ArrangeAsync(stock: true);
        var today = DateTime.UtcNow.Date;
        var query = new ChemicalHistoryQuery(today.AddDays(-1), today.AddDays(1), null, null, null);
        var history = new ChemicalHistoryService(BackendConfigurationPnDbContext!, new ChemicalRegisterReader(ChemicalsDbContext!), Names);
        static (DateTime, ChemicalHistoryActionEnum, int?) Key(ChemicalHistoryEventModel e) => (e.At, e.Action, e.PlacementId);
        var eventsBefore = (await history.GetAllAsync(first.PropertyId, query)).Select(Key).ToList();
        var (_, rowsBefore) = XlsxTestReader.Read((await CreateSut().HistoryXlsxAsync(first.PropertyId, query)).Content);

        var other = await ArrangeAsync(stock: true);
        var inventory = CreateInventoryService();
        await inventory.AddStockEntryAsync(Web, new ChemicalAddStockEntryCommand(other.Open1, ChemicalStockEntryKindEnum.Consumed, Liters(1)));
        var worker = await AddWorkerAsync(other.PropertyId);
        await inventory.SetWorkerPermissionsAsync(Web, other.PropertyId,
            [new ChemicalSetWorkerPermissionCommand(worker, ChemicalPermissionFlagsModel.None with { View = true })]);
        await new ChemicalAlertLog
        {
            PlacementId = other.Open2, Threshold = ChemicalAlertThresholdEnum.StatusChanged, Status = 6,
            Channel = ChemicalAlertChannelEnum.Push, SentAt = DateTime.UtcNow, CreatedByUserId = 0, UpdatedByUserId = 0,
        }.Create(BackendConfigurationPnDbContext!);

        var otherEvents = await history.GetAllAsync(other.PropertyId, query);
        Assert.That(otherEvents.Select(e => e.Action), Is.SupersetOf(new[]
        {
            ChemicalHistoryActionEnum.LocationCreated, ChemicalHistoryActionEnum.Registered, ChemicalHistoryActionEnum.StockReceived,
            ChemicalHistoryActionEnum.StockConsumed, ChemicalHistoryActionEnum.Removed, ChemicalHistoryActionEnum.PermissionChanged,
            ChemicalHistoryActionEnum.SettingsChanged, ChemicalHistoryActionEnum.StatusChanged,
        }), "positive control: the other property has history from every source");

        var eventsAfter = await history.GetAllAsync(first.PropertyId, query);
        var otherPlacements = new int?[] { other.Open1, other.Open2, other.Closed };
        Assert.That(eventsAfter.Where(e => otherPlacements.Contains(e.PlacementId)), Is.Empty);
        Assert.That(eventsAfter.Select(Key), Is.EqualTo(eventsBefore));
        var (_, rowsAfter) = XlsxTestReader.Read((await CreateSut().HistoryXlsxAsync(first.PropertyId, query)).Content);
        Assert.That(rowsAfter, Is.EqualTo(rowsBefore));
    }

    [Test]
    public async Task Exports_ForAnEmptyProperty_AreValidFilesWithHeadersOnly()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var pdf = await sut.AuthorityListPdfAsync(TestUserId, property.Id);
        var (_, inventoryRows) = XlsxTestReader.Read((await sut.InventoryXlsxAsync(TestUserId, property.Id, null)).Content);
        var (_, removedRows) = XlsxTestReader.Read((await sut.RemovedPlacementsXlsxAsync(TestUserId, property.Id)).Content);

        Assert.That(Encoding.ASCII.GetString(pdf.Content, 0, 5), Is.EqualTo("%PDF-"));
        Assert.That(inventoryRows, Has.Count.EqualTo(1));
        Assert.That(removedRows, Has.Count.EqualTo(1));
    }
}
