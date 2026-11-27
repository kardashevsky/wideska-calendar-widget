using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows;
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
public sealed record EventItem(string Title, string Time, string Color, string Details, bool AllDay = false, double StartMinute = 0, double EndMinute = 0,
    string Description = "", string Location = "", string CalendarName = "", string DateLabel = "", string TimeZoneLabel = "")
{
    public bool IsSleepCalendar => string.Equals(CalendarName.Trim(), "Sleep", StringComparison.OrdinalIgnoreCase) || string.Equals(CalendarName.Trim(), "Сон", StringComparison.OrdinalIgnoreCase);
    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}
public sealed record DayCell(DateTime Date, bool InMonth, bool Today, IReadOnlyList<EventItem> Events)
{
    public string Label => Date.ToString("ddd, d", CultureInfo.CurrentCulture);
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public string Number => Date.Day.ToString(CultureInfo.InvariantCulture);
    public IEnumerable<EventItem> PreviewEvents => Events.Take(1);
    public string Summary => Events.Count == 0 ? Date.ToString("D") : string.Join("\n", Events.Select(e => $"{e.Time}  {e.Title}"));
}
public sealed record MonthItem(string Title, IReadOnlyList<DayCell> Days);
public sealed record QuarterItem(string Label, string Range, IReadOnlyList<MonthItem> Months);
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
    private readonly DispatcherTimer minuteTimer = new();
    private readonly DispatcherTimer secondTimer = new();
    private bool clockVisible;
    private DateTimeOffset clockTime = DateTimeOffset.UtcNow;
    public string ClockText => CalendarTime.Local(clockTime, CalendarTimeZoneId).ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    public string DayHeading => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(anchor.ToString("dddd, d", CultureInfo.CurrentCulture));
    public bool IsWeekendHeading => anchor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public bool IsSingleDay => View == CalendarView.SingleDay;
    private DateTimeOffset currentTime = DateTimeOffset.UtcNow;
    public DateTimeOffset CurrentTime => currentTime;
    public string CalendarTimeZoneId => (snapshot.Calendars.FirstOrDefault(c => c.Id == Settings.TimeZoneCalendarId) ?? snapshot.Calendars.FirstOrDefault(c => c.IsPrimary) ?? snapshot.Calendars.FirstOrDefault())?.TimeZoneId ?? "";
    public string CalendarTimeZoneLabel => CalendarTime.Resolve(CalendarTimeZoneId).DisplayName;
    public string TimeZoneCalendarId { get => Settings.TimeZoneCalendarId; set { Settings.TimeZoneCalendarId = value; Changed(); UpdateTimeZone(); _ = SaveSafelyAsync(); } }
    public bool GeometryLocked { get => Settings.GeometryLocked; set { Settings.GeometryLocked = value; Changed(); Changed(nameof(CanEditGeometry)); Changed(nameof(CanMoveWidget)); AppearanceChanged?.Invoke(); _ = SaveSafelyAsync(); } }
    public bool CanEditGeometry => ShowWidget && !GeometryLocked;
    public bool CanMoveWidget => CanEditGeometry && !IsDesktopFilled;
    public bool IsDesktopFilled
    {
        get => Settings.IsDesktopFilled;
        set
        {
            if (Settings.IsDesktopFilled == value) return;
            Settings.IsDesktopFilled = value;
            Changed(); Changed(nameof(CanMoveWidget)); Changed(nameof(WidgetCornerRadius)); Changed(nameof(DesktopSizeActionLabel));
            AppearanceChanged?.Invoke(); _ = SaveSafelyAsync();
        }
    }
    public CornerRadius WidgetCornerRadius => new(IsDesktopFilled ? 0 : 24);
    public string DesktopSizeActionLabel => IsDesktopFilled ? "Restore widget size" : "Fill desktop work area";
    private EventItem? selectedEvent;
    public EventItem? SelectedEvent { get => selectedEvent; private set { if (Set(ref selectedEvent, value)) { Changed(nameof(IsEventOpen)); Changed(nameof(IsCalendarEnabled)); } } }
    public bool IsEventOpen => SelectedEvent is not null;
    public bool IsCalendarEnabled => !IsEventOpen;
    public ICommand OpenEventCommand { get; }
    public ICommand CloseEventCommand { get; }
    public event Action? FitDesktopRequested;
    public ICommand FitDesktopCommand { get; }
    private CalendarSnapshot snapshot = new([], [], default);
    private readonly Dictionary<DateTime, List<CalendarEvent>> eventsByDay = [];
    public WidgetSettings Settings { get; private set; } = new();
    public ObservableCollection<CalendarSelection> Calendars { get; } = [];
    public ObservableCollection<DayCell> Days { get; } = [];
    public ObservableCollection<MonthItem> Months { get; } = [];
    public ObservableCollection<QuarterItem> Quarters { get; } = [];
    public ViewChoice[] Views { get; } = [new(CalendarView.SingleDay, "Day"), new(CalendarView.ThreeDays, "3 days"), new(CalendarView.Workweek, "Workweek"), new(CalendarView.Week, "Week"), new(CalendarView.Month, "Month"), new(CalendarView.Year, "Year")];
    public BehaviorChoice[] Behaviors { get; } = [new(WindowBehavior.Normal, "Normal window"), new(WindowBehavior.AlwaysOnTop, "Always on top"), new(WindowBehavior.Desktop, "Behind other windows")];
    private DateTime anchor = DateTime.Today;
    private bool followToday = true;
    private string status = "Connect Google Calendar to display your events.";
    public string Status { get => status; private set => Set(ref status, value); }
    private bool busy;
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    private bool connected;
    public bool Connected
    {
        get => connected;
        private set
        {
            if (!Set(ref connected, value)) return;
            Changed(nameof(ConnectionLabel)); NotifyPresentation();
        }
    }
    private bool isInitialized;
    public bool IsInitialized { get => isInitialized; private set { Set(ref isInitialized, value); NotifyPresentation(); } }
    public bool ShowWidget => IsInitialized && Connected;
    public bool ShowAuthorization => !ShowWidget;
    public bool HasCredentials => !string.IsNullOrWhiteSpace(ClientId);
    public string CredentialStatus => HasCredentials ? "Desktop OAuth client is ready" : "Import your Google Desktop OAuth file to get started";
    private void NotifyPresentation()
    {
        Changed(nameof(ShowWidget)); Changed(nameof(ShowAuthorization));
        Changed(nameof(CanEditGeometry));
        Changed(nameof(CanMoveWidget));
        UpdateClockSchedule();
        AppearanceChanged?.Invoke();
    }
    public string ConnectionLabel => Connected ? "Google Calendar connected" : "No account connected";
    private bool settingsOpen;
    public bool SettingsOpen { get => settingsOpen; set { Set(ref settingsOpen, value); if (value) SelectedEvent = null; } }
    public string Period => View == CalendarView.Year ? anchor.ToString("yyyy") : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(anchor.ToString("MMMM yyyy"));
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
    public double BackgroundOpacity { get => Settings.BackgroundOpacity; set { Settings.BackgroundOpacity = Math.Clamp(value, 0, .85); Changed(); AppearanceChanged?.Invoke(); } }
    public WindowBehavior Behavior { get => Settings.Behavior; set { Settings.Behavior = value; Changed(); AppearanceChanged?.Invoke(); _ = SaveSafelyAsync(); } }
    public bool StartWithWindows { get => Settings.StartWithWindows; set { Settings.StartWithWindows = value; Changed(); AppearanceChanged?.Invoke(); _ = SaveSafelyAsync(); } }
    public string ClientId { get => Settings.ClientId; set { Settings.ClientId = value; Changed(); Changed(nameof(HasCredentials)); Changed(nameof(CredentialStatus)); } }
    public string ClientSecret { get => Settings.ClientSecret; set { Settings.ClientSecret = value; Changed(); } }
    public const int AutoSyncMinutes = 1;
    public int SyncMinutes => AutoSyncMinutes;
    public event Action? AppearanceChanged;
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SelectDayCommand { get; }
    public MainViewModel(LocalStore store, ICalendarProvider provider)
    {
        this.store = store; this.provider = provider;
        OpenEventCommand = new RelayCommand(value => { if (ShowWidget && value is EventItem item) { SettingsOpen = false; SelectedEvent = item; } });
        CloseEventCommand = new RelayCommand(_ => SelectedEvent = null);
        FitDesktopCommand = new RelayCommand(_ => { if (!GeometryLocked) FitDesktopRequested?.Invoke(); });
        minuteTimer.Tick += (_, _) => { UpdateCurrentTime(DateTimeOffset.UtcNow); ScheduleMinute(); };
        secondTimer.Tick += (_, _) => { clockTime = DateTimeOffset.UtcNow; Changed(nameof(ClockText)); AlignClockTick(); };
        PreviousCommand = new RelayCommand(_ => Navigate(-1));
        NextCommand = new RelayCommand(_ => Navigate(1));
        TodayCommand = new RelayCommand(_ => { followToday = true; anchor = CalendarTime.Local(currentTime, CalendarTimeZoneId).Date; Rebuild(); _ = RefreshIfNeededAsync(); });
        RefreshCommand = new AsyncCommand(RefreshAsync, ReportError);
        ConnectCommand = new AsyncCommand(ConnectAsync, ReportError);
        DisconnectCommand = new AsyncCommand(async () =>
        {
            await syncGate.WaitAsync(lifetime.Token);
            Busy = true;
            try
            {
                SelectedEvent = null; SettingsOpen = false; Connected = false;
                snapshot = new([], [], default); eventsByDay.Clear(); Calendars.Clear(); Rebuild();
                Status = "Disconnecting Google account…";
                await store.DeleteAsync("calendar-cache.bin");
                await provider.DisconnectAsync(lifetime.Token);
                Status = "Account disconnected. Sign in to show your desktop calendar.";
            }
            finally { Busy = false; syncGate.Release(); }
        }, ReportError);
        SaveSettingsCommand = new AsyncCommand(async () => { await SaveAsync(); SettingsOpen = false; await RefreshAsync(); }, ReportError);
        SelectDayCommand = new RelayCommand(value => { if (value is DateTime date) { followToday = false; anchor = date; if (View == CalendarView.SingleDay) Rebuild(); else View = CalendarView.SingleDay; _ = RefreshIfNeededAsync(); } });
        Rebuild();
    }
    public async Task ImportCredentialsAsync(string path)
    {
        var configuration = await GoogleOAuthConfiguration.ReadAsync(path, lifetime.Token);
        ClientId = configuration.ClientId;
        ClientSecret = configuration.ClientSecret;
        await SaveAsync();
        Status = "OAuth client imported. Continue with Google to connect your calendar.";
    }
    public async Task ConnectAsync()
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
            Settings.BackgroundOpacity = double.IsFinite(Settings.BackgroundOpacity) ? Math.Clamp(Settings.BackgroundOpacity, 0, .85) : .14;
            Settings.SyncMinutes = AutoSyncMinutes;
            if (!Enum.IsDefined(Settings.View)) Settings.View = CalendarView.Month;
            if (!Enum.IsDefined(Settings.Behavior)) Settings.Behavior = WindowBehavior.Normal;
            Settings.HiddenCalendars ??= [];
            Changed(""); AppearanceChanged?.Invoke();
            Connected = await provider.IsConnectedAsync(lifetime.Token);
            if (Connected) snapshot = await store.ReadAsync<CalendarSnapshot>("calendar-cache.bin", true, lifetime.Token) ?? snapshot;
            ReindexEvents();
            ApplyCalendars(); Rebuild();
            await RefreshAsync();
            refreshLoop = RefreshLoopAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportError(ex); }
        finally { IsInitialized = true; }
        UpdateCurrentTime(DateTimeOffset.UtcNow);
        ScheduleMinute();
    }
    private void ScheduleMinute()
    {
        if (lifetime.IsCancellationRequested) return;
        minuteTimer.Interval = TimeSpan.FromMilliseconds(60000 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 60000);
        minuteTimer.Start();
    }
    public void UpdateCurrentTime(DateTimeOffset now)
    {
        DateTime before = CalendarTime.Local(currentTime, CalendarTimeZoneId).Date;
        currentTime = now; Changed(nameof(CurrentTime));
        clockTime = now; Changed(nameof(ClockText));
        DateTime today = CalendarTime.Local(now, CalendarTimeZoneId).Date;
        if (before != today) { if (followToday) anchor = today; Rebuild(); }
    }
    private void UpdateTimeZone()
    {
        Changed(nameof(CalendarTimeZoneId)); Changed(nameof(CalendarTimeZoneLabel));
        Changed(nameof(ClockText));
        if (followToday) anchor = CalendarTime.Local(currentTime, CalendarTimeZoneId).Date;
        ReindexEvents(); Rebuild();
    }
    public void SetClockVisible(bool visible) { clockVisible = visible; UpdateClockSchedule(); }
    private void UpdateClockSchedule()
    {
        if (!clockVisible || !ShowWidget || !IsDays || lifetime.IsCancellationRequested) { secondTimer.Stop(); return; }
        clockTime = DateTimeOffset.UtcNow; Changed(nameof(ClockText)); AlignClockTick();
    }
    private void AlignClockTick()
    {
        secondTimer.Interval = TimeSpan.FromMilliseconds(1000 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1000);
        secondTimer.Start();
    }
    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(AutoSyncMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
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
            var next = await provider.GetSnapshotAsync(CalendarTime.Midnight(start, CalendarTimeZoneId), CalendarTime.Midnight(end, CalendarTimeZoneId), lifetime.Token);
            snapshot = next;
            ReindexEvents();
            await store.WriteAsync("calendar-cache.bin", snapshot, true, lifetime.Token);
            ApplyCalendars(); Rebuild();
            Status = $"Updated {snapshot.Updated:t}";
            completed = true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (CalendarAuthorizationException ex)
        {
            SelectedEvent = null; SettingsOpen = false; Connected = false;
            snapshot = new([], [], default); eventsByDay.Clear(); Calendars.Clear(); Rebuild();
            await store.DeleteAsync("calendar-cache.bin");
            Status = ex.Message;
        }
        catch (Exception ex) { ReportError(ex); }
        finally { Busy = false; syncGate.Release(); }
        if (completed && !lifetime.IsCancellationRequested) await RefreshIfNeededAsync();
    }
    private void ApplyCalendars()
    {
        string selected = Settings.TimeZoneCalendarId;
        Calendars.Clear();
        foreach (var calendar in snapshot.Calendars)
            Calendars.Add(new(calendar, !Settings.HiddenCalendars.Contains(calendar.Id), () => { Settings.HiddenCalendars = Calendars.Where(c => !c.Visible).Select(c => c.Calendar.Id).ToList(); Rebuild(); _ = SaveSafelyAsync(); }));
        Settings.TimeZoneCalendarId = snapshot.Calendars.Any(c => c.Id == selected) ? selected : (snapshot.Calendars.FirstOrDefault(c => c.IsPrimary) ?? snapshot.Calendars.FirstOrDefault())?.Id ?? "";
        Changed(nameof(TimeZoneCalendarId)); UpdateTimeZone();
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
            .Select(e =>
            {
                var start = CalendarTime.Local(e.Start, CalendarTimeZoneId);
                var end = CalendarTime.Local(e.End, CalendarTimeZoneId);
                string time = e.AllDay ? "All day" : $"{start:t} – {end:t}";
                double startMinute = start.Date < day ? 0 : start.TimeOfDay.TotalMinutes;
                double endMinute = end.Date > day ? 1440 : end.TimeOfDay.TotalMinutes;
                if (!e.AllDay && endMinute <= startMinute && e.End > e.Start) endMinute = Math.Min(1440, startMinute + (e.End - e.Start).TotalMinutes);
                string description = EventText.ToPlainText(e.Description);
                DateTime first = e.AllDay ? e.Start.Date : start.Date;
                DateTime last = e.AllDay ? e.End.Date.AddDays(-1) : end.Date;
                string dateLabel = first == last ? first.ToString("D") : $"{first:D} – {last:D}";
                return new EventItem(e.Title, time, e.Color, $"{e.Title}\n{time}\n{e.Location}\n{description}".Trim(), e.AllDay,
                    startMinute, endMinute, description, e.Location, snapshot.Calendars.FirstOrDefault(c => c.Id == e.CalendarId)?.Name ?? "", dateLabel, CalendarTimeZoneLabel);
            }).ToList();
        return new(day, day.Month == month, day == CalendarTime.Local(currentTime, CalendarTimeZoneId).Date, events);
    }
    private void ReindexEvents()
    {
        eventsByDay.Clear();
        foreach (var item in snapshot.Events)
        {
            DateTime first = item.AllDay ? item.Start.Date : CalendarTime.Local(item.Start, CalendarTimeZoneId).Date;
            DateTime last = item.AllDay ? item.End.Date : CalendarTime.Local(item.End, CalendarTimeZoneId);
            if (snapshot.RangeStart != default && first < CalendarTime.Local(snapshot.RangeStart, CalendarTimeZoneId).Date) first = CalendarTime.Local(snapshot.RangeStart, CalendarTimeZoneId).Date;
            if (snapshot.RangeEnd != default && last > CalendarTime.Local(snapshot.RangeEnd, CalendarTimeZoneId)) last = CalendarTime.Local(snapshot.RangeEnd, CalendarTimeZoneId);
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
        Changed(nameof(DayHeading)); Changed(nameof(IsWeekendHeading)); Changed(nameof(IsSingleDay)); UpdateClockSchedule();
        Days.Clear(); Months.Clear(); Quarters.Clear();
        if (IsYear)
        {
            for (int i = 1; i <= 12; i++) { var month = new DateTime(anchor.Year, i, 1); Months.Add(new(month.ToString("MMMM"), MonthDays(month))); }
            for (int q = 0; q < 4; q++)
            {
                var first = new DateTime(anchor.Year, q * 3 + 1, 1);
                Quarters.Add(new($"Q{q + 1}", $"{first:MMM} – {first.AddMonths(2):MMM}", Months.Skip(q * 3).Take(3).ToList()));
            }
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
        minuteTimer.Stop();
        secondTimer.Stop();
        lifetime.Cancel();
        if (refreshLoop is not null) await refreshLoop;
        await syncGate.WaitAsync();
        try { await SaveAsync(); }
        finally { syncGate.Release(); }
    }
    public void Dispose() { minuteTimer.Stop(); secondTimer.Stop(); lifetime.Cancel(); provider.Dispose(); lifetime.Dispose(); }
}
