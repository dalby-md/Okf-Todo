# OKF Todo

> **Version 0.1 alpha - work in progress.** Expect incomplete features and changes to the data model and user interface.

OKF Todo is an **AI-first, local-first desktop task system**. It is built with .NET and Photino for Windows, macOS, and Linux, and gives both people and AI assistants a transparent way to work with the same local data.

## AI-First Data

OKF Todo is designed so an AI assistant can read, analyze, and update task data without depending on a proprietary cloud API. There are two complementary access paths:

- **SQLite directly:** the application database contains the actual tasks, lookups, comments, history, tags, relationships, images, and attachments.
- **OKF-guided access:** the repository's [Open Knowledge Format context graph](docs/okf/todo-database/) describes the database concepts, schema, relationships, integrity rules, and lifecycle conventions so an AI can discover and reason about the data before working with SQLite.

OKF is the knowledge and navigation layer; SQLite remains the source of task data. An AI that writes directly to SQLite must respect the documented foreign keys, lookup codes, lifecycle rules, and history behavior. Close the desktop application and create a backup before allowing an external tool to perform direct writes.

The same database remains fully usable through the desktop interface. AI assistance is optional, local data stays under the user's control, and no hosted service is required.

A dedicated **OKF Todo CLI** is coming soon. It will provide direct terminal access for people, scripts, and AI agents, with supported commands for querying and updating tasks without writing ad hoc SQL. The CLI is also planned as the foundation for **Model Context Protocol (MCP) support**, allowing MCP-compatible AI clients to discover task capabilities and work with local data through structured tools.

![OKF Todo task workspace showing task views, rich task details, tags, waiting status, Markdown editing, and a checklist](docs/images/okf-todo-task-workspace.png)

*OKF Todo 0.1 alpha: local task views and a detailed workspace with classification, waiting state, rich content, tags, and checklist progress.*

## Coming Next

Planned for the next few days:

- An OKF Todo CLI for direct task queries and updates from terminals, scripts, and AI workflows.
- MCP support built on the CLI and OKF context graph for structured AI-tool integration.
- Improved filtering and sorting of tasks.
- A Windows MSIX installer for easier installation and updates.

It is designed for the work that often falls between formal systems: production errors, support cases, deployment checks, investigations, ideas, notes, requests, and follow-up tasks. The application runs locally, requires no account or cloud service, and keeps tasks, history, images, and attachments together in one SQLite database.

## Product Highlights

- Fast task capture with only a title and task type required.
- Active, urgent, waiting, overdue, completed, and all-task views.
- HTML and Markdown-capable rich-text editors.
- Paste, drop, or select images for task bodies.
- Priorities, deadlines, waiting targets, tags, and optional source references.
- Checklists with progress shown in the task list.
- File attachments stored inside the SQLite database.
- Typed relationships between tasks.
- A combined timeline of comments and automatic change history.
- Editable task types, priorities, and statuses.
- Light and dark color schemes with flexible desktop layouts.
- Complete database backup from inside the application.

## Getting Started

### Requirements

The current alpha is run from source. You need:

- Windows 10 or later, macOS 10.15 or later, or a current Linux desktop distribution.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- The platform webview used by Photino: WebView2 on Windows, the system WebKit view on macOS, or GTK/WebKit on Linux.

Windows is the primary tested platform for version 0.1. The application architecture and Photino shell are cross-platform, but macOS and Linux packaging and verification are still in progress.

### Run the application

Clone the repository, open a terminal in the repository root, and run:

```shell
dotnet restore
dotnet run --project ./Okf-Todo/Okf-Todo.csproj
```

On first launch, OKF Todo creates its database and initial lookup values automatically. No setup wizard or account is required.

The database is stored under the operating system's local application-data directory:

