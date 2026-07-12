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

