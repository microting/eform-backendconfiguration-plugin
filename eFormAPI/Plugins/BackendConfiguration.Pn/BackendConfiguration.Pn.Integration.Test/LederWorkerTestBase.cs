#nullable enable
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.Leder;
using eFormCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using NSubstitute;
using PnEntities = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

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

    /// <summary>The service as DI builds it; localisation echoes keys so tests assert the key.</summary>
    protected BackendConfigurationAssignmentWorkerService Service(IUserService caller, ILederService leder)
    {
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new BackendConfigurationAssignmentWorkerService(LederTestData.CoreHelper(Core), UserManager, caller,
            BackendConfigurationPnDbContext!, localization, ItemsPlanningPnDbContext!, TimePlanningPnDbContext!,
            CaseTemplatePnDbContext!, BaseDbContext, TestContextLogger<BackendConfigurationAssignmentWorkerService>.Instance,
            Substitute.For<ICalendarAssignmentReconciliationService>(), leder);
    }

    /// <summary>A caller for the service: the real username lookup plus an id, a JWT admin flag and "is first user".</summary>
    protected IUserService ServiceCaller(int userId, bool isAdmin)
    {
        var caller = IdentityTestUtils.CreateRealUserService(BaseDbContext, UserManager);
        caller.UserId.Returns(userId);
        caller.IsAdmin().Returns(isAdmin);
        caller.GetFirstUserIdInDb().Returns(Task.FromResult(userId));
        return caller;
    }

    /// <summary>The real Leder check for <paramref name="caller"/>.</summary>
    protected LederService RealLeder(IUserService caller) =>
        new(caller, BaseDbContext, BackendConfigurationPnDbContext!, LederTestData.CoreHelper(Core));

    /// <summary>The service called by the actor as a JWT admin (and first user).</summary>
    protected BackendConfigurationAssignmentWorkerService AdminService() =>
        Service(ServiceCaller(ActorId, isAdmin: true), LederTestData.Leder(true));

    /// <summary>The Medarbejdere list as the actor sees it, with the given caller flags.</summary>
    protected async Task<List<DeviceUserModel>> ListAsync(bool callerIsAdmin = true, bool callerIsLeder = true,
        bool showResigned = false)
    {
        var result = await Service(ServiceCaller(ActorId, callerIsAdmin), LederTestData.Leder(callerIsAdmin || callerIsLeder))
            .IndexDeviceUser(new PropertyWorkersFiltrationModel { ShowResigned = showResigned });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    protected static DeviceUserModel Row(List<DeviceUserModel> rows, Worker w) => rows.Single(r => r.SiteId == w.SiteId);

    protected async Task<PnEntities.Property> PropertyAsync()
    {
        var property = new PnEntities.Property
        {
            Name = $"Example Property {Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = ActorId, UpdatedByUserId = ActorId
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property;
    }

    protected async Task<PnEntities.PropertyWorker> LinkAsync(int propertyId, int siteId)
    {
        var link = new PnEntities.PropertyWorker
        {
            PropertyId = propertyId, WorkerId = siteId, TaskManagementEnabled = false,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = ActorId, UpdatedByUserId = ActorId
        };
        await link.Create(BackendConfigurationPnDbContext!);
        return link;
    }

    /// <summary>An active event (Status true, live) on <paramref name="propertyId"/> naming the site directly.</summary>
    protected async Task ActiveEventNamingAsync(int propertyId, int siteId)
    {
        var area = new PnEntities.Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = ActorId, UpdatedByUserId = ActorId
        };
        await area.Create(BackendConfigurationPnDbContext!);
        var areaRule = new PnEntities.AreaRule
        {
            AreaId = area.Id, PropertyId = propertyId, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = ActorId, UpdatedByUserId = ActorId
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        var arp = new PnEntities.AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId, AreaId = area.Id, ItemPlanningId = 0,
            StartDate = DateTime.UtcNow.Date, Status = true, RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = ActorId, UpdatedByUserId = ActorId
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        await new PnEntities.PlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = siteId, AreaId = area.Id, AreaRuleId = areaRule.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = ActorId, UpdatedByUserId = ActorId
        }.Create(BackendConfigurationPnDbContext!);
    }
}
