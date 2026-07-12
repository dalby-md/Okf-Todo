# PRD — Local Developer/Internal Support Task System

## Purpose

Build a personal local task handling system tilted toward developer/internal support work.

The system is not intended to become a full Jira/TFS/ServiceDesk clone. It should be fast, local, practical, and optimized for capturing and tracking the kind of messy work that comes from development, support, deployment, debugging software, ServiceDesk cases, emails, and similar sources.

The app already has a Photino prototype demonstrating usage of an HTML/Markdown editor. The editor should be reused or evolved rather than replaced unnecessarily.

## Product principles

- Local-first personal system.
- Fast task capture.
- Very few required fields.
- Structured enough for sorting, filtering, and history.
- Avoid unnecessary multi-user concepts.
- Prefer table-based lookup values over hardcoded enums.
- Use configuration only to seed initial lookup values when tables are empty.
- Do not overwrite user-customized lookup values after initial seeding.
- Deactivate lookup values when they have been used.
- Allow hard deletion only for non-system lookup values that have not been used.
- Store attachments in SQLite as BLOBs.
- Keep integrations out of the first version unless explicitly requested later.
- The task editor decides whether the body is Markdown or HTML; the user should not have to care. User preference should be persisted
- Permanent delete actions require confirmation in an application HTML dialog; do not use the browser-native confirmation dialog.

## Target platform

- Personal local desktop application.
- Existing prototype is based on Photino.
- Modern .NET / C#.
- SQLite database.
- HTML/Markdown-capable editor.
- Local database file should be easy to back up.

## Core task attributes

Each task should support:

- Title
- Free text body, stored as HTML or Markdown
- Attachments
- Comments (merged into log entries)
- Automatic log entries
- Priority
- Waiting target
- Task status
- Task type
- Deadline
- Zero or more string-only tags
- Completed state/timestamp
- Checklist items
- Task relationships
- Optional source

## Required fields

Only these should be required in the first version:

- Title
- Task type

Everything else should be optional.

A newly created task starts with status `ACTIVE`.

## Task body

The task body is free text.

The user should not be forced to choose Markdown or HTML directly. The editor decides the format and the app stores the selected format with the content.

Store:

- `Body`
- `BodyFormatId`

Initial body formats:

- Markdown
- HTML

## Task type vs task status

Task type and task status are separate concepts.

Task type answers:

> What kind of task is this?

Initial task types:

- Critical error
- Error
- Request
- Idea
- Note
- Investigation
- Improvement

Task status answers:

> Where is this task in its lifecycle?

Initial task statuses:

- Active
- Completed
- Cancelled

## Priority

Initial priorities:

- Urgent
- Normal
- Can wait

Priority affects sorting and filtering.

## Lifecycle

Use the following lifecycle in the first version:

```text
Active
Completed
Cancelled
```

Rules:

```text
Create task          => Active
Add wait target      => Active with waiting target
Clear wait target    => Active without waiting target
Complete task        => Completed
Reopen task          => Active
Cancel task          => Cancelled
```

Automatic log entries must be created for lifecycle changes.

## Lifecycle timestamps

Store timestamps directly on the task for fast querying:

- CreatedAt
- UpdatedAt
- ActivatedAt
- WaitingSince
- CompletedAt
- CancelledAt

`WaitingSince` is only set while the task currently has an active wait target.

## Waiting target

Each task can have at most one active wait target.

The wait target is important enough to affect task visibility and emphasis, while the task remains active.

When a wait target is added:

- Task status remains `ACTIVE`.
- `WaitingSince` is set.
- Automatic log entries are created.

When a wait target is cleared:

- The wait target gets `ResolvedAt`.
- Task status changes to `ACTIVE`.
- `WaitingSince` is cleared.
- Automatic log entries are created.

Waiting for is a simple text field. The user must be able to enter direct text, for example:

```text
INC123456
```

It should not be necessary to register the wait target elsewhere before it can be used.

Do not add waiting type, URL, follow-up date, or other structured waiting fields in the first version.

## Source

A task can optionally have a source.

Source answers:

> Where did this task come from?

Source is classification/reference only. It should not trigger automatic opening behavior or integrations.

Store:

- SourceId nullable
- SourceReference nullable
- SourceUrl nullable

Initial sources:

- Manual
- ServiceDesk
- Email
- Teams
- Deployment
- Monitoring/logs
- User report

Examples:

```text
Source: ServiceDesk
SourceReference: INC123456
```

```text
Source: TFS / Azure DevOps
SourceReference: Release #1842
```

Source is not the same as waiting target.

Source fields are hidden in task details by default. User preferences can show them, and the choice persists across application restarts.

Example:

```text
Source: Email from Anna
Waiting for: ServiceDesk INC123456
```

## Comments and automatic logs

Comments and logs are separate concepts.

Comments are human-written notes.

Logs are automatic factual history.

Example timeline:

```text
2026-07-03 12:15  Auto     Task created
2026-07-03 12:22  Comment  Looks related to the release variable replacement script.
2026-07-03 12:40  Auto     Priority changed from Normal to Urgent
2026-07-03 12:45  Auto     Waiting for changed to ServiceDesk INC123456
2026-07-05 09:10  Auto     Waiting for ServiceDesk INC123456 was cleared
2026-07-05 10:30  Auto     Task completed
```

