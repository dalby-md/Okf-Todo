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

## Core task command contracts

The following payloads are sufficient for a harness that has only this installed OKF bundle and access to the installed command adapter. JSON property names use camel case.

Create a task with `task.create`:

```json
{
  "messageId": "create-1",
  "type": "task.create",
  "payload": {
    "title": "Investigate failed deployment",
    "taskTypeCode": "INVESTIGATION",
    "body": "Evidence and proposed next steps.",
    "bodyFormatCode": "MARKDOWN",
    "taskPriorityCode": "NORMAL",
    "taskSourceCode": "DEPLOYMENT",
    "sourceReference": "Release 1842",
    "sourceUrl": null,
    "owner": "Platform team",
    "responsible": "Anna Jensen",
    "deadline": null,
    "activeWaitingForLabel": null,
    "tags": ["deployment", "investigation"]
  }
}
```

Only `title` and `taskTypeCode` are required. The command returns the saved task detail in `payload`, including its numeric `id`. New tasks start with status `ACTIVE` and receive a `TASK_CREATED` history entry.

Read a task with `task.get`:

```json
{
  "messageId": "get-1",
  "type": "task.get",
  "payload": {
    "id": 42
  }
}
```

Replace editable task fields with `task.update`:

```json
{
  "messageId": "update-1",
  "type": "task.update",
  "payload": {
    "id": 42,
    "title": "Investigate failed deployment and release variables",
    "taskTypeCode": "INVESTIGATION",
    "body": "Evidence and proposed next steps.",
    "bodyFormatCode": "MARKDOWN",
    "taskPriorityCode": "NORMAL",
    "taskSourceCode": "DEPLOYMENT",
    "sourceReference": "Release 1842",
    "sourceUrl": null,
    "owner": "Platform team",
    "responsible": "Anna Jensen",
    "deadline": null,
    "activeWaitingForLabel": null,
    "tags": ["deployment", "investigation"]
  }
}
```

This is replacement semantics: call `task.get` first and preserve every field that must remain. A null or omitted optional value is cleared, and a null or empty tag collection removes all tags. The command returns the complete saved task detail and creates history for changed fields.

## Attachment command contract

Add an attachment with `task.attachment.create` after `task.create` returns the task ID:

```json
{
  "messageId": "attachment-1",
  "type": "task.attachment.create",
  "payload": {
    "taskId": 42,
    "fileName": "error.log",
    "contentType": "text/plain",
    "base64Data": "VGltZW91dCBhZnRlciAzMCBzZWNvbmRzLg==",
    "description": "Customer diagnostic output"
  }
}
```

`base64Data` contains the complete file content. The application normalizes the file name, rejects invalid Base64 and files larger than 25 MB, calculates the byte length and SHA-256 hash, stores the content as a SQLite BLOB, updates the task timestamp, and creates an `ATTACHMENT_ADDED` history entry. The response payload contains the updated attachment list.

Related commands use these payloads:

```json
{"messageId":"attachment-list-1","type":"task.attachment.list","payload":{"taskId":42}}
{"messageId":"attachment-get-1","type":"task.attachment.get","payload":{"attachmentId":7}}
{"messageId":"attachment-delete-1","type":"task.attachment.delete","payload":{"taskId":42,"attachmentId":7}}
```

The get response includes `fileName`, `contentType`, and `base64Data`.

## Direct SQLite capability and boundary

A harness that is separately granted write access to the SQLite database can use the linked table documents to construct direct inserts and updates, including BLOB rows in `TaskAttachments`. OKF is documentation and cannot prohibit those writes.

Direct SQL is not equivalent to an application command. It bypasses filename normalization, file-size validation, automatic hashes unless the harness calculates them, task timestamp coordination, lifecycle services, and automatic history. Use a transaction, enable SQLite foreign keys, and restrict direct writes to disposable or explicitly approved databases. Prefer the application command adapter for normal task and attachment mutations.

## Sources

- `Okf-Todo/Program.cs`
- `Okf-Todo/Services/ApplicationCommandService.cs`
- `Okf-Todo/Services/OkfCommandRunner.cs`
- `Okf-Todo/Bridge/BridgeMessageHandler.cs`
