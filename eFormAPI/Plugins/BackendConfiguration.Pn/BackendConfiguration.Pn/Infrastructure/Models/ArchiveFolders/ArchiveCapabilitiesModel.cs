namespace BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;

/// <summary>
/// What the caller may do in the archive (D-L7). Today CanDelete and CanUseInbox equal CanManage; they are separate so a
/// later split needs no client change.
/// </summary>
public class ArchiveCapabilitiesModel
{
    public bool CanManage { get; set; }
    public bool CanDelete { get; set; }
    public bool CanUseInbox { get; set; }

    /// <summary>The tenant's first user (lowest AspNetUsers Id): only they may use "Lav ny adresse".</summary>
    public bool IsFirstUser { get; set; }
}
