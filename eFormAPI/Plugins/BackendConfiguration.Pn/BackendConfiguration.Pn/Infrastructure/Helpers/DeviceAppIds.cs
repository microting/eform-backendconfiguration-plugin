namespace BackendConfiguration.Pn.Infrastructure.Helpers;

/// <summary>
/// The <c>app_id</c> each mobile app sends to <c>RegisterPushToken</c> and
/// <c>ReportInstall</c> (DeviceToken.AppId). Not the keys of the latest-versions
/// file: Compliance's app sends "eform".
/// </summary>
public static class DeviceAppIds
{
    public const string Eform = "eform";
    public const string Adhoc = "adhoc";
    public const string Chemistry = "chemistry";
    public const string Halebid = "halebid";
}
