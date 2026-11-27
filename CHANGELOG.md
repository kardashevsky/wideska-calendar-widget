# Changelog

## 0.1.0

First downloadable Windows x64 release of Wideska.

- Transparent desktop calendar with day, three-day, workweek, week, month and year views.
- Google Calendar connection through a user-provided Desktop OAuth client, multiple calendars and automatic synchronization.
- Russian and English interface with system-language detection and a saved language preference.
- System tray controls, persistent geometry, resize/lock/fill modes and optional Windows startup.
- Refined date headers, highlighted current day, current-time marker with seconds, overlapping event cards and event details with clickable links.
- Sticky day/all-day headers, preserved scroll position, half-hour wheel snapping and a slim scrollbar.
- Responsive navigation and optional Show today action; annual mini-calendars hide dates outside their month.
- Storage, cancellation, memory-lifecycle and external-input hardening, with targeted regression coverage.

The portable executable includes the .NET runtime. A shared Google OAuth client is not bundled: see [Google Calendar setup](GOOGLE_CALENDAR_SETUP.md).

Release assets include `Wideska.exe`, a Windows x64 ZIP bundle, the setup guide, the audit report and SHA-256 checksums.
