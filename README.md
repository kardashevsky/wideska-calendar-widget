# Wideska

An MIT-licensed Windows desktop calendar widget built with C#, .NET 10 and WPF. The application uses MVVM and has no third-party runtime packages.

## Features

- Day, three consecutive days, Monday–Friday workweek, Monday–Sunday week, month and twelve-month year views.
- Today navigation, previous/next periods and clickable dates. Event details appear on hover. Month cells scroll when events do not fit; open a day to see the full agenda.
- Multiple Google calendars with individual visibility and calendar colors.
- Read-only Google Calendar synchronization, recurring event instances, all-day events and events spanning midnight. Timed events use the Windows local time zone; all-day dates retain their calendar dates and exclusive end dates.
- Automatic synchronization every 1–60 minutes (default: 5), manual synchronization and an encrypted offline cache. Navigation reuses fresh cached data.
- Frameless resizable window, saved position and size, 30–100% opacity, light and dark themes, Windows startup and system tray integration.
- Normal, always-on-top and behind-other-windows behavior. The last option maintains bottom window order; it does not attach the widget to Explorer's wallpaper process.

## Build and run

Requirements: Windows, a .NET SDK supporting `net10.0-windows`, and the .NET 10 Desktop Runtime when running a framework-dependent build.

```powershell
dotnet build Wideska/Wideska.slnx -c Release
.\Wideska\bin\Release\net10.0-windows\Wideska.exe
```

Drag the header to move the widget and the edges to resize it. Double-click the header to maximize or restore it. The close and minimize buttons hide it to the system tray. Double-click the tray icon to restore it; right-click for Show and Quit. Use Quit to exit completely. Only one instance runs per Windows session.

Open Settings to choose the theme, transparency, window behavior, startup preference, synchronization interval and visible calendars. Position and size are saved after moving or resizing. Other settings are saved when changed or through Save settings. Windows startup uses the current executable path: move the executable to a permanent location before enabling it.

## Connect Google Calendar

1. In your own Google Cloud project, enable the Google Calendar API and configure the OAuth consent screen. For a testing application, add your Google account as a test user.
2. Create an OAuth 2.0 client with application type **Desktop app**.
3. Open Wideska Settings and enter that client's ID and client secret. Do not add these values or downloaded credential files to this repository.
4. Select **Connect Google**. Finish consent in the system browser within three minutes. Wideska requests only `https://www.googleapis.com/auth/calendar.readonly`.
5. Select the calendars to display. Use **Disconnect** to remove the account credentials and local event cache. The application also attempts to revoke the Google token. If offline, remove access later in your Google account's connected-app settings.

The desktop flow uses a random loopback port, OAuth state validation and PKCE (S256). Tokens refresh automatically. Authorization cancellation, expired/revoked consent and API/network errors are reported in the widget status. Reconnect when consent has been revoked. Google testing-mode consent may expire according to your project's OAuth policy.

No OAuth project or live account credentials are bundled. Real authorization and account synchronization require your own OAuth client and Google account; automated tests use synthetic HTTP responses.

References: [Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Calendar events list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list).

## Local data and privacy

Settings, OAuth credentials and calendar cache are stored under `%LOCALAPPDATA%\Wideska` in separate `.bin` files. All three use Windows DPAPI bound to the current Windows user. Writes use temporary files and atomic replacement. Settings include the OAuth client configuration. No credentials or personal event data belong in Git.

Calendar data goes only to the Google Calendar API. There is no application telemetry. The cache covers the displayed year plus the preceding December and following January; navigation into another year loads that range. API pagination is supported. An unsuccessful synchronization retains the previous snapshot and indicates cached data in the status. Calendar data is read-only; creating and editing events are outside this widget's requirements.

## Verification

```powershell
dotnet run --project Wideska.Tests -c Release
```

This standalone test executable has no test framework dependency. It exits nonzero on failure. It checks calendar ranges, leap days, event overlap, exclusive all-day ends, visibility, caching, offline behavior, DPAPI persistence, Google pagination, recurring instance requests, token refresh and API errors. It loads the compiled XAML, renders all six views, checks binding errors, minimum window dimensions and light/dark resources. Preview PNGs are written to the ignored `artifacts` directory. Tests briefly display an isolated window and tray icon with synthetic data, and exercise native window ordering. They do not read your Wideska settings, use your Google account or change startup registration.

Manual acceptance checks on the target desktop: sign in to Google; compare recurring exceptions, all-day and cross-time-zone events with Google Calendar; hide/show calendars; disconnect; restart and check restored settings; verify tray actions, startup, multi-monitor movement and the preferred DPI settings. Live Google authorization, Windows logon startup and Explorer interactions require interactive verification.

## Structure

- `Models`: provider-neutral events, calendars and settings.
- `Providers`: `ICalendarProvider` and Google OAuth/API implementation; independent of WPF.
- `Services`: asynchronous local persistence and Windows DPAPI protection.
- `ViewModels`: calendar projection, navigation, synchronization and settings.
- `Infrastructure`: observable properties, commands, tray, startup and window ordering.
- `Resources`: shared XAML brushes, styles and event/day templates.
- `MainWindow`: the WPF view and window lifecycle integration.
- `Wideska.Tests`: isolated logic, protocol and rendering checks.

Additional providers implement `ICalendarProvider` without depending on the presentation layer.
