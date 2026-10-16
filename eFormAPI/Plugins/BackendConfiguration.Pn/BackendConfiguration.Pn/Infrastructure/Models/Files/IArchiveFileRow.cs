#nullable enable
using System.Collections.Generic;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Infrastructure.Models.Files;

/// <summary>A document row that carries its folder path and its effective read groups.</summary>
public interface IArchiveFileRow
{
    int Id { get; }
    int FolderId { get; }
    bool OwnAccess { get; }
    List<string> FolderPath { get; set; }
    List<CommonTagModel> ReadGroups { get; set; }
}
