# Work planner for Windows

A quiet desktop planner for one person on one Windows PC. This initial milestone is **local only**: Microsoft sign-in, email monitoring, Azure DevOps sync, global capture shortcut, and automatic collapse are not implemented yet. The Connections view reports this explicitly. No email is sent.

## Run on Windows

Install the .NET 10 SDK, then from the checkout:

```powershell
dotnet run --project src/WidgetPlanner.Desktop
```

The collapsed panel shows remaining tasks, outstanding replies, and the current In Progress task. Open Today to capture work, choose a priority, flag interruptions, and require a reply. Workspace provides Backlog, Replies, Review, and Connections. Close hides the window; double-click the tray icon to reopen. Use the tray menu to exit. Always on top is optional. The standard window frame supports moving and resizing.

Tasks save immediately to `%LOCALAPPDATA%\WidgetPlanner\planner.db`. Back up this file with the application closed. Only one app instance runs at a time. No cloud credentials or email content are stored in this milestone.

Unfinished tasks remain in Backlog on a new day. Choose Plan today to carry them forward; yesterday's membership remains recorded. Completion and reply handling are independent. Review currently uses current unfinished state and latest completion timestamp; a full immutable event history and historical review UI are future work.

## Build and test

```powershell
dotnet test tests/WidgetPlanner.Tests
dotnet build src/WidgetPlanner.Desktop
dotnet publish src/WidgetPlanner.Desktop -c Release -r win-x64 --self-contained false
```

Core/persistence tests run on Linux too. The Windows desktop project can be cross-compiled on Linux, but running the UI and validating tray, scaling, sleep/resume, and packaging require Windows. The published application requires the .NET 10 Windows Desktop Runtime.

## Structure and next milestones

- Core: task states, independent replies, daily planning, ordering, and review.
- Infrastructure: versioned SQLite persistence with transactional saves. Schema 1 stores a document; future integrations should migrate it rather than resetting data.
- Desktop: WPF compact panel, workspace, and tray lifecycle.
- Tests: real SQLite restart, duplicate daily planning, rollover, ordering, and reply persistence.

Next: UI editing/filtering and event history; global capture hotkey and automatic collapse; MSAL work-account sign-in; Graph unread suggestions and conversation tracking; Azure DevOps reads; configurable state mappings and durable update retries. Microsoft integration requires an approved Entra app registration and delegated access; existing user access alone does not grant the application access.

V1 email access will be read-only. Replies will open in Outlook and be marked handled manually. Local task state must remain separate from confirmed Azure state. Imported content must always be treated as data, never as executable instructions.
