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
| `Core` | net10.0 | Domain models, parsers, filtering, abstractions |
| `DTO` | net10.0 | Shared contracts (`DatasetItemDto`, `PageResponse<T>`, …) |
| `APIBackend` | net10.0 | API endpoints, EF/Postgres, Parquet storage, ingestion, HF integration |
| `ClientApp` | net10.0 | Blazor WASM viewer, caching, state, JS interop |
| `Extensions/SDK` (+ `SDK.Api`) | net10.0 | Extension contracts (client-safe + API-only) |

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

Extensions are manifest-described plugins discovered at startup on the API. The contract is
split across two assemblies so the WASM client could reference the client-safe half without
pulling in ASP.NET hosting types: `IExtension` (`Extensions.SDK`) and
`IApiExtension : IExtension` (`Extensions.SDK.Api`, adds `ConfigureApp(IApplicationBuilder)`).
`BaseApiExtension`/`BaseClientExtension` implement the boilerplate (`Context`/`Logger`/`Services`
accessors, virtual `OnInitializeAsync`/`OnConfigureApp`/`OnValidateAsync` hooks) so a concrete
extension only overrides what it needs.

A manifest (`extension.manifest.json`, one per extension directory under
`src/Extensions/BuiltIn/`) needs at minimum:
```json
{
  "schemaVersion": 1,
  "metadata": { "id": "MyExtension", "name": "My Extension", "version": "1.0.0" },
  "deploymentTarget": "Api"
}
```
`deploymentTarget` is `Api` | `Client` | `Both`. `ApiExtensionRegistry` scans
`Extensions:BuiltInPath` (defaults to `../Extensions/BuiltIn` relative to the API's content
root) for these manifests, then looks for a matching `{Id}.Api.dll` next to each one — built by
`dotnet publish`ing that extension's own `.Api.csproj` (see the `Dockerfile`'s extension-publish
stage for the one real example: `CoreViewer.Api`).

**Real state as of Phase 7** (found by actually running the pipeline end-to-end for the first
time, not assumed from the design): of the 5 built-in manifests
(`src/Extensions/BuiltIn/{CoreViewer,Creator,Editor,AdvancedTools,AITools}`), only **CoreViewer**
has both a real project and working code — and even that only after fixing a lifecycle bug where
`BaseApiExtension`'s `Logger` property threw before `InitializeAsync` ran, which is *before*
`ConfigureServices`/`ConfigureApp` are called in the real startup order. Its two endpoints are
explicitly reference/example code (hardcoded placeholder data, no permission checks) — see the
class-level comment in `CoreViewerApiExtension.cs`; copy that file's shape for a real extension,
not the endpoints' own bodies. `Creator` has empty stub `.Api`/`.Client` projects (build, but
contain no `IExtension` implementation). `Editor`/`AdvancedTools`/`AITools` are manifest-only, no
project at all. `ApiExtensionRegistry` handles all of this gracefully (logs "API assembly not
found", doesn't crash), so this is an honest capability gap, not a runtime bug. There is
currently **no client-side extension loading** — the prior `ClientExtensionRegistry` was deleted
in Phase 7: it scanned the real filesystem (`Directory.Exists`/`File.Exists`), which doesn't work
in a Blazor WebAssembly browser sandbox, and had zero call sites of its own. A working
client-extension loader needs a different design (e.g. an HTTP-served manifest + dynamic WASM
assembly loading), not built here — `BaseClientExtension` and the `.Client` projects exist only
as SDK surface/compile-checked scaffolding today.

---

## 7. Roadmap / known gaps

- **Filtering:** server-side on the items endpoint (README §4.11). Local text search is a
  substring scan, not an index; HuggingFace streaming supports search only; S3 has no filters.
- **Full defect list and refactor sequencing:** `docs/refactor-audit.md`. UI target: `DESIGN.md`.
- **Extensions:** only 1 of 5 built-in manifests (CoreViewer) has real, working code; endpoint
  auto-registration, permission enforcement for extension routes, and any client-side extension
  loading are all still unbuilt. See §6 above for the real state.
- **AI Tools:** nav entry and page both explicitly labeled "Coming soon" (Phase 7) rather than
  shipped as a placeholder that reads as broken — needs its own model-serving decision (local
  hosting vs. a third-party API) before it's buildable.
- **Search/observability:** a dedicated search index and metrics/telemetry are future work.
