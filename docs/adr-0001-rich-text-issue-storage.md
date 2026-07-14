# ADR 0001: Rich Text Issue Storage

## Status

Proposed

## Context

The app is a desktop issue tracking tool built with Photino.NET, static HTML/CSS/jQuery, TinyMCE, C#, and SQLite. It must be offline-first and self-contained. It must not depend on ASP.NET, IIS, REST APIs, external web servers, or external services for application behavior.

The current repository serves static files through `Photino.NET.Server`. For v1, keep it as the internal packaged static asset loader for `wwwroot` assets. Do not add business/data operations to it.

## Decision

Store each issue body as canonical TinyMCE HTML in SQLite. Do not convert issue bodies to Markdown internally.

Store images as SQLite BLOBs. Issue HTML references images by stable application identifiers such as:

```html
<img src="app://image/42">
```

Use the Photino message bridge for application operations between JavaScript and C#. Use structured JSON envelopes for every bridge request and response.

Use EF Core with SQLite and create or upgrade database files with versioned migrations. Apply pending migrations at application startup before seeding. Enable foreign-key enforcement explicitly on every SQLite connection. `InitialCreate` is the earliest supported database version; no compatibility path is required for databases created before it.

Do not implement export formats in v1. Do not include Markdown import, export, or internal conversion in v1.

Sanitize issue HTML in C# before saving. Use `HtmlSanitizer` from the `Ganss.Xss` package. The v1 allowlist is limited to TinyMCE editing features: paragraphs, headings, inline emphasis, block quotes, code blocks, lists, links, images, tables, spans/divs, and the small CSS set needed for alignment, image sizing, and tables.

Use this JSON bridge envelope:

```json
{
  "messageId": "client-generated-id",
  "type": "issue.save",
  "payload": {}
}
```

Responses use the same `messageId`, a `.result` type suffix, `ok`, and either `payload` or `error`.

```json
{
  "messageId": "client-generated-id",
  "type": "issue.save.result",
  "ok": false,
  "error": {
    "code": "ValidationFailed",
    "message": "Title is required.",
    "details": {}
  }
}
```

Include image file import in v1 through the static UI/TinyMCE path. Defer native Photino file picker integration.

## Consequences

The editor remains offline-first and self-contained.

HTML rendering preserves TinyMCE features such as tables, lists, image sizing, links, undo/redo state, and rich formatting.

Image rendering needs an explicit resolver. `app://image/{id}` is a storage reference, not automatically a renderable browser URL. On editor load, translate image references to editor-safe temporary Blob/Object URLs or `data:` URLs using image bytes fetched through the message bridge. On save, normalize the editor content back to stable `app://image/{id}` references.

Stored HTML must be sanitized or constrained before save/render. At minimum, block script tags, event handler attributes, unsupported remote images, and unsafe link targets.

## Closed Decisions

- HTML sanitization: use a .NET sanitizer with the v1 TinyMCE allowlist.
- Bridge contract: use `messageId`, `type`, `payload`, `ok`, and structured `error`.
- Image import: include first implementation via static UI/TinyMCE; defer native file picker integration.
