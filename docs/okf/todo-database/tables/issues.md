---
type: SQLite Table
title: Issues
description: Stores the legacy rich-text issue records.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---


# Issues

## Purpose

Stores the legacy rich-text issue records.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `Title` | `TEXT` | No | `-` | value |
| `Status` | `TEXT` | No | `-` | value |
| `Priority` | `INTEGER` | No | `-` | value |
| `DueUtc` | `TEXT` | Yes | `-` | value |
| `CreatedUtc` | `TEXT` | No | `-` | value |
| `ModifiedUtc` | `TEXT` | No | `-` | value |
| `BodyHtml` | `TEXT` | No | `-` | value |
| `BodyMarkdown` | `TEXT` | No | `-` | value |
| `EditorMode` | `TEXT` | No | `-` | value |

## Relationships

No foreign keys originate from this table.

## Indexes

No secondary indexes are defined.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
