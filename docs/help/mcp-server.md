# Using the MCP Server

The optional OKF-Todo Model Context Protocol (MCP) server gives an MCP-compatible AI client structured, local tools for listing, reading, creating, and updating tasks and reading task timelines.

## How it works

The MCP client starts `Okf-Todo.Mcp.exe` as a standard-input/standard-output process when the client needs it. It is not a Windows service, web server, or cloud API, and you normally do not start it by double-clicking the executable.

The server uses the same application command service and, by default, the same SQLite database as the desktop application. Tasks created through MCP therefore appear in the GUI and follow the same field validation and automatic history behavior.

> **Local by design:** OKF-Todo does not send the database to a hosted OKF-Todo service. Your chosen MCP client and AI model may have their own data-handling behavior, so review that client's settings before granting it task access.

## Install and locate the server

The Windows installer offers **Install MCP server** as an optional component and selects it by default. In the standard installation, these files are created:

```text
%LOCALAPPDATA%\Programs\Okf-Todo\mcp\Okf-Todo.Mcp.exe
%LOCALAPPDATA%\Programs\Okf-Todo\integration\mcp-config.json
```

The generated `mcp-config.json` contains the actual absolute executable path chosen during installation. Copy its `okf-todo` server entry into your MCP client's configuration.

If the `mcp` folder or configuration file is absent, run the same installer again and select the MCP component.

### Installed configuration shape

```json
{
  "mcpServers": {
    "okf-todo": {
      "command": "C:\\Users\\<you>\\AppData\\Local\\Programs\\Okf-Todo\\mcp\\Okf-Todo.Mcp.exe"
    }
  }
}
```

Client products store MCP configuration in different places and may use a different outer property name. Preserve the server name, command, and arguments, but follow the client's documentation for where to put the entry. Restart or reload the client after changing its configuration.

### Run from a source checkout

Clone the repository and change to its root directory if you do not already have a checkout.

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

Build the MCP server:

```powershell
dotnet build .\Okf-Todo.Mcp\Okf-Todo.Mcp.csproj -c Release
```

Then configure the client to start the built DLL:

```json
{
  "mcpServers": {
    "okf-todo": {
      "command": "dotnet",
      "args": [
        "C:\\git\\Okf-Todo\\Okf-Todo.Mcp\\bin\\Release\\net8.0\\Okf-Todo.Mcp.dll"
      ]
    }
  }
}
```

Replace the DLL path with the absolute path for your checkout.

## Available tools

| Tool | Purpose and important behavior |
| --- | --- |
| `task_list` | Lists tasks for a view. It defaults to active tasks and supports `active`, `urgent`, `waiting`, `overdue`, `completed`, and `all`. |
| `task_get` | Returns one task by numeric ID, including the editable values needed before an update. |
| `task_create` | Creates and returns a task. The title and stable task type code are required; the other fields are optional. |
| `task_update` | Replaces all editable fields. Read the task first and preserve every value that should not change. |
| `task_get_timeline` | Returns comments and application-generated history for a task, newest first. |

The current MCP surface does not expose completion, cancellation, comments, checklists, attachments, or relationships as tools. Use the desktop GUI or the [OKF application command adapter](okf-layer.md#run-an-application-command) for those operations.

## Recommended AI workflow

1. Ask the client to call `task_list` to find candidate tasks.
2. Call `task_get` for the exact task before drawing conclusions or changing it.
3. For an update, retain all returned editable values, change only the requested values, and call `task_update`.
4. Call `task_get` again to verify the saved state.
5. Use `task_get_timeline` when change history or comments matter.

> **Treat `task_update` as a full replacement.** Omitted optional fields are cleared. Null or empty tags remove all tags. Always read before updating, even if the requested change seems small.

### Useful prompts

- “List my active OKF-Todo tasks and summarize the three that need attention first. Do not change anything.”
- “Get task 42 and show its current priority, deadline, waiting target, tags, and latest timeline entries.”
- “Create an investigation task titled ‘Review deployment logs’, priority URGENT, tagged production and deployment.”
- “Read task 42 first. Change only its priority to URGENT, preserve every other editable field, then read it again to verify.”

## Tool inputs

### Create and update fields

| Field | Meaning |
| --- | --- |
| `title` | Required task title. |
| `taskTypeCode` | Stable task type code, not the display name. |
| `body` | HTML or Markdown body matching `bodyFormatCode`. |
| `bodyFormatCode` | `HTML` or `MARKDOWN`. Create defaults to `HTML`. |
| `taskPriorityCode` | Optional stable priority code. |
| `taskSourceCode` | Optional stable source code. |
| `sourceReference`, `sourceUrl` | Optional external reference and URL. |
| `deadline` | Optional ISO 8601 date/time, preferably with timezone. |
| `activeWaitingForLabel` | Optional free-text waiting target. Supplying it records the task as waiting while it remains active. |
| `tags` | Optional collection of plain strings. |

### Default stable codes

Users can customize non-system lookups in Setup, so the current database is authoritative. A new installation starts with:

- Task types: `CRITICAL_ERROR`, `ERROR`, `REQUEST`, `IDEA`, `NOTE`, `INVESTIGATION`, `IMPROVEMENT`.
- Priorities: `URGENT`, `NORMAL`, `CAN_WAIT`.
- Sources: `MANUAL`, `SERVICEDESK`, `EMAIL`, `TEAMS`, `TFS_AZURE_DEVOPS`, `ORACLE_APEX`, `POWER_PLATFORM`, `DEPLOYMENT`, `MONITORING_LOGS`, `USER_REPORT`.

For example, use `"taskTypeCode": "ERROR"`, not `"taskTypeCode": "Error"`.

## Use the same database as the GUI

Without extra arguments, the server uses:

```text
%LOCALAPPDATA%\Okf-Todo\okf-todo.db
```

For an isolated development or test database, add arguments to the server entry:

```json
{
  "mcpServers": {
    "okf-todo-test": {
      "command": "C:\\path\\to\\Okf-Todo.Mcp.exe",
      "args": ["--database-path", "C:\\Temp\\okf-todo-mcp.db"]
    }
  }
}
```

> **A database override creates a separate task store.** If MCP-created tasks do not appear in the desktop app, compare the MCP argument with the GUI database path before troubleshooting anything else.

## Security and permissions

- The MCP server runs with your Windows user permissions and can read and modify the selected OKF-Todo database.
- Only configure it in clients you trust. A client allowed to invoke mutation tools can create or replace task content.
- Use read-only wording in prompts when you want analysis without changes, and review tool confirmations offered by your MCP client.
- Back up the database from **Setup → Data** before large automated changes.
- Do not wrap the executable in a script that prints banners or diagnostics to standard output; MCP protocol messages use that stream.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| The client shows no OKF-Todo server | Validate the client's configuration location and JSON shape, use an absolute command path, then restart or reload the client. |
| The executable is missing | Rerun the Windows installer and select **Install MCP server**, or build the MCP project from source. |
| The process opens and immediately closes | That is expected when it has no MCP client connected. Let the client start it over stdio. |
| The client reports invalid JSON or protocol output | Remove wrappers that write to stdout. Server and framework logs already go to stderr. |
| A lookup code is rejected | Use stable codes from the current database. Lookup display names are not valid inputs and customized codes may differ from defaults. |
| An update cleared data | `task_update` is replacement-based. Restore from a backup if necessary, then use read-before-update. |
| MCP and GUI show different tasks | Remove or correct `--database-path` so both processes use the same database. |

For the descriptive database graph and direct application command envelope, continue with [Using the OKF Layer](okf-layer.md).
