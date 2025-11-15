# Dataset Editor Architecture & Performance Blueprint

> **Goal**: Deliver a modular, API-first platform capable of serving billions of dataset items to hundreds of concurrent users with near-instant interactivity.

---

## 1. Guiding Principles

1. **API-First**: Every UI action calls an HTTP API. The API orchestrates business logic and data access.
2. **Streaming Everywhere**: Parse, ingest, and serve data in chunks; avoid full in-memory materialization.
3. **Virtualized UI**: Render only what the user sees; aggressively prefetch just ahead of the viewport.
4. **Client Caching**: Use browser storage/IndexedDB for hot pages, tiles, and user preferences.
5. **Modular Extensibility**: Pluggable parsers, modalities, and viewers via dependency injection/providers.
6. **Observability**: Telemetry across ingestion, APIs, and client fetch cycles for diagnostics and scaling.

---

## 2. High-Level Architecture

```
┌────────────────────────┐         ┌────────────────────────┐
│        Browser         │         │          API           │
│  (Blazor WebAssembly)  │ <──────>│  ASP.NET Core Minimal  │
│                        │  HTTPS  │    APIs / Controllers  │
└────────────────────────┘         └──────────┬─────────────┘
                                              │
                         ┌────────────────────┴────────────────────┐
                         │              Backing Services           │
                         │   • Ingestion Workers (Hosted Services) │
                         │   • Storage (Blob/S3/Azure)             │
                         │   • Database (Postgres/Dynamo/etc.)     │
                         │   • Search Index (Elastic/OpenSearch)   │
                         └─────────────────────────────────────────┘
```

### 2.1 Client Responsibilities
- Upload wizard calls API endpoints with chunked uploads.
- UI requests paginated lists/search results via `HttpClient`.
- `Virtualize<T>` (or MudBlazor equivalent) uses `ItemsProvider` to retrieve pages.
- Prefetch and cache upcoming pages in IndexedDB to hide network latency.
- Central `DatasetCacheService` coordinates fetch, cache, and eviction.

### 2.2 API Responsibilities
- Handle dataset lifecycle: upload, ingestion status, metadata, query endpoints.
- Coordinate ingestion workers to parse files and persist metadata.
- Expose REST endpoints for dataset browsing (`GET /datasets/{id}/items` with cursor paging), search, and filters.
- Enforce rate limits/auth (future multi-tenant support).

### 2.3 Backend Services
- **Ingestion Worker**: Background service reading uploaded files, using streaming parser (CsvHelper async reader) to populate storage.
- **Storage**: Raw files in blob storage; thumbnails and derived assets stored or generated on demand.
- **Database**: Stores dataset metadata, item attributes, indexes (B-tree or columnar). Candidate: PostgreSQL + PostGIS; alternative: DynamoDB.
- **Search Index**: Elastic/OpenSearch for full-text, tag search, filters.

---

## 3. Detailed Component Plan

### 3.1 Client (Blazor WASM)

| Component / Service | Purpose | Notes |
|---------------------|---------|-------|
| `DatasetUploader` | Initiates upload via API (multipart or chunks). | Replace current `ReadToEndAsync`; use chunked POST to `/api/uploads`. |
| `DatasetCacheService` | Manages cached pages in memory + IndexedDB. | Keeps sliding window of items. Exposes async `GetPageAsync(pageToken)`. |
| `VirtualizedImageGrid` | Wraps `Virtualize<T>` with `ItemsProvider`. | Batches requests, shows skeletons, handles prefetch. |
| `FilterPanel` | Sends filter state to API query endpoint. | Debounce input; uses `GET /datasets/{id}/items?filter=...`. |
| `NavigationService` | Routes with dataset IDs instead of local state. | Query string or route parameters for cursors. |
| `NotificationService` | Surfaces ingest status/progress. | Subscribe to SignalR (future) for real-time updates. |
| `ViewState` / `DatasetState` | Stores UI state only (selected items, view mode). | Items themselves are streamed through cache service. |

#### TODOs for Client
- [ ] Scaffold `DatasetCacheService` with fetching + caching strategies.
- [ ] Refactor `ImageGrid` to use `ItemsProvider` pattern with API.
- [ ] Replace local dataset state list with cache references.
- [ ] Implement IndexedDB storage using `Blazored.LocalStorage` or direct JS interop for larger caches.
- [ ] Create skeleton components for dataset detail, stats, search facets fed from API.

### 3.2 API (ASP.NET Core Web API)

| Area | Endpoints | Description |
|------|-----------|-------------|
| Dataset Lifecycle | `POST /api/datasets` | Creates dataset record. Returns dataset ID. |
|  | `POST /api/datasets/{id}/upload` | Accepts file upload (multipart or chunked). Initiates ingestion job. |
|  | `GET /api/datasets/{id}` | Metadata + ingestion status. |
| Items & Filtering | `GET /api/datasets/{id}/items` | Cursor-based paging, filter params (search, tags, dimensions). |
|  | `GET /api/datasets/{id}/items/{itemId}` | Fetch single item metadata/details. |
| Search | `GET /api/datasets/{id}/search` | Delegates to search index with advanced query syntax. |
| Stats | `GET /api/datasets/{id}/stats` | Returns aggregates (counts, tag histograms, etc.). |

#### Implementation Outline
- Minimal API or conventional controllers (choose minimal for speed).
- DTOs defined in shared project or `HartsysDatasetEditor.Contracts` for reuse.
- Background ingestion hosted service monitoring queue (e.g., `Channel` or DB status statements).
- Use EF Core or Dapper to interact with relational DB; repository interfaces for alternative stores.
- Integration tests to validate API contract.

