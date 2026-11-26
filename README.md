# Wideska

An MIT-licensed Windows desktop calendar widget built with C#, .NET 10 and WPF. The application uses MVVM and has no third-party runtime packages.

## Features

- Day, three consecutive days, Monday–Friday workweek, Monday–Sunday week, month and twelve-month year views.
- Today navigation, previous/next periods and clickable dates. Event details appear on hover. Month cells show a compact event preview and a count; smaller widgets use event dots. Hover a date for the event summary, or open a day to see the full agenda.
- Multiple Google calendars with individual visibility and calendar colors.
- Read-only Google Calendar synchronization, recurring event instances, all-day events and events spanning midnight. Timed events use the Windows local time zone; all-day dates retain their calendar dates and exclusive end dates.
- Automatic synchronization every 1–60 minutes (default: 5), manual synchronization and an encrypted offline cache. Navigation reuses fresh cached data.
- Transparent frameless widget with rounded glass, quiet hover controls, saved position and size, light and dark text, Windows startup and system tray integration. Background tint adjusts from fully transparent to 85% without fading the text. The widget never creates a taskbar entry.
- Normal, always-on-top and behind-other-windows behavior. The last option maintains bottom window order; it does not attach the widget to Explorer's wallpaper process.

## Build and run

Requirements: Windows, a .NET SDK supporting `net10.0-windows`, and the .NET 10 Desktop Runtime when running a framework-dependent build.

```powershell
dotnet build Wideska/Wideska.slnx -c Release
.\Wideska\bin\Release\net10.0-windows\Wideska.exe
```

Drag the month heading to move the widget and the bottom-right corner grip to resize it. Controls appear when the mouse is over the widget or it has keyboard focus. Use the top-right close icon to hide it to the system tray. Double-click the tray icon to restore it; right-click for Show and Quit. Use Quit to exit completely. Only one instance runs per Windows session.

Open the gear menu to choose text contrast, background tint, window behavior, startup preference, synchronization interval and visible calendars. Position and size are saved after moving or resizing. Select Done to save settings. Choosing fully transparent removes the calendar's background while preserving opaque text and event colors; no simulated wallpaper image is used. On light wallpaper, turn off light text or increase the tint for contrast. The connection screen and settings panel retain a readable background.

Windows startup uses the current executable path: move the executable to a permanent location before enabling it. If the running widget locks the usual build output, quit it from the tray before rebuilding, or build into a separate directory:

```powershell
dotnet build Wideska/Wideska.slnx -c Release --artifacts-path artifacts/build
.\artifacts\build\bin\Wideska\release\Wideska.exe
```

## Connect Google Calendar

The first launch shows only the connection screen. The calendar and any cached events remain hidden until the initial account check finishes. A saved valid session reconnects automatically on subsequent launches. Expired or revoked authorization returns to the connection screen. Disconnecting also hides the calendar and removes its cache.

This source build needs your Google Desktop OAuth client: no shared production Google client is bundled.

1. Open [Google Cloud](https://console.cloud.google.com/apis/library/calendar-json.googleapis.com), select or create your project, and enable **Google Calendar API**.
2. In **Google Auth platform**, configure the consent screen in Branding and Audience. For a testing application, add your Google email as a test user.
3. In **Google Auth platform → Clients**, create an OAuth client with application type **Desktop app** and download the JSON file. A Web app client or service account file will not work here.
4. On Wideska's connection screen, select **Import Google OAuth file**, choose the downloaded JSON, then select **Continue with Google**. You can also expand manual entry to supply the client ID and client secret.
5. Finish consent in your system browser within three minutes. Wideska requests only `https://www.googleapis.com/auth/calendar.readonly`. The desktop calendar appears after authorization.
6. Select the calendars to display in the gear menu. **Disconnect Google account** removes the account credentials and local event cache and returns to the connection screen. The widget also attempts to revoke the Google token. If offline, remove access later in your Google account's connected-app settings.

Do not add OAuth JSON files or credential values to this repository. Imported client configuration is kept in encrypted local settings, not copied into the application directory. The connection screen includes the same steps and links to Google Cloud and the official setup guide.

The desktop flow uses a random loopback port, OAuth state validation and PKCE (S256). Tokens refresh automatically. Authorization cancellation, expired/revoked consent and API/network errors are reported in the widget status. Reconnect when consent has been revoked. Google testing-mode consent may expire according to your project's OAuth policy.

No OAuth project or live account credentials are bundled. Real authorization and account synchronization require your own OAuth client and Google account; automated tests use synthetic HTTP responses.

References: [Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Calendar events list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list).

## Local data and privacy

Settings, OAuth credentials and calendar cache are stored under `%LOCALAPPDATA%\Wideska` in separate `.bin` files. All three use Windows DPAPI bound to the current Windows user. Writes use temporary files and atomic replacement. Settings include the OAuth client configuration. No credentials or personal event data belong in Git.

Calendar data goes only to the Google Calendar API. There is no application telemetry. The cache covers the displayed year plus the preceding December and following January; navigation into another year loads that range. API pagination is supported. Network failures retain the previous snapshot and indicate cached data in the status. Authentication failures hide the calendar and remove cached events until you sign in again. Calendar data is read-only; creating and editing events are outside this widget's requirements.

## Verification

```powershell
dotnet run --project Wideska.Tests -c Release
```

This standalone test executable has no test framework dependency. It exits nonzero on failure. It checks calendar ranges, leap days, event overlap, exclusive all-day ends, visibility, caching, offline behavior, DPAPI persistence, Google pagination, recurring instance requests, token refresh and API errors. It loads the compiled XAML, renders all six views, checks binding errors, minimum window dimensions and light/dark resources. It also checks authorization-first launch, rejected consent, expired sessions, Desktop OAuth JSON import, taskbar absence, custom resizing and rendered pixel alpha. Preview PNGs are written to the ignored `artifacts` directory; wallpaper previews use a synthetic background only in the test harness. Tests briefly display an isolated window and tray icon with synthetic data, and exercise native window ordering. They do not read your Wideska settings, use your Google account or change startup registration.

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
