# AGENTS.md

## 1. Project Overview

- **Product:** Wideska
- **Repository:** wideska-calendar-widget
- **Platform:** Windows
- **License:** MIT
- **Type:** Open-source desktop calendar widget.

Wideska is a lightweight, customizable desktop calendar widget designed to display calendar events directly on the Windows desktop without requiring a separate calendar application.

## 2. Functional Requirements

### Calendar Views

- Single Day — 1 day.
- Three Days — 3 consecutive days.
- Workweek — 5 working days.
- Week — 7 days.
- Month — monthly calendar.
- Year — annual calendar with 12 months.

### Calendar Integration

- Google Calendar as the initial calendar provider.
- Support for multiple calendars.
- Automatic event synchronization.
- Support for recurring and all-day events.
- Calendar visibility management.
- Extensible architecture for additional calendar providers.

### Desktop Widget

- Frameless window.
- Adjustable position and dimensions.
- Persistent window position and dimensions.
- Configurable transparency.
- Light and dark themes.
- Optional automatic startup with Windows.
- System tray integration.
- Configurable desktop window behavior.

## 3. Technology Stack

- Language: C#.
- Framework: .NET 10 LTS.
- UI framework: WPF.
- UI markup: XAML.
- Architecture: MVVM.
- IDE: Visual Studio and Visual Studio Code.
- Version control: Git.
- Authentication: OAuth 2.0.

Do not replace the technology stack without explicit approval.

## 4. Architecture

Maintain a modular architecture with clear separation of responsibilities.

- `Models/` — application data models.
- `Views/` — XAML views.
- `ViewModels/` — presentation logic.
- `Services/` — application services.
- `Providers/` — calendar provider implementations.
- `Controls/` — reusable UI controls.
- `Converters/` — WPF value converters.
- `Resources/` — shared styles, colors and assets.
- `Infrastructure/` — platform-specific and technical infrastructure.

Create directories only when required.

Calendar provider implementations must remain independent of the presentation layer.

Keep business logic separate from UI code.

Avoid unnecessary abstraction and premature architectural complexity.

## 5. UI/UX Principles

- Minimalistic and modern visual design.
- Low visual noise.
- Clear information hierarchy.
- Consistent typography, spacing and alignment.
- Responsive layouts based on widget dimensions.
- Reusable styles and UI components.
- Support for different Windows DPI scaling settings.
- Minimal animations and visual effects.

Define reusable visual properties through XAML resource dictionaries.

Do not introduce unrelated visual elements or modify approved designs without explicit instructions.

## 6. Coding Standards

- Use descriptive English names for files, classes, methods and variables.
- Follow standard C# and .NET naming conventions.
- Prefer readable and maintainable code.
- Follow established MVVM principles.
- Use asynchronous operations for network requests and I/O.
- Do not block the UI thread.
- Handle exceptions appropriately.
- Avoid duplicate code.
- Avoid hardcoded configuration values.
- Avoid unnecessary third-party dependencies.
- Preserve existing functionality when implementing changes.

All source code and technical documentation must be written in English.

## 7. Performance

Wideska must be suitable for continuous background operation.

- Minimize CPU and memory consumption.
- Avoid unnecessary rendering and background processing.
- Avoid excessive API requests.
- Cache calendar data where appropriate.
- Release unused resources.
- Maintain UI responsiveness during synchronization.
- Dispose of resources and cancel background tasks when required.

## 8. Security and Privacy

- Never commit OAuth tokens, credentials or personal data.
- Never hardcode secrets in source code.
- Store authentication tokens securely using Windows-supported mechanisms.
- Request only necessary API permissions.
- Do not transmit calendar data to unrelated third-party services.
- Do not introduce telemetry without explicit approval.
- Exclude sensitive local configuration files from Git.

## 9. Git Workflow

### Branch Strategy

- `main` — stable production releases.
- `develop` — primary development and integration branch.
- `feature/*` — new functionality.
- `fix/*` — bug fixes.
- `refactor/*` — code restructuring.
- `docs/*` — documentation changes.
- `chore/*` — maintenance, dependencies and configuration.
- `release/*` — release preparation.
- `hotfix/*` — urgent production fixes.

### Autonomous Git Operations

The agent is authorized to independently:

