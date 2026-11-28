# Wideska Widget

An MIT-licensed collection of Windows desktop widgets for personal planning: a calendar and an Eisenhower task matrix. Built with C#, .NET 10, WPF and MVVM, without third-party runtime packages. The product name is Wideska Widget. Existing executable names, data paths, OAuth configuration and repository URLs remain compatible.

## Task matrix prototype

Build and launch the current development executable. The matrix opens alongside
the calendar automatically, without waiting for Google synchronization. Use
**Task matrix** in the tray to hide or restore the matrix independently.
Exit an older running build first: the single-instance mechanism
otherwise restores that existing version.

The independent widget has Backlog on the left, four priority quadrants in the
middle, and Done on the right. Add a task and drag its card between these areas.
The card menu also moves tasks and edits their titles. Completed tasks can be
returned to any quadrant or Backlog. The ranges are planning guidance, not
automatically assigned deadlines:

| Quadrant | Planning horizon |
| --- | --- |
| Important and urgent | 1–2 days |
| Urgent, not important | 1–3 days |
| Important, not urgent | 3–7 days |
| Neither urgent nor important | 7–31 days |

Tasks are saved locally with Windows encryption in a separate `task-board.bin`
file under the existing application data directory. They do not require Google
authorization. Drag the heading to move the widget; use its lower-right grip to
resize it. Position is saved after dragging/resizing and on exit. The task matrix
tray action toggles its visibility. Exit from the tray saves both widgets, and
failed task writes are shown with a Retry action.

This prototype does not yet schedule tasks into Google Calendar, synchronize
tasks across devices, reorder cards within a column or replace the current
calendar. The repository and workspace folder have not been renamed. A future
repository name can be `wideska-widget` to reflect both widgets.

## Download

