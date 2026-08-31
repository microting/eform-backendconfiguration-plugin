#nullable enable
namespace BackendConfiguration.Pn.Services.CalendarChangeNotification;

using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.PushNotificationService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class CalendarChangeNotifier(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarChangeNotifier> logger) : ICalendarChangeNotifier
{
    public const string CalendarChangedType = "calendar_changed";
    internal const string TypeKey = "type";
    internal const string EventIdKey = "event_id";
    internal const int MaxDistinctPairsPerOperation = 100;

    public void NotifyInBackground(CalendarChangeBatch batch)
    {
    }

    internal Task? LastDispatch { get; private set; }

    internal Task DispatchAsync(IReadOnlyCollection<CalendarChangePair> pairs) => Task.CompletedTask;

    internal Task SendAsync(IPushNotificationService push, IReadOnlyCollection<CalendarChangePair> pairs) =>
        Task.CompletedTask;
}
