using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers.PluginDbOptions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

/// <summary>
/// #1477: the Halebid toggle of the property create and edit dialogs, through BackendConfigurationPropertiesService.
/// </summary>
[TestFixture]
public class PropertiesServiceTailBiteToggleTests : TailBiteTestBase
{
    private const int WorkerSite = 7;
    private eFormCore.Core? _core;

    // Property create leaves SDK and area rows the tail-bite table reset does not clear (AreaProperties would block it).
    protected override bool ResetDatabasePerTest => true;

    [SetUp]
    public void ForgetCore() => _core = null;

    private TailBiteSetupService TailBiteSetup() => new(Db, new TailBitePropertyLock(Db), NewAccess(), new TailBiteSnapshotLoader(Db), Clock);

    private BackendConfigurationPropertiesService Sut(bool admin = true)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.IsAdmin().Returns(admin);
        var options = Substitute.For<IPluginDbOptions<BackendConfigurationBaseSettings>>();
        options.Value.Returns(new BackendConfigurationBaseSettings { MaxChrNumbers = 1000, MaxCvrNumbers = 1000 });
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(async _ => _core ??= await GetCore());
        return new BackendConfigurationPropertiesService(coreHelper, userService, Db, new BackendConfigurationLocalizationService(),
            ItemsPlanningPnDbContext!, options, TestContextLogger<BackendConfigurationPropertiesService>.Instance, TailBiteSetup());
    }

    private static PropertyCreateModel NewProperty(bool tailBite) => new()
    {
        Name = $"Gården {Guid.NewGuid():N}"[..20],
        Chr = Guid.NewGuid().ToString("N")[..8],
        Cvr = Guid.NewGuid().ToString("N")[..8],
        Address = "Eksempelvej 1",
        LanguagesIds = [1],
        MainMailAddress = "farm@example.com",
        WorkorderEnable = false,
        TailBiteEnabled = tailBite,
    };

    private async Task<int> CreateAsync(PropertyCreateModel model, bool admin = true)
    {
        var result = await Sut(admin).Create(model);
        Assert.That(result.Success, Is.True, result.Message);
        Db.ChangeTracker.Clear();
        return await Db.Properties.Where(p => p.Name == model.Name).Select(p => p.Id).SingleAsync();
    }

    private async Task UpdateAsync(PropertyCreateModel created, int id, bool? tailBite)
    {
        var result = await Sut().Update(new PropertiesUpdateModel
        {
            Id = id, Name = created.Name, Chr = created.Chr, Cvr = created.Cvr, Address = created.Address,
            LanguagesIds = created.LanguagesIds, WorkorderEnable = created.WorkorderEnable,
            MainMailAddress = created.MainMailAddress, TailBiteEnabled = tailBite,
        });
        Assert.That(result.Success, Is.True, result.Message);
        Db.ChangeTracker.Clear();
    }

    private async Task<bool> ReadTailBiteAsync(int id)
    {
        var read = await Sut().Read(id);
        Assert.That(read.Success, Is.True, read.Message);
        return read.Model.TailBiteEnabled;
    }

    private async Task<bool> IndexTailBiteAsync(int id)
    {
        var index = await Sut().Index(new PropertiesRequestModel { Sort = "Id", PageSize = 1000 });
        Assert.That(index.Success, Is.True, index.Message);
        return index.Model.Entities.Single(p => p.Id == id).TailBiteEnabled;
    }

    private async Task<bool> IsEnabledAsync(int propertyId)
        => (await Db.TailBiteProperties.SingleAsync(p => p.PropertyId == propertyId)).Enabled;

    private Task<int> RootCountAsync(int propertyId)
        => Db.TailBiteLocations.CountAsync(l => l.PropertyId == propertyId && l.ParentId == null);

    private Task<int> RuleCountAsync(int propertyId)
        => (from r in Db.TailBiteRules join l in Db.TailBiteLocations on r.LocationId equals l.Id
            where l.PropertyId == propertyId select r.Id).CountAsync();

    [Test]
    public async Task Create_WithTailBiteOn_SetsUpRootDefaultRuleAndEnables()
    {
        var model = NewProperty(tailBite: true);
        var id = await CreateAsync(model);

        var prop = await Db.TailBiteProperties.SingleAsync(p => p.PropertyId == id);
        Assert.That(prop.Enabled, Is.True);
        Assert.That(prop.EnabledAt, Is.Not.Null);
        var root = await Db.TailBiteLocations.SingleAsync(l => l.PropertyId == id && l.ParentId == null);
        Assert.That(root.Name, Is.EqualTo(model.Name));
        Assert.That(root.QrCode, Is.Not.Empty);
        Assert.That(await RuleCountAsync(id), Is.EqualTo(1));
        Assert.That(await Db.TailBiteActionTypes.CountAsync(a => a.PropertyId == id), Is.EqualTo(TailBiteDefaults.ActionTypes.Length));
        Assert.That(await ReadTailBiteAsync(id), Is.True);
        Assert.That(await IndexTailBiteAsync(id), Is.True);
    }

    [Test]
    public async Task Create_WithTailBiteOff_SetsNothingUp()
    {
        var id = await CreateAsync(NewProperty(tailBite: false));

        Assert.That(await Db.TailBiteProperties.AnyAsync(p => p.PropertyId == id), Is.False);
        Assert.That(await Db.TailBiteLocations.AnyAsync(l => l.PropertyId == id), Is.False);
        Assert.That(await ReadTailBiteAsync(id), Is.False);
        Assert.That(await IndexTailBiteAsync(id), Is.False);
    }

    [Test]
    public async Task Create_WithTailBiteOn_ByANonAdmin_SetsNothingUp()
    {
        var id = await CreateAsync(NewProperty(tailBite: true), admin: false);

        Assert.That(await Db.TailBiteProperties.AnyAsync(p => p.PropertyId == id), Is.False);
    }

    [Test]
    public async Task Update_OffThenOn_KeepsData_HidesFromApp_AndResumesWithOneRootAndRule()
    {
        var model = NewProperty(tailBite: true);
        var id = await CreateAsync(model);
        await new PropertyWorker { PropertyId = id, WorkerId = WorkerSite, TailBiteManager = true }.Create(Db);
        var rootId = (await Db.TailBiteLocations.SingleAsync(l => l.PropertyId == id && l.ParentId == null)).Id;
        await new TailBiteLocation { PropertyId = id, ParentId = rootId, Name = "Stald A", QrCode = TailBiteDefaults.NewQrCode() }.Create(Db);
        Db.ChangeTracker.Clear();
        Assert.That((await TailBiteSetup().ListEnabledPropertiesAsync(WorkerSite)).Select(p => p.PropertyId), Does.Contain(id));

        await UpdateAsync(model, id, tailBite: false);
        Assert.That(await IsEnabledAsync(id), Is.False);
        Assert.That(await Db.TailBiteLocations.CountAsync(l => l.PropertyId == id), Is.EqualTo(2));
        Assert.That(await RuleCountAsync(id), Is.EqualTo(1));
        Assert.That(await Db.PropertyWorkers.AnyAsync(pw => pw.PropertyId == id && pw.TailBiteManager), Is.True);
        Assert.That((await TailBiteSetup().ListEnabledPropertiesAsync(WorkerSite)).Select(p => p.PropertyId), Does.Not.Contain(id));
        Assert.That(await ReadTailBiteAsync(id), Is.False);
        Assert.That(await IndexTailBiteAsync(id), Is.False);

        await UpdateAsync(model, id, tailBite: true);
        Assert.That(await IsEnabledAsync(id), Is.True);
        Assert.That(await RootCountAsync(id), Is.EqualTo(1));
        Assert.That(await Db.TailBiteLocations.CountAsync(l => l.PropertyId == id), Is.EqualTo(2));
        Assert.That(await RuleCountAsync(id), Is.EqualTo(1));
        Assert.That((await TailBiteSetup().ListEnabledPropertiesAsync(WorkerSite)).Select(p => p.PropertyId), Does.Contain(id));
        Assert.That(await ReadTailBiteAsync(id), Is.True);
    }

    [Test]
    public async Task Update_OnForAPropertyCreatedWithoutIt_SetsItUp()
    {
        var model = NewProperty(tailBite: false);
        var id = await CreateAsync(model);

        await UpdateAsync(model, id, tailBite: true);

        Assert.That(await IsEnabledAsync(id), Is.True);
        Assert.That(await RootCountAsync(id), Is.EqualTo(1));
        Assert.That(await RuleCountAsync(id), Is.EqualTo(1));
    }

    [Test]
    public async Task Update_WithoutTailBiteValue_LeavesItAsItIs()
    {
        var model = NewProperty(tailBite: true);
        var id = await CreateAsync(model);

        await UpdateAsync(model, id, tailBite: null);

        Assert.That(await IsEnabledAsync(id), Is.True);
        Assert.That(await ReadTailBiteAsync(id), Is.True);
    }
}
