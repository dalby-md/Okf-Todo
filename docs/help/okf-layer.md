# Using the OKF Layer

The Open Knowledge Format (OKF) layer explains the OKF-Todo database to people and AI agents, then points them to a safe application command interface when they need to change tasks.

## What the OKF layer is

OKF-Todo ships a linked Markdown context graph describing the SQLite schema, tables, columns, relationships, lookup codes, integrity constraints, lifecycle rules, and supported application commands. Start at the [OKF context graph entry point](../okf/todo-database/index.md) and follow only the links needed for the current job.

> **Important:** OKF is knowledge and navigation, not an executable API. SQLite contains the actual tasks. Use the desktop app, the application command adapter, or the MCP server for supported writes.

| Need | Use |
| --- | --- |
| Understand the data model or prepare a query | Read the OKF context graph. |
| Inspect data during diagnostics | Use OKF to understand the schema, then query SQLite read-only. |
| Script a supported task operation | Invoke `Okf-Todo.exe --okf-command`. |
| Give an MCP-compatible AI client task tools | Use the [MCP server](mcp-server.md). |

## Find and navigate the context graph

In a standard Windows installation, the entry point is relative to the application folder:

```text
okf\todo-database\index.md
```

The default application folder is:

```text
%LOCALAPPDATA%\Programs\Okf-Todo
```

In a source checkout, use:

```text
docs\okf\todo-database\index.md
```

If you do not already have a source checkout, clone the repository and change to its root directory.

For Bash:

```bash
git clone https://github.com/dalby-md/OKF-Todo.git
cd OKF-Todo
```

For Windows Command Prompt:

```cmd
git clone https://github.com/dalby-md/OKF-Todo.git
cd OKF-Todo
```

All following source-based `dotnet` commands in this guide assume the current directory is the repository root.

Recommended navigation:

1. Read [`index.md`](../okf/todo-database/index.md) for graph entry points and the source-of-truth statement.
2. Open the database document for database-wide rules and the physical SQLite location.
3. Open only the relevant table documents, such as tasks, tags, waiting targets, comments, or log entries.
4. Follow relationship and integrity links before joining tables or proposing a mutation.
5. For a write, open the [application command interface](../okf/todo-database/references/application-command-interface.md) and use an application command instead of direct SQL.

Stable lookup *codes* are application inputs. Lookup *names* are display text and can be changed by the user. The installed defaults include:

- Task types such as `REQUEST`, `ERROR`, `NOTE`, and `INVESTIGATION`.
- Priorities such as `URGENT`, `NORMAL`, and `CAN_WAIT`.
- Body formats `HTML` and `MARKDOWN`.

## Run an application command

The command adapter reads one JSON envelope from standard input, applies migrations and validation, executes the same application service used by the desktop UI, and writes one JSON response envelope to standard output. Diagnostic logs go to standard error.

### Installed application: list active tasks

```powershell
$request = @'
{"messageId":"okf-list-1","type":"task.list","payload":{"view":"active"}}
'@

$app = "$env:LOCALAPPDATA\Programs\Okf-Todo\Okf-Todo.exe"
$request | & $app --okf-command
```

If you selected another installation folder, replace `$app` with that absolute path.

### Source checkout: get one task

Build the application first:

```powershell
dotnet build -c Release
```

Then send the request:

```powershell
$request = @'
{"messageId":"okf-get-42","type":"task.get","payload":{"id":42}}
'@

$request | dotnet run -c Release --no-build `
  --project .\Okf-Todo\Okf-Todo.csproj -- --okf-command
```

### Create a task

```powershell
$request = @'
{
  "messageId": "okf-create-1",
  "type": "task.create",
  "payload": {
    "title": "Review deployment logs",
    "taskTypeCode": "INVESTIGATION",
    "body": "## Checks\n\n- Inspect errors\n- Record findings",
    "bodyFormatCode": "MARKDOWN",
    "taskPriorityCode": "URGENT",
    "taskSourceCode": "MONITORING_LOGS",
    "sourceReference": "Production API",
    "sourceUrl": null,
    "deadline": "2026-07-20T12:00:00Z",
    "activeWaitingForLabel": null,
    "tags": ["production", "deployment"]
  }
}
'@

$request | & $app --okf-command
```

New tasks are active. Supplying `activeWaitingForLabel` records the active waiting target while the task remains in the active lifecycle state.

### Update safely: read, merge, replace

`task.update` replaces the editable task fields. First call `task.get`, copy every value that must remain, change only the intended values, and then submit the complete replacement payload.

An omitted or null optional field is cleared. An empty or null tag collection removes all tags.

> **Do not send a partial update.** A request containing only an ID and a changed title can unintentionally clear the body, priority, source, deadline, waiting target, and tags.

## Command envelope and results

Every request uses this envelope:

```json
{
  "messageId": "unique-caller-correlation-id",
  "type": "task.get",
  "payload": { "id": 42 }
}
```

- Use a unique `messageId` for each call. The response preserves it.
- A successful response has `"ok": true` and the result in `payload`.
- A failed response has `"ok": false` and a structured `error`.
- Exit code `0` means success.
- Exit code `1` means validation or command processing failed.
- Exit code `2` means no input command was supplied.

| Command group | Examples |
| --- | --- |
| Read | `task.list`, `task.get`, `task.timeline.get`, lookup and tag reads |
| Task fields | `task.create`, `task.update` |
| Lifecycle | `task.start`, `task.undoStart`, `task.complete`, `task.reopen`, `task.cancel` |
| Task details | Waiting, comments, checklist items, relations, and attachments |

The detailed command list and source-code references are in the [application command interface](../okf/todo-database/references/application-command-interface.md).

## Use another database only deliberately

Commands normally use the same personal database as the desktop application:

```text
%LOCALAPPDATA%\Okf-Todo\okf-todo.db
```

For an isolated test, add an absolute database path:

```powershell
$request | & $app --okf-command `
  --okf-database-path C:\Temp\okf-command-test.db
```

The application creates the directory and database when needed, applies pending migrations, and seeds empty lookup tables before executing the command.

If a command appears to succeed but its task is missing from the GUI, first check whether a database override caused the command and GUI to use different files.

## Safety and troubleshooting

- **Never write task tables directly.** Direct SQL bypasses validation, lifecycle behavior, timestamps, relationships, and automatic timeline history.
- **Keep standard output clean.** Scripts should parse the single JSON response and send their own diagnostics elsewhere.
- **Use ISO 8601 dates.** Include a timezone, for example `2026-07-20T12:00:00Z`.
- **Verify mutations.** Read the task and call `task.timeline.get` after an important change.
- **Check stable codes.** Users can customize lookup values in Setup; inspect the current lookup data when a code is rejected.
- **Build before using `--no-build`.** From source, run `dotnet build -c Release` after code changes.

For an AI client that should discover structured tools instead of sending JSON envelopes, continue with [Using the MCP Server](mcp-server.md).
