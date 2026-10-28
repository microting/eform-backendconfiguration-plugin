#nullable enable
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Services.Leder;
using eFormCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Medarbejdere saves through the real Helper against the real databases, with the caller passed as a LederCaller (as
/// WorkerService does). Fictional people only; the actor is a fresh login, never a fixed id.
/// </summary>
public abstract class LederWorkerTestBase : TestBaseSetup
{
    protected sealed record Worker(int SiteUid, int SiteId, string Email, string LastName);

    protected Core Core = null!;
    protected UserManager<EformUser> UserManager = null!;
    protected IUserService UserService = null!;
    protected int ActorId;

    protected LederCaller Admin => new(true, true, ActorId);
    protected LederCaller NonAdmin => new(false, false, ActorId);

    /// <summary>A Leder (or an "eForm admins" member) who is not a JWT admin: may change worker tags, not toggle Leder.</summary>
    protected LederCaller LederNonAdmin => new(false, true, ActorId);

    [SetUp]
    public async Task ArrangeLeder()
    {
        await IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext);
        Core = await GetCore();
        UserManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext);
        UserService = IdentityTestUtils.CreateRealUserService(BaseDbContext, UserManager);
        ActorId = (await LederTestData.LoginAsync(BaseDbContext, UserManager)).Id;
    }

    [TearDown]
    public void DisposeUserManager() => UserManager?.Dispose();

    protected static string NewEmail(string domain = "example.com") => $"{Guid.NewGuid():N}@{domain}";

    protected static string NewLastName(string prefix = "Doe") => $"{prefix} {Guid.NewGuid():N}";

    private static DeviceUserModel BuildModel(string lastName, string email, bool web, bool archive, bool? isLeader,
        List<int> tags, string pinCode) =>
        new()
        {
            LanguageCode = "da", UserFirstName = "Jane", UserLastName = lastName, WorkerEmail = email,
            WebAccessEnabled = web, ArchiveEnabled = archive, TimeRegistrationEnabled = false,
            IsLeader = isLeader, Tags = tags, PinCode = pinCode, EmployeeNo = ""
        };

    protected async Task<OperationDataResult<int>> TryCreateAsync(LederCaller? caller, string lastName, string email,
        bool web = true, bool archive = false, bool? isLeader = null, List<int>? tags = null) =>
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(
            BuildModel(lastName, email, web, archive, isLeader, tags ?? [], ""),
            Core, ActorId, TimePlanningPnDbContext!, BaseDbContext, UserService, UserManager, null,
            TestContextLogger<LederWorkerTestBase>.Instance, caller, BackendConfigurationPnDbContext);

    protected async Task<Worker> CreateWorkerAsync(LederCaller? caller = null, bool web = true, bool archive = false,
        bool? isLeader = null, List<int>? tags = null, string? email = null)
    {
        var lastName = NewLastName();
        var address = email ?? NewEmail();
        var result = await TryCreateAsync(caller ?? Admin, lastName, address, web, archive, isLeader, tags);
        Assert.That(result.Success, Is.True, result.Message);
        var site = await MicrotingDbContext!.Sites.AsNoTracking().SingleAsync(x => x.Name == $"Jane {lastName}");
        return new Worker((int)site.MicrotingUid!, site.Id, address, lastName);
    }

    protected Task<List<int>> StoredTagIdsAsync(Worker w) =>
        MicrotingDbContext!.SiteTags.AsNoTracking()
            .Where(x => x.SiteId == w.SiteId && x.TagId != null && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.TagId!.Value).OrderBy(x => x).ToListAsync();

    protected async Task<OperationResult> UpdateAsync(Worker w, LederCaller? caller, bool? isLeader = null,
        bool web = true, bool archive = false, string? email = null, List<int>? tags = null, bool resigned = false,
        string? lastName = null)
    {
        var model = BuildModel(lastName ?? w.LastName, email ?? w.Email, web, archive, isLeader,
            tags ?? await StoredTagIdsAsync(w), "****");
        model.SiteMicrotingUid = w.SiteUid;
        model.Resigned = resigned;
        model.ResignedAtDate = resigned ? DateTime.UtcNow.Date : DateTime.MinValue;
        return await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(model, Core, ActorId,
            UserService, UserManager, BackendConfigurationPnDbContext!, TimePlanningPnDbContext!, BaseDbContext,
            TestContextLogger<LederWorkerTestBase>.Instance, ItemsPlanningPnDbContext!, null, null, caller);
    }

    protected Task<EformUser> LoginOfAsync(Worker w) =>
        BaseDbContext.Users.AsNoTracking().SingleAsync(u => u.Email == w.Email);

    protected async Task<int> LoginIdOfAsync(Worker w) => (await LoginOfAsync(w)).Id;

    protected Task<EformUser> LoginByIdAsync(int id) => BaseDbContext.Users.AsNoTracking().SingleAsync(u => u.Id == id);

    protected Task<bool> SiteExistsAsync(string lastName) =>
        MicrotingDbContext!.Sites.AsNoTracking().AnyAsync(x => x.Name == $"Jane {lastName}");

    /// <summary>Puts the login in a group the plugin does not manage ("Bogholderi"), making it an owner-group login.</summary>
    protected async Task MakeOwnerGroupMemberAsync(int loginId) =>
        await LederTestData.AddMemberAsync(BaseDbContext, loginId,
            await LederTestData.NewGroupAsync(BaseDbContext, "Bogholderi"));

    protected Task<bool> IsLederAsync(int eformUserId) =>
        LederAssignmentStore.HasLiveRowAsync(BackendConfigurationPnDbContext!, eformUserId);

    protected Task<List<string>> GroupNamesOfAsync(int eformUserId) =>
        LederTestData.GroupNamesOfAsync(BaseDbContext, eformUserId);

    protected async Task<int> SdkTagAsync(string name)
    {
        var tag = new Tag { Name = $"{name} {Guid.NewGuid():N}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext!.Tags.AddAsync(tag);
        await MicrotingDbContext.SaveChangesAsync();
        return tag.Id;
    }
}
