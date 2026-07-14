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

The application calls EF Core `Database.Migrate()` at startup before seeding or normal data access. EF Core creates a missing database and applies every migration not recorded in `__EFMigrationsHistory`.

`InitialCreate` is the earliest supported database version. Every future physical schema change must include a reviewed migration. Builds and normal startup must not delete the database automatically.

EF Core mappings, the model snapshot, and committed migrations define intended structure. A live database may lag until startup applies pending migrations and remains evidence of deployed state, not the authority for intended state.
