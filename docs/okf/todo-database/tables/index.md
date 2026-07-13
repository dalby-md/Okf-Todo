# SQLite Tables

* [BodyFormats](body-formats.md) - Defines selectable body formats referenced by tasks.
* [Images](images.md) - Stores issue and task image bytes with exactly one owning record.
* [Issues](issues.md) - Stores the legacy rich-text issue records.
* [TaskAttachments](task-attachments.md) - Stores task-owned attachment metadata and content BLOBs.
* [TaskChecklistItems](task-checklist-items.md) - Stores ordered checklist items owned by tasks.
* [TaskComments](task-comments.md) - Stores human-written comments owned by tasks.
* [TaskItems](task-items.md) - Stores the primary task records and lifecycle state.
* [TaskLogEntries](task-log-entries.md) - Stores automatic append-oriented task history entries.
* [TaskLogTypes](task-log-types.md) - Defines stable types for automatic task history entries.
* [TaskPriorities](task-priorities.md) - Defines selectable task priorities.
* [TaskRelationTypes](task-relation-types.md) - Defines forward and reverse labels for task relationships.
* [TaskRelations](task-relations.md) - Stores directed typed relationships between distinct tasks.
* [TaskSources](task-sources.md) - Defines selectable origins for tasks.
* [TaskStatuses](task-statuses.md) - Defines stable task lifecycle statuses.
* [TaskTags](task-tags.md) - Stores case-insensitively unique tag strings.
* [TaskTaskTags](task-task-tags.md) - Associates tasks and tags through a composite primary key.
* [TaskTypes](task-types.md) - Defines selectable task categories.
* [TaskWaitingFors](task-waiting-fors.md) - Stores task wait-target history with at most one active target per task.
