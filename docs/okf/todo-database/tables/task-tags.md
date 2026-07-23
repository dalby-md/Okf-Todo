---
type: SQLite Table
title: TaskTags
description: Stores case-insensitively unique tag strings.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---


# TaskTags

## Purpose

Stores case-insensitively unique tag strings.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `Value` | `TEXT` | No | `-` | value |

## Relationships

No foreign keys originate from this table.

## Indexes

- `IX_TaskTags_Value` on `Value`: unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
