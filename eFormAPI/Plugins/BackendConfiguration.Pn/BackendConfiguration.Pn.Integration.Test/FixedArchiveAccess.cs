#nullable enable
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.Leder;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The real access service (SQL filter, visibility, folder rules) with a fixed caller and no SDK: the caller
/// context, group names and live groups are what the test says. ArchiveAccessContextTests covers the SDK half.
/// </summary>
public sealed class FixedArchiveAccess(ArchiveAccessContext ctx, BackendConfigurationPnDbContext db, params int[] liveGroups)
    : ArchiveAccessService(db, Substitute.For<IUserService>(), Substitute.For<IEFormCoreService>(),
        Substitute.For<IWorkerTagMembershipService>(), Substitute.For<ILederService>(),
        NullLogger<ArchiveAccessService>.Instance)
{
    public ArchiveAccessContext Context { get; set; } = ctx;

    public override Task<ArchiveAccessContext> GetContextAsync() => Task.FromResult(Context);

    public override Task<Dictionary<int, string>> GetGroupNamesAsync(IReadOnlyCollection<int> tagIds) =>
        Task.FromResult(tagIds.Distinct().ToDictionary(id => id, id => $"Gruppe {id}"));

    public override Task<HashSet<int>> GetLiveGroupIdsAsync() => Task.FromResult(liveGroups.ToHashSet());
}
