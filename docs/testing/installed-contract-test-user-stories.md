# Installed Contract Test User Stories

The installed contract test project verifies OKF-Todo from the perspective of an external AI harness. It uses only the Windows-installed executables, installed OKF files, and isolated disposable SQLite databases.

The project is disabled in the main solution by default because these stories require a Windows installation of OKF-Todo. They can still be run explicitly whenever the installed product contract needs verification.

## MCP user stories

### Create and read a task

As a user working through an AI harness, I want to create a task through the installed MCP server and read it back, so that I know MCP can persist a usable task through its published tool contract.

The test verifies that:

- The installed MCP server advertises `task_create` and `task_get`.
- A task can be created with a title, type, Markdown body, priority, and tags.
- The new task starts in the `ACTIVE` state.
- The task can be read back through MCP.
- The task is present in SQLite.
- A `TASK_CREATED` history entry is recorded.

### Update a task without losing existing information

As a user working through an AI harness, I want an MCP update to preserve information that I did not ask to change, so that replacement-style updates do not accidentally remove the task body or tags.

The test verifies that:

- The harness reads the current task before updating it.
- The title can be changed through `task_update`.
- The existing body and tags are preserved in the replacement payload.
- The changed title is stored in SQLite.
- At least one `TASK_UPDATED` history entry is recorded.

## OKF command-adapter user stories

### Create a task with a diagnostic attachment

As a user who has given an AI harness the installed OKF context, I want it to create a task and attach customer diagnostic evidence through the installed command adapter, so that application validation, attachment metadata, and history are preserved.

The test verifies that:

- The installed OKF contract documents `task.create`, `task.attachment.create`, `taskTypeCode`, and `base64Data`.
- A customer investigation task can be created with source information and tags.
- A text attachment can be supplied as Base64 content.
- The attachment content and content type are persisted.
- A SHA-256 hash is calculated.
- An `ATTACHMENT_ADDED` history entry is recorded.

### Update a task from the installed OKF contract

As a user who has given an AI harness the installed OKF context, I want it to follow the documented read-first update workflow, so that fields outside the requested change remain intact.

The test verifies that:

- The installed OKF contract documents `task.get`, `task.update`, and the requirement to preserve fields.
- The harness reads the existing task before updating it.
- The title can be changed while the existing body and tags are preserved.
- The changed title is stored in SQLite.
- At least one `TASK_UPDATED` history entry is recorded.

## OKF-guided direct SQLite user stories

### Create a task and attachment from installed OKF table knowledge

As a user who has explicitly granted an AI harness direct database access, I want it to use the installed OKF table descriptions to create a task and attachment, so that it can construct a valid database change without MCP or repository source context.

The test verifies that:

- The installed OKF files describe the required `TaskItems`, `TaskAttachments`, `TaskTypes`, and `TaskStatuses` columns.
- Active task-type and task-status identifiers are read from the database rather than guessed.
- SQLite foreign-key enforcement is enabled.
- The task and attachment are inserted in one transaction using parameterized SQL.
- Attachment bytes, file size, content type, SHA-256 hash, description, and UTC timestamp are stored.
- The inserted records can be read directly from SQLite.
- The installed application can also read the directly inserted task.
- Direct insertion does not automatically produce `TASK_CREATED` or `ATTACHMENT_ADDED` history.

### Update a task from installed OKF table knowledge

As a user who has explicitly granted an AI harness direct database access, I want it to update a selected task using the installed OKF table description, so that the harness can make a controlled direct change and verify its limitations.

The test verifies that:

- The installed OKF files describe the required `TaskItems` columns.
- The task is selected by its numeric ID.
- The title and `UpdatedAt` value are changed using parameterized SQL.
- Exactly one task must be affected.
- The updated task can be read through SQLite and the installed application.
- Direct updating does not automatically produce a `TASK_UPDATED` history entry.

## Isolation boundary

All stories run against a new disposable database under the test runner's temporary directory. The tests do not use the normal user database, repository documentation, application project references, publish output, or installer staging as product context.
