---
type: SQLite Database
title: OKF-Todo SQLite Database
description: Describes the local SQLite database and its ownership boundary.
resource: Okf-Todo/Data/DatabasePathProvider.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---

# OKF-Todo SQLite Database

## Purpose

The application owns one local SQLite database under the operating system's per-user application-data directory. It stores tasks, controlled lookups, history, relationships, tags, attachments, and image BLOBs.

The path is resolved by `DatabasePathProvider` using these platform rules:

- Windows: `%LOCALAPPDATA%\Okf-Todo\okf-todo.db`.
- macOS: `~/Library/Application Support/Okf-Todo/okf-todo.db`.
- Linux: `$XDG_DATA_HOME/Okf-Todo/okf-todo.db` when `XDG_DATA_HOME` is an absolute path; otherwise `~/.local/share/Okf-Todo/okf-todo.db`.

## Contents

The database currently contains 18 physical application tables. Browse them through the [table index](../tables/index.md).

## Integrity

SQLite foreign-key enforcement is enabled in every application connection. See [Database Integrity Rules](../references/integrity-rules.md) and [Database Relationships](../references/relationships.md).

## Lifecycle

See [Database Schema Lifecycle](../references/schema-lifecycle.md).
