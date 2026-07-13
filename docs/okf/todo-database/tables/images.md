---
type: SQLite Table
title: Images
description: Stores issue and task image bytes with exactly one owning record.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-13T00:00:00Z
---


# Images

## Purpose

Stores issue and task image bytes with exactly one owning record.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `IssueId` | `INTEGER` | Yes | `-` | foreign key to Issues.Id |
| `TaskId` | `INTEGER` | Yes | `-` | foreign key to TaskItems.Id |
| `Filename` | `TEXT` | Yes | `-` | value |
| `MimeType` | `TEXT` | No | `-` | value |
| `Width` | `INTEGER` | Yes | `-` | value |
| `Height` | `INTEGER` | Yes | `-` | value |
| `ImageData` | `BLOB` | No | `-` | value |
| `CreatedUtc` | `TEXT` | No | `-` | value |

## Relationships

- `IssueId` references [Issues](issues.md).`Id`; delete `CASCADE`, update `NO ACTION`.
- `TaskId` references [TaskItems](task-items.md).`Id`; delete `CASCADE`, update `NO ACTION`.

## Indexes

- `IX_Images_IssueId` on `IssueId`: non-unique.
- `IX_Images_TaskId` on `TaskId`: non-unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.
- Check `CK_Images_OneOwner` enforces `(IssueId IS NOT NULL AND TaskId IS NULL) OR (IssueId IS NULL AND TaskId IS NOT NULL)`.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
