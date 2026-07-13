---
type: SQLite Table
title: TaskAttachments
description: Stores task-owned attachment metadata and content BLOBs.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-13T00:00:00Z
---


# TaskAttachments

## Purpose

Stores task-owned attachment metadata and content BLOBs.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `TaskId` | `INTEGER` | No | `-` | foreign key to TaskItems.Id |
| `FileName` | `TEXT` | No | `-` | value |
| `ContentType` | `TEXT` | Yes | `-` | value |
| `FileSize` | `INTEGER` | No | `-` | value |
| `Sha256Hash` | `TEXT` | Yes | `-` | value |
| `ContentBlob` | `BLOB` | No | `-` | value |
| `Description` | `TEXT` | Yes | `-` | value |
| `CreatedAt` | `TEXT` | No | `-` | value |

## Relationships

- `TaskId` references [TaskItems](task-items.md).`Id`; delete `CASCADE`, update `NO ACTION`.

## Indexes

- `IX_TaskAttachments_TaskId` on `TaskId`: non-unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
