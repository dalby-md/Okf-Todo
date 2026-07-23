---
type: SQLite Table
title: TaskRelations
description: Stores directed typed relationships between distinct tasks.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---


# TaskRelations

## Purpose

Stores directed typed relationships between distinct tasks.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `SourceTaskId` | `INTEGER` | No | `-` | foreign key to TaskItems.Id |
| `TargetTaskId` | `INTEGER` | No | `-` | foreign key to TaskItems.Id |
| `TaskRelationTypeId` | `INTEGER` | No | `-` | foreign key to TaskRelationTypes.Id |
| `Note` | `TEXT` | Yes | `-` | value |
| `CreatedAt` | `TEXT` | No | `-` | value |

## Relationships

- `SourceTaskId` references [TaskItems](task-items.md).`Id`; delete `CASCADE`, update `NO ACTION`.
- `TargetTaskId` references [TaskItems](task-items.md).`Id`; delete `RESTRICT`, update `NO ACTION`.
- `TaskRelationTypeId` references [TaskRelationTypes](task-relation-types.md).`Id`; delete `RESTRICT`, update `NO ACTION`.

## Indexes

- `IX_TaskRelations_SourceTaskId` on `SourceTaskId`: non-unique.
- `IX_TaskRelations_TargetTaskId` on `TargetTaskId`: non-unique.
- `IX_TaskRelations_TaskRelationTypeId` on `TaskRelationTypeId`: non-unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.
- Check `CK_TaskRelations_SourceTarget_Different` enforces `SourceTaskId <> TargetTaskId`.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
