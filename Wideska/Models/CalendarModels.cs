namespace Wideska.Models;

public enum CalendarView { SingleDay, ThreeDays, Workweek, Week, Month, Year }
public enum WindowBehavior { Normal, AlwaysOnTop, Desktop }
public sealed class WidgetSettings
{
    public string Language { get; set; } = "system";
    public ScreenPlacement? DesktopPlacement { get; set; }
    public ScreenPlacement? RestorePlacement { get; set; }
    public bool IsDesktopFilled { get; set; }
    public bool GeometryLocked { get; set; }
    public string TimeZoneCalendarId { get; set; } = "";
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 720;
    public double Height { get; set; } = 560;
    public double BackgroundOpacity { get; set; } = 0.14;
    public bool DarkTheme { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public WindowBehavior Behavior { get; set; }
    public CalendarView View { get; set; } = CalendarView.Month;
    public int SyncMinutes { get; set; } = 1;
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public List<string> HiddenCalendars { get; set; } = [];
}
public sealed record ScreenPlacement(int X, int Y, int Width, int Height);
public sealed record CalendarInfo(string Id, string Name, string Color, string TimeZoneId = "", bool IsPrimary = false);
public sealed record CalendarEvent(string Id, string CalendarId, string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string Color, string Location, string Description);
public sealed record CalendarSnapshot(List<CalendarInfo> Calendars, List<CalendarEvent> Events, DateTimeOffset Updated, DateTimeOffset RangeStart = default, DateTimeOffset RangeEnd = default);
