# Implementation Plan — Local Developer/Internal Support Task System

## Goal

Build the system incrementally from the existing Photino prototype.

Do not ask Codex to build everything in one pass. Use small vertical slices.

## Recommended implementation order

```text
1. Add documentation files
2. Add SQLite / EF Core model
3. Add lookup seeding from configuration
4. Add lifecycle/logging service
5. Add basic task create/edit/list
6. Integrate existing HTML/Markdown editor
7. Add waiting target behavior
8. Add comments and timeline
9. Add checklist items
10. Add string-only multi-value tags
11. Add attachments as SQLite BLOBs
12. Add task relationships
13. Add lookup management UI
14. Add task views and sorting
```

## Milestone 1 — Documentation

Add:

```text
/docs/PRD.md
/docs/DATA_MODEL.md
/docs/IMPLEMENTATION_PLAN.md
/AGENTS.md
```

Purpose:

- Make product decisions explicit.
- Give Codex stable context.
- Reduce repeated explanation.
- Avoid rebuilding the wrong thing.

## Milestone 2 — Data foundation

Scope:

- Add EF Core entities.
- Add SQLite DbContext.
- Add migrations or database creation flow.
- Add lookup entities.
- Add initial config seed objects.
- Add startup seeding from config.

Do not build the full UI yet.

Acceptance criteria:

- App starts with SQLite database.
- Empty lookup tables are seeded from config.
- Non-empty lookup tables are not changed.
- Used lookup rows are not hard-deleted.
- Unused non-system lookup rows can be hard-deleted.
- System lookup codes are stable.

Suggested Codex prompt:

```text
Read /docs/PRD.md and /docs/DATA_MODEL.md.

Implement the initial SQLite/EF Core data model for the local task system.

Scope:
- Add lookup tables with common fields: Id, Code, Name, Description, SortOrder, IsActive, IsSystem, CreatedAt, UpdatedAt.
- Add TaskItem, TaskWaitingFor, TaskComment, TaskLogEntry, TaskChecklistItem, TaskAttachment, TaskTag, TaskTaskTag, TaskRelation, TaskRelationType.
- Add startup seeding from configuration: only seed a lookup table if it is empty.
- Do not hard-delete used lookup rows. Use deactivation for values that have existing references.
- Allow hard deletion only for unused non-system lookup rows.
- Do not build UI in this step except what is necessary to compile.

After implementation:
- Show changed files.
- Explain how to create/update the SQLite database.
- Add or update tests where appropriate.
```

## Milestone 3 — Lifecycle and logging service

Scope:

- Add service methods for lifecycle operations.
- Add automatic log entries.
- Add timestamp handling.
- Use stable status codes.

Acceptance criteria:

- Creating a task logs `Task created`.
- Starting a task changes status to `ACTIVE` and logs the transition.
- Adding a wait target keeps status `ACTIVE`, sets `WaitingSince`, and logs the waiting target change.
- Clearing a wait target changes status to `ACTIVE`, clears `WaitingSince`, resolves the wait target, and logs both events.
- Completing a task sets `CompletedAt` and logs completion.
- Reopening a task changes status to `ACTIVE` and logs reopening.
- Cancelling a task sets `CancelledAt` and logs cancellation.

Suggested Codex prompt:

```text
Read /docs/PRD.md and /docs/DATA_MODEL.md.

Implement a lifecycle service for TaskItem.

Rules:
- Create task => ACTIVE
- Add wait target => ACTIVE with waiting target
- Clear wait target => ACTIVE
- Complete task => COMPLETED
- Reopen task => ACTIVE
- Cancel task => CANCELLED

For every state-changing operation:
- Update timestamps.
- Add TaskLogEntry rows.
- Use stable lookup Code values, not display Name values.

Add tests for the lifecycle rules.
Do not build unrelated UI.
```

## Milestone 4 — Basic task UI

Scope:

