using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.UserPropertyAccess;

[TestFixture]
public class BackendConfigurationUserPropertyAccessTests
{
    private BackendConfigurationPnDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<BackendConfigurationPnDbContext>()
            .UseInMemoryDatabase(dbName)
            .EnableSensitiveDataLogging()
            .Options;
        return new BackendConfigurationPnDbContext(options);
    }

    [Test]
    public async Task GetAccessiblePropertyIdsAsync_returns_only_properties_where_site_has_active_property_worker()
    {
        await using var ctx = NewContext(nameof(GetAccessiblePropertyIdsAsync_returns_only_properties_where_site_has_active_property_worker));
        ctx.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Created });
        ctx.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 101, WorkflowState = Constants.WorkflowStates.Removed });
        ctx.PropertyWorkers.Add(new PropertyWorker { WorkerId = 99, PropertyId = 102, WorkflowState = Constants.WorkflowStates.Created });
        await ctx.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(ctx);

        var ids = await sut.GetAccessiblePropertyIdsAsync(7);

        Assert.That(ids, Is.EquivalentTo(new[] { 100 }));
    }

    [Test]
    public async Task GetAccessiblePropertyIdsAsync_returns_empty_for_unknown_site()
    {
        await using var ctx = NewContext(nameof(GetAccessiblePropertyIdsAsync_returns_empty_for_unknown_site));
        ctx.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Created });
        await ctx.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(ctx);

        var ids = await sut.GetAccessiblePropertyIdsAsync(42);

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public async Task HasAccessAsync_true_when_active_property_worker_exists()
    {
        await using var ctx = NewContext(nameof(HasAccessAsync_true_when_active_property_worker_exists));
        ctx.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Created });
        await ctx.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(ctx);

        Assert.That(await sut.HasAccessAsync(7, 100), Is.True);
    }

    [Test]
    public async Task HasAccessAsync_false_for_removed_property_worker()
    {
        await using var ctx = NewContext(nameof(HasAccessAsync_false_for_removed_property_worker));
        ctx.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Removed });
        await ctx.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(ctx);

        Assert.That(await sut.HasAccessAsync(7, 100), Is.False);
    }

    [Test]
    public async Task HasAccessAsync_false_for_different_property()
    {
        await using var ctx = NewContext(nameof(HasAccessAsync_false_for_different_property));
        ctx.PropertyWorkers.Add(new PropertyWorker { WorkerId = 7, PropertyId = 100, WorkflowState = Constants.WorkflowStates.Created });
        await ctx.SaveChangesAsync();

        var sut = new BackendConfigurationUserPropertyAccess(ctx);

        Assert.That(await sut.HasAccessAsync(7, 999), Is.False);
    }
}
