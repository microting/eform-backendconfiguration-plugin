#nullable enable
using System.Collections.Generic;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;

/// <summary>
/// One folder of the tenant-wide tree (D16). <see cref="ReadGroups"/> are the folder's EFFECTIVE groups (its own,
/// else its parent's; empty = open); <see cref="OwnGroups"/> says they are its own ("Arver fra …" when false and
/// restricted); <see cref="Restricted"/> = effective groups non-empty.
/// <see cref="Count"/> = readable documents in it (a top folder: plus all its subfolders') for the selected properties.
/// </summary>
public class ArchiveFolderModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public bool IsDefault { get; set; }
    public int Count { get; set; }
    public List<CommonTagModel> ReadGroups { get; set; } = [];
    public bool OwnGroups { get; set; }
    public bool Restricted { get; set; }
}

public class ArchiveFolderCreateModel
{
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
}

/// <summary>Null <see cref="Name"/>/<see cref="ParentId"/> = unchanged; <see cref="MoveToTop"/> makes it a top folder.</summary>
public class ArchiveFolderUpdateModel
{
    public string? Name { get; set; }
    public int? ParentId { get; set; }
    public bool MoveToTop { get; set; }
}

/// <summary>Medarbejdergrupper (SDK tag ids). Empty = open to everyone with archive access.</summary>
public class ArchiveFolderAccessModel
{
    public List<int> TagIds { get; set; } = [];
}

/// <summary>How many documents' effective read access changed (D11 message: "3 dokumenter kan nu læses af alle").</summary>
public class AccessChangeModel
{
    public int AccessChanged { get; set; }
}
