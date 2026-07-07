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
- Do not hard-delete lookup values; use deactivation.
- Store attachments in SQLite as BLOBs.
- Keep integrations out of the first version unless explicitly requested later.
- The task editor decides whether the body is Markdown or HTML; the user should not have to care. User preference should be persisted

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
- Stakeholders
- Comments (merged into log entries)
- Automatic log entries
- Priority
- Waiting target
- Task status
- Task type
- Deadline
- Tags
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

Do not add waiting type, URL, follow-up date, stakeholder link, or other structured waiting fields in the first version.

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

The task list may show progress:

```text
Fix failed deployment    3/5 done
```

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
- AttachmentKindId
- ContentBlob
- Description
- CreatedAt

Initial attachment kinds:

- Screenshot
- Log file
- Document
- Export
- SQL script
- Config file
- Source code
- Other

A soft size limit should be considered, for example 25–50 MB per attachment.

## Stakeholders

Stakeholders should remain separate from tags.

Tags answer:

> What is the task about?

Stakeholders answer:

> Who or what is involved?

Examples of tags:

- Oracle APEX
- TFS
- Power Platform
- Bug
- Deployment
- Security

Examples of stakeholders:

- ServiceDesk
- Peter Hansen
- Payroll team
- External vendor
- TFS build agent

Stakeholders can appear visually as chips, similar to tags.

Initial stakeholder types:

- Person
- Team
- System
- Vendor
- ServiceDesk
- Customer/user
- Other

Initial stakeholder roles:

- Requester
- Affected user
- Helper
- Approver
- Technical contact
- Business contact
- External contact
- Other

Waiting for is deliberately separate from stakeholders. Do not link waiting targets to stakeholders in the first version.

## Tags

Tags are table rows.

Tags should be user-created and editable.

Initial seeded tags may be useful:

- Oracle APEX
- .NET
- Power Platform
- TFS
- SQL Server
- SQLite
- Deployment
- Build
- Bug
- Support
- Security
- Documentation

Tags should support:

- Name
- Color nullable
- SortOrder
- IsActive

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

Only `Blocks` / `Depends on` may affect sorting later.

## Owner/responsible person

Do not add owner/responsible person in the first version.

This is a personal local system, so ownership is implicit:

```text
Owner = me
```

Use stakeholders to track involved people, teams, systems, vendors, or cases.

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

Lookup values must not be hard-deleted. Use deactivation only.

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

Add a settings/admin area for lookup values:

- Task types
- Statuses
- Priorities
- Sources
- Attachment kinds
- Stakeholder types
- Stakeholder roles
- Relation types
- Tags
- Body formats
- Log types

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
- Hard deletion of lookup values.
- Checklist items as full tasks.
