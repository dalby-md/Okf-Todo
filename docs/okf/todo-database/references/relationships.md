---
type: Database Relationships
title: Database Relationships
description: Summarizes the foreign-key graph and delete behavior.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-13T00:00:00Z
---

# Database Relationships

- [Images](../tables/images.md).`IssueId` -> [Issues](../tables/issues.md).`Id`; delete `CASCADE`.
- [Images](../tables/images.md).`TaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `CASCADE`.
- [TaskAttachments](../tables/task-attachments.md).`TaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `CASCADE`.
- [TaskChecklistItems](../tables/task-checklist-items.md).`TaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `CASCADE`.
- [TaskComments](../tables/task-comments.md).`TaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `CASCADE`.
- [TaskItems](../tables/task-items.md).`BodyFormatId` -> [BodyFormats](../tables/body-formats.md).`Id`; delete `RESTRICT`.
- [TaskItems](../tables/task-items.md).`TaskPriorityId` -> [TaskPriorities](../tables/task-priorities.md).`Id`; delete `RESTRICT`.
- [TaskItems](../tables/task-items.md).`TaskSourceId` -> [TaskSources](../tables/task-sources.md).`Id`; delete `RESTRICT`.
- [TaskItems](../tables/task-items.md).`TaskStatusId` -> [TaskStatuses](../tables/task-statuses.md).`Id`; delete `RESTRICT`.
- [TaskItems](../tables/task-items.md).`TaskTypeId` -> [TaskTypes](../tables/task-types.md).`Id`; delete `RESTRICT`.
- [TaskLogEntries](../tables/task-log-entries.md).`TaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `CASCADE`.
- [TaskLogEntries](../tables/task-log-entries.md).`TaskLogTypeId` -> [TaskLogTypes](../tables/task-log-types.md).`Id`; delete `RESTRICT`.
- [TaskRelations](../tables/task-relations.md).`SourceTaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `CASCADE`.
- [TaskRelations](../tables/task-relations.md).`TargetTaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `RESTRICT`.
- [TaskRelations](../tables/task-relations.md).`TaskRelationTypeId` -> [TaskRelationTypes](../tables/task-relation-types.md).`Id`; delete `RESTRICT`.
- [TaskTaskTags](../tables/task-task-tags.md).`TaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `CASCADE`.
- [TaskTaskTags](../tables/task-task-tags.md).`TaskTagId` -> [TaskTags](../tables/task-tags.md).`Id`; delete `CASCADE`.
- [TaskWaitingFors](../tables/task-waiting-fors.md).`TaskId` -> [TaskItems](../tables/task-items.md).`Id`; delete `CASCADE`.