| Platform | Typical database path |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Okf-Todo\okf-todo.db` |
| macOS | `~/Library/Application Support/Okf-Todo/okf-todo.db` |
| Linux | `~/.local/share/Okf-Todo/okf-todo.db` |

Do not delete this file unless you intentionally want to remove all application data.

### Create your first task

1. Select **New task**.
2. Enter a title and select **Save** in the dialog.
3. Choose the task type. Add priority, deadline, tags, waiting information, or body content as needed.
4. Select **Save** in the task editor.

New tasks start as active. Images and attachments can be added after the task has been saved once.

## User Manual

### Workspace and navigation

The task list is on the left and the selected task is shown in the editor. Drag the divider to resize the list. In stacked layout, the divider adjusts the list height instead.

Use the view buttons to focus the list:

| View | Shows |
| --- | --- |
| **Active** | Current active tasks. |
| **Urgent** | Active tasks with urgent priority. |
| **Waiting** | Active tasks with a waiting target. |
| **Overdue** | Active tasks whose deadline has passed. |
| **Completed** | Completed tasks. |
| **All** | Every task, including cancelled tasks. |

Use **Search tasks** to filter the current view. The arrow keys move through the visible task list; **Home** and **End** select its first and last entries.

When you switch tasks with unsaved changes, the application asks whether to save, discard, or keep editing.

### Create and edit tasks

Select **New task** and enter a title. The task opens as a draft using the configured default task type.

The editor supports:

- Title and task type.
- Optional priority and deadline.
- A plain-text **Waiting for** value such as `INC123456`.
- Zero or more string-only tags.
- Rich body content.
- Optional source, source reference, and source URL fields when enabled in settings.

Select **Save** to persist changes. Changes to an existing task are recorded in its timeline.

### Body editor and images

The default HTML editor provides familiar rich-text formatting, lists, quotations, links, undo, redo, and images. Markdown mode provides Markdown and WYSIWYG editing plus formatting tools for headings, lists, links, images, tables, and code.

Choose the preferred editor under **Setup > Editor mode**. This preference applies when tasks are opened and is remembered between sessions.

Images can be pasted, dropped, or selected from the editor. Supported formats are PNG, JPEG, GIF, and WebP, with a maximum size of 5 MB per image. Save a new task before adding images. Image bytes are stored in SQLite rather than as separate files.

### Task lifecycle

Use the action buttons in the task header:

- **Complete** marks an active task as completed.
- **Cancel** marks an active task as cancelled.
- **Reopen** returns a completed or cancelled task to active status.

Completing a waiting task asks whether to clear its waiting target. Lifecycle changes and timestamps are recorded automatically.

Cancelled tasks appear only in **All** and are visually distinguished from active work.

### Waiting targets

Enter any useful text in **Waiting for**, for example a person, team, ticket number, or external dependency. A task can have one active waiting target.

Saving a new value marks the task as waiting while keeping it active. Clearing the field resolves the current waiting target and records the change in the timeline.

### Tags

Use **Tags** to organize tasks with simple text values:

1. Select an existing tag or type a new value.
2. Press **Enter** to attach it.
3. Use the tag chip's remove control to detach it from the task.

Under **Setup > Tags**, tags can be renamed. Unused tags can be permanently deleted. A used tag can be merged into another tag, moving all task associations to the target without creating duplicates.

### Checklists

Add checklist items below the task body. Items can be completed, reopened, edited, reordered, and deleted. Progress is shown as a completed/total count in both the task detail and task list.

Checklist additions and completion changes are recorded in the timeline. Deletion requires confirmation.

### Attachments

Select **Add file** in the Attachments section. Files up to 25 MB are stored directly in the database with their metadata and content.

Use the attachment actions to save a copy or remove the attachment. Removing a file requires confirmation and cannot be undone from the application.

### Task relationships

Relationships are hidden by default. Enable **Setup > Show relationships** to display them.

Choose a relationship type and another task, then select **Add**. Relationship labels are shown from the current task's perspective, such as **Blocks** or **Blocked by**. Select the related task name to navigate to it.

Self-relations and duplicate relations are rejected. Removing a relationship requires confirmation and is logged on both involved tasks.

### Timeline and comments

The Timeline combines:

- Human-written comments.
- Automatic logs for lifecycle and field changes.
- Checklist, attachment, tag, and relationship events.

Enter a comment at the bottom of the task and select **Add**, or press **Ctrl+Enter**. Comments can be permanently deleted after confirmation. Automatic history entries cannot be edited through the normal interface.

### Setup and preferences

Select the gear-shaped **Setup** button to configure:

- HTML or Markdown editor mode.
- Editor height.
- Light or dark color scheme.
- Automatic, side-by-side, or stacked task layout.
- Visibility of source fields and relationships.
- Database backup.
- Task types, priorities, statuses, and tags.

Layout, visibility, editor, and color preferences are restored when the application starts again.

Task types, priorities, and statuses may be renamed, reordered, assigned badge colors, activated or deactivated, and selected as defaults where permitted. Values already used by tasks are retained to protect historical data. System values required by the application cannot be removed or disabled.

### Back up and restore data

Open **Setup**, then select **Back up database**. Choose a destination in the native save dialog. The application creates and validates a complete SQLite backup before replacing the selected destination.

The backup includes tasks, body images, attachments, lookups, tags, relationships, comments, checklists, and history. Interface preferences such as layout and color scheme are stored separately and are not included.

Restore is manual in version 0.1:

1. Close OKF Todo.
2. Keep a copy of the current database if needed.
3. Replace the platform-specific `Okf-Todo/okf-todo.db` file listed under Getting Started with the backup file.
4. Start OKF Todo again.

Never replace the active database while the application is running.

## Data and Privacy

OKF Todo is a single-user, local-first application. It has no authentication, cloud synchronization, application telemetry, or external task-system integration. Application data stays in the local SQLite database unless you create a backup or save an attachment copy yourself.

## Current Limitations

Version 0.1 is an alpha release intended for evaluation and personal use:

- Windows, macOS, and Linux can run from source; Windows currently receives the most testing.
- Packaged installers and automatic updates are not available yet. A Windows MSIX package is the first planned installer.
- There is no cloud sync or multi-user collaboration.
- Database migrations are not supported during early development; schema changes may require a fresh development database.
- Restore is a manual file-replacement operation.
- Deep integrations with email, ServiceDesk, Teams, and Azure DevOps are not included.

Use the in-application backup command regularly while evaluating the alpha.

## Development

Build the solution:

```powershell
dotnet build -c Release
```

Run the test suite:

```powershell
dotnet test .\Okf-Todo.Tests\Okf-Todo.Tests.csproj -c Release
```

Product and architecture documentation is available in [`docs`](docs/).