Logs should store both:

- Readable message
- Structured old/new values where useful

New tasks log only `Task created`. For an existing task, every changed field must create a log entry. Fields with dedicated lifecycle logs keep those messages. Other fields use `Field: Changed 'old value' to 'new value'`. Editor body or format changes use only `Editor changed`. Tag changes log the old and new tag lists.

## Checklist items

Tasks can have lightweight checklist items.

Checklist items are not full tasks.

Checklist items should not have their own priority, status, deadline, attachments, or comments in the first version.

A checklist item should support:

- Text
- Sort order
- IsCompleted
- CompletedAt
- CreatedAt

Example:

```text
Task: Fix failed deployment

Checklist:
[ ] Check build artifact
[ ] Compare appsettings.json
[ ] Verify release variables
[ ] Run console app manually
[ ] Update deployment note
```

The task list shows progress when a task has checklist items:

```text
Fix failed deployment    3/5 done
```

The task editor supports adding, editing, deleting, reordering, completing, and reopening checklist items. Added, completed, and reopened items create automatic timeline logs. The checklist appears above attachments and the timeline.

## Attachments

Attachments are stored in SQLite as BLOBs.

Reason:

- Single portable database file.
- Easy backup.
- No broken file paths.
- Simpler local deployment.
- Simpler export/import later.

Recommended attachment fields:

- FileName
- ContentType
- FileSize
- Sha256Hash
- ContentBlob
- Description
- CreatedAt

A soft size limit should be considered, for example 25–50 MB per attachment.

The initial UI supports adding, downloading, and removing attachments. Attachments are limited to 25 MB and appear above the task timeline.

## Tags

A task can have zero or more tags. Each tag is only a string expression with no color, order, activation state, or other metadata.

Entering a new value creates the tag and attaches it to the task. Removing a tag chip detaches it from the task.

User preferences provide tag administration. A tag value can be renamed. An unused tag can be permanently deleted. A used tag can be merged into another tag; every task association moves to the target tag without duplicates, and the source tag is deleted.

## Task relationships

Tasks can be related to other tasks.

Use a flexible relation table instead of hardcoding columns on `Task`.

Initial relation types:

- Blocks / Blocked by
- Depends on / Required by
- Duplicate of / Has duplicate
- Related to / Related to
- Created from / Created task
- Follow-up to / Has follow-up

Relationships are mainly for navigation and overview in the first version.

The task editor supports adding and removing relationships, shows the correct forward or reverse name, and navigates directly to the related task. Duplicate and self relationships are rejected. Relationship removal uses the application HTML confirmation dialog.

The relationships section is hidden in task details by default. User preferences can show it, and the choice persists across application restarts.

Only `Blocks` / `Depends on` may affect sorting later.

## Owner/responsible person

Do not add owner/responsible person in the first version.

This is a personal local system, so ownership is implicit:

```text
Owner = me
```

## Lookup values

All controlled values should be table-based, not hardcoded as enums.

The initial values should come from configuration files.

Startup rule:

```text
If lookup table is empty:
    insert initial values from configuration
Else:
    leave table unchanged
```

This gives good initial defaults while allowing local customization.

Lookup values should be editable in the app UI.

Lookup values should normally be deactivated instead of deleted.

Hard deletion is allowed only when all of these are true:

- The lookup value is not a system value.
- The lookup value is not referenced by any task or history row.
- Deleting it will not break application logic.

Used lookup values must remain in the database and can only be deactivated.

System-critical lookup rows should be protected:

- Code should not be editable in the normal UI.
- IsSystem should not be editable in the normal UI.
- System rows should not be deactivated if application logic depends on them.

The display name can be edited.

Example:

```text
Code: ACTIVE
Name: Active
```

The user may rename `Active` to `Open`, but the code remains `ACTIVE`.

## Lookup management UI

Add a settings/admin area for editable lookup values:

- Task types
- Statuses
- Priorities

Task sources, relationship types, body formats, and log types are system-managed in the first version and are not editable in the preferences UI.

## First useful views

Suggested first views:

```text
Active tasks
Urgent active tasks
Waiting tasks
Overdue tasks
Completed tasks
All tasks
```

Cancelled tasks appear only in `All tasks`, where their titles use red struck-through text and all pills are gray. They do not appear in `Completed tasks`.

## Sorting ideas

Initial sorting can be simple:

```text
1. Overdue tasks
2. Urgent active tasks
3. Active tasks
4. Waiting tasks
5. Can wait
6. Completed hidden by default
```

Waiting tasks should not disappear. They should be easy to review.

## Out of scope for first version

- Multi-user support.
- Authentication.
- Cloud sync.
- Deep integrations with ServiceDesk, TFS/Azure DevOps, Teams, or email.
- Automatic opening behavior for source URLs.
- Advanced workflow states like Resolved, Verified, Closed.
- Hard deletion of lookup values that are system values or already used.
- Checklist items as full tasks.
