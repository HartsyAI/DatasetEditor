# Dataset Studio — Architecture

> **Goal:** browse, curate, and edit billion-scale AI image datasets through a desktop-grade
> web UI, with flat client memory and O(1)-per-page server access regardless of dataset size.

---

## 1. Guiding principles

1. **Two data paths.** Stream from HuggingFace (nothing stored) *or* import locally into Parquet + PostgreSQL.
2. **Perceived performance first.** Instant color placeholders, eager first screen, true virtualization — then real O(1) paging underneath.
3. **Flat client memory.** The browser never holds the whole dataset; only a few screenfuls of cards are in the DOM at any time.
4. **Stream, don't buffer.** Server-side ingestion and paging avoid materializing whole files/datasets where possible.
5. **Modular.** Viewers and features can be added as extensions (see [EXTENSION_ARCHITECTURE.md](EXTENSION_ARCHITECTURE.md)).

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
| `Core` | net8.0 | Domain models, parsers, filtering, abstractions |
| `DTO` | net8.0 | Shared contracts (`DatasetItemDto`, `PageResponse<T>`, …) |
| `APIBackend` | net10.0 | API endpoints, EF/Postgres, Parquet storage, ingestion, HF integration |
| `ClientApp` | net8.0 | Blazor WASM viewer, caching, state, JS interop |
| `Extensions/SDK` (+ `SDK.Api`) | net8.0 | Extension contracts (client-safe + API-only) |

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
`GET /api/datasets/{id}/items?cursor=…&pageSize=…` returns `PageResponse<DatasetItemDto>`:

- **Local datasets:** cursor is the opaque Parquet `"shard:row"` token — O(1) per page, no full scans.
- **Streaming datasets:** the request maps to a HuggingFace `/rows?offset=…` call; rows are mapped
  to DTOs on the fly. Config/split are auto-discovered once and cached on the dataset.

### Editing
Single (`PATCH /api/items/{id}`) and bulk (`PATCH /api/items/bulk`) edits update items
**shard-scoped**: only the Parquet shard(s) actually containing changed items are rewritten, not
the whole dataset. Writes are serialized **per dataset** (a keyed semaphore), so editing one
dataset never blocks another.

### Ingestion
Uploads are saved to a temp file and **queued** (`IngestionQueue`); the endpoint returns `202`
immediately. A hosted `IngestionBackgroundService` drains the queue off the request thread, parses
the file (CSV/TSV/Parquet/JSON/JSONL/ZIP/image folders) and writes items in batches. On failure it
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

Extensions are manifest-described plugins discovered at startup on both API and client. The
contract is split so the WASM client never references ASP.NET hosting types:
`IExtension` (client-safe, `Extensions.SDK`) and `IApiExtension : IExtension`
(`Extensions.SDK.Api`, adds `ConfigureApp(IApplicationBuilder)`). See
[EXTENSION_ARCHITECTURE.md](EXTENSION_ARCHITECTURE.md).

---

## 7. Roadmap / known gaps

- **Ingestion memory:** ingestion now runs in the background, but some parsers still materialize
  the whole file before writing — chunked (streaming) parse + insert is the follow-up for fully
  bounded memory on multi-GB imports.
- **Client filtering:** filter UI exists, but full-dataset filtering needs server-side query
  support (today filtering applies to the buffered window).
- **Extensions:** loading/discovery work; endpoint auto-registration and permission enforcement
  are still planned (see [EXTENSION_SYSTEM_IMPLEMENTATION_PLAN.md](EXTENSION_SYSTEM_IMPLEMENTATION_PLAN.md)).
- **Search/observability:** a dedicated search index and metrics/telemetry are future work.
