#nullable enable
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Microting.TimePlanningBase 10.0.67 made <c>AssignedSites.ResignedAtDate</c> nullable and clears it for every
/// non-resigned site. Medarbejdere must list such a worker, and a save must not put a placeholder date back.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Fixtures)]
public class AssignedSiteResignedAtDateTests : LederWorkerTestBase
{
    private Task<DateTime?> StoredResignedAtDateAsync(Worker w) =>
        TimePlanningPnDbContext!.AssignedSites.AsNoTracking()
            .Where(x => x.SiteId == w.SiteUid && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.ResignedAtDate).SingleAsync();

    [Test]
    public async Task IndexDeviceUser_ListsATimeRegistrationWorkerWithoutAResignationDate()
    {
        var worker = await CreateWorkerAsync(timeRegistration: true);
        await TimePlanningPnDbContext!.AssignedSites.Where(x => x.SiteId == worker.SiteUid)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ResignedAtDate, (DateTime?)null));

        var row = Row(await ListAsync(), worker);

        Assert.That(row.TimeRegistrationEnabled, Is.True);
    }

    [Test]
    public async Task UpdateDeviceUser_NotResigned_StoresNoResignationDate()
    {
        var worker = await CreateWorkerAsync(timeRegistration: true);

        // The web dialog sends a date even when the worker has not resigned.
        var result = await UpdateAsync(worker, Admin, timeRegistration: true, resignedAtDate: DateTime.UtcNow.Date);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await StoredResignedAtDateAsync(worker), Is.Null);
    }
}
