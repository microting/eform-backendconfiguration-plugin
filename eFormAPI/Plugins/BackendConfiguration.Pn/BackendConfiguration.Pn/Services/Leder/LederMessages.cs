#nullable enable
namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>localization.json keys of the Leder refusals. The service layer turns them into text.</summary>
public static class LederMessages
{
    public const string LederRequiresLogin = nameof(LederRequiresLogin);
    public const string LederLoginChangeRequiresAdmin = nameof(LederLoginChangeRequiresAdmin);
    public const string WorkerTagsRequireLeder = nameof(WorkerTagsRequireLeder);
}
