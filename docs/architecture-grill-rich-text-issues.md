# Architecture Grill: Rich Text Issue Tracking

## Verdict

The plan is directionally good for an offline-first Photino app. For v1, make pragmatic assumptions that let the app run as a standalone desktop app with pasted and imported images.

The important distinction: static asset loading and application/data communication are separate concerns. Use Photino's static asset hosting for packaged `wwwroot` assets because that matches the current repo and keeps the app standalone. Do not expose business/data operations as REST, ASP.NET, IIS, or dynamic localhost APIs. All issue, image, and database operations go through the Photino message bridge.

## Working Assumptions

1. Static assets

   Keep the UI static under `Okf-Todo/wwwroot`. Vendor TinyMCE under `Okf-Todo/wwwroot/tinymce`. Use `Photino.NET.Server` only as the packaged static asset loader. Do not add dynamic routes to it.

2. No REST

   Do not add ASP.NET, controllers, minimal APIs, IIS, REST endpoints, or dynamic localhost routes. JavaScript calls C# through the Photino message bridge only.

3. Image storage

   Store pasted, dropped, and imported image bytes in SQLite BLOBs. Do not store images as loose files on disk.

4. Image rendering

   Treat `app://image/{id}` as the canonical stored reference. When loading content into TinyMCE, resolve image bytes through `image.get` bridge messages and replace `app://image/{id}` with editor-safe temporary Blob/Object URLs or `data:` URLs. When saving, normalize those temporary URLs back to `app://image/{id}`.

5. Image import

   Support paste, drag/drop, and file-input image import in TinyMCE. Imported image bytes go into SQLite through the same `image.create` bridge message. Defer a native Photino file picker until the static UI path is working.

6. Issue model

   The document schema stores rich HTML, but an issue tracker also needs state. At minimum define:

   - Id
   - Title
   - Status
   - Priority
   - DueUtc optional
   - CreatedUtc
   - ModifiedUtc
   - BodyHtml

   Example statuses: `Open`, `InProgress`, `Blocked`, `Done`, `Archived`.

7. Image ownership

   Images are owned by one issue in v1. Use `IssueId NOT NULL`. This gives simple deletion and garbage collection through cascade deletes.

8. Pasted and dropped image flow

   Required flow:

   1. User pastes or drops an image.
   2. TinyMCE calls the configured image upload handler.
   3. JavaScript sends image bytes and metadata through the Photino bridge.
   4. C# stores the BLOB in SQLite.
   5. C# returns an image id.
   6. JavaScript inserts `app://image/{id}` or a temporary editor URL.

9. Message contract

   Do not send ad hoc strings. Use JSON envelopes with `messageId`, `type`, and `payload`.

   Example request:

   ```json
   {
     "messageId": "5f7d6e3e",
     "type": "issue.save",
     "payload": {
       "id": 42,
       "title": "Meeting notes",
       "bodyHtml": "<h1>Meeting notes</h1>"
     }
   }
   ```

   Example response:

   ```json
   {
     "messageId": "5f7d6e3e",
     "type": "issue.save.result",
     "ok": true,
     "payload": {
       "id": 42,
       "modifiedUtc": "2026-07-02T12:00:00Z"
     }
   }
   ```

   Example error:

   ```json
   {
     "messageId": "5f7d6e3e",
     "type": "issue.save.result",
     "ok": false,
     "error": {
       "code": "ValidationFailed",
       "message": "Title is required.",
       "details": {
         "field": "title"
       }
     }
   }
   ```

10. HTML safety

   Use server-side sanitization in C# with `HtmlSanitizer` from the `Ganss.Xss` package.

   Allow elements needed by TinyMCE v1: `p`, `br`, `strong`, `b`, `em`, `i`, `u`, `s`, `blockquote`, `pre`, `code`, `h1`, `h2`, `h3`, `h4`, `ul`, `ol`, `li`, `a`, `img`, `table`, `thead`, `tbody`, `tr`, `th`, `td`, `colgroup`, `col`, `span`, `div`.

   Allow attributes only where needed: `href`, `target`, `rel` on links; `src`, `alt`, `title`, `width`, `height` on images; `colspan`, `rowspan` on table cells; `style` only for a small allowlist of TinyMCE-generated formatting.

   Allow CSS properties only for editor output that v1 needs: `text-align`, `font-weight`, `font-style`, `text-decoration`, `width`, `height`, `max-width`, `border-collapse`, `border`, `padding`, `vertical-align`.

   Allow URL schemes: `http`, `https`, `mailto` for links; `app` for saved image references. `blob` and `data` image URLs may exist while editing but must be normalized to `app://image/{id}` before save.

   Always strip `script`, `iframe`, event handler attributes such as `onclick`, JavaScript URLs, remote image URLs, and unsanitized inline styles.

