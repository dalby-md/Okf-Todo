---
type: SQLite Database
title: Okf-Todo SQLite Database
description: Describes the local SQLite database and its ownership boundary.
resource: Okf-Todo/Data/DatabasePathProvider.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-13T00:00:00Z
---

# Okf-Todo SQLite Database

## Purpose

The application owns one local SQLite database at `%LOCALAPPDATA%\Okf-Todo\okf-todo.db`. It stores tasks, controlled lookups, history, relationships, tags, attachments, and image BLOBs.

## Contents

The database currently contains 18 physical application tables. Browse them through the [table index](../tables/index.md).

## Integrity

SQLite foreign-key enforcement is enabled in every application connection. See [Database Integrity Rules](../references/integrity-rules.md) and [Database Relationships](../references/relationships.md).

## Lifecycle

See [Database Schema Lifecycle](../references/schema-lifecycle.md).
