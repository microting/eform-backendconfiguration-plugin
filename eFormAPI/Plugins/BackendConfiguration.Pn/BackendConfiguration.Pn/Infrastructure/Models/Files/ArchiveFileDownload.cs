#nullable enable
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Infrastructure.Models.Files;

/// <summary>A readable document's current name and its stored file, for a zip download.</summary>
public sealed record ArchiveFileDownload(string FileName, UploadedData Data);
