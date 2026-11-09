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
                │ PostgreSQL (EF Core / Npgsql)             │   Apache Parquet (sharded)     │
                │   dataset metadata, users, captions       │   dataset items, 10M rows/shard │
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
  repo/config/split, error message) and related tables (users, captions, permissions) via
  `DatasetStudioDbContext`. Migrations live in `APIBackend/DataAccess/PostgreSQL/Migrations` and
  are applied automatically on API startup.
- **Items (Parquet):** sharded files `dataset_{id}_shard_{n}.parquet`, **10M rows per shard**,
  written in batches. Read/write/paging primitives in `ParquetItemReader` / `ParquetItemWriter`,
  fronted by `ParquetItemRepository`.

---

## 4. Server: paging, editing, ingestion

### Paging
`GET /api/datasets/{id}/items?cursor=…&pageSize=…` returns `PageResponse<DatasetItemDto>`.
`pageSize` is clamped to 1–100 on the list and item endpoints; a malformed cursor returns `400`
rather than restarting the read from the beginning.

- **Local datasets:** cursor is the opaque Parquet `"shard:row"` token. `ReadPageAsync` reads row
  groups sequentially starting from the cursor's position until the page fills — cost is bounded
  by a row group's size (the ingestion writer's batch size, `ParquetSchemaDefinition.
  DefaultBatchSize` = 10,000 rows, see §3/§4 Ingestion), not O(1) and not the full shard or
  dataset. The Phase 1 append fix (`FileMode.Open, FileAccess.ReadWrite` instead of `Append`,
  needed for the footer rewrite on append) doesn't change row-group sizing itself, but does mean
  a re-ingested/appended shard can end up with more, smaller row groups than a single bulk write
  would — still bounded, just a different constant. No full scans either way.
- **Streaming datasets:** the request maps to a HuggingFace `/rows?offset=…` call; rows are mapped
  to DTOs on the fly. Config/split are auto-discovered once and cached on the dataset.

### Editing
Single (`PATCH /api/items/{id}`) and bulk (`PATCH /api/items/bulk`) edits update items
**shard-scoped**: only the Parquet shard(s) actually containing changed items are rewritten, not
the whole dataset. Writes are serialized **per dataset** (a keyed semaphore), so editing one
dataset never blocks another.

### Ingestion
Uploads are saved to a staging file under `Storage:UploadPath` (not the OS temp dir) and **queued** (`IngestionQueue`); the endpoint returns `202`
immediately. A hosted `IngestionBackgroundService` drains the queue off the request thread, parses
the file (CSV/TSV/JSON/JSONL/ZIP/image folders — Parquet upload is **not** supported, see
`docs/refactor-audit.md` I-6) and writes items in batches. On failure it
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
  the list view (`Columns = 1`). Column count and row height are computed from the container width
  (a small `ResizeObserver` JS interop), and nearing the end triggers prefetch via the cache.
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

- **Client filtering:** filter UI exists, but nothing applies it — `DatasetViewer.ApplyFiltersQuiet`
  passes the unfiltered window through, and the items endpoint accepts no filter parameters
  (`docs/refactor-audit.md` U-6, G-2). Real filtering needs server-side query support.
- **Full defect list and refactor sequencing:** `docs/refactor-audit.md`. UI target: `DESIGN.md`.
- **Extensions:** only 1 of 5 built-in manifests (CoreViewer) has real, working code; endpoint
  auto-registration, permission enforcement for extension routes, and any client-side extension
  loading are all still unbuilt. See §6 above for the real state.
- **AI Tools:** nav entry and page both explicitly labeled "Coming soon" (Phase 7) rather than
  shipped as a placeholder that reads as broken — needs its own model-serving decision (local
  hosting vs. a third-party API) before it's buildable.
- **Search/observability:** a dedicated search index and metrics/telemetry are future work.
