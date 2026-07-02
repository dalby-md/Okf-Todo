# AGENTS.md Instructions

Respond to the user in English by default.

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
