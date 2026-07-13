---
type: SQLite Table
title: TaskChecklistItems
description: Stores ordered checklist items owned by tasks.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-13T00:00:00Z
---


# TaskChecklistItems

## Purpose

Stores ordered checklist items owned by tasks.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `TaskId` | `INTEGER` | No | `-` | foreign key to TaskItems.Id |
| `Text` | `TEXT` | No | `-` | value |
| `SortOrder` | `INTEGER` | No | `-` | value |
| `IsCompleted` | `INTEGER` | No | `-` | value |
| `CompletedAt` | `TEXT` | Yes | `-` | value |
| `CreatedAt` | `TEXT` | No | `-` | value |
| `UpdatedAt` | `TEXT` | No | `-` | value |

## Relationships

- `TaskId` references [TaskItems](task-items.md).`Id`; delete `CASCADE`, update `NO ACTION`.

## Indexes

- `IX_TaskChecklistItems_TaskId` on `TaskId`: non-unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
