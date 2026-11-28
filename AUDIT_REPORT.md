# Wideska pre-release audit

This report lists changes made during the pre-release audit, rather than existing features. The validation counts below describe that audit stage; subsequent UI refinements are covered by the release verification.

## 0.2.0 release verification (2026-11-28)

- Release build: zero warnings and zero errors; 463 passing assertions.
- Wheel scrolling moves retained visual layers without arranging or redrawing the calendar on every frame. Tests cover rapid downward movement followed by slow upward input, real hit testing over empty slots, direction changes, pinned headers and scrollbar handoff.
- Period navigation uses frozen snapshots, coalesces repeated arrow clicks without losing requested dates, and releases snapshots on completion, hide and close. Each snapshot remains capped at 2048 pixels per side; two square pixel buffers can total 32 MiB before framework/driver overhead.
- Native WPF event descriptions preserve paragraphs, emphasis, lists and safe links. Input is bounded to 65,536 characters, 2,048 tags and 32 nested formatting spans. Scripts, embedded objects and remote images are not rendered; navigation accepts only HTTP, HTTPS and mailto destinations.
- The real ten-second idle timer is tested for restart after input, focus preservation, gentle positioning at one third of the time-grid viewport, cancellation, modal/settings suspension and month/year return behavior. Window-lifecycle coverage includes the new timer subscriptions.
- Synthetic visual checks cover the responsive event-details card at 640 pixels and in a 420-pixel widget, compact half-hour event times, midnight bounds and the revised date header.

These checks do not constitute a multi-day production soak or a new live Google OAuth test. Screen-capture exclusion remains unimplemented, and the distributed executable is unsigned.

## Memory and lifecycle fixes

- Closed windows could remain referenced through view-model subscriptions and bindings. Subscriptions now start with the loaded window, are removed on close, and the closed window releases its data context. Regression coverage keeps the view model alive and verifies garbage collection of three shown windows and one closed before showing.
- Navigation snapshots could remain allocated while the widget was hidden or covered by settings/event details. Those transitions now stop and release their snapshots immediately. Closing the window also stops the geometry timer and animation clocks.
- Large/DPI-scaled navigation snapshots had no pixel budget. The longest bitmap side is now capped at 2048 pixels: at most 16 MiB for one square Pbgra32 pixel buffer, excluding framework/driver overhead.
- Multi-day event cells repeatedly normalized the same description and copied its full text into every tooltip. Normalized descriptions are now shared within the event index; hover previews are capped at 400 characters while modal details retain the full normalized text. Unchanged refreshes also keep the original snapshot data graph instead of retaining duplicate equal event records.
- Repeated initialization could start multiple synchronization loops. Initialization and shutdown now each share one completion task. Shutdown waits for initialization and active synchronization; repeated disposal is safe. Cancellation continuations use a cached token instead of accessing a disposed token source.
- Failed tray initialization now rolls back its native icon and HWND hooks. Desktop-host disposal is idempotent.

## Synchronization and performance fixes

- Identical Google responses previously cleared and recreated calendars, day cells and event cards, reindexed events and rewrote the encrypted cache every minute. Data/range comparisons now skip those operations when nothing changed. A regression test performs 20 unchanged refreshes and verifies zero day-collection changes, preserved object identities and an unchanged cache-file modification time.
- Changed data no longer triggers duplicate rebuilds through calendar/time-zone updates. Updated events reach the UI before a cache-write failure can interrupt presentation.
- Cache serialization, deserialization and DPAPI work no longer perform their expensive processing on the UI thread. Google provider awaits no longer marshal response processing back to the UI synchronization context.
- Rapid settings changes now share a 250 ms save debounce instead of queueing a write for every slider movement. Explicit save and shutdown still persist the final settings immediately.
- Appearance updates no longer recreate the complete theme palette when only the background opacity or window state changed.
- Missing provider range metadata previously risked recursive refresh requests. Follow-up refreshes are now limited to an actual change of the displayed year during an active request.
- An open event description could become stale after synchronization. Event/calendar identities now update the open card, and deleting its source event closes the stale details.
- Network timeouts are no longer described as authorization cancellation and do not disconnect the account.

