# Data Model — Local Developer/Internal Support Task System

## Overview

Database target: SQLite. IMPORTANT: Use contraints and referential integrity. Describe datamodel using  ([OKF](https://github.com/GoogleCloudPlatform/knowledge-catalog/tree/main/okf))

The model is optimized for a personal local task system built with modern .NET, EF Core, and Photino.

All controlled values are lookup tables. Initial lookup values are seeded from configuration only when the corresponding table is empty.

## Common lookup columns

Most lookup tables should share these columns:

```text
Id
Code
Name
Description nullable
SortOrder
IsActive
IsSystem
CreatedAt
UpdatedAt
```

Rules:

- `Code` is stable and used by application logic.
- `Name` is editable and shown in the UI.
- `Description` is editable.
- `SortOrder` is editable.
- `IsActive` controls whether the value can be selected for new data.
- `IsSystem` protects application-critical values.
- Lookup values that have been used are not hard-deleted.
- Non-system lookup values that have not been used may be hard-deleted.
- Inactive values remain valid for existing tasks and history.

## Lookup tables

### TaskType

Initial values:

```text
CRITICAL_ERROR   Critical error
ERROR            Error
REQUEST          Request
IDEA             Idea
NOTE             Note
INVESTIGATION    Investigation
IMPROVEMENT      Improvement
```

### TaskStatus

Initial values:

```text
ACTIVE       Active
COMPLETED    Completed
CANCELLED    Cancelled
```

System-critical. Lifecycle logic depends on these codes.

### TaskPriority

Initial values:

```text
URGENT       Urgent
NORMAL       Normal
CAN_WAIT     Can wait
```

### TaskSource

Initial values:

```text
MANUAL             Manual
SERVICEDESK        ServiceDesk
EMAIL              Email
TEAMS              Teams
TFS_AZURE_DEVOPS   TFS / Azure DevOps
ORACLE_APEX        Oracle APEX
POWER_PLATFORM     Power Platform
DEPLOYMENT         Deployment
MONITORING_LOGS    Monitoring/logs
USER_REPORT        User report
```

### AttachmentKind

Initial values:

```text
SCREENSHOT    Screenshot
LOG_FILE      Log file
DOCUMENT      Document
EXPORT        Export
SQL_SCRIPT    SQL script
CONFIG_FILE   Config file
SOURCE_CODE   Source code
OTHER         Other
```

### TaskRelationType

Recommended columns:

```text
Id
Code
Name
ReverseName
Description nullable
SortOrder
IsActive
IsSystem
CreatedAt
UpdatedAt
```

Initial values:

```text
BLOCKS          Blocks          Blocked by
DEPENDS_ON      Depends on      Required by
DUPLICATE_OF    Duplicate of    Has duplicate
RELATED_TO      Related to      Related to
CREATED_FROM    Created from    Created task
FOLLOW_UP_TO    Follow-up to    Has follow-up
```

### TaskLogType

Initial values:

```text
TASK_CREATED                 Task created
STATUS_CHANGED               Status changed
TYPE_CHANGED                 Type changed
PRIORITY_CHANGED             Priority changed
DEADLINE_CHANGED             Deadline changed
WAITING_FOR_CHANGED          Waiting for changed
WAITING_FOR_CLEARED          Waiting for cleared
CHECKLIST_ITEM_ADDED         Checklist item added
CHECKLIST_ITEM_COMPLETED     Checklist item completed
CHECKLIST_ITEM_REOPENED      Checklist item reopened
ATTACHMENT_ADDED             Attachment added
ATTACHMENT_REMOVED           Attachment removed
RELATION_ADDED               Relation added
RELATION_REMOVED             Relation removed
TASK_COMPLETED               Task completed
TASK_REOPENED                Task reopened
TASK_CANCELLED               Task cancelled
COMMENT_ADDED                Comment added
```

### BodyFormat

Initial values:

```text
MARKDOWN    Markdown
HTML        HTML
```

## Main tables

## Task

```text
Id
Title
Body nullable
BodyFormatId nullable
TaskTypeId
TaskStatusId
TaskPriorityId nullable
TaskSourceId nullable
SourceReference nullable
SourceUrl nullable
Deadline nullable
CreatedAt
UpdatedAt
ActivatedAt nullable
WaitingSince nullable
CompletedAt nullable
CancelledAt nullable
```

Notes:

- `Title` is required.
- `TaskTypeId` is required.
- `TaskStatusId` is required.
- New tasks start with status code `ACTIVE`.
- `BodyFormatId` is controlled by the editor.
- `TaskSourceId` is optional.
- `SourceUrl` is information only. No automatic opening behavior.
- `WaitingSince` is set only while the task has an active wait target.

## TaskWaitingFor

One task can have at most one active wait target.

```text
Id
TaskId
Label
WaitingSince
ResolvedAt nullable
CreatedAt
UpdatedAt
```

Rules:

- `Label` stores the full "waiting for" text, such as `INC123456`.
- A wait target does not need to exist in any other table.
- If `ResolvedAt` is null, the wait target is active.
- Enforce at most one active wait target per task.
- Adding an active wait target keeps task status `ACTIVE` and sets `Task.WaitingSince`.
- Clearing an active wait target sets `ResolvedAt`, clears `Task.WaitingSince`, and sets task status to `ACTIVE`.
- Do not add waiting type, URL, follow-up date, or other structured waiting fields in the first version.

Suggested SQLite uniqueness rule:

```text
Unique active wait target per task:
TaskId where ResolvedAt is null
```

In EF Core/SQLite this may be implemented as a filtered unique index if supported by the provider/version.

## TaskComment

Human-written notes.

```text
Id
TaskId
CommentText
CreatedAt
UpdatedAt nullable
```

Comments are separate from automatic logs.

## TaskLogEntry

Automatic factual history.

```text
Id
TaskId
TaskLogTypeId
Message
OldValue nullable
NewValue nullable
CreatedAt
```

Notes:

- Store readable message for the timeline.
- Store old/new values where useful.
- Logs should be append-only in normal application use.

Example messages:

```text
Task created
Priority changed from Normal to Urgent
Waiting for changed to ServiceDesk INC123456
Waiting for ServiceDesk INC123456 was cleared
Task completed
```

## TaskChecklistItem

```text
Id
TaskId
Text
SortOrder
IsCompleted
CompletedAt nullable
CreatedAt
UpdatedAt
```

Checklist items are lightweight and should not have their own priority/status/deadline/attachments/comments in the first version.

## TaskAttachment

Attachments stored in SQLite as BLOBs.

```text
Id
TaskId
FileName
ContentType nullable
FileSize
Sha256Hash nullable
AttachmentKindId nullable
ContentBlob
Description nullable
CreatedAt
```

Notes:

- Consider a soft max file size, for example 25–50 MB.
- Store hash to detect duplicate attachments.

## TaskTag

```text
Id
Value
```

`Value` is required and unique case-insensitively. Tags have no additional metadata.

## TaskTaskTag

```text
TaskId
TaskTagId
```

Composite key: `TaskId, TaskTagId`.

## TaskRelation

```text
Id
SourceTaskId
TargetTaskId
TaskRelationTypeId
Note nullable
CreatedAt
```

Rules:

- Source and target must not be the same task.
- UI should show both forward and reverse relationships using `Name` and `ReverseName`.
- Relationships are mainly navigational in the first version.
- `BLOCKS` and `DEPENDS_ON` may later affect sorting.

## Suggested EF Core entity names

```text
TaskItem
TaskType
TaskStatus
TaskPriority
TaskSource
TaskWaitingFor
TaskComment
TaskLogEntry
TaskLogType
TaskChecklistItem
TaskAttachment
AttachmentKind
TaskTag
TaskTaskTag
TaskRelation
TaskRelationType
BodyFormat
```

Use `TaskItem` instead of `Task` as the C# entity name to avoid confusion with `System.Threading.Tasks.Task`.

## Lifecycle service rules

Implement lifecycle behavior in a service, not scattered across UI code.

Suggested service operations:

```text
CreateTask
AddWaitingFor
ClearWaitingFor
CompleteTask
ReopenTask
CancelTask
ChangePriority
ChangeType
ChangeDeadline
```

Each operation should:

- Update the relevant task fields.
- Update timestamps.
- Add automatic log entries.

Rules:

```text
CreateTask:
- Status = ACTIVE
- CreatedAt = now
- ActivatedAt = now
- UpdatedAt = now
- Log: Task created

AddWaitingFor:
- Create active TaskWaitingFor
- Task.Status remains ACTIVE
- Task.WaitingSince = now
- UpdatedAt = now
- Log: Waiting for changed to ...

ClearWaitingFor:
- Set TaskWaitingFor.ResolvedAt = now
- Task.Status = ACTIVE
- Task.WaitingSince = null
- UpdatedAt = now
- Log: Waiting for ... was cleared
- Log: Status changed from Waiting to Active

CompleteTask:
- Status = COMPLETED
- CompletedAt = now
- UpdatedAt = now
- Optionally resolve active wait target
- Log: Task completed

ReopenTask:
- Status = ACTIVE
- CompletedAt = null
- CancelledAt = null
- UpdatedAt = now
- Log: Task reopened

CancelTask:
- Status = CANCELLED
- CancelledAt = now
- UpdatedAt = now
- Log: Task cancelled
```

## Startup lookup seeding

Initial values should live in configuration files.

Startup rule:

```text
For each lookup table:
    If table is empty:
        insert initial values from config
    Else:
        do nothing
```

Do not synchronize config into populated tables.

Reason:

- First run gets useful defaults.
- User customizations are protected.
- Config updates do not unexpectedly overwrite local changes.

## Example lookup seed configuration shape

```json
{
  "LookupSeed": {
    "TaskTypes": [
      {
        "code": "CRITICAL_ERROR",
        "name": "Critical error",
        "sortOrder": 10,
        "isSystem": false
      },
      {
        "code": "ERROR",
        "name": "Error",
        "sortOrder": 20,
        "isSystem": false
      }
    ],
    "TaskStatuses": [
      {
        "code": "ACTIVE",
        "name": "Active",
        "sortOrder": 10,
        "isSystem": true
      },
      {
        "code": "COMPLETED",
        "name": "Completed",
        "sortOrder": 20,
        "isSystem": true
      },
      {
        "code": "CANCELLED",
        "name": "Cancelled",
        "sortOrder": 30,
        "isSystem": true
      }
    ]
  }
}
```
