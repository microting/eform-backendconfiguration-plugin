using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Integration.Test.UserPropertyAccess;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationUserPropertyAccessTests : TestBaseSetup
{
    [SetUp]
    public async Task ClearPropertyWorkers()
    {
        BackendConfigurationPnDbContext!.PropertyWorkers.RemoveRange(
            BackendConfigurationPnDbContext.PropertyWorkers);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    [Test]
    public async Task GetAccessiblePropertyIdsAsync_returns_only_properties_where_site_has_active_property_worker()
    {
        BackendConfigurationPnDbContext!.Properties.Add(new Property { Id = 100 });
        BackendConfigurationPnDbContext.Properties.Add(new Property { Id = 101 });
        BackendConfigurationPnDbContext.Properties.Add(new Property { Id = 102 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Created });
        BackendConfigurationPnDbContext.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 101, WorkflowState = Constants.WorkflowStates.Removed });
        BackendConfigurationPnDbContext.PropertyWorkers.Add(new PropertyWorker { WorkerId = 99, PropertyId = 102, WorkflowState = Constants.WorkflowStates.Created });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext);

        var ids = await sut.GetAccessiblePropertyIdsAsync(7);

        Assert.That(ids, Is.EquivalentTo(new[] { 100 }));
    }

    [Test]
    public async Task GetAccessiblePropertyIdsAsync_returns_empty_for_unknown_site()
    {
        BackendConfigurationPnDbContext!.Properties.Add(new Property { Id = 100 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Created });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext);

        var ids = await sut.GetAccessiblePropertyIdsAsync(42);

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public async Task HasAccessAsync_true_when_active_property_worker_exists()
    {
        BackendConfigurationPnDbContext!.Properties.Add(new Property { Id = 100 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Created });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext);

        Assert.That(await sut.HasAccessAsync(7, 100), Is.True);
    }

    [Test]
    public async Task HasAccessAsync_false_for_removed_property_worker()
    {
        BackendConfigurationPnDbContext!.Properties.Add(new Property { Id = 100 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Removed });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext);

        Assert.That(await sut.HasAccessAsync(7, 100), Is.False);
    }

    [Test]
    public async Task HasAccessAsync_false_for_different_property()
    {
        BackendConfigurationPnDbContext!.Properties.Add(new Property { Id = 100 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Created });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext);

        Assert.That(await sut.HasAccessAsync(7, 999), Is.False);
    }
}
