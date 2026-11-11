# Dataset Studio — Architecture

> **Goal:** browse, curate, and edit billion-scale AI image datasets through a desktop-grade
> web UI, with flat client memory and per-page server access bounded by a single Parquet
> row-group's size, not the full dataset.

---

## 1. Guiding principles

1. **Three data paths.** Stream from HuggingFace or an S3-compatible bucket (nothing stored) *or* import locally into Parquet + PostgreSQL.
2. **Perceived performance first.** Instant color placeholders, eager first screen, true virtualization — then row-group-bounded paging underneath (see §4).
3. **Flat client memory.** The browser never holds the whole dataset; only a few screenfuls of cards are in the DOM at any time.
4. **Stream, don't buffer.** Server-side ingestion and paging avoid materializing whole files/datasets where possible.
5. **Modular.** Viewers and features can be added as extensions (see §6 — real state, not aspirational).

---

## 2. High-level architecture

```
┌────────────────────────┐      HTTPS      ┌──────────────────────────┐
│        ClientApp        │ <────────────>  │        APIBackend         │
│  (Blazor WebAssembly)   │                 │   (ASP.NET Core, net10)   │
│  • VirtualizedItemView  │                 │  • Dataset/Item endpoints │
│  • DatasetCacheService  │                 │  • Background ingestion    │
│  • IndexedDB cache       │                │  • HuggingFace integration │
└────────────────────────┘                 └─────────────┬─────────────┘
                                                          │
                ┌─────────────────────────────────────────┼──────────────────────────────┐
                │ PostgreSQL (EF Core / Npgsql)             │   Apache Parquet (segments)    │
                │   dataset metadata, users, shares         │   dataset items + manifest      │
                └────────────────────────────────────────────────────────────────────────────┘
                                                          │
                                       HuggingFace datasets-server (streaming rows)
```

### Projects

| Project | Target | Responsibility |
|---|---|---|
| `Core` | net10.0 | Client-side domain models and enums, storage keys, the `Logs` wrapper (used by the client only) |
| `DTO` | net10.0 | Shared contracts (`DatasetDto` extending the library's `DatasetSummaryDto`, `DatasetItemDto`, `PageResponse<T>`, …). The API's own dataset row type is `DatasetRecord` in `APIBackend/DataAccess`; it is never serialised |
| `APIBackend` | net10.0 | API endpoints, EF/Postgres, Parquet storage, ingestion, HF integration |
| `ClientApp` | net10.0 | Blazor WASM viewer, caching, state, JS interop |
| `Extensions/SDK` | net10.0 | Extension manifest model and the `IApiExtension` contract |

---

## 3. Data model

Items are stored and transferred as **`DatasetItemDto`** (id, dataset id, external id, title,
description, image/thumbnail URLs, width/height, tags, favorite, a string `Metadata` dictionary,
timestamps). Rich image attributes (photographer, average color, engagement counts, etc.) live in
`Metadata` and are surfaced via extension methods on the client
(`DatasetItemDtoExtensions.AverageColor()`, `Photographer()`, …).

- **Metadata (Postgres):** `DatasetEntity` (name, status, counts, source type/URI, HuggingFace
  repo/config/split, error message) and related tables (users, permissions, ingestion jobs) via
  `DatasetStudioDbContext`. `permissions` holds dataset shares (one row per dataset and user).
  `item_overlays` holds the edits of streamed (HuggingFace, S3) items, keyed by dataset and item id.
  Item data is only in Parquet; the unused `dataset_items` and `captions` tables were dropped. Dataset updates are field-level patches with `xmin` as the concurrency
  token, so request-time edits and background ingestion writes never overwrite each other. Migrations live in `APIBackend/DataAccess/PostgreSQL/Migrations` and
  are applied automatically on API startup.
- **Items (Parquet):** per dataset, a manifest (`dataset_{id}.manifest.json`) over immutable
  segment files (`dataset_{id}_seg_{n}.parquet`), each with a sorted id index. Every write builds
  new files and commits by replacing the manifest (temp file plus rename), so a crash never
  damages committed data. Details in `src/APIBackend/DataAccess/Parquet/README.md`.

---

## 4. Server: paging, editing, ingestion

### Paging
`GET /api/datasets/{id}/items?cursor=…&pageSize=…` returns `PageResponse<DatasetItemDto>`.
`pageSize` is clamped to 1–100 on the list and item endpoints; a malformed cursor returns `400`
rather than restarting the read from the beginning.

- **Local datasets:** cursor is an opaque `r{offset}` token (absolute row offset; the older
  `"shard:row"` form is still accepted). `ReadPageAsync` skips
  every row group that ends before the cursor using the footer's row counts (nothing is decoded),
  then decodes groups from the cursor until the page fills. Cost is a footer read plus the groups
  the page returns rows from, each `ParquetSchemaDefinition.DefaultBatchSize` = 10,000 rows when
  written by ingestion. A page that ends exactly at the last row returns no cursor. Re-importing
  in many small uploads still produces smaller groups (no compaction yet). No full scans.
- **Streaming datasets:** the request maps to a HuggingFace `/rows?offset=…` call (or an S3
  `ListObjectsV2` page); rows are mapped to DTOs on the fly. Config/split are auto-discovered once
  and cached on the dataset. Item ids are deterministic (`StreamingItemIds`: a SHA-256 name-based
  UUID of dataset id + repository/config/split/row index, or dataset id + profile/bucket/key), so
  the same row or object has the same id on every request.

### Editing
Single (`PATCH /api/items/{id}`) and bulk (`PATCH /api/items/bulk`) edits update items
**segment-scoped**: only the segments holding changed items are rewritten, a row group at a
time, through a temp file renamed over the segment. Items are found through per-segment id
indexes, within the dataset the request names. Writes are serialized **per dataset** (a keyed semaphore), so editing one
dataset never blocks another. The edit is applied to the item as it stands inside that lock, and
the `If-Match` check against the item's `Version` (`UpdatedAt` ticks) happens in the same lock, so
concurrent edits of different fields both survive and an edit from a stale copy gets `412`.

Streamed items can't be written back, so their edits are stored in `item_overlays` (Postgres)
and applied over each page of the dataset as it is served, loaded in one query for the page's
ids. An overlay edit locks the item's row (`SELECT … FOR UPDATE` in a transaction, after
inserting an empty row if there is none), so the same concurrency and `If-Match` rules hold; the
version is the overlay's edit counter. Overlays are deleted with their dataset (FK cascade).