#### TODOs for API
- [x] Create `HartsysDatasetEditor.Api` project (ASP.NET Core Web API).
- [x] Setup dependency injection for repositories, parsers, background services.
- [x] Implement sample in-memory repository + stub ingestion worker for local dev.
- [x] Define shared contracts project for DTOs (DatasetSummaryDto, ItemDto, FilterRequest, PageResponse<T>).
- [ ] Add Swagger/OpenAPI UI and document authentication requirements.
- [ ] Configure CORS for WASM client.

### 3.3 Ingestion / Workers
- Background hosted services in API project or separate worker service.
- Use asynchronous CSV parsing (CsvHelper `GetRecordsAsync`) and streaming writes.
- Support plugin parser discovery (DI). Parser chooses correct `IDatasetItem` builder.
- Persist ingestion progress to DB for resume/failure handling.
- Generate thumbnails via serverless task or background job if needed.

#### TODOs
- [ ] Implement ingestion interface `IDatasetIngestionService` with methods `StartIngestionAsync`, `GetStatusAsync`.
- [ ] Provide sample implementation storing to in-memory or file-based store for dev.
- [ ] Document expected storage contract (tables, blob structure).

### 3.4 Storage & Search Strategy
- **Primary DB**: Table `DatasetItems` with indexes on datasetId + itemId, plus filter fields (tags, dimensions). For high-scale, partition by datasetId.
- **Blob Storage**: `datasets/{datasetId}/source/{file}` and `datasets/{datasetId}/thumbnails/{itemId}.jpg`.
- **Search**: Elastic/OpenSearch index keyed by `datasetId` + `itemId`. Document includes full-text fields and filter facets.
- **Caching**: CDN or reverse proxy for thumbnail delivery.

### 3.5 Observability Plan
- Structured logs via Serilog/ILogger with correlation IDs per upload/request.
- Metrics (Prometheus/OpenTelemetry): ingest throughput, API latency, cache hit rate.
- Alerts on ingestion failures or API error spikes.

---

## 4. Data Flow Walkthrough

1. **Upload**
   - Client `DatasetUploader` POST -> `/api/datasets` -> returns ID.
   - Client `PUT /api/datasets/{id}/upload` streaming file.
   - API stores raw file in blob, enqueues ingestion job.
   - Ingestion worker streams file, parses rows, writes to DB/search index.
   - Status updates accessible via `GET /api/datasets/{id}` (progress %, counts).

2. **Browse/View**
   - Client requests first page: `GET /api/datasets/{id}/items?pageSize=100`.
   - API returns `PageResponse<ItemDto>` with `NextCursor`.
   - `Virtualize` requests more items with `NextCursor` when nearing bottom.
   - Cache service stores previous pages; prefetch next page in background.
   - Image URLs point to CDN or direct Unsplash URLs with transformations.

3. **Filter/Search**
   - Filter state serialized into query string or POST body.
   - API translates to DB/search query; returns page + counts.
   - Cache invalidated or segmented per filter signature.

---

## 5. Checklist & Milestones

### Phase A – Infrastructure Setup
- [x] Add `HartsysDatasetEditor.Api` (ASP.NET Core) project.
- [x] Create shared contracts project (`HartsysDatasetEditor.Contracts`).
- [ ] Configure solution to host WASM + API together (aspnet-hosted). 
- [x] Add README section summarizing architecture and how to run both projects.

### Phase B – API Skeleton
- [x] `POST /api/datasets` -> returns datasetId.
- [x] `POST /api/datasets/{id}/upload` -> accepts multipart (stub ingestion for now).
- [x] `GET /api/datasets/{id}/items` -> returns stubbed page from in-memory store.
- [ ] Swagger UI + CORS configuration.

### Phase C – Client Refactor
- [ ] Replace local `DatasetLoader` usage with API-backed service.
- [ ] Implement `DatasetCacheService` and update viewer to use `ItemsProvider`.
- [ ] Add placeholder ingestion status UI (polling `GET /api/datasets/{id}`).
- [ ] Implement IndexedDB caching (optional initial stub).

### Phase D – Ingestion & Persistence
- [ ] Implement ingestion background service with streaming parser.
- [ ] Connect to real database (EF Core migrations).
- [ ] Integrate search index (optional stub first).
- [ ] Add progress reporting and retry handling.

### Phase E – Advanced Features
- [ ] Pre-fetch heuristics & CDN integration.
- [ ] Multi-dataset dashboard with summary stats.
- [ ] Real-time notifications (SignalR) for ingestion completion.
- [ ] Modular parser/plugin loading via reflection.

---

## 6. Developer Notes & Conventions
- Use async/await end-to-end. Avoid blocking calls in WASM/API.
- DTOs should be lean; avoid sending raw dataset rows.
- Prefer `CancellationToken` on all async methods.
- Parse filters server-side; keep client filter state serializable (JSON or query).
- Document extension hooks with XML comments + README references.
- Add TODOs to code linking back to sections of this doc for future contributors.

---

## 7. Future Considerations
- Authentication & multi-tenant dataset isolation.
- Role-based access (viewers vs. editors).
- Audit logs and versioning of datasets.
- Integration with external AI services (captioning, tagging).
- Federation: ability to query across multiple dataset stores.

---

## 8. Immediate Next Client Integration Steps
- Wire `DatasetCacheService` to call `GET /api/datasets/{id}/items` using cursor-based pagination.
- Update uploader flow to use `POST /api/datasets` and capture returned dataset ID for viewer navigation.
- Introduce a dataset list screen that consumes `GET /api/datasets` summaries.
- Replace local dataset state with API-backed DTOs and store cursors in `DatasetState`.
- Document interim client/api contract expectations (error shapes, pagination) in README.

---

*This document should evolve with each milestone. Update checklists and references as features land.*
