# Changelog

## Unreleased

- Apply animated offsets directly through the physical scroll presenter instead of queuing a command per frame.
- Cancel wheel animation on actual scrollbar, pointer or keyboard input rather than guessing from delayed offset notifications; detach all input handlers on unload.
- Keep wheel bursts smooth when input arrives before WPF applies a queued animation frame; do not mistake that frame for manual scrollbar input.
- Animate Show Today with the same directional carousel used by period navigation, including horizontally hidden day columns.
- Use continuous frame-driven wheel scrolling that preserves velocity across repeated wheel input, with no delayed snapping or overshoot.
- Skip duplicate rendering timestamps and reuse sticky-header transforms and clipping geometry during scrolling.
- Stop scroll rendering callbacks when idle, hidden or unloaded, and respect Windows animation preferences.
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