- Task list.
- Create task.
- Edit task.
- Required fields only:
  - Title
  - Task type
- Optional fields:
  - Priority
  - Deadline
  - Source
  - Source reference
  - Source URL
  - Body

Acceptance criteria:

- User can create a task quickly.
- New task starts as `ACTIVE`.
- User can edit title/body/type/priority/deadline/source.
- Changes update `UpdatedAt`.
- Meaningful changes create log entries where appropriate.

Suggested Codex prompt:

```text
Add a minimal task list and task create/edit screen using the existing Photino application style.

Required fields:
- Title
- Task type

Optional fields:
- Body
- Priority
- Deadline
- Source
- Source reference
- Source URL

Do not implement attachments, checklist items, tags, or relationships in this step.
Use the existing lifecycle/logging service.
```

## Milestone 5 — Editor integration

Scope:

- Integrate existing HTML/Markdown editor prototype with `TaskItem.Body` and `TaskItem.BodyFormatId`.
- The user should not manually choose Markdown/HTML unless the existing editor already exposes that naturally.
- Store the format chosen by the editor.

Acceptance criteria:

- Task body can be edited.
- Body persists to SQLite.
- Body format persists.
- Existing task body loads correctly when editing.

Suggested Codex prompt:

```text
Integrate the existing HTML/Markdown editor prototype into the task edit screen.

Persist:
- TaskItem.Body
- TaskItem.BodyFormatId

The editor should decide whether content is Markdown or HTML.
Do not add attachments or image upload in this step unless already part of the editor and trivial to keep.
```

## Milestone 6 — Waiting target UI

Scope:

- Add one active wait target per task.
- Waiting for is a single text field.
- Direct entry must be possible, for example `INC123456`.

Acceptance criteria:

- Adding wait target keeps task status `ACTIVE` and sets the active waiting target.
- Clearing wait target keeps task status `ACTIVE` and clears the active waiting target.
- Logs are created.
- Only one active wait target is allowed per task.
- Do not add waiting type, URL, follow-up date, or other structured waiting fields.

Suggested Codex prompt:

```text
Add waiting target UI to the task edit screen.

Rules:
- A task can have only one active wait target.
- User can enter direct text/reference such as INC123456.
- Adding a wait target keeps the task ACTIVE and sets the active waiting target.
- Clearing a wait target keeps the task ACTIVE and clears the active waiting target.
- Set/clear WaitingSince.
- Create automatic log entries.
- Do not add waiting type, URL, follow-up date, stakeholder link, or other structured waiting fields.

Use the lifecycle service. Do not duplicate lifecycle logic in UI code.
```

## Milestone 7 — Comments and timeline

Scope:

- Add comments.
- Show combined timeline of comments and logs.

Acceptance criteria:

- User can add comments.
- Automatic logs appear in the same timeline.
- Timeline clearly distinguishes comments from automatic logs.
- Logs are append-only in normal UI.

Suggested Codex prompt:

```text
Add a task timeline showing both TaskComment and TaskLogEntry.

Requirements:
- Comments are human-written.
- Logs are automatic.
- Timeline must visually distinguish Comment vs Auto.
- User can add comments.
- Adding a comment creates a COMMENT_ADDED log entry if that log type exists.
```

## Milestone 8 — Checklist items

Status: implemented.

Scope:

- Add checklist item CRUD.
- Add completed/reopened behavior.
- Add progress indicator.

Acceptance criteria:

- User can add/reorder/check/uncheck checklist items.
- Completed items get `CompletedAt`.
- Reopened items clear `CompletedAt`.
- Task list can show `3/5 done`.

Suggested Codex prompt:

```text
Add checklist items to tasks.

Checklist items are lightweight:
- Text
- SortOrder
- IsCompleted
- CompletedAt
- CreatedAt
- UpdatedAt

Do not make checklist items into full tasks.
Add log entries for checklist item added/completed/reopened.
```

## Milestone 9 — Tags

Scope:

