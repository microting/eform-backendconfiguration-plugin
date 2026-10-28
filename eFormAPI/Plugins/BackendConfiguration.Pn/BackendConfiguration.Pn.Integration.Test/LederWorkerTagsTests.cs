#nullable enable
using BackendConfiguration.Pn.Services.Leder;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>D-L12 / S5: Læseadgang comes from worker tags, so only admins and Leders change them, on every plugin path.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederWorkerTagsTests : LederWorkerTestBase
{
    [Test]
    public async Task UpdateDeviceUser_NonLeder_ChangedTags_IsRefused_TagsUnchanged()
    {
        var drift = await SdkTagAsync("Drift");
        var ledelse = await SdkTagAsync("Ledelse");
        var w = await CreateWorkerAsync(tags: [drift]);

        var result = await UpdateAsync(w, NonAdmin, tags: [drift, ledelse]);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(LederMessages.WorkerTagsRequireLeder));
        Assert.That(await StoredTagIdsAsync(w), Is.EqualTo(new[] { drift }));
    }

    [Test]
    public async Task UpdateDeviceUser_NonLeder_SameTags_Saves()
    {
        var drift = await SdkTagAsync("Drift");
        var w = await CreateWorkerAsync(tags: [drift]);

        var result = await UpdateAsync(w, NonAdmin, tags: [drift], lastName: NewLastName("Roe"));

        Assert.That(result.Success, Is.True, result.Message);
    }

    [Test]
    public async Task UpdateDeviceUser_NonLeder_StoredSetHoldsARemovedTag_StillSaves()
    {
        // Review Focus 1: the list leaves removed tags out, so the web sends the live set back.
        var drift = await SdkTagAsync("Drift");
        var gone = await SdkTagAsync("Udgået");
        var w = await CreateWorkerAsync(tags: [drift, gone]);
        await MicrotingDbContext!.Tags.Where(t => t.Id == gone)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.WorkflowState, Constants.WorkflowStates.Removed));

        var result = await UpdateAsync(w, NonAdmin, tags: [drift]);

        Assert.That(result.Success, Is.True, result.Message);
    }

    [Test]
    public async Task UpdateDeviceUser_LederNonAdmin_ChangesTags()
    {
        var drift = await SdkTagAsync("Drift");
        var w = await CreateWorkerAsync();

        var result = await UpdateAsync(w, LederNonAdmin, tags: [drift]);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await StoredTagIdsAsync(w), Is.EqualTo(new[] { drift }));
    }

    [Test]
    public async Task CreateDeviceUser_NonLederWithTags_IsRefused_NothingCreated()
    {
        var drift = await SdkTagAsync("Drift");
        var lastName = NewLastName();

        var result = await TryCreateAsync(NonAdmin, lastName, NewEmail(), tags: [drift]);

        Assert.That(result.Message, Is.EqualTo(LederMessages.WorkerTagsRequireLeder));
        Assert.That(await SiteExistsAsync(lastName), Is.False);
    }

    [Test]
    public async Task CreateDeviceUser_LederNonAdminWithTags_Creates()
    {
        var drift = await SdkTagAsync("Drift");
        var w = await CreateWorkerAsync(LederNonAdmin, tags: [drift]);
        Assert.That(await StoredTagIdsAsync(w), Is.EqualTo(new[] { drift }));
    }
}
