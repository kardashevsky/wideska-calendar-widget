using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Wideska.Infrastructure;
using Wideska.Models;
using Wideska.Providers;
using Wideska.Services;

namespace Wideska.ViewModels;
public sealed class CalendarSelection(CalendarInfo calendar, bool visible, Action changed) : ObservableObject
{
    public CalendarInfo Calendar { get; } = calendar;
    private bool visible = visible;
    public bool Visible { get => visible; set { if (Set(ref visible, value)) changed(); } }
}
public sealed record EventItem(string Title, string Time, string Color, string Details);
public sealed record DayCell(DateTime Date, bool InMonth, bool Today, IReadOnlyList<EventItem> Events)
{
    public string Label => Date.ToString("ddd, d", CultureInfo.CurrentCulture);
    public string Number => Date.Day.ToString(CultureInfo.InvariantCulture);
}
public sealed record MonthItem(string Title, IReadOnlyList<DayCell> Days);
public sealed record ViewChoice(CalendarView Value, string Title)
{
    public override string ToString() => Title;
}
public sealed record BehaviorChoice(WindowBehavior Value, string Title)
{
    public override string ToString() => Title;
}
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly LocalStore store;
    private readonly ICalendarProvider provider;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private Task? refreshLoop;
    private CalendarSnapshot snapshot = new([], [], default);
    private readonly Dictionary<DateTime, List<CalendarEvent>> eventsByDay = [];
    public WidgetSettings Settings { get; private set; } = new();
    public ObservableCollection<CalendarSelection> Calendars { get; } = [];
    public ObservableCollection<DayCell> Days { get; } = [];
    public ObservableCollection<MonthItem> Months { get; } = [];
    public ViewChoice[] Views { get; } = [new(CalendarView.SingleDay, "Day"), new(CalendarView.ThreeDays, "3 days"), new(CalendarView.Workweek, "Workweek"), new(CalendarView.Week, "Week"), new(CalendarView.Month, "Month"), new(CalendarView.Year, "Year")];
    public BehaviorChoice[] Behaviors { get; } = [new(WindowBehavior.Normal, "Normal window"), new(WindowBehavior.AlwaysOnTop, "Always on top"), new(WindowBehavior.Desktop, "Behind other windows")];
    private DateTime anchor = DateTime.Today;
    private DateTime observedToday = DateTime.Today;
    private bool followToday = true;
    private string status = "Connect Google Calendar to display your events.";
    public string Status { get => status; private set => Set(ref status, value); }
    private bool busy;
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    private bool connected;
    public bool Connected { get => connected; private set { Set(ref connected, value); Changed(nameof(ConnectionLabel)); } }
    public string ConnectionLabel => Connected ? "Google Calendar connected" : "No account connected";
    private bool settingsOpen;
    public bool SettingsOpen { get => settingsOpen; set => Set(ref settingsOpen, value); }
    public string Period => View == CalendarView.Year ? anchor.ToString("yyyy") : anchor.ToString("MMMM yyyy");
    public bool IsYear => View == CalendarView.Year;
    public bool IsMonth => View == CalendarView.Month;
    public bool IsDays => !IsYear && !IsMonth;
    public int Columns => View switch { CalendarView.SingleDay => 1, CalendarView.ThreeDays => 3, CalendarView.Workweek => 5, _ => 7 };
    public CalendarView View
    {
        get => Settings.View;
        set { if (Settings.View == value) return; Settings.View = value; Changed(); Rebuild(); _ = SaveSafelyAsync(); }
    }
    public bool DarkTheme { get => Settings.DarkTheme; set { Settings.DarkTheme = value; Changed(); AppearanceChanged?.Invoke(); _ = SaveSafelyAsync(); } }
    public double Opacity { get => Settings.Opacity; set { Settings.Opacity = Math.Clamp(value, .3, 1); Changed(); AppearanceChanged?.Invoke(); } }
    public WindowBehavior Behavior { get => Settings.Behavior; set { Settings.Behavior = value; Changed(); AppearanceChanged?.Invoke(); _ = SaveSafelyAsync(); } }
    public bool StartWithWindows { get => Settings.StartWithWindows; set { Settings.StartWithWindows = value; Changed(); AppearanceChanged?.Invoke(); _ = SaveSafelyAsync(); } }
    public string ClientId { get => Settings.ClientId; set { Settings.ClientId = value; Changed(); } }
    public string ClientSecret { get => Settings.ClientSecret; set { Settings.ClientSecret = value; Changed(); } }
    public int SyncMinutes { get => Settings.SyncMinutes; set { Settings.SyncMinutes = Math.Clamp(value, 1, 60); Changed(); } }
    public event Action? AppearanceChanged;
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SelectDayCommand { get; }
    public MainViewModel(LocalStore store, ICalendarProvider provider)
    {
        this.store = store; this.provider = provider;
        PreviousCommand = new RelayCommand(_ => Navigate(-1));
        NextCommand = new RelayCommand(_ => Navigate(1));
        TodayCommand = new RelayCommand(_ => { followToday = true; anchor = DateTime.Today; Rebuild(); _ = RefreshIfNeededAsync(); });
        RefreshCommand = new AsyncCommand(RefreshAsync, ReportError);
        ConnectCommand = new AsyncCommand(ConnectAsync, ReportError);
        DisconnectCommand = new AsyncCommand(async () =>
        {
            await syncGate.WaitAsync(lifetime.Token);
            try { await provider.DisconnectAsync(lifetime.Token); Connected = false; snapshot = new([], [], default); eventsByDay.Clear(); Calendars.Clear(); await store.DeleteAsync("calendar-cache.bin"); Rebuild(); Status = "Account disconnected. Local calendar cache removed."; }
            finally { syncGate.Release(); }
        }, ReportError);
        SettingsCommand = new RelayCommand(_ => SettingsOpen = !SettingsOpen);
        SaveSettingsCommand = new AsyncCommand(async () => { await SaveAsync(); SettingsOpen = false; await RefreshAsync(); }, ReportError);
        SelectDayCommand = new RelayCommand(value => { if (value is DateTime date) { followToday = false; anchor = date; if (View == CalendarView.SingleDay) Rebuild(); else View = CalendarView.SingleDay; _ = RefreshIfNeededAsync(); } });
        Rebuild();
    }
    private async Task ConnectAsync()
    {
        await syncGate.WaitAsync(lifetime.Token);
        Busy = true;
        Status = "Complete authorization in your browser…";
        try
        {
            await provider.ConnectAsync(ClientId, ClientSecret, lifetime.Token);
            Connected = true;
            snapshot = new([], [], default);
            eventsByDay.Clear(); Calendars.Clear(); Rebuild();
            await store.DeleteAsync("calendar-cache.bin");
            await SaveAsync();
        }
        finally { Busy = false; syncGate.Release(); }
        await RefreshAsync();
    }
    public async Task InitializeAsync()
    {
        try
        {
            Settings = await store.ReadAsync<WidgetSettings>("settings.bin", true, lifetime.Token) ?? new();
            Settings.Opacity = double.IsFinite(Settings.Opacity) ? Math.Clamp(Settings.Opacity, .3, 1) : .96;
            Settings.SyncMinutes = Math.Clamp(Settings.SyncMinutes, 1, 60);
            if (!Enum.IsDefined(Settings.View)) Settings.View = CalendarView.Month;
            if (!Enum.IsDefined(Settings.Behavior)) Settings.Behavior = WindowBehavior.Normal;
            Settings.HiddenCalendars ??= [];
            Changed(""); AppearanceChanged?.Invoke();
            Connected = await provider.IsConnectedAsync(lifetime.Token);
            snapshot = await store.ReadAsync<CalendarSnapshot>("calendar-cache.bin", true, lifetime.Token) ?? snapshot;
            ReindexEvents();
            ApplyCalendars(); Rebuild();
            await RefreshAsync();
            refreshLoop = RefreshLoopAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportError(ex); }
    }
    private async Task RefreshLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(SyncMinutes), lifetime.Token);
                if (observedToday != DateTime.Today)
                {
                    observedToday = DateTime.Today;
                    if (followToday) anchor = observedToday;
                    Rebuild();
                }
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) { }
    }
    public async Task RefreshAsync()
    {
        if (!Connected || !await syncGate.WaitAsync(0, lifetime.Token)) return;
        Busy = true; Status = "Synchronizing calendars…";
        bool completed = false;
        try
        {
            // Cache the displayed year and adjacent months for navigation and offline use.
            var start = new DateTime(anchor.Year, 1, 1).AddMonths(-1);
            var end = new DateTime(anchor.Year + 1, 1, 1).AddMonths(1);
            var next = await provider.GetSnapshotAsync(new DateTimeOffset(start), new DateTimeOffset(end), lifetime.Token);
            snapshot = next;
            ReindexEvents();
            await store.WriteAsync("calendar-cache.bin", snapshot, true, lifetime.Token);
            ApplyCalendars(); Rebuild();
            Status = $"Updated {snapshot.Updated:t} · {snapshot.Calendars.Count} calendars · {snapshot.Events.Count} events";
            completed = true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ReportError(ex); }
        finally { Busy = false; syncGate.Release(); }
        if (completed && !lifetime.IsCancellationRequested) await RefreshIfNeededAsync();
    }
    private void ApplyCalendars()
    {
        Calendars.Clear();
        foreach (var calendar in snapshot.Calendars)
            Calendars.Add(new(calendar, !Settings.HiddenCalendars.Contains(calendar.Id), () => { Settings.HiddenCalendars = Calendars.Where(c => !c.Visible).Select(c => c.Calendar.Id).ToList(); Rebuild(); _ = SaveSafelyAsync(); }));
    }
    private Task RefreshIfNeededAsync()
    {
        var start = new DateTimeOffset(new DateTime(anchor.Year, 1, 1));
        return snapshot.RangeStart <= start && snapshot.RangeEnd >= start.AddYears(1) && DateTimeOffset.Now - snapshot.Updated < TimeSpan.FromMinutes(SyncMinutes) ? Task.CompletedTask : RefreshAsync();
    }
    private void Navigate(int direction)
    {
        followToday = false;
        anchor = View switch { CalendarView.Year => anchor.AddYears(direction), CalendarView.Month => anchor.AddMonths(direction), CalendarView.SingleDay => anchor.AddDays(direction), CalendarView.ThreeDays => anchor.AddDays(3 * direction), _ => anchor.AddDays(7 * direction) };
        Rebuild(); _ = RefreshIfNeededAsync();
    }
    private static DateTime Monday(DateTime day) => day.AddDays(-((int)day.DayOfWeek + 6) % 7);
    private DayCell Cell(DateTime day, int month)
    {
        var events = (eventsByDay.GetValueOrDefault(day) ?? []).Where(e => !Settings.HiddenCalendars.Contains(e.CalendarId))
            .OrderByDescending(e => e.AllDay).ThenBy(e => e.Start)
            .Select(e => new EventItem(e.Title, e.AllDay ? "All day" : $"{e.Start.LocalDateTime:t} – {e.End.LocalDateTime:t}", e.Color,
                $"{e.Title}\n{(e.AllDay ? "All day" : $"{e.Start.LocalDateTime:g} – {e.End.LocalDateTime:g}")}\n{e.Location}\n{e.Description}".Trim())).ToList();
        return new(day, day.Month == month, day == DateTime.Today, events);
    }
    private void ReindexEvents()
    {
        eventsByDay.Clear();
        foreach (var item in snapshot.Events)
        {
            DateTime first = item.Start.LocalDateTime.Date;
            DateTime last = item.End.LocalDateTime;
            if (snapshot.RangeStart != default && first < snapshot.RangeStart.LocalDateTime.Date) first = snapshot.RangeStart.LocalDateTime.Date;
            if (snapshot.RangeEnd != default && last > snapshot.RangeEnd.LocalDateTime) last = snapshot.RangeEnd.LocalDateTime;
            for (DateTime day = first; day < last; day = day.AddDays(1))
            {
                if (!eventsByDay.TryGetValue(day, out var list)) eventsByDay[day] = list = [];
                list.Add(item);
            }
        }
    }
    private List<DayCell> MonthDays(DateTime month)
    {
        DateTime start = Monday(new DateTime(month.Year, month.Month, 1));
        return Enumerable.Range(0, 42).Select(i => Cell(start.AddDays(i), month.Month)).ToList();
    }
    private void Rebuild()
    {
        Changed(nameof(Period)); Changed(nameof(IsYear)); Changed(nameof(IsMonth)); Changed(nameof(IsDays)); Changed(nameof(Columns));
        Days.Clear(); Months.Clear();
        if (IsYear)
        {
            for (int i = 1; i <= 12; i++) { var month = new DateTime(anchor.Year, i, 1); Months.Add(new(month.ToString("MMMM"), MonthDays(month))); }
        }
        else if (IsMonth) foreach (var day in MonthDays(anchor)) Days.Add(day);
        else
        {
            DateTime start = View is CalendarView.Workweek or CalendarView.Week ? Monday(anchor) : anchor.Date;
            for (int i = 0; i < Columns; i++) Days.Add(Cell(start.AddDays(i), start.Month));
        }
    }
    public Task SaveAsync() => store.WriteAsync("settings.bin", Settings, true);
    private async Task SaveSafelyAsync() { try { await SaveAsync(); } catch (Exception ex) { ReportError(ex); } }
    public void ReportError(Exception ex) => Status = ex is OperationCanceledException ? "Authorization timed out or was cancelled." : $"{ex.Message} {(snapshot.Updated != default ? "Showing cached events." : "")}";
    public async Task ShutdownAsync()
    {
        lifetime.Cancel();
        if (refreshLoop is not null) await refreshLoop;
        await syncGate.WaitAsync();
        try { await SaveAsync(); }
        finally { syncGate.Release(); }
    }
    public void Dispose() { lifetime.Cancel(); provider.Dispose(); lifetime.Dispose(); }
}
