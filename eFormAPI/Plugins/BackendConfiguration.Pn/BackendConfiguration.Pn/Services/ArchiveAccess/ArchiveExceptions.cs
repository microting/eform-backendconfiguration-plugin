#nullable enable
using System;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>
/// D7: the caller's account is disabled. ArchiveAccessFilterAttribute answers 403 {"error":"account_disabled"}.
/// </summary>
public sealed class ArchiveAccountDisabledException() : Exception("account_disabled");

/// <summary>
/// A document or folder that does not exist or the caller cannot read. ArchiveAccessFilterAttribute answers 404,
/// the same for both, so a restricted document's existence does not leak.
/// </summary>
public sealed class ArchiveNotFoundException() : Exception("archive item not found or not readable");

/// <summary>
/// The destination folder of an upload or inbox filing is no longer a live folder the caller can read, found by the
/// re-check inside the archiver's transaction (a concurrent delete or access change). The callers answer it with the
/// same ArchiveFolderNotFound refusal as their early check.
/// </summary>
public sealed class ArchiveFolderUnavailableException() : Exception("archive folder removed or no longer readable");

public static class ArchiveExceptions
{
    /// <summary>
    /// False for the archive refusals, which ArchiveAccessFilter answers 403/404; use as a catch filter so a broad
    /// catch never turns them into success=false.
    /// </summary>
    public static bool IsUnexpected(Exception e) => e is not (ArchiveNotFoundException or ArchiveAccountDisabledException);
}