### Ingestion
Uploads are saved to a staging file under `Storage:UploadPath` (not the OS temp dir) and **queued** (`IngestionQueue`); the endpoint returns `202`
immediately. A hosted `IngestionBackgroundService` drains the queue off the request thread, parses
the file (CSV/TSV/JSON/JSONL/Parquet, ZIPs of images and of those formats, image folders) and
writes items in batches. On failure it
records `Failed` + an error message on the dataset, which the client's status poller surfaces.

---

## 5. Client: virtualized viewer

```
API → DatasetApiClient → DatasetCacheService → DatasetState.Items (sliding window)
    → ViewerContainer → VirtualizedItemView (<Virtualize>, row-based) → ImageCard / ImageListRow
```

- **`DatasetCacheService`** keeps a fixed-size in-memory window over the dataset (cursor paging,
  forward/back, eviction), with an optional **IndexedDB** layer caching pages across reloads.
- **`VirtualizedItemView`** wraps Blazor's `<Virtualize>` using **row virtualization** (each
  virtualized element is one row of *N* cards), so a responsive N-column grid stays virtualized.
  Only a few screenfuls of cards exist in the DOM at any scroll depth. The same component renders
  the list view (`Columns = 1`). Column count (`GridLayout.ColumnsFor`: container width, min card
  180 px, capped by the user setting) and row height come from the container width, reported
  debounced by a `ResizeObserver`. The next page is requested only when an end-of-list sentinel
  nears the list's visible area (`IntersectionObserver`), never in a background chain.
- **`ImageCard`** shows a dominant-color (`average_color`) placeholder instantly, then fades in a
  width-bounded thumbnail (`loading="lazy"`, `decoding="async"`) over it — zero layout shift.

---

## 6. Extensions

An extension is an API plugin: a folder with an `extension.manifest.json` and a compiled
`{Id}.Api.dll` that implements `IApiExtension`. Plan item 6.3 cut the extension system down to
three pieces that work; everything else it used to declare (client extensions, lifecycle and
health hooks, endpoint descriptors, permission lists) was deleted (audit X-5).

