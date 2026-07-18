# AGENTS.md Instructions

Respond to the user in English by default.

## Project

This repository contains a personal local developer/internal support task system.

The system is a local-first desktop app based on Photino, SQLite, EF Core, and a static HTML/CSS/JavaScript UI.

## Product Source Of Truth

Read these before implementing product changes:

```text
/docs/PRD.md
/docs/DATA_MODEL.md
/docs/IMPLEMENTATION_PLAN.md
```

Those files are authoritative for product behavior, data model rules, lifecycle behavior, lookup behavior, and implementation sequence.

Keep `AGENTS.md` focused on how Codex should work in this repo. Do not duplicate detailed product requirements here unless they are needed as stable repo operating constraints.

## Help Audience — Hard Rule

`docs/help/okf-layer.md` and `docs/help/mcp-server.md` are end-user guides and are the canonical source for the application's offline Help.

- Write them for a user who wants to use a harness such as Codex or Claude Code to turn customer email, support transcripts, notes, logs, and similar source material into useful artifacts.
- Lead with outcomes, practical workflows, prompt examples, review and approval steps, privacy guidance, and current product boundaries.
- Preserve the draft-review-save-verify workflow: analyze first, let the user review, require explicit approval for writes, and verify saved results.
- Explain OKF as the context layer and MCP as the optional local action bridge. Do not imply that either component is an AI model, email connector, or autonomous ingestion service.
- Do not make schemas, JSON envelopes, CLI commands, source builds, database paths, or other implementation details prerequisites for understanding or using the guides. The OKF entry-file path is the required exception: put the concrete installed and source-checkout `index.md` paths at the top of the OKF guide.
- Keep necessary technical material in clearly marked advanced-reference sections and link to the detailed OKF or command documentation instead of allowing it to dominate the user guidance.
- When changing related implementation details, update these guides without changing their end-user target.
- **Strict synchronization rule:** every change to `docs/help/okf-layer.md` or `docs/help/mcp-server.md` must update the in-program offline Help in the same change. These canonical files must be copied unchanged into build and publish output; never maintain a separately edited Help copy under `wwwroot`.
- After changing either canonical Help file, build the desktop project and verify that the corresponding file under the normal application output `wwwroot/help` directory exactly matches the canonical source. Treat a documentation-only diff without this verification as incomplete. A temporary or alternate validation output does not count as updating the program Help; refresh the normal application output and any publish or installer artifact included in the task.

## Technology Assumptions

- Modern .NET / C#.
- Photino desktop application.
- SQLite.
- EF Core unless the existing implementation has made another explicit choice.
- Dependency injection.
- Windows development environment.
- PowerShell or Windows cmd for command examples.

## Coding Style

- Use dependency injection.
- Do not use `Console.WriteLine` in application code.
- Use logging abstractions where logging is needed.
- Keep business rules in services, not scattered through UI event handlers.
- Use stable lookup `Code` values for application logic.
- Use lookup `Name` values only for display.
- Prefer readable, boring code over clever abstractions.

## Frontend

- Keep the UI static under `Okf-Todo/wwwroot`.
- Do not add npm, Vite, Vue, React, or a bundler unless explicitly requested.
- Use local `Okf-Todo/wwwroot/js/jquery.min.js`.
- Put app code in `Okf-Todo/wwwroot/js/app.js`.
- Put styles in `Okf-Todo/wwwroot/css/app.css`.

## Photino Startup

- It is expected that the desktop UI loads from `http://localhost:<port>/index.html`; this is the local Photino static file server, not the internet.
- For blank-window diagnostics, distinguish these cases:
  - Static server not ready: no successful `GET /index.html`.
  - WebView navigation hang: server readiness probe succeeds, `PhotinoWindow.Load(...)` logs, but no `GET /index.html?...` follows.
  - Frontend/bridge issue: HTML/CSS/JS load, but no app bridge log or the UI shows a bridge timeout.
- For this app, keep the startup pattern:
  - start `PhotinoServer.CreateStaticFileServer`
  - probe `/index.html`
  - load `index.html?v=<timestamp>` in Photino to force fresh WebView navigation
  - use `ILogger`, not `Console.WriteLine`

## Entity Naming

Use `TaskItem` as the C# entity name instead of `Task` to avoid confusion with `System.Threading.Tasks.Task`.

## Database Migrations

- Apply pending EF Core migrations at application startup before seeding or normal data access.
- Do not use `EnsureCreated()` in application startup.
- Include a reviewed migration and refreshed OKF bundle with every committed physical schema change.
- Treat `InitialCreate` as the earliest supported database version; no pre-migration database compatibility is required.

## OKF Database Context

- After every database-design change, use the repo-local `compile-okf-context` skill.
- Database-design changes include EF entities or mappings, tables, columns, keys, relationships, indexes, constraints, delete behavior, lookup schema, and database initialization rules.
- Regenerate and validate the OKF bundle under `docs/okf/todo-database` in the same change.
- Do not treat a successful application build as proof that the OKF bundle is current.

## Development Approach

- Use small vertical slices.
- Do not implement schema, lifecycle, UI, attachments, tags, stakeholders, and relationships in one change.
- Keep local data portable.
- Avoid introducing integrations before the local core works.

## When Finishing A Task

Report:

- Files changed.
- What was implemented.
- How to run/build/test.
- Any assumptions.
- Any incomplete parts.
