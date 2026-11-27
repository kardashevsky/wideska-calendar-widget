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
    string Description = "", string Location = "", string CalendarName = "", string DateLabel = "", string TimeZoneLabel = "", string EventId = "", string CalendarId = "")
{
    public bool IsSleepCalendar => string.Equals(CalendarName.Trim(), "Sleep", StringComparison.OrdinalIgnoreCase) || string.Equals(CalendarName.Trim(), "Сон", StringComparison.OrdinalIgnoreCase);
    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}
public sealed record DayCell(DateTime Date, bool InMonth, bool Today, IReadOnlyList<EventItem> Events)
{
    public string Label => $"{Date.ToString("ddd", AppLocalization.Culture).ToUpper(AppLocalization.Culture)}, {Date.Day}";
    public string FullLabel => $"{Date.ToString("ddd", AppLocalization.Culture).ToUpper(AppLocalization.Culture)}, {AppLocalization.Culture.TextInfo.ToTitleCase(Date.ToString("d MMMM", AppLocalization.Culture))}";
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public string Number => Date.Day.ToString(CultureInfo.InvariantCulture);
    public IEnumerable<EventItem> PreviewEvents => Events.Take(1);
    public string Summary => Events.Count == 0 ? Date.ToString("D", AppLocalization.Culture) : string.Join("\n", Events.Select(e => $"{e.Time}  {e.Title}"));
}
public sealed record MonthItem(string Title, IReadOnlyList<DayCell> Days);
public sealed record QuarterItem(string Label, string Range, IReadOnlyList<MonthItem> Months);
public sealed record LanguageChoice(string Value, string Title);
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
    private readonly Dictionary<CalendarEvent, string> descriptions = new(ReferenceEqualityComparer.Instance);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private CancellationTokenSource? saveDebounce;
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private Task? shutdownTask;
    private Task? initializationTask;
    private bool disposed;
    private readonly DispatcherTimer minuteTimer = new();
    private readonly DispatcherTimer secondTimer = new();
    private bool clockVisible;
    private DateTimeOffset clockTime = DateTimeOffset.UtcNow;
    public string ClockText => CalendarTime.Local(clockTime, CalendarTimeZoneId).ToString("HH:mm:ss", AppLocalization.Culture);
    public string DayHeading => AppLocalization.Culture.TextInfo.ToTitleCase(anchor.ToString("dddd, d", AppLocalization.Culture));
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
    public string DesktopSizeActionLabel => IsDesktopFilled ? AppLocalization.Text("Restore widget size") : AppLocalization.Text("Fill desktop work area");
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
    public ViewChoice[] Views => [new(CalendarView.SingleDay, AppLocalization.Text("Day")), new(CalendarView.ThreeDays, AppLocalization.Text("3 days")), new(CalendarView.Workweek, AppLocalization.Text("Workweek")), new(CalendarView.Week, AppLocalization.Text("Week")), new(CalendarView.Month, AppLocalization.Text("Month")), new(CalendarView.Year, AppLocalization.Text("Year"))];
    public BehaviorChoice[] Behaviors => [new(WindowBehavior.Normal, AppLocalization.Text("Normal window")), new(WindowBehavior.AlwaysOnTop, AppLocalization.Text("Always on top")), new(WindowBehavior.Desktop, AppLocalization.Text("Behind other windows"))];
    private DateTime anchor = DateTime.Today;
    private bool followToday = true;
    private string status = "Connect Google Calendar to display your events.";
    public string Status { get => AppLocalization.Text(status); private set => Set(ref status, value); }
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
    public string CredentialStatus => HasCredentials ? AppLocalization.Text("Desktop OAuth client is ready") : AppLocalization.Text("Import your Google Desktop OAuth file to get started");
    private void NotifyPresentation()
    {
        Changed(nameof(ShowWidget)); Changed(nameof(ShowAuthorization));
        Changed(nameof(CanEditGeometry));
        Changed(nameof(CanMoveWidget));
        UpdateClockSchedule();
        AppearanceChanged?.Invoke();
    }
    public string ConnectionLabel => Connected ? AppLocalization.Text("Google Calendar connected") : AppLocalization.Text("No account connected");
    private bool settingsOpen;
    public bool SettingsOpen { get => settingsOpen; set { Set(ref settingsOpen, value); if (value) SelectedEvent = null; } }
    public LanguageChoice[] Languages => [new("system", AppLocalization.Text("System language")), new("ru", "Русский"), new("en", "English")];
    public string Language
    {
        get => Settings.Language;
        set
        {
            if (value is not ("system" or "ru" or "en") || Settings.Language == value) return;
            Settings.Language = value; AppLocalization.Apply(value);
            Changed(nameof(Language)); Changed(nameof(Languages)); Changed(nameof(Views));
            Changed(nameof(CredentialStatus)); Changed(nameof(ConnectionLabel)); Changed(nameof(DesktopSizeActionLabel)); Changed(nameof(Status)); Changed(nameof(Behaviors));
            Rebuild(); _ = SaveSafelyAsync();
        }
    }
    public bool ShouldShowToday => IsYear ? !Months.Any(m => m.Days.Any(d => d.Today && d.InMonth)) : !Days.Any(d => d.Today);
    public string HeaderMonth => IsYear ? anchor.Year.ToString(AppLocalization.Culture) : AppLocalization.Culture.TextInfo.ToTitleCase(anchor.ToString("MMMM", AppLocalization.Culture));
    public string HeaderYear => IsYear ? "" : anchor.Year.ToString(AppLocalization.Culture);
    public string Period => View == CalendarView.Year ? anchor.ToString("yyyy") : AppLocalization.Culture.TextInfo.ToTitleCase(anchor.ToString("MMMM yyyy", AppLocalization.Culture));
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
        lifetimeToken = lifetime.Token;
        AppLocalization.EnsureInitialized();
        this.store = store; this.provider = provider;
        OpenEventCommand = new RelayCommand(value => { if (ShowWidget && value is EventItem item) { SettingsOpen = false; SelectedEvent = item; } });
        CloseEventCommand = new RelayCommand(_ => SelectedEvent = null);
        FitDesktopCommand = new RelayCommand(_ => { if (!GeometryLocked) FitDesktopRequested?.Invoke(); });
        minuteTimer.Tick += OnMinuteTick;
        secondTimer.Tick += (_, _) => { clockTime = DateTimeOffset.UtcNow; Changed(nameof(ClockText)); AlignClockTick(); };
        PreviousCommand = new RelayCommand(_ => Navigate(-1));
        NextCommand = new RelayCommand(_ => Navigate(1));
        TodayCommand = new RelayCommand(_ =>
        {
            followToday = true;
            anchor = CalendarTime.Local(currentTime, CalendarTimeZoneId).Date;
            // A workweek cannot display Saturday/Sunday, so make the explicit "Show today" action useful there too.
            if (View == CalendarView.Workweek && anchor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) View = CalendarView.SingleDay;
            else Rebuild();
            _ = RefreshIfNeededAsync();
        });
        RefreshCommand = new AsyncCommand(RefreshAsync, ReportError);
        ConnectCommand = new AsyncCommand(ConnectAsync, ReportError);
        DisconnectCommand = new AsyncCommand(async () =>
        {
            await syncGate.WaitAsync(lifetimeToken);
            Busy = true;
            try
            {
                SelectedEvent = null; SettingsOpen = false; Connected = false;
                snapshot = new([], [], default); eventsByDay.Clear(); Calendars.Clear(); Rebuild();
                Status = "Disconnecting Google account…";
                // A locked/read-only cache must not prevent removing the Google credentials.
                try { await store.DeleteAsync("calendar-cache.bin"); }
                finally { await provider.DisconnectAsync(lifetimeToken); }
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
        var configuration = await GoogleOAuthConfiguration.ReadAsync(path, lifetimeToken);
        ClientId = configuration.ClientId;
        ClientSecret = configuration.ClientSecret;
        await SaveAsync();
        Status = "OAuth client imported. Continue with Google to connect your calendar.";
    }
    public async Task ConnectAsync()
    {
        await syncGate.WaitAsync(lifetimeToken);
        Busy = true;
        Status = "Complete authorization in your browser…";
        try
        {
            await provider.ConnectAsync(ClientId, ClientSecret, lifetimeToken);
            Connected = true;
            snapshot = new([], [], default);
            eventsByDay.Clear(); Calendars.Clear(); Rebuild();
            await store.DeleteAsync("calendar-cache.bin");
            await SaveAsync();
        }
        finally { Busy = false; syncGate.Release(); }
        await RefreshAsync();
    }
    public Task InitializeAsync() => initializationTask ??= InitializeCoreAsync();
    private async Task InitializeCoreAsync()
    {
        try
        {
            Settings = await store.ReadAsync<WidgetSettings>("settings.bin", true, lifetimeToken) ?? new();
            if (Settings.Language is not ("system" or "ru" or "en")) Settings.Language = "system";
            AppLocalization.Apply(Settings.Language);
            Settings.BackgroundOpacity = double.IsFinite(Settings.BackgroundOpacity) ? Math.Clamp(Settings.BackgroundOpacity, 0, .85) : .14;
            Settings.SyncMinutes = AutoSyncMinutes;
            if (!Enum.IsDefined(Settings.View)) Settings.View = CalendarView.Month;
            if (!Enum.IsDefined(Settings.Behavior)) Settings.Behavior = WindowBehavior.Normal;
            Settings.HiddenCalendars ??= [];
            Changed(""); AppearanceChanged?.Invoke();
            Connected = await provider.IsConnectedAsync(lifetimeToken);
            if (Connected)
            {
                var cached = await store.ReadAsync<CalendarSnapshot>("calendar-cache.bin", true, lifetimeToken);
                if (cached is { Calendars: not null, Events: not null }) snapshot = cached;
            }
            ReindexEvents();
            ApplyCalendars(); Rebuild();
            await RefreshAsync();
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
        minuteTimer.Interval = DelayUntilNextMinute(DateTimeOffset.UtcNow);
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
    internal static TimeSpan DelayUntilNextMinute(DateTimeOffset now) =>
        TimeSpan.FromTicks(TimeSpan.TicksPerMinute - now.UtcTicks % TimeSpan.TicksPerMinute);
    private async void OnMinuteTick(object? sender, EventArgs e)
    {
        minuteTimer.Stop();
        // Recompute from wall-clock time before waiting for I/O: no accumulated drift or overlapping requests.
        ScheduleMinute();
        try
        {
            UpdateCurrentTime(DateTimeOffset.UtcNow);
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
        catch (Exception ex) { ReportError(ex); }
    }
    public async Task RefreshAsync()
    {
        if (disposed || lifetime.IsCancellationRequested || !Connected) return;
        try { if (!await syncGate.WaitAsync(0, lifetimeToken)) return; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        int requestedYear = anchor.Year;
        Busy = true; Status = "Synchronizing calendars…";
        bool completed = false;
        try
        {
            // Cache the displayed year and adjacent months for navigation and offline use.
            var start = new DateTime(anchor.Year, 1, 1).AddMonths(-1);
            var end = new DateTime(anchor.Year + 1, 1, 1).AddMonths(1);
            var next = await provider.GetSnapshotAsync(CalendarTime.Midnight(start, CalendarTimeZoneId), CalendarTime.Midnight(end, CalendarTimeZoneId), lifetimeToken);
            bool contentChanged = !snapshot.Calendars.SequenceEqual(next.Calendars) || !snapshot.Events.SequenceEqual(next.Events)
                || snapshot.RangeStart != next.RangeStart || snapshot.RangeEnd != next.RangeEnd;
            snapshot = contentChanged ? next : snapshot with { Updated = next.Updated };
            if (contentChanged)
            {
                var selected = SelectedEvent;
                ApplyCalendars(); ReindexEvents(); Rebuild();
                if (selected is { EventId.Length: > 0 })
                    SelectedEvent = Days.SelectMany(d => d.Events).FirstOrDefault(e => e.EventId == selected.EventId && e.CalendarId == selected.CalendarId);
                await store.WriteAsync("calendar-cache.bin", next, true, lifetimeToken);
            }
            Status = "";
            completed = true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) { Status = "Calendar request timed out. Try again shortly."; }
        catch (CalendarAuthorizationException ex)
        {
            SelectedEvent = null; SettingsOpen = false; Connected = false;
            snapshot = new([], [], default); eventsByDay.Clear(); Calendars.Clear(); Rebuild();
            Status = AppLocalization.Text(ex.Message);
            try { await store.DeleteAsync("calendar-cache.bin"); }
            catch (Exception cleanupError) when (cleanupError is System.IO.IOException or UnauthorizedAccessException)
            {
                Status += " " + AppLocalization.Text("Cached events could not be removed. Check file permissions.");
            }
        }
        catch (Exception ex) { ReportError(ex); }
        finally { Busy = false; syncGate.Release(); }
        if (completed && !lifetime.IsCancellationRequested && requestedYear != anchor.Year) await RefreshAsync();
    }
    private void ApplyCalendars()
    {
        if (Calendars.Select(c => c.Calendar).SequenceEqual(snapshot.Calendars)) return;
        string selected = Settings.TimeZoneCalendarId;
        Calendars.Clear();
        foreach (var calendar in snapshot.Calendars)
            Calendars.Add(new(calendar, !Settings.HiddenCalendars.Contains(calendar.Id), () => { Settings.HiddenCalendars = Calendars.Where(c => !c.Visible).Select(c => c.Calendar.Id).ToList(); Rebuild(); _ = SaveSafelyAsync(); }));
        Settings.TimeZoneCalendarId = snapshot.Calendars.Any(c => c.Id == selected) ? selected : (snapshot.Calendars.FirstOrDefault(c => c.IsPrimary) ?? snapshot.Calendars.FirstOrDefault())?.Id ?? "";
        Changed(nameof(TimeZoneCalendarId)); Changed(nameof(CalendarTimeZoneId)); Changed(nameof(CalendarTimeZoneLabel));
        if (followToday) anchor = CalendarTime.Local(currentTime, CalendarTimeZoneId).Date;
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
                string time = e.AllDay ? AppLocalization.Text("All day") : $"{start.ToString("HH:mm", CultureInfo.InvariantCulture)} – {end.ToString("HH:mm", CultureInfo.InvariantCulture)}";
                double startMinute = start.Date < day ? 0 : start.TimeOfDay.TotalMinutes;
                double endMinute = end.Date > day ? 1440 : end.TimeOfDay.TotalMinutes;
                if (!e.AllDay && endMinute <= startMinute && e.End > e.Start) endMinute = Math.Min(1440, startMinute + (e.End - e.Start).TotalMinutes);
                if (!descriptions.TryGetValue(e, out string? description))
                    descriptions[e] = description = EventText.ToPlainText(e.Description);
                string preview = description.Length > 400 ? description[..400] + "…" : description;
                DateTime first = e.AllDay ? e.Start.Date : start.Date;
                DateTime last = e.AllDay ? e.End.Date.AddDays(-1) : end.Date;
                string dateLabel = first == last ? first.ToString("D", AppLocalization.Culture) : $"{first.ToString("D", AppLocalization.Culture)} – {last.ToString("D", AppLocalization.Culture)}";
                return new EventItem(e.Title, time, e.Color, $"{e.Title}\n{time}\n{e.Location}\n{preview}".Trim(), e.AllDay,
                    startMinute, endMinute, description, e.Location, snapshot.Calendars.FirstOrDefault(c => c.Id == e.CalendarId)?.Name ?? "", dateLabel, CalendarTimeZoneLabel, e.Id, e.CalendarId);
            }).ToList();
        return new(day, day.Month == month, day == CalendarTime.Local(currentTime, CalendarTimeZoneId).Date, events);
    }
    private void ReindexEvents()
    {
        descriptions.Clear();
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
        Changed(nameof(HeaderMonth)); Changed(nameof(HeaderYear)); Changed(nameof(Period)); Changed(nameof(IsYear)); Changed(nameof(IsMonth)); Changed(nameof(IsDays)); Changed(nameof(Columns));
        Changed(nameof(DayHeading)); Changed(nameof(IsWeekendHeading)); Changed(nameof(IsSingleDay)); UpdateClockSchedule();
        Days.Clear(); Months.Clear(); Quarters.Clear();
        if (IsYear)
        {
            for (int i = 1; i <= 12; i++) { var month = new DateTime(anchor.Year, i, 1); Months.Add(new(month.ToString("MMMM", AppLocalization.Culture), MonthDays(month))); }
            for (int q = 0; q < 4; q++)
            {
                var first = new DateTime(anchor.Year, q * 3 + 1, 1);
                Quarters.Add(new(string.Format(AppLocalization.Culture, AppLocalization.Text("Quarter"), q + 1), $"{first.ToString("MMM", AppLocalization.Culture)} – {first.AddMonths(2).ToString("MMM", AppLocalization.Culture)}", Months.Skip(q * 3).Take(3).ToList()));
            }
        }
        else if (IsMonth) foreach (var day in MonthDays(anchor)) Days.Add(day);
        else
        {
            DateTime start = View is CalendarView.Workweek or CalendarView.Week ? Monday(anchor) : anchor.Date;
            for (int i = 0; i < Columns; i++) Days.Add(Cell(start.AddDays(i), start.Month));
        }
        Changed(nameof(ShouldShowToday));
    }
    public Task SaveAsync() => store.WriteAsync("settings.bin", Settings, true);
    private async Task SaveSafelyAsync()
    {
        if (disposed || lifetimeToken.IsCancellationRequested) return;
        var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        var previous = saveDebounce;
        saveDebounce = pending;
        previous?.Cancel();
        try { await Task.Delay(250, pending.Token); await SaveAsync(); }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception ex) { ReportError(ex); }
        finally
        {
            if (ReferenceEquals(saveDebounce, pending)) saveDebounce = null;
            pending.Dispose();
        }
    }
    public void ReportError(Exception ex)
    {
        if (disposed || lifetimeToken.IsCancellationRequested) return;
        Status = ex is OperationCanceledException ? AppLocalization.Text("Authorization timed out or was cancelled.")
            : $"{AppLocalization.Text(ex.Message)} {(snapshot.Updated != default ? AppLocalization.Text("Showing cached events.") : "")}";
    }
    public Task ShutdownAsync() => shutdownTask ??= ShutdownCoreAsync();
    private async Task ShutdownCoreAsync()
    {
        if (disposed) return;
        minuteTimer.Stop();
        secondTimer.Stop();
        lifetime.Cancel();
        if (initializationTask is not null) await initializationTask;
        await syncGate.WaitAsync();
        try { await SaveAsync(); }
        finally { syncGate.Release(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        minuteTimer.Stop(); secondTimer.Stop(); lifetime.Cancel(); provider.Dispose();
        AppearanceChanged = null; FitDesktopRequested = null;
        lifetime.Dispose();
    }
}
