# Changelog

## 0.2.0 - 2026-11-28

- After ten seconds without widget input, gently return to today's schedule without taking focus; place current time one third down the time grid and retain two thirds for upcoming events.
- Pause automatic navigation while reading event details, editing settings or dragging; cancel it immediately on new input and return month/year views to the current period.
- Give event details a responsive 640-pixel reading width and retain description paragraphs, emphasis, lists and safe links using native WPF text instead of stripping HTML formatting.
- Keep only the date header's top corners rounded so its lower edge joins the calendar grid seamlessly.
- End the timeline at the final midnight label without trailing shaded space; use uniform one-pixel grid strokes at the current DPI.
- Keep hourly slots compact; show short-event titles and complete time ranges side by side when a second line does not fit.
- Narrow the live-time badge and left-align its monospaced clock so ticking seconds cannot shift the text.
- Remove the empty dark header corner above the time axis; give the date strip soft edges and subtle separators, and align the period heading with the timeline axis.
- Keep a stationary transparent input surface over empty calendar slots so wheel events cannot fall through to native scrolling during visual movement.
- Coalesce rapid period navigation into one follow-up carousel transition while retaining every requested date change; reuse clean snapshots instead of recapturing moving layers.
- Slide frozen calendar snapshots for period navigation and Show Today without animating the live calendar layout; retain the displayed composition during rapid navigation and release both snapshots on completion, hide or resize.
- Dim past calendar days and their events without blocking interaction; use the selected calendar time zone and preserve today's highlighting.
- Animate wheel scrolling through retained visual translation without arranging or redrawing the calendar each frame; commit the native offset only when movement finishes.
- Keep the sticky header, event hit targets and scrollbar aligned during movement; preserve native input when Windows animations are disabled.
- Animate Show Today with the same directional carousel used by period navigation, including horizontally hidden day columns.
- Use zero-padded 24-hour event times in both interface languages.
- Remove delayed half-hour snapping that caused a second movement after wheel scrolling.

## 0.1.1

- Enable Windows sign-in startup by default for new installations; existing explicit preferences remain configurable.
- Align calendar refresh requests and the minute indicator to wall-clock minute boundaries, without accumulated timer drift.
- Remove the separate periodic synchronization timer and retain cancellation and non-overlapping requests.

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