11. Database access

   Use EF Core with SQLite. Create and upgrade databases with versioned migrations applied at startup, and enable SQLite foreign-key enforcement explicitly on every connection.

12. Schema changes

   `InitialCreate` is the earliest supported database version. Add a reviewed migration for every future physical schema change and apply pending migrations at startup. Never delete the database automatically during a normal build or application startup.

13. Export

    Do not implement export formats in v1. Remove `ExportService` from the v1 architecture unless a later requirement adds a specific export target.

14. Image file import

    Include image import in the first implementation. Use a static UI file input or TinyMCE image picker that reads a local image in JavaScript and sends bytes to C# through `image.create`. Defer a Photino-native file picker.

## Proposed V1 Scope

Build this first:

- Static jQuery UI under `Okf-Todo/wwwroot`.
- TinyMCE vendored under `Okf-Todo/wwwroot/tinymce`.
- Photino window with no REST API.
- JSON request/response message bridge.
- SQLite database with `Issues` and `Images` created from the current EF Core model.
- HTML body stored as TinyMCE HTML.
- Image paste/drop/import stored as SQLite BLOBs.
- Editor load/save with image reference normalization.

Defer:

- Shared image library.
- Full-text search.
- Multi-window editing.
- Sync or cloud backup.

## Minimum Schema

```sql
CREATE TABLE Issues (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL,
    Status TEXT NOT NULL,
    Priority INTEGER NOT NULL DEFAULT 0,
    BodyHtml TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    ModifiedUtc TEXT NOT NULL
);

CREATE TABLE Images (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    IssueId INTEGER NOT NULL,
    Filename TEXT NULL,
    MimeType TEXT NOT NULL,
    Width INTEGER NULL,
    Height INTEGER NULL,
    ImageData BLOB NOT NULL,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY (IssueId) REFERENCES Issues(Id) ON DELETE CASCADE
);
```

## Message Types

Start with these bridge messages:

- `issue.list`
- `issue.get`
- `issue.create`
- `issue.save`
- `issue.delete`
- `image.create`
- `image.get`
- `app.ready`
- `app.error`

## Bridge Envelope

Requests:

```json
{
  "messageId": "client-generated-id",
  "type": "issue.get",
  "payload": {
    "id": 42
  }
}
```

Successful responses:

```json
{
  "messageId": "client-generated-id",
  "type": "issue.get.result",
  "ok": true,
  "payload": {}
}
```

Failed responses:

```json
{
  "messageId": "client-generated-id",
  "type": "issue.get.result",
  "ok": false,
  "error": {
    "code": "NotFound",
    "message": "Issue was not found.",
    "details": {}
  }
}
```

Use stable error codes: `ValidationFailed`, `NotFound`, `Conflict`, `ImageTooLarge`, `UnsupportedImageType`, `DatabaseUnavailable`, `SchemaInitializationFailed`, `UnexpectedError`.

## Decision Checklist

- [x] Allow Photino static asset loading for packaged `wwwroot` assets.
- [x] Keep REST, ASP.NET, IIS, and dynamic localhost APIs out of the app.
- [x] Resolve `app://image/{id}` through bridge-loaded temporary editor URLs.
- [x] Use `Issues` as the domain model name for v1.
- [x] Use EF Core migrations with SQLite and explicit foreign-key enforcement.
- [x] Keep images in SQLite BLOBs and owned by one issue in v1.
- [x] Do not implement export formats in v1.
- [x] Do not consider Markdown in v1.
- [x] Use a .NET HTML sanitizer with the v1 allowlist documented above.
- [x] Use the JSON bridge envelope documented above.
- [x] Include image file import in v1 through the static UI/TinyMCE path.
- [x] Defer native Photino file picker integration.
