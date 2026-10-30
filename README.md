# Work planner for Windows

A quiet desktop planner for one person on one Windows PC. Azure DevOps tickets can now be imported with a personal access token. Email integration, global capture shortcut, and automatic collapse are not implemented yet. No email is sent.

## Connect Azure DevOps

Open Today → Workspace → Connections. Enter your organisation name (for example `contoso` from `https://dev.azure.com/contoso`) and a user-created PAT with **Work Items (Read)** scope. Choose Import / refresh my tickets. No app registration is needed for this PAT connection. Your organisation must permit PATs and your account must already have project access.

The PAT uses Basic authentication over HTTPS, stays in memory for the current app session, and is not saved to the database or logged. Forget token clears it; exiting clears it too. Closing the window only hides it and retains the session. Enter the token again after restarting.

Refresh is manual in this milestone. The importer queries work assigned to the PAT owner across accessible projects, discovers each work item type's workflow categories, and imports unfinished items into Backlog. Repeated imports update existing records without resetting local state, priority, Today order, or reply reminders. Previously imported assigned items can refresh to a completed Azure state without silently completing the local task. Items no longer assigned to you retain their last confirmed snapshot; this version does not separately refresh those items. Large result sets exceeding Azure DevOps query limits will fail visibly rather than silently truncate. Refresh is applied only after all requests succeed.

Rows show project, type, Azure priority, confirmed Azure state, confirmation time, description excerpt, and a ticket link. Remote descriptions are displayed as text. Local completion **does not update Azure DevOps**. Work Items (Read & Write) and a separate explicit update workflow would be needed for state changes.

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

Next: UI editing/filtering and event history; global capture hotkey and automatic collapse; connecting the user's existing email application once its supported access method is known; automatic sync; configurable Azure state mappings and durable update retries. Microsoft Graph delegated access, if used directly, requires an appropriate app registration; Azure DevOps PAT reads do not.

V1 email access will be read-only. Replies will open in Outlook and be marked handled manually. Local task state must remain separate from confirmed Azure state. Imported content must always be treated as data, never as executable instructions.
