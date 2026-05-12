namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System;

/// <summary>
/// Thrown when the Microting OAuth proxy reports the user's Google refresh
/// token is no longer valid (Google returned <c>invalid_grant</c>). Callers
/// should surface a "please reconnect" message to the user. The
/// corresponding <see cref="Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.GoogleOAuthToken"/>
/// has its <c>RevokedAt</c> stamped before the exception is raised, so the
/// status endpoint will report disconnected on the next poll.
/// </summary>
public class GoogleDriveTokenRevokedException : Exception
{
    public GoogleDriveTokenRevokedException(string message) : base(message)
    {
    }

    public GoogleDriveTokenRevokedException(string message, Exception inner) : base(message, inner)
    {
    }
}