Download the latest [Windows x64 executable](https://github.com/kardashevsky/wideska-calendar-widget/releases/latest/download/Wideska.exe) or browse [all releases](https://github.com/kardashevsky/wideska-calendar-widget/releases). The portable executable includes the .NET runtime. Follow [Google Calendar setup](GOOGLE_CALENDAR_SETUP.md) to connect your own account.

See [CHANGELOG.md](CHANGELOG.md) for version history.

## Features

- Day, three consecutive days, Monday–Friday workweek, Monday–Sunday week, month and twelve-month year views. The year uses two rows of six months, grouped into labeled Q1–Q4 panels with three months each. Square date cells and proportional scaling keep month grids balanced. The annual grid fits the available area, stays centered and caps its width on wide widgets without changing its two-row structure.
- Today navigation, previous/next periods and clickable dates. Hover events for a quick tooltip, or click a timed/all-day card or month preview for an in-widget details dialog. The dialog shows title, calendar, dates, time zone, location and the full scrollable description. HTML descriptions are displayed as readable plain text. Select Back to calendar or click the shaded backdrop to close it. Empty time slots do not create events. Month cells show a compact event preview; smaller widgets use event dots. Hover a date for the event summary, or open a day to see the full agenda.
- Multiple Google calendars with individual visibility and calendar colors.
- Read-only Google Calendar synchronization, recurring event instances, all-day events and events spanning midnight. Timed events and the red current-time marker use the primary Google calendar time zone by default; select another calendar as the time-zone source in settings. When cached metadata has no zone, the Windows local zone is used; all-day dates retain their calendar dates and exclusive end dates.
- Automatic synchronization every minute, optional manual synchronization from the tray and an encrypted offline cache. Navigation reuses fresh cached data.
- Transparent frameless widget with rounded glass, quiet hover controls, saved position and size, light and dark text, Windows startup and system tray integration. Background tint adjusts from fully transparent to 85% without fading the text. The widget never creates a taskbar entry.
- The signed-in calendar is hosted inside the Explorer desktop view, above the wallpaper and below application windows. It survives Show Desktop / Win+D and uses nonactivating tool-window styles to stay out of Alt+Tab and the taskbar. Previous always-on-top preferences no longer lift the calendar above applications.

## Build and run

Requirements: Windows, a .NET SDK supporting `net10.0-windows`, and the .NET 10 Desktop Runtime when running a framework-dependent build.

```powershell
dotnet build Wideska/Wideska.slnx -c Release
.\Wideska\bin\Release\net10.0-windows\Wideska.exe
```

Drag the month heading to move the widget and any edge or corner to resize it. Controls appear when the mouse is over the widget or it has keyboard focus. Settings, manual sync, hide and exit are available only from the system tray; the widget has no close or refresh button. Click or double-click the tray icon to restore it. Right-click for Show widget, Hide widget (keep running), Settings / Google sign-in, Sync calendars, Move to visible screen, Lock position and size, Fill desktop work area, and Exit Wideska. Showing a signed-in widget keeps it in the desktop layer and preserves application focus. A fully off-screen window is brought back onto the primary screen. Use Quit to exit completely. Only one instance runs per Windows session. Launching an updated Wideska again requests showing the running instance. Older builds do not understand this request: exit the old build once before starting the updated executable.

Open Settings / Google sign-in from the tray to choose text contrast, background tint, geometry locking, desktop fill, startup preference, calendar time zone and visible calendars. Synchronization runs every minute, including for previously saved settings. Physical screen position and size are saved after moving or resizing, including negative coordinates on secondary displays. Desktop fill uses the current monitor work area and excludes the taskbar. Filled mode removes outer corner rounding and prevents header dragging while preserving resize handles. Use Restore widget size in the tray/settings to recover the previous geometry, or resize an edge to return to movable rounded mode. Fill state and restore bounds persist across restarts. Locking disables drag, resize, centering and fill. Select Done to save settings. Choosing fully transparent removes the calendar's background while preserving opaque text and event colors; no simulated wallpaper image is used. On light wallpaper, turn off light text or increase the tint for contrast. The connection screen and settings panel retain a readable background.

Windows startup uses the current executable path: move the executable to a permanent location before enabling it. If the running widget locks the usual build output, quit it from the tray before rebuilding, or build into a separate directory:

```powershell
dotnet build Wideska/Wideska.slnx -c Release --artifacts-path artifacts/build
.\artifacts\build\bin\Wideska\release\Wideska.exe
```

Synchronization runs silently in the background without status text, progress indicators or layout changes. Requests are scheduled at wall-clock minute boundaries (`HH:mm:00`), using the same timer as the minute indicator. Slow requests do not overlap; network response arrival is not a real-time guarantee.

## Widget heading

The widget has no in-content logo or product name; the application icon remains in the tray and executable. The selected month and year form a compact left-aligned heading (year only in the annual view). The date panel highlights the complete current-day header. The red current-time badge inside the day/week grid shows the calendar-local `HH:mm:ss` clock. A separate drawing layer updates only the badge each second while a time-grid view is visible; the minute-based timeline and Google synchronization remain separate. The interface uses graphite surfaces and cool blue accents in dark mode, with matching neutral light-mode colors. Google calendar colors are preserved.

## Time scale and desktop hosting

Day, three-day, workweek and week views use a shared 24-hour grid. Only event cards use a hand cursor; empty time slots and labels use the standard arrow. Dates and all-day events remain pinned while the hourly grid scrolls below them, including their clickable event cards. In multi-day views the current calendar-local day has a subtle full-column accent. Timed events occupy their calendar-local slots and overlapping events use wider, partially overlapping cards with staggered horizontal positions. All-day events remain above the hourly grid. A red line and starting dot appear only in the current calendar-local day and render above events. A dispatcher timer updates the clock at minute boundaries without API calls or rebuilding the view. Opening a day/week view scrolls near the current time; later ticks preserve the chosen scroll position. Month and year views have no hourly scale and do not display the line.

Desktop integration uses Win32 `SetParent` with the existing `SHELLDLL_DefView` desktop view. This also accommodates the raised desktop composition used by recent Windows 11 versions. It does not use a periodically lowered top-level window or change Explorer styles. Authentication remains a separate presentation state in the existing window; its browser flow, scopes and token handling are unchanged. Explorer must be running for desktop hosting. The shell desktop class is an Explorer implementation detail: validate new Windows shell builds and restart Wideska if Explorer is restarted and destroys the hosted window.

The executable declares Per-Monitor V2 awareness. Geometry uses native physical screen coordinates, while WPF content is adjusted for the monitor DPI because a child HWND otherwise inherits the desktop host DPI. Display/DPI changes re-evaluate scaling and recover windows on disconnected monitors. Mixed-DPI monitor layouts and Windows logon should also be checked on the target hardware.

## Build the portable Windows executable

```powershell
dotnet publish Wideska/Wideska.csproj -p:PublishProfile=PortableWindows --disable-build-servers
```

The output is `artifacts/portable-win-x64/Wideska.exe`, a self-contained Windows x64 build. It includes the .NET runtime and does not bundle OAuth credentials. Distribute it together with [GOOGLE_CALENDAR_SETUP.md](GOOGLE_CALENDAR_SETUP.md).

## Interface language

The interface supports Russian and English. The default follows the Windows UI language, with English as the fallback for other languages. Choose System language, Русский or English in tray settings; the selection is saved and updates the interface without restarting. Calendar names and event content remain unchanged.

Show today appears only when the current calendar-local date is absent from the displayed period or its day column is outside the horizontal viewport.

## Connect Google Calendar

See [the step-by-step Google Calendar setup guide](GOOGLE_CALENDAR_SETUP.md) for connecting your own account and selecting calendars.

The first launch shows only the connection screen. The calendar and any cached events remain hidden until the initial account check finishes. A saved valid session reconnects automatically on subsequent launches. Expired or revoked authorization returns to the connection screen. Disconnecting also hides the calendar and removes its cache.

This source build needs your Google Desktop OAuth client: no shared production Google client is bundled.

1. Open [Google Cloud](https://console.cloud.google.com/apis/library/calendar-json.googleapis.com), select or create your project, and enable **Google Calendar API**.
2. In **Google Auth platform**, configure the consent screen in Branding and Audience. For a testing application, add your Google email as a test user.
3. In **Google Auth platform → Clients**, create an OAuth client with application type **Desktop app** and download the JSON file. A Web app client or service account file will not work here.
4. On Wideska's connection screen, select **Import Google OAuth file**, choose the downloaded JSON, then select **Continue with Google**. You can also expand manual entry to supply the client ID and client secret.
5. Finish consent in your system browser within three minutes. Wideska requests only `https://www.googleapis.com/auth/calendar.readonly`. The desktop calendar appears after authorization.
6. Select the calendars to display in settings opened from the tray. **Disconnect Google account** removes the account credentials and local event cache and returns to the connection screen. The widget also attempts to revoke the Google token. If offline, remove access later in your Google account's connected-app settings.

Do not add OAuth JSON files or credential values to this repository. Imported client configuration is kept in encrypted local settings, not copied into the application directory. The connection screen includes the same steps and links to Google Cloud and the official setup guide.

The desktop flow uses a random loopback port, OAuth state validation and PKCE (S256). Tokens refresh automatically. Authorization cancellation, expired/revoked consent and API/network errors are reported in the widget status. Reconnect when consent has been revoked. Google testing-mode consent may expire according to your project's OAuth policy.

No OAuth project or live account credentials are bundled. Real authorization and account synchronization require your own OAuth client and Google account; automated tests use synthetic HTTP responses.

References: [Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Calendar events list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list).

## Local data and privacy

Settings, OAuth credentials and calendar cache are stored under `%LOCALAPPDATA%\Wideska` in separate `.bin` files. All three use Windows DPAPI bound to the current Windows user. Writes use temporary files and atomic replacement. Settings include the OAuth client configuration. No credentials or personal event data belong in Git.

Calendar data goes only to the Google Calendar API. There is no application telemetry. The cache covers the displayed year plus the preceding December and following January; navigation into another year loads that range. API pagination is supported. Network failures retain the previous snapshot. Authentication failures hide the calendar and remove cached events until you sign in again. Calendar data is read-only; creating and editing events are outside this widget's requirements.

## Verification

```powershell
dotnet build Wideska/Wideska.slnx -c Release
.\Wideska.Tests\Test.ps1
```

This standalone test executable has no test framework dependency. It exits nonzero on failure. It checks calendar ranges, leap days, event overlap, exclusive all-day ends, visibility, caching, offline behavior, DPAPI persistence, Google pagination, recurring instance requests, token refresh and API errors. It loads the compiled XAML, renders all six views, checks binding errors, minimum window dimensions and light/dark resources. It also checks authorization-first launch, rejected consent, expired sessions, Desktop OAuth JSON import, taskbar absence, custom resizing and rendered pixel alpha. Native-message tray tests cover opening the menu while hidden, every menu action, off-screen recovery, desktop-mode reveal and the existing-instance show request. Preview PNGs are written to the ignored `artifacts` directory; wallpaper previews use a synthetic background only in the test harness. Tests briefly display an isolated window and tray icon with synthetic data, and exercise native window ordering. They do not read your Wideska settings, use your Google account or change startup registration.

Manual acceptance checks on the target desktop: sign in to Google; compare recurring exceptions, all-day and cross-time-zone events with Google Calendar; hide/show calendars; disconnect; restart and check restored settings; verify tray actions, startup, multi-monitor movement and the preferred DPI settings. Live Google authorization, Windows logon startup and Explorer interactions require interactive verification.

For an interactive shell check, run the test EXE with `--desktop-smoke`. It temporarily minimizes ordinary windows, checks desktop hosting and ToggleDesktop, saves a native screenshot of synthetic widget data, and restores the windows. Use the EXE so that the DPI and Windows compatibility manifest is active. Regular tests do not run Show Desktop. The suite also checks DST, calendar-zone midnight, minute updates without synchronization, overlap stacking and click targets, lock state and physical geometry across a fresh view-model instance.

## Structure

- `Models`: provider-neutral events, calendars and settings.
- `Providers`: `ICalendarProvider` and Google OAuth/API implementation; independent of WPF.
- `Services`: asynchronous local persistence and Windows DPAPI protection.
- `ViewModels`: calendar projection, navigation, synchronization and settings.
- `Infrastructure`: observable properties, commands, tray, startup and window ordering.
- `Resources`: shared XAML brushes, styles and event/day templates.
- `MainWindow`: the WPF view and window lifecycle integration.
- `Wideska.Tests`: isolated logic, protocol and rendering checks. See [TESTING.md](TESTING.md) for selective verification.

Additional providers implement `ICalendarProvider` without depending on the presentation layer.
