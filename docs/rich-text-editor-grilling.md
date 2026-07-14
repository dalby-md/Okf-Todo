# Rich Text Editor Architecture Grilling

Date: 2026-07-02

Scope: Photino.NET desktop document editor with static HTML/CSS/jQuery UI, TinyMCE, SQLite persistence, no ASP.NET, no localhost API, and no external service dependency.
j
## Verdict

The direction is viable, but the current plan has three unresolved architecture risks:

1. `app://image/42` rendering is not proven without either a custom scheme handler or a runtime URL substitution layer.
2. "Copy/Paste from Microsoft Office" is underspecified if TinyMCE must remain LGPL/open-source only. TinyMCE's stronger Office paste support is PowerPaste, which is premium.
3. The current repo uses `Photino.NET.Server` and `Console.WriteLine`; the plan says no HTTP server and the user instructions prefer logger syntax.

The plan should be tightened before implementation by making image resolution, bridge protocol, Office paste expectations, and packaging rules explicit.

## Grilling Questions

### Product Scope

- Is this a Word-like editor for rich HTML documents, or a `.docx` replacement?
- Is `.docx` import/export required for the first milestone, or only copy/paste from Office?
- Does "Word-like" mean page layout, headers, footers, print preview, spellcheck, comments, tracked changes, and templates, or only rich text editing controls?
- What is the first useful document lifecycle: create, save, open recent, rename, duplicate, delete, search, export?
- Does the editor need multiple open documents, tabs, or one active document at a time?

### TinyMCE

- Which TinyMCE version is the target: 7 or 8?
- Is premium TinyMCE acceptable? If not, Office paste must be downgraded to "best effort browser/TinyMCE paste behavior."
- Which plugins are required in the first milestone: `image`, `table`, `lists`, `link`, `code`, `autoresize`, `wordcount`, `searchreplace`, `fullscreen`, `paste`?
- Will TinyMCE assets be vendored under `Okf-Todo/wwwroot/tinymce`, or loaded from a package during build?
- How will TinyMCE plugin/theme files be included in single-file publish output?

### No HTTP Server

- Static UI loading must not use `Photino.NET.Server` if "no localhost" is a hard constraint. Will the app use `PhotinoWindow.Load("wwwroot/index.html")`?
- If the app uses local files, are relative TinyMCE asset paths reliable across Windows publish modes?
- Does Photino.NET expose a custom scheme handler suitable for `app://image/{id}` on all target platforms? If not, do not store active editor `src` attributes as `app://`.
- Are `file://`, `blob:`, and `data:` URLs acceptable inside the editor runtime if persisted HTML is normalized before save?

### Image Storage

- Do images belong to exactly one document, or can documents share images?
- If `DocumentId` is optional, what prevents orphaned images from accumulating?
- Is image deletion immediate, soft-deleted, or garbage-collected by scanning saved HTML?
- Are duplicate images deduplicated by hash?
- What is the max supported image size, and should large images be recompressed or rejected?
- Are width and height measured from original bytes, TinyMCE-rendered dimensions, or saved display attributes?
- Do pasted clipboard images preserve original MIME type, or are they converted to PNG/WebP?

### Persistence

- Is autosave required? If yes, what debounce and dirty-state model?
- Is undo/redo only TinyMCE session undo, or persistent document history?
- Are saves atomic? A document save with new images needs one SQLite transaction.
- What happens if image insertion succeeds but document save fails?
- Should the database have a schema version and migrations from day one?
- Is EF Core worth it here, or is `Microsoft.Data.Sqlite` simpler for a small local schema?

### Bridge Protocol

- Is the Photino bridge synchronous or asynchronous from the editor's point of view?
- What is the message envelope? Example: `{ "id": "...", "type": "image.save", "payload": {} }`.
- How are errors returned to JavaScript?
- How are large image payloads passed: base64 in one message, chunked messages, or temporary browser blob URLs?
- What is the maximum bridge payload size in practice?
- How are concurrent requests correlated?

### Security And Sanitization

- If HTML is canonical, what HTML is allowed?
- Are `<script>`, event attributes, external images, remote CSS, and iframes stripped?
- Are local file links allowed?
- Should hyperlinks open externally only after confirmation?
- Should remote images be blocked, imported into SQLite, or allowed as remote references?

### Packaging

- "Single executable" conflicts with WebView2 runtime reality on Windows unless WebView2 is assumed installed or packaged separately.
- "Single SQLite database" does not mean "single file application"; where is the DB stored?
- Should user data live under `%AppData%`, `%LocalAppData%`, or next to the executable?
- How are backups handled?

## Revised Architecture

Persisted document HTML should stay stable and portable:

```html
<h1>Meeting Notes</h1>
<p>This is today's meeting.</p>
<img src="app://image/42" data-image-id="42">
```

Runtime editor HTML should not depend on `app://` until Photino custom scheme support has been proven:

```html
<img src="blob:runtime-url" data-image-id="42">
```

Recommended flow:

1. Load document HTML from SQLite.
2. Parse HTML in JavaScript.
3. Replace each `app://image/{id}` with a runtime `blob:` URL fetched through the Photino message bridge.
4. Keep `data-image-id` on each image element.
5. For pasted or dropped images, use TinyMCE's image upload handler to send bytes to C# through the bridge.
6. C# stores the image in SQLite and returns `{ imageId, runtimeUrlOrBase64 }`.
7. Before save, normalize editor HTML so image `src` values become `app://image/{id}` again.
8. Save document HTML and image references in one SQLite transaction.

This preserves the stated canonical format without requiring an HTTP endpoint.

## ADR-001: HTML Is The Canonical Document Format

Status: Accepted

