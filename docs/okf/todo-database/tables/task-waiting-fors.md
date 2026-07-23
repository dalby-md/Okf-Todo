---
type: SQLite Table
title: TaskWaitingFors
description: Stores task wait-target history with at most one active target per task.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---


# TaskWaitingFors

## Purpose

Stores task wait-target history with at most one active target per task.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `TaskId` | `INTEGER` | No | `-` | foreign key to TaskItems.Id |
| `Label` | `TEXT` | No | `-` | value |
| `WaitingSince` | `TEXT` | No | `-` | value |
| `ResolvedAt` | `TEXT` | Yes | `-` | value |
| `CreatedAt` | `TEXT` | No | `-` | value |
| `UpdatedAt` | `TEXT` | No | `-` | value |

## Relationships

- `TaskId` references [TaskItems](task-items.md).`Id`; delete `CASCADE`, update `NO ACTION`.

## Indexes

- `IX_TaskWaitingFors_TaskId` on `TaskId`: unique, partial.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