- Inspect repository status and history.
- Fetch remote changes.
- Create and switch branches.
- Stage relevant files.
- Create commits.
- Merge completed task branches into `develop`.
- Push task branches and `develop` to the remote repository using non-force operations.
- Delete successfully merged local task branches.

Do not request approval for routine Git operations permitted by these instructions.

### Task Workflow

For every development task:

1. Inspect the current repository status and active branch.
2. Identify and preserve existing uncommitted user changes.
3. Fetch remote changes.
4. Ensure the local `develop` branch is synchronized with `origin/develop` using fast-forward-only updates.
5. Determine the appropriate branch type based on the task.
6. Create a descriptive task branch from `develop`, or reuse an existing branch dedicated to the same task.
7. Implement the requested changes.
8. Create logical, atomic commits throughout implementation.
9. Build the application and run relevant tests.
10. Review the final changes.
11. Push the task branch.
12. Merge the task branch into `develop` after successful verification.
After merging into develop, build the application
and run relevant tests again.

Do not push develop if post-merge verification fails.
13. Push the updated `develop` branch.
14. Delete the merged local task branch.
15. Return to `develop`.

If verification fails, do not merge the task branch into `develop`.

If synchronization or merging requires conflict resolution involving existing user changes, stop and report the conflict.

### Branch Naming

Use lowercase English names with hyphen-separated words.

Examples:

- `feature/google-calendar-integration`
- `feature/month-view`
- `feature/widget-transparency`
- `fix/calendar-event-rendering`
- `refactor/calendar-provider`
- `docs/update-readme`
- `chore/update-dependencies`

Avoid generic branch names such as `feature/update` or `fix/changes`.

### Commit Convention

Follow the Conventional Commits specification.

Format:

`type(scope): description`

Supported types:

- `feat` — new functionality.
- `fix` — bug fixes.
- `refactor` — code restructuring.
- `perf` — performance improvements.
- `style` — code formatting.
- `docs` — documentation.
- `test` — tests.
- `build` — build system and dependencies.
- `ci` — continuous integration.
- `chore` — maintenance.
- `revert` — reverting previous changes.

Examples:

- `feat(calendar): add month view`
- `feat(auth): implement Google OAuth authentication`
- `fix(widget): restore saved window position`
- `refactor(providers): extract calendar provider interface`
- `perf(sync): reduce redundant API requests`
- `docs(readme): update installation instructions`

### Commit Rules

- Write all commit messages in English.
- Use clear, concise and descriptive messages.
- Use the imperative mood.
- Keep commits atomic and logically scoped.
- Do not mix unrelated changes in a single commit.
- Stage only files relevant to the current task.
- Never commit secrets, credentials, tokens or generated build artifacts.
- Do not create empty commits.
- Do not amend existing commits without explicit approval.

### Branch Protection

- Never commit directly to `main`.
- Never merge into `main` without explicit approval.
- Never publish releases or create release tags without explicit approval.
- Never force-push.
- Never rewrite published Git history.
- Never use destructive Git operations without explicit approval.
- Never discard or overwrite existing user changes.
- Never delete `main`, `develop` or unmerged branches.
- Never delete remote branches without explicit approval.
- Do not bypass branch protection rules or required CI checks.

Release and hotfix operations require explicit approval.

### Task Completion Report

After completing a task, report:

- Implemented changes.
- Created branches.
- Created commits.
- Build and test results.
- Merge and push status.
- Current active branch.
- Remaining issues.

Do not claim that a Git operation succeeded unless its result has been verified.

## 10. Development Workflow

Before implementing a task:

1. Inspect the existing project structure and relevant files.
2. Identify existing functionality that can be reused.
3. Implement only the requested scope.
4. Preserve established architectural and visual conventions.

After implementing a task:

1. Review the modified files.
2. Build the application.
3. Run relevant tests when available.
4. Resolve errors introduced by the changes.
5. Report the implemented changes, build results and remaining issues.

Do not claim successful implementation or testing unless verified.

## 11. Agent Behavior

- Follow the user's explicit requirements.
- Communicate with the user in Russian.
- Keep responses concise and technical.
- Make reasonable implementation decisions within the approved architecture.
- Do not implement unrequested features.
- Do not perform unrelated refactoring.
- Obtain explicit approval before introducing major dependencies or architectural changes.
- Report technical limitations and unresolved problems.
- Prioritize stability, maintainability, security and performance.