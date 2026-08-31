#nullable enable
namespace BackendConfiguration.Pn.Services.PushNotificationService;

using System.Collections.Generic;
using System.Threading.Tasks;

public interface IPushNotificationService
{
    /// <summary>
    /// Sends a push to every live flutter-eform device registered to
    /// <paramref name="targetSdkSiteId"/>.
    /// </summary>
    /// <remarks>
    /// Leave both <paramref name="title"/> and <paramref name="body"/> empty to
    /// send a data-only silent push: no visible notification, and iOS is woken
    /// in the background to process <paramref name="data"/>.
    ///
    /// This method never throws and never reports failure. A push is a
    /// courtesy on top of whatever request triggered it, so a Firebase outage,
    /// an unconfigured credential or a dead token must not fail that request;
    /// the mobile client reconciles on its next foreground either way.
    /// </remarks>
    Task SendToSiteAsync(
        int targetSdkSiteId,
        string title,
        string body,
        Dictionary<string, string>? data = null);
}
