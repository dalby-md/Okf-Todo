---
type: Database Schema Lifecycle
title: Database Schema Lifecycle
description: Explains fresh-database creation and development reset behavior.
resource: Okf-Todo/Program.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-13T00:00:00Z
---

# Database Schema Lifecycle

The application calls EF Core `Database.EnsureCreated()` at startup. Migrations and upgrades of old schemas are intentionally out of scope.

During development, delete `%LOCALAPPDATA%\Okf-Todo\okf-todo.db` explicitly when a model change requires a fresh database. Builds and normal startup must not delete it automatically.

EF Core mappings and current source define intended structure. A live database may lag the source after a design change and is evidence of deployed state, not the authority for intended state.
