---
type: SQLite Table
title: BodyFormats
description: Defines selectable body formats referenced by tasks.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---


# BodyFormats

## Purpose

Defines selectable body formats referenced by tasks.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `Code` | `TEXT` | No | `-` | value |
| `Name` | `TEXT` | No | `-` | value |
| `Description` | `TEXT` | Yes | `-` | value |
| `BackgroundColor` | `TEXT` | Yes | `-` | value |
| `ForegroundColor` | `TEXT` | Yes | `-` | value |
| `IsSelected` | `INTEGER` | No | `0` | value |
| `SortOrder` | `INTEGER` | No | `-` | value |
| `IsActive` | `INTEGER` | No | `-` | value |
| `IsSystem` | `INTEGER` | No | `-` | value |
| `CreatedAt` | `TEXT` | No | `-` | value |
| `UpdatedAt` | `TEXT` | No | `-` | value |

## Relationships

No foreign keys originate from this table.

## Indexes

- `IX_BodyFormats_Code` on `Code`: unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
