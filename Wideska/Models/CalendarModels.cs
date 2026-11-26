namespace Wideska.Models;

public enum CalendarView { SingleDay, ThreeDays, Workweek, Week, Month, Year }
public enum WindowBehavior { Normal, AlwaysOnTop, Desktop }
public sealed class WidgetSettings
{
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 720;
    public double Height { get; set; } = 560;
    public double BackgroundOpacity { get; set; } = 0.14;
    public bool DarkTheme { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public WindowBehavior Behavior { get; set; }
    public CalendarView View { get; set; } = CalendarView.Month;
    public int SyncMinutes { get; set; } = 5;
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public List<string> HiddenCalendars { get; set; } = [];
}
public sealed record CalendarInfo(string Id, string Name, string Color);
public sealed record CalendarEvent(string Id, string CalendarId, string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string Color, string Location, string Description);
public sealed record CalendarSnapshot(List<CalendarInfo> Calendars, List<CalendarEvent> Events, DateTimeOffset Updated, DateTimeOffset RangeStart = default, DateTimeOffset RangeEnd = default);
