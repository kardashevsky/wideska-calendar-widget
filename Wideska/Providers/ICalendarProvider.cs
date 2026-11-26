using Wideska.Models;
namespace Wideska.Providers;
public interface ICalendarProvider : IDisposable
{
    Task<bool> IsConnectedAsync(CancellationToken token);
    Task ConnectAsync(string clientId, string clientSecret, CancellationToken token);
    Task DisconnectAsync(CancellationToken token);
    Task<CalendarSnapshot> GetSnapshotAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken token);
}