- Add zero or more string-only tags to tasks.
- Use Select2 with tag creation enabled.

Acceptance criteria:

- User can type a new value to create and attach a tag.
- User can select existing tag values.
- User can remove a tag association using the chip's remove control.
- Tags have no metadata beyond their string value.

Suggested Codex prompt:

```text
Add string-only multi-value tags using TaskTag and TaskTaskTag.

Use a Select2 multi-select with `tags: true`. Put it on the same row as Waiting for. Creating text adds a tag association; removing a chip removes that association. Do not add colors, sort order, activation state, or other tag metadata.
```

## Milestone 10 — Attachments

Status: implemented.

Scope:

- Store files in SQLite as BLOBs.
- Add hash.
- Add basic attachment list/download/open-save behavior.

Acceptance criteria:

- User can attach a file.
- File content is stored in SQLite.
- Metadata is stored.
- File can be saved/exported again.
- Add log entries for attachment added/removed.
- No filesystem path dependency.

Suggested Codex prompt:

```text
Add task attachments stored in SQLite as BLOBs.

Fields:
- FileName
- ContentType
- FileSize
- Sha256Hash
- ContentBlob
- Description
- CreatedAt

Do not store only a file path.
Add log entries for attachment added/removed.
Consider a configurable soft file size warning.
```

## Milestone 11 — Task relationships

Status: implemented.

Scope:

- Add task relation CRUD.
- Show forward and reverse relation names.

Acceptance criteria:

- User can relate two tasks.
- Source and target task cannot be the same.
- Relation type controls display name and reverse display name.
- Add log entries for relation added/removed.

Suggested Codex prompt:

```text
Add task relationships.

Use:
- TaskRelation
- TaskRelationType

Support relation types with Name and ReverseName.
Prevent a task from being related to itself.
Show related tasks on the task detail screen.
Add log entries for relation added/removed.
```

## Milestone 12 — Lookup management UI

Status: implemented.

Scope:

- Add settings screens for lookup tables.
- Allow rename, description edit, sort order edit, activation/deactivation.
- Protect system codes.

Acceptance criteria:

- Lookup values can be edited in the app.
- Used lookup values are not hard-deleted.
- Unused non-system lookup values can be hard-deleted.
- System lookup codes cannot be changed in normal UI.
- System values required by lifecycle cannot be deactivated.
- Inactive values are not offered for new selections.

Suggested Codex prompt:

```text
Add lookup management UI.

Requirements:
- Lookup rows are editable.
- Do not allow hard delete for used or system lookup rows.
- Allow hard deletion only for unused non-system lookup rows.
- Allow deactivation for used non-system lookup rows.
- Protect Code and IsSystem for system rows.
- Prevent deactivation of system values required by application logic.
- Inactive values remain visible on existing tasks.
```

## Milestone 13 — Views and sorting

Scope:

Add first useful views:

```text
Active tasks
Urgent active tasks
Waiting tasks
Overdue tasks
Completed tasks
All tasks
```

Suggested sort:

```text
1. Overdue tasks
2. Urgent active tasks
3. Active tasks
4. Waiting tasks
5. Can wait
6. Completed hidden by default
```

Acceptance criteria:

- Views use lookup codes, not display names.
- Completed tasks are hidden by default in active views.
- Waiting tasks remain easy to find.

## Recommended first real Codex task

Start with:

```text
Implement the database model, lookup seeding from configuration, and lifecycle/logging service.
Do not build the full UI yet.
```

This creates the foundation before the UI grows.

## General implementation guidance

- Keep the first version simple.
- Prefer services for business rules.
- Do not scatter lifecycle rules in UI code.
- Use dependency injection.
- Keep local data portable.
- Avoid introducing integrations before the local core works.
- Use stable lookup `Code` values for application logic.
- Use editable lookup `Name` values for display.
- Use deactivation for lookup values that have existing references.
- Allow hard deletion only for unused non-system lookup values.