## Storage and input hardening

- Independent store instances used the same `.tmp` filename and could interfere with one another. Writes now use unique temporary files, atomic replacement and cleanup on failure/cancellation. Cancelled writes preserve the previous file. Storage operations reject directory paths as filenames.
- Temporary managed plaintext buffers used for encrypted reads/writes are cleared after use. This does not imply that all application strings or native DPAPI buffers are securely erasable.
- Failure to delete a read-only/locked cache previously prevented Google account disconnection. Credential removal is now attempted even if cache removal fails. Authorization-error cleanup failures no longer terminate the background synchronization loop.
- HTML-to-text processing used regexes without a match budget. It now uses non-backtracking expressions, explicit timeouts and a 65,536-character input limit. Link rendering is bounded to 128 hyperlinks, with the remainder displayed as text. Closing-parenthesis processing is linear instead of repeatedly scanning the entire URL.
- The loopback OAuth callback reader previously allowed an unlimited request line and a slow client to occupy the whole authorization wait. Reads are now bounded to 8192 bytes, with a five-second per-connection deadline. Duplicate query parameters, unexpected request paths/origins and invalid request methods are rejected. Normal state validation, PKCE, Google scopes and the authorization flow are preserved.
- OAuth file imports now reject files over 1 MiB before JSON parsing and validate the root/client/property types. Malformed external colors fall back to a valid color instead of failing rendering.
- HTTP form content used for token exchange/revocation is now explicitly disposed.

## UX and animation fixes

- Settings, event details and the format dropdown now have a 140 ms opacity-only entrance animation. It is skipped when Windows disables client-area animations or WPF reports software rendering. There are no perpetual animation loops, blur effects or layout-driven animation.
- Escape dismisses settings/event details without closing the widget; an open combo box retains its own Escape handling.
- Sleep cards had insufficient text contrast over white wallpaper. Their dark translucent fill was adjusted while keeping them translucent, subdued and without hover highlighting. A regression test verifies a text contrast ratio of at least 4.5:1 over a white background.
- Show today now considers whether the current column is actually visible in the horizontal viewport, rather than just belonging to the current week. Clicking it reveals that column. If the selected workweek excludes today's Saturday/Sunday, the explicit action opens the single-day view.
- The startup-focus regression check no longer fails merely because the user switches between unrelated applications while the test awaits the dispatcher. It still rejects activation of the widget itself.

## Validation

- Release solution build: zero warnings and zero errors.
- Final regular suite: 186 assertions passed, including the added audit regressions.
- Additional native desktop smoke run passed MinimizeAll/ToggleDesktop checks and restored ordinary windows. Later changes to storage/provider/viewport handling passed the regular suite; desktop hosting itself was not redesigned.
- NuGet vulnerability check against the configured source reported no vulnerable packages.
- Existing synthetic previews cover all six views, minimum dimensions, both text themes, event details and English/Russian resources.

The audit used synthetic Google responses and local Windows UI tests. It did not include a multi-day production soak, an external penetration test, or a new live OAuth session with the user's Google account. The targeted GC, buffer-budget and cancellation checks are evidence for the corrected cases, not a guarantee that every possible leak or defect has been eliminated.

## Reproduction

```powershell
dotnet build Wideska/Wideska.slnx -c Release --artifacts-path artifacts/audit-build --disable-build-servers
& artifacts/audit-build/bin/Wideska.Tests/release/Wideska.Tests.exe
# Optional: temporarily minimizes/restores ordinary windows.
& artifacts/audit-build/bin/Wideska.Tests/release/Wideska.Tests.exe --desktop-smoke
dotnet list Wideska/Wideska.csproj package --vulnerable --include-transitive
dotnet publish Wideska/Wideska.csproj -p:PublishProfile=PortableWindows --disable-build-servers
```