**Manifest.** `ExtensionManifest` in `src/Extensions/SDK`:
```json
{
  "schemaVersion": 1,
  "metadata": { "id": "MyExtension", "name": "My Extension", "version": "1.0.0" },
  "deploymentTarget": "Api",
  "dependencies": { "OtherExtension": ">=1.0.0" }
}
```
The id may contain letters, digits, `.`, `-` and `_` only, because it becomes a file name and a
URL segment. `deploymentTarget` is `Api` | `Client` | `Both`; the API skips `Client`. Unknown
fields are ignored, including `requiredPermissions` from older manifests, which was never
enforced.

**Contract.** `IApiExtension` (same assembly) has two methods:
- `ConfigureServices(IServiceCollection, IConfiguration)` runs before the host is built, after the
  API's own registrations. There is no service provider at that point, by design: Program.cs no
  longer builds a throwaway one (audit C-5).
- `MapEndpoints(IEndpointRouteBuilder)` runs after the host is built. The builder is a route group
  at `/api/extensions/{id}` (id in lower case) that already requires an authenticated user. The
  group cannot check dataset permissions (`IDatasetPermissionService` lives in the API, not the
  SDK), so an extension must not return or change dataset data.

**Loader.** `ApiExtensionLoader` (`src/APIBackend/Services/Extensions`) scans
`Extensions:BuiltInPath` and `Extensions:CommunityPath` (defaults `../Extensions/BuiltIn` and
`../Extensions/Community` relative to the API's content root), skips ids listed in
`Extensions:DisabledExtensions`, loads each extension's assembly and creates its instance with
the public parameterless constructor. Dependencies load before their dependents; an extension
whose dependency is missing, disabled, in a cycle or failed to load is skipped, and when two
folders use the same id the built-in one wins. It looks for `{Id}.Api.dll` in two layouts: flat next to
the manifest (what `dotnet publish -o` produces; the Dockerfile does this for CoreViewer), or in
the project's build output `{extension}/{Id}.Api/bin/{Configuration}/{tfm}/`, where `{tfm}` is the
API's own target framework and the newest build wins. APIBackend has a build-order-only reference
to `CoreViewer.Api`, so `dotnet run` builds it and the loader finds it. Each extension gets its
own `ExtensionLoadContext` with an `AssemblyDependencyResolver` over its `.deps.json`, so a
library that ships only with the extension resolves from its folder. Any assembly the API itself
has (the SDK, ASP.NET Core, DTO) always comes from the API, so there is one
`IApiExtension` type and an extension gets the API's version of a shared library. Reference the
SDK with `Private="false" ExcludeAssets="runtime"`, as `CoreViewer.Api.csproj` does, to keep it
out of the extension's output. Any failure is logged and skips that extension; it never
stops the API starting. Extensions are never unloaded.

**Trust model (audit X-3).** An extension runs in the API process with the API's privileges and
full access to `IServiceCollection`. Nothing is sandboxed or signature-checked; any
`{Id}.Api.dll` placed in an extensions folder runs. Install only trusted extensions and keep the
folders admin-writable (docs/DEPLOYMENT.md, "Extensions").

**CoreViewer** (`src/Extensions/BuiltIn/CoreViewer`) is the only built-in extension. It is a
reference: it registers nothing and maps nothing. Its two old routes returned placeholder data
without authentication and were removed. There is no client-side extension loading; a WASM
loader would need a different design (an HTTP-served manifest plus dynamic assembly loading).

---

## 7. Roadmap / known gaps

- **Filtering:** server-side on the items endpoint (README §4.11). Local text search is a
  substring scan, not an index; HuggingFace streaming supports search only; S3 has no filters.
- **Full defect list and refactor sequencing:** `docs/refactor-audit.md`. UI target: `DESIGN.md`.
- **Extensions:** API-only, and the one built-in extension (CoreViewer) is a reference that does
  nothing. Dataset permission checks for extension routes, sandboxing, signing and client-side
  extension loading are not built. See §6 above.
- **AI Tools:** nav entry and page both explicitly labeled "Coming soon" (Phase 7) rather than
  shipped as a placeholder that reads as broken — needs its own model-serving decision (local
  hosting vs. a third-party API) before it's buildable.
- **Search/observability:** a dedicated search index and metrics/telemetry are future work.
