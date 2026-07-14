# ADR 0002: Automatic Database Migrations

## Status

Accepted

## Context

OKF-Todo stores all task data and BLOB content in one local SQLite database. Once users receive the application, schema changes must preserve that data across application upgrades. There are no supported databases from before the initial migration.

## Decision

Use EF Core migrations as the database schema history. `InitialCreate` represents the earliest supported database version.

At startup, call `Database.Migrate()` before lookup seeding, sample-data seeding, or normal application access. EF Core creates a missing database and applies only migrations not recorded in `__EFMigrationsHistory`.

Every committed physical schema change must include a reviewed migration. Do not call `EnsureCreated()` in application startup and do not delete the database automatically.

Disposable tests may continue to use `EnsureCreated()` when they are testing the current EF model rather than migration behavior. Migration-specific integration tests must create a database through `Migrate()`.

## Consequences

- New installations are created from the migration history.
- Existing supported databases are upgraded automatically while retaining data.
- Application startup fails rather than continuing against an incompatible schema when a migration fails.
- Database downgrades are not automatic.
- Databases created before `InitialCreate` are unsupported and require no baseline adoption logic.