Decision: Store TinyMCE-produced HTML as the canonical document body. Do not convert to Markdown internally.

Reason: TinyMCE is an HTML editor. Tables, inline styles, image sizing, lists, and Office paste artifacts map more directly to HTML than Markdown.

Consequence: The app needs sanitization, schema evolution for embedded references, and export-specific converters later.

## ADR-002: No ASP.NET, REST, IIS, Or Localhost API

Status: Accepted with implementation correction

Decision: JavaScript and C# communicate through the Photino message bridge only.

Reason: The app is a desktop app, not a local web app.

Consequence: The current `Photino.NET.Server` dependency and static file server pattern should be removed or explicitly kept only if "no localhost" is relaxed.

## ADR-003: Images Are Stored In SQLite

Status: Accepted

Decision: Store image bytes in SQLite BLOBs, with metadata in an `Images` table. Persist document references as stable image IDs.

Reason: This supports offline-first behavior, simplifies backup, and avoids asset folders.

Consequence: The app needs image garbage collection, size limits, transaction boundaries, and runtime URL substitution.

## ADR-004: Runtime Image URLs Are Not Canonical

Status: Proposed

Decision: Use `app://image/{id}` only in persisted HTML. Use `blob:` or `data:` URLs while editing unless a Photino custom scheme spike proves `app://` works reliably.

Reason: Browser engines know how to render `blob:` and `data:` without a server. `app://` requires shell support that is not yet proven.

Consequence: Save/load must normalize image URLs.

## ADR-005: Office Paste Is Best-Effort Without Premium TinyMCE

Status: Proposed

Decision: If the editor uses LGPL/open-source TinyMCE only, define Office paste as "best effort paste of supported browser content." If high-fidelity Word/Excel paste is required, budget for TinyMCE PowerPaste or another import pipeline.

Reason: TinyMCE documentation identifies PowerPaste as the enhanced/premium path for Microsoft Office paste cleanup.

Consequence: The MVP acceptance criteria must avoid promising premium paste fidelity unless the dependency choice changes.

## ADR-006: Use EF Core With Versioned Migrations

Status: Proposed

Decision: Use EF Core for relational mapping and version the SQLite schema with migrations from `InitialCreate` onward.

Reason: User data must survive application schema upgrades, and the implemented model has enough relationships and integrity rules to benefit from EF Core mappings and migration history.

Consequence: Apply pending migrations before application data access and include a reviewed migration with every physical schema change.

## Suggested Schema

```sql
CREATE TABLE Documents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL,
    Html TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    ModifiedUtc TEXT NOT NULL
);

CREATE TABLE Images (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NULL,
    Filename TEXT NULL,
    MimeType TEXT NOT NULL,
    Width INTEGER NULL,
    Height INTEGER NULL,
    ByteLength INTEGER NOT NULL,
    Sha256 TEXT NULL,
    ImageData BLOB NOT NULL,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY (DocumentId) REFERENCES Documents(Id) ON DELETE SET NULL
);

CREATE INDEX IX_Images_DocumentId ON Images(DocumentId);
```

## Message Bridge Contract

Use a small JSON-RPC-style envelope:

```json
{
  "id": "request-123",
  "type": "image.create",
  "payload": {
    "documentId": 12,
    "filename": "clipboard.png",
    "mimeType": "image/png",
    "base64": "..."
  }
}
```

Response:

```json
{
  "id": "request-123",
  "ok": true,
  "payload": {
    "imageId": 42,
    "src": "blob:runtime-url"
  }
}
```

Errors:

```json
{
  "id": "request-123",
  "ok": false,
  "error": {
    "code": "image.tooLarge",
    "message": "Image exceeds the configured size limit."
  }
}
```

## MVP Acceptance Criteria

- App starts without ASP.NET, IIS, or a localhost HTTP server.
- Static UI remains under `Okf-Todo/wwwroot`.
- TinyMCE is loaded locally from `wwwroot`.
- User can create, edit, save, close, reopen, and rename one document.
- Saved HTML is persisted in SQLite without Markdown conversion.
- User can paste or drop a PNG/JPEG image.
- Image bytes are stored in SQLite, not on disk.
- Reopened documents display stored images.
- Saved HTML contains `app://image/{id}` references, not `blob:` or base64 image data.
- Tables, lists, links, undo/redo, and standard keyboard shortcuts work through TinyMCE.
- Office paste behavior is documented as best effort unless premium paste support is accepted.

## Glossary

Canonical HTML: The saved document body stored in SQLite.

Runtime HTML: The HTML currently loaded into TinyMCE. It may contain temporary `blob:` or `data:` URLs.

Image ID: Stable database identifier for an image row.

Runtime image URL: A temporary browser-renderable URL used while editing.

Message bridge: Photino's JavaScript-to-C# communication channel.

Document service: C# service that owns document save/load behavior and transaction boundaries.

Image service: C# service that stores, retrieves, normalizes, and garbage-collects image BLOBs.

Office paste: Content pasted from Microsoft Word, Excel, Outlook, or similar Office apps.

PowerPaste: TinyMCE premium plugin for enhanced paste cleanup from Office and similar sources.

## Implementation Order

1. Remove or isolate `Photino.NET.Server` and load static UI without localhost.
2. Add structured logging and dependency injection in `Program.cs`.
3. Define the bridge envelope and a JavaScript request/response helper.
4. Add SQLite initialization and repository/service boundaries.
5. Integrate local TinyMCE from `wwwroot/tinymce`.
6. Implement document save/load with plain HTML.
7. Implement image create/read through the bridge.
8. Implement load/save HTML normalization for image refs.
9. Add image garbage collection for unreferenced rows.
10. Add export/import only after the native editing model is stable.
