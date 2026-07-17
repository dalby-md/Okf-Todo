---
type: Application Command Interface
title: Task Application Command Interface
description: Defines the supported command path for agents that consume the OKF bundle and need to read or mutate tasks.
resource: Okf-Todo/Services/ApplicationCommandService.cs
tags:
  - okf
  - todo
  - commands
timestamp: 2026-07-14T00:00:00Z
---

# Task Application Command Interface

## Purpose

OKF is a descriptive Markdown knowledge format and does not execute commands itself. An agent that consumes this bundle must invoke the application's `--okf-command` adapter when it needs to read or mutate task data.

The adapter and the Photino JavaScript bridge both dispatch through `ApplicationCommandService`. Task mutations therefore use the same validation, lifecycle, timestamp, relationship, and automatic history-log behavior as the desktop UI.

Do not write directly to [TaskItems](../tables/task-items.md), [TaskLogEntries](../tables/task-log-entries.md), or related tables.

## Invocation

Pass one JSON command envelope on standard input:

```powershell
$request = @'
{"messageId":"okf-1","type":"task.get","payload":{"id":42}}
'@

$request | dotnet run -c Release --no-build --project .\Okf-Todo\Okf-Todo.csproj -- --okf-command
```

The adapter writes one JSON response envelope to standard output and diagnostic logs to standard error.

By default, commands use the same personal database as the desktop application. For isolated automation or testing, pass an absolute database file path with `--okf-database-path`:

```powershell
$request | dotnet run -c Release --no-build --project .\Okf-Todo\Okf-Todo.csproj -- `
  --okf-command `
  --okf-database-path C:\Temp\okf-command-test.db
```

`--okf-database-path` is accepted only together with `--okf-command`. The application creates the parent directory when necessary, applies pending migrations, and seeds empty lookup tables before executing the command.

Exit codes:

- `0`: the command succeeded and the response has `"ok": true`.
- `1`: validation or application command processing failed and the response has `"ok": false`.
- `2`: standard input did not contain a command.

## Command Envelope

```json
{
  "messageId": "caller-defined-correlation-id",
  "type": "task.update",
  "payload": {}
}
```

Use a unique `messageId` per call. Responses preserve the identifier and use `<command-type>.result` as their response type.

## Task Mutation Workflow

For updates, read the task first with `task.get`, preserve fields that are not changing, and then submit the complete `task.update` payload. The update path records every changed field in [TaskLogEntries](../tables/task-log-entries.md).

Supported task mutation commands include:

- `task.create`
- `task.update`
- `task.start`
- `task.undoStart`
- `task.complete`
- `task.reopen`
- `task.cancel`
- `task.waiting.add`
- `task.waiting.clear`
- `task.comment.create`
- `task.comment.delete`
- `task.checklist.create`, `task.checklist.update`, `task.checklist.complete`, `task.checklist.reorder`, and `task.checklist.delete`
- `task.relation.create` and `task.relation.delete`
- `task.attachment.create` and `task.attachment.delete`

Use `task.timeline.get` to verify the resulting automatic history entries.

## Sources

- `Okf-Todo/Program.cs`
- `Okf-Todo/Services/ApplicationCommandService.cs`
- `Okf-Todo/Services/OkfCommandRunner.cs`
- `Okf-Todo/Bridge/BridgeMessageHandler.cs`
