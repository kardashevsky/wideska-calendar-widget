# Testing policy

Use `./Wideska.Tests/Test.ps1` from PowerShell. It selects checks from committed,
staged, unstaged and untracked changes relative to `origin/develop`, builds once
and runs the resulting executable directly. `-Plan` prints the selection without
building or running anything. Use `-BaseRef` to select another comparison branch.

| Change | Required checks |
| --- | --- |
| Documentation only | Whitespace check; no build or application tests |
| Providers, models, storage and services | `-Suite quick` |
| Task board model or view in isolation | `-Suite tasks` |
| XAML, controls, navigation, scrolling, localization, native window behavior | `-Suite ui` |
| Idle return, shared test infrastructure, project configuration, broad changes or release | `-Suite full` |
| GPU, CPU or memory investigation explicitly requested | `-Suite stress` |

Unknown files conservatively select the full suite. Automatic selection never
runs stress diagnostics. Review the plan and use an explicit suite when the
actual impact warrants it; filenames cannot prove the absence of indirect effects.

The quick suite covers calendar models, provider HTTP fixtures, encrypted local
storage, descriptions and links, time zones, minute boundaries and offscreen
timeline rendering. It does not open widget windows. The UI suite additionally
exercises real WPF layouts, tray integration, navigation, scrolling and lifecycle
regressions. Full adds real idle timer waits. Google fixtures never authenticate
with or modify a personal calendar.

Running the test executable without arguments selects **quick**. Use `--suite full`
to reproduce the previous complete regression run. `--list` lists the modes.
Screenshots are optional: add `-Previews` (or `--previews` to the executable).
Rendering and pixel assertions still run without exporting preview PNG files.
The `tasks` suite covers task transitions, encrypted persistence, failure recovery
and both task-board sizes without waiting for calendar animations or idle timers.
`--desktop-smoke` explicitly adds the desktop visibility exercise, which can
temporarily minimize application windows. Do not enable it routinely.

For small reversible visual constants, build and inspect the affected layout when
needed; do not create tests that merely repeat the constant. Do not run stress
tests, idle waits or all previews merely because a file changed.

Run relevant verification once after the final edit. Repeat it only after a new
change, a failure or unresolved evidence. After a merge, compare the validated
commit with the merge result (`git diff --exit-code <validated-commit> HEAD`).
If the trees are identical, reuse the recorded build and test result. If inputs
changed, run the affected checks again. Do not reuse results across dependency,
toolchain or environment changes. Record commands, assertion counts and failures;
never describe a skipped check as passed.

The opt-in `stress` suite and `Measure-ResourceUsage.ps1` are diagnostic tools,
not a normal CI gate. Their output is local under ignored `artifacts/`; they do
not establish that every possible device or long-running session is leak-free.

UI and idle suites use real Windows input and timing. Moving the pointer over a
test window can intentionally cancel idle motion and fail its assertion. Record
this as an interactive interruption, preserve the failure details and rerun only
when that cause is established. Do not silently retry or weaken assertions.

## Verification of the suite split

On the development machine, quick passed 92 assertions in 0.9?1.0 seconds and
full passed all 463 assertions in 49.1 seconds. Build completed without warnings
or errors. Six planner cases covered documentation, services, XAML, idle return,
test infrastructure and an unknown file; invalid CLI suites return exit code 2.
The first full run failed the existing idle-position assertion; the subsequent
run passed. The cause was not established. The assertion remains intact with
additional view, offset and animation diagnostics; intermittent idle-test failure
remains a known limitation. Resource stress was not rerun for this change.
