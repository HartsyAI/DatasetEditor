# Dataset Studio

A self-hosted web app for **browsing, curating and editing very large AI image datasets** —
the kind that don't fit in memory, often don't fit on one disk, and frequently live somewhere
else (HuggingFace, an S3 bucket) that you'd rather not copy.

ASP.NET Core API + Blazor WebAssembly client. PostgreSQL for users/datasets/permissions,
sharded Apache Parquet on disk for dataset items, and on-demand proxying for remote sources.

> **Project documents**
> - [DESIGN.md](DESIGN.md) — the UI design system every client change must follow.
> - [AGENTS.md](AGENTS.md) — how to work in this repo (build, run, test, conventions).
> - [docs/refactor-audit.md](docs/refactor-audit.md) — known defects and the refactor plan.
> - [docs/architecture.md](docs/architecture.md) — data flows and storage layout in depth.
> - [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) — production deployment with Docker + Caddy.

---

## 1. Why this exists

Image datasets for model training routinely run to millions of rows. The tools people use
to look at them are either a notebook (`df.head()`), a HuggingFace viewer tab (read-only, no
curation), or a desktop app that insists on downloading everything first. None of them let a
team *open* a 70,000-row or 70,000,000-row dataset, scroll it like a photo library, fix a
caption, tag a hundred items, mark the bad ones, and share the result — without first making
a local copy.

Dataset Studio is built around three commitments:

1. **Never hold the dataset.** The browser keeps a few screenfuls of cards; the server reads
   one Parquet row group at a time; remote sources are paged and proxied, never mirrored.
2. **Same viewer for every source.** Whether the rows come from an uploaded CSV, a ZIP of
   images, a folder on the server, a HuggingFace repo or an S3 bucket, the grid, list, detail
   panel and editing tools are identical.
3. **Self-hostable and multi-user.** One container plus Postgres; real accounts; private by
   default; a public toggle for sharing.

---

## 2. How it is meant to work

```
                    ┌──────────────────────────────────────────────────┐
  Browser           │  Blazor WASM client (MudBlazor)                  │
                    │  · pages: Dashboard, Datasets, Create, Viewer,   │
                    │           Settings, Login/Register               │
                    │  · DatasetCacheService: sliding window of pages  │
                    │  · VirtualizedItemView: row-virtualised grid     │
                    └──────────────┬───────────────────────────────────┘
                                   │ HTTP + cookie session
                    ┌──────────────▼───────────────────────────────────┐
  API               │  ASP.NET Core minimal API                        │
                    │  /api/auth  /api/datasets  /api/items            │
                    │  IngestionQueue → IngestionBackgroundService     │
                    │  ThumbnailService (SkiaSharp, disk cache)        │
                    └───┬──────────────┬───────────────┬───────────────┘
                        │              │               │
             ┌──────────▼───┐  ┌───────▼────────┐  ┌───▼─────────────────────┐
  Storage    │ PostgreSQL   │  │ Parquet shards │  │ Remote, proxied on demand│
             │ users        │  │ dataset items  │  │ HuggingFace datasets-   │
             │ datasets     │  │ 10M rows/shard │  │   server (/rows, /size)  │
             │ permissions  │  │ + served files │  │ S3-compatible buckets    │
             └──────────────┘  └────────────────┘  └──────────────────────────┘
```

**A dataset is a row in Postgres** (name, status, source type, counts, owner, visibility) plus
one of three item backends chosen by `SourceType`:

| Source type | Where items live | Where images live |
|---|---|---|
| `LocalUpload` (CSV/TSV/JSON/JSONL/ZIP) | Parquet shards under `Storage:DatasetRootPath` | URLs in the file, or extracted ZIP images served by the API |
| `LocalFolder` (admin only) | Parquet shards | The original folder, served by the API |
| `HuggingFaceStreaming` | Not stored; each page is a `/rows` call | HuggingFace CDN, thumbnails proxied + cached by the API |
| `ExternalS3Streaming` | Not stored; each page is a `ListObjectsV2` call | The bucket, proxied + thumbnailed by the API |

**Every page request is bounded.** Local: a `"shard:row"` cursor and one row group at a time.
HuggingFace: an integer offset against `/rows`. S3: the continuation token. The client asks for
the next page when the virtualised grid nears its buffer edge, and evicts old pages.

**Ingestion is asynchronous.** Uploads return `202` as soon as the file is on disk; a
background worker parses it as a stream and writes Parquet in batches; the dataset's `Status`
moves Pending → Processing → Completed/Failed and the client polls it.

**Authorization is per dataset.** Anonymous visitors see public datasets. Owners and site
admins can write and delete. Private datasets return 404 (not 403) to anyone else, so their
existence isn't leaked.

---

## 3. Quick start

Requirements: **.NET 10 SDK**, **Docker** (PostgreSQL; MinIO optionally for the S3 source).

```bash
./start.sh                 # postgres (+minio) in Docker, API on :5000, client on :5002
```

Manual equivalent:

```bash
docker compose up -d postgres
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/APIBackend   # :5000, Swagger at /swagger
dotnet run --project src/ClientApp                                       # :5002
```

`ASPNETCORE_ENVIRONMENT=Development` matters: the base `appsettings.json` has no database
connection string, and only the Development overlay supplies the local one (`postgres`/`postgres`,
database `dataset_studio_dev`). Outside Development the API refuses to start until
`ConnectionStrings__DatasetStudio` is set. `start.sh` and `launchSettings.json` set the environment
for you; `--no-launch-profile` does not, so set the variable yourself.

To get an admin account, set `ADMIN_USERNAME`, `ADMIN_EMAIL`, `ADMIN_PASSWORD` before starting
the API. Anyone can register a normal account from the UI.

Stop with `./stop.sh` (`--wipe` also deletes the database volume). Production deployment (one
container + Caddy TLS): [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

---

## 4. Features — what, how, why

Status key: **Working** (verified end to end) · **Partial** (works with caveats listed) ·
**Placeholder** (UI exists, no behaviour) · **Dead** (code exists, nothing uses it). Audit IDs
refer to [docs/refactor-audit.md](docs/refactor-audit.md).

| # | Feature | Status |
|---|---|---|
| 4.1 | Accounts, sessions, roles | Working |
| 4.2 | Dataset library, ownership, visibility | Working (sharing grants Partial) |
| 4.3 | Upload ingestion (CSV/TSV/JSON/JSONL/ZIP) | Working |
| 4.4 | HuggingFace streaming | Working (row count Partial) |
| 4.5 | HuggingFace download/import | Partial |
| 4.6 | S3-compatible buckets | Working |
| 4.7 | Server folder import | Working (admin-only) |
| 4.8 | Thumbnails | Working |
| 4.9 | Virtualised viewer (grid / list / detail / lightbox) | Working (caveats) |
| 4.10 | Item editing (single, bulk) | Partial (local datasets; broken in the dev setup) |
| 4.11 | Filtering and search | Placeholder (UI only) |
| 4.12 | Settings, theme, language | Placeholder (API keys only) |
| 4.13 | AI tools | Placeholder |
| 4.14 | Extensions | Partial (one example) |
| 4.15 | Production deployment | Working |

### 4.1 Accounts, sessions, roles — Working

**What.** Register (username, email, password ≥ 8), log in with username or email, log out.
Roles are `User` and `Admin`. A site admin is bootstrapped from environment variables.

**How.** `src/APIBackend/Endpoints/AuthEndpoints.cs`, `Services/Auth/*`. ASP.NET Core cookie
authentication (`DatasetStudio.Auth`, HttpOnly, SameSite=Lax, 7-day sliding). Passwords are
BCrypt (work factor 11). `AdminBootstrapper` runs after migrations on every start and
creates-or-resets the admin from `ADMIN_*`. The client's `CookieAuthenticationStateProvider`
asks `/api/auth/me` and feeds `AuthorizeView`/`[Authorize]`; `CookieCredentialsHandler` makes
the typed `HttpClient`s send the cookie cross-origin in dev (client :5002 → API :5000).

**Why.** Cookies rather than JWT so the WASM client never holds a token it could leak and the
API can return plain 401/403. `SecurePolicy=SameAsRequest` because dev is plain HTTP; Caddy
terminates TLS in production. The admin is re-synced on every boot so a forgotten password is
fixed by changing an env var and restarting. The seeded admin row ships with an unusable
placeholder hash so an unconfigured install has no default credentials.

**Gaps.** Open registration with no rate limit (A-1, A-2); role/active flags frozen in the
cookie until expiry (A-3).

### 4.2 Dataset library, ownership, visibility — Working

**What.** `/my-datasets` lists what you may see: your own plus public datasets (anonymous
visitors see public only). Cards show status, source, counts, open/delete. New datasets are
private by default; `IsPublic` makes them visible to everyone.

**How.** `DatasetEndpoints.GetAllDatasets/GetDataset/CreateDataset/DeleteDataset`,
`DataAccess/PostgreSQL/Repositories/DatasetRepository.cs`, `Services/Auth/DatasetPermissionService.cs`.
A `DatasetVisibilityFilter` is built from the principal and applied as SQL `WHERE`, so pages
are never under-filled by post-filtering. `CanRead/CanWrite/CanDelete` consult owner, admin
role, and the `permissions` table.

**Why.** 404-not-403 for private datasets mirrors GitHub's handling of private repos. The
filter lives in SQL so counts and pages agree.

**Gaps.** The `permissions` grant table has no write path — sharing a private dataset with a
specific user is impossible from the API (D-2). `POST /api/datasets` returns default
timestamps (D-1). Full-row `UpdateAsync` can clobber concurrent status writes (D-4).

### 4.3 Upload ingestion — Working

**What.** Create a dataset, upload a `.csv`, `.tsv`, `.json` (array), `.jsonl`/`.ndjson`, or
`.zip` of images (up to 5 GB). Column names are matched loosely (`image_url|url|image`,
`title|name|caption|text`, `description`, `id`, `width`, `height`, `tags`); every column is
kept in item metadata. ZIP members that are images are extracted into the dataset's storage
folder and served by the API.

**How.** `DatasetEndpoints.UploadDatasetFile` → temp file → `IngestionQueue` (an unbounded
`Channel`) → `IngestionBackgroundService` (single consumer, own DI scope per job) →
`DatasetIngestionService.IngestAsync`. Each parser is an `IAsyncEnumerable<DatasetItemDto>`;
`WriteInChunksAsync` drains it in 2,000-row chunks into `ParquetItemRepository.AddRangeAsync`,
which appends a row group to the current shard (`dataset_{id}_shard_{n}.parquet`, 10M rows per
shard). Status and the cumulative item count are written at the end; on failure the dataset is
marked Failed with a message and already-written rows are kept.

**Why.** Streaming parsers so a multi-GB file never materialises. A queue instead of
`Task.Run` so background work owns its own DI scope. Per-entry ZIP containment checks instead
of trusting `ExtractToDirectory`. Partial writes are kept because re-ingesting into an
existing dataset is supported and rolling back would destroy earlier data.

**Gaps.** `.parquet` upload is *not* supported despite earlier docs (I-6). The queue is
in-memory: a restart loses queued jobs and strands datasets in Pending/Processing (I-1).
Strictly serial (I-3). On the
client, the Create page uses `MudStepper`, which doesn't exist in MudBlazor 7.8, so both
"steps" render stacked (U-3); the uploader buffers ZIPs in a `MemoryStream`, so >2 GB fails
despite the 5 GB limit (U-14); the client waits fixed `Task.Delay`s instead of polling status.

### 4.4 HuggingFace streaming — Working

**What.** Paste `owner/name`; discovery lists configs/splits with row counts; pick one; the
viewer pages rows straight from the HuggingFace datasets-server with real thumbnails. Nothing
is downloaded. Gated repos work with a personal access token (sent per request, stored only in
the browser).

**How.** `POST /api/datasets/huggingface/discover` → `HuggingFaceDiscoveryService` (Hub
`/api/datasets/{repo}` + datasets-server `/size`, `/rows` probe fallback). Import stores
repo/config/split on the dataset and marks it Completed. `GetDatasetItems` maps
`/rows?offset=&length=` responses to `DatasetItemDto` (image URL sniffed from common column
names or `{src:…}` objects), with `ThumbnailUrl` pointing at `/api/datasets/{id}/hf-thumbnail?offset=n`.
That endpoint re-resolves the row through `HuggingFaceRowPageCache` (100-row pages, 5-min
TTL), fetches the image server-side, resizes via `ThumbnailService`, and caches on disk.

**Why.** Server-side thumbnailing keeps HF's signed CDN URLs and tokens off the client and lets
the grid request width-bounded JPEGs. The row cache turns 100 thumbnail requests for one
screen into one upstream `/rows` call. Refusing client-supplied image URLs prevents SSRF.

**Resolution (PR 1.6).** A streaming import with no config/split picks the `train` split (or the
first one listed), persists that config/split on the dataset, and records its row count. Each
page request forwards the user's token to `/size` and `/rows`, and back-fills the dataset's
count from `num_rows_total` when they differ. `/size` reports the selected split's count, not
the sum over all splits. Upstream failures return `403` for an auth refusal (401/403) and
`502` for anything else, with the reason in `error`; they are no longer an empty `200`.

**Gaps.** Thumbnails fetched with one user's token are cached for everyone who can read the
dataset (H-5).

### 4.5 HuggingFace download/import — Partial

**What.** Non-streaming import: download one data file from the repo and ingest it locally.

**How.** `DatasetIngestionService.ImportFromHuggingFaceAsync` non-streaming branch:
`HuggingFaceClient.GetDatasetInfoAsync` (non-recursive tree), pick `DataFilePath` or the first
`.parquet`/`.csv`, stream to `Storage:UploadPath`, then `IngestAsync`.

**Gaps.** The auto-pick prefers `.parquet`, which `IngestAsync` can't parse (I-6); the tree
listing is non-recursive so repos with `data/` folders report "no suitable file" (I-7); no size
cap on the download (H-4). Treat this path as experimental.

### 4.6 S3-compatible buckets — Working

**What.** Operators define storage profiles in configuration (endpoint, keys, allowed
buckets). Users attach a dataset to `profile + bucket + prefix`; the viewer lists objects live
and the API proxies images and thumbnails. Works with AWS S3, MinIO, R2, Backblaze.

**How.** `Services/Storage/{S3StorageProfileResolver,S3StorageService,S3KeySafety}.cs`;
`DatasetEndpoints.ImportFromS3/GetS3File`; items page = `ListObjectsV2` with the continuation
token as cursor, filtered to image extensions.

**Why.** Proxying rather than presigned URLs makes the dataset permission check and the
thumbnail cache the single enforcement point. Bucket allow-lists exist because registration
is open.

**Gaps.** A public S3 dataset is an anonymous, unmetered egress proxy for the bucket (S-1);
thumbnail generation buffers whole objects (S-2); list failures are swallowed silently (S-4).

### 4.7 Server folder import — Working, admin-only

**What.** Point a dataset at a directory already on the server; images are catalogued in
place and served from there.

**How.** `POST /{id}/import-folder` (`RequireRole("Admin")`) → `IngestFromFolderAsync`:
`Directory.EnumerateFiles` lazily, items get `/api/datasets/{id}/files/{relative}` URLs,
`StoragePath` is set so `ServeDatasetFile` serves from the folder with containment checks.

**Why.** Admin-only because it reads arbitrary server paths; copying was rejected because
"point at a folder" is the whole point for huge local collections.

**Gaps.** No path allow-list and `ServeDatasetFile` has no extension allow-list, so an admin
can expose non-image files (F-1); directory symlinks escape containment (F-2); `SourceType`
isn't set to `LocalFolder` (F-3).

### 4.8 Thumbnails — Working

**How.** `ThumbnailService` (SkiaSharp): width clamped 32–1024, never upscales, always JPEG
q80, disk cache keyed by SHA-256 of the source key with two-level fan-out, per-key dedupe
gate plus a global `cores/2` generation semaphore, atomic write-then-rename. Used by local
files (`?w=`), S3 objects and HF rows.

**Why.** A cold virtualised grid issues a burst of resize requests; bounding concurrency keeps
the API responsive. JPEG-only keeps one content type.

**Gaps.** No eviction or size cap (T-1); local cache key ignores file mtime (T-2); originals
are fully buffered before decode (T-3).

### 4.9 Virtualised viewer — Working

**What.** Grid, list and gallery modes; a right-hand detail panel; a lightbox; favourite
toggle; inline title edit; an ingestion status band that polls while processing; optional
IndexedDB page cache across reloads.

**How.** `Features/Datasets/Pages/DatasetViewer.razor(.cs)` → `ViewerContainer` →
`VirtualizedItemView` (Blazor `<Virtualize>` over *rows* of N cards, N from container width
via a `ResizeObserver` interop) → `ImageCard` / `ImageListRow`. `DatasetCacheService` keeps a
sliding window of pages from `DatasetApiClient`, prefetching near the end and evicting at the
front; `IndexedDbCache` (Dexie) optionally persists pages.

**Why.** Row virtualisation keeps a responsive N-column grid inside a single `<Virtualize>`.
Placeholder colour + lazy `<img>` give instant layout with zero shift.

**Gaps.** The cache prefetch cascade keeps loading pages in the background until the whole
dataset (up to 100k items) is in memory, regardless of scrolling (U-5). The IndexedDB toggle
uses a v6 parameter name and never fires (U-4). Component `<style>` blocks are emitted once
per card instance (U-1). Grid column count is fixed at 4 (no settings UI changes it). The
"Resume viewer" dashboard button opens the viewer with no dataset. Delete has no confirmation.

### 4.10 Item editing — Partial

**What.** Edit title, description, tags, favourite and metadata on one item; add/remove tags,
set favourite, add metadata on many.

**How.** `PATCH /api/items/{id}` and `/api/items/bulk` → `ParquetItemRepository.UpdateItemsAsync`:
group by dataset, under that dataset's lock read each shard, patch rows, delete and rewrite
the shard. Writes for one dataset are serialised; other datasets are unaffected.

**Why.** Parquet is immutable, so updates are rewrites; limiting the rewrite to touched shards
is the cheapest correct option without a delta store.

**Gaps.** In the two-process dev setup every edit fails: `ItemEditService` uses the raw
`HttpClient` bound to the client origin (:5002) instead of the typed API client, so `PATCH`
goes to the wrong host (U-2); it works only in the single-origin production container. Only
local datasets are editable — streaming items get a fresh `Id` per page so a PATCH always 404s
(E-3). Item lookup by id scans every dataset on the server (P-2); a rewrite loads a whole shard
(up to 10M rows) into memory and holds the lock, blocking reads (P-3); delete-then-write is
not atomic (P-4).

### 4.11 Filtering and search — Placeholder

**What.** The viewer's filter panel offers text search, tags, dimensions and date range; the
library filters by status/source and search text.

**How.** `FilterState` collects criteria, but `DatasetViewer.ApplyFiltersQuiet` assigns the
unfiltered window straight through, so the grid never changes — only the count label does
(U-6). Tag checkboxes use v6 parameter names and never fire. The API's items endpoint accepts
no filter parameters, although `ParquetItemReader.MatchesFilter` exists unreachably. The
library's "Only ready datasets" switch doesn't re-filter.

**Gaps.** Real filtering needs server-side support (G-2) and a client that applies it.

### 4.12 Settings, theme, language — Placeholder

**What.** A Settings page with theme, view preferences, API keys (HuggingFace token stored in
browser local storage).

**How.** `ViewState` holds preferences and has `LocalStorage` load/save methods that nothing
calls; `MainLayout` builds the `MudTheme` and starts dark because `ViewSettings.Theme`
defaults to Dark. `ThemeSelector` uses v6 parameter names, so it renders without an icon and
never fires; the Settings handlers are no-ops; the page shows TODO text to users. Only the
API-key panel persists anything. Translations (`wwwroot/translations/*.json`) are not loaded
by anything.

### 4.13 AI tools — Placeholder

Nav entry and page explicitly say "Coming soon". Needs a model-serving decision first.

### 4.14 Extensions — Partial

Manifest-discovered API plugins (`src/Extensions`). One real example (`CoreViewer`) with two
placeholder endpoints and no auth; the other four manifests have no code. No client-side
loading. See `docs/architecture.md` §6 and audit X-1…X-4.

### 4.15 Production deployment — Working

One image builds the API and publishes the client into it; Postgres and Caddy (automatic TLS)
via `docker-compose.prod.yml`; secrets via `.env`. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

---

## 5. Project layout

| Path | Target | Role |
|---|---|---|
| `src/Core` | net10.0 | Domain models, shared abstractions, a client-era parser/filter library (largely unused by the API) |
| `src/DTO` | net10.0 | Wire contracts shared by API and client |
| `src/APIBackend` | net10.0 | Minimal API, EF Core 10/Npgsql, Parquet storage, ingestion, HF/S3 integration |
| `src/ClientApp` | net10.0 | Blazor WASM UI (MudBlazor 7.8) |
| `src/Extensions` | net10.0 | SDK, SDK.Api, built-in manifests |

All projects target net10.0 (audit C-4). Package versions are pinned, and each project keeps a committed `packages.lock.json` (audit C-9). Restore with `dotnet restore` and review the lock-file diff when you change a package.
| `tests/` | — | `APIBackend.Tests` (xUnit, Testcontainers Postgres and S3Mock), `ClientApp.Tests` |

---

## 6. Configuration

API settings: `src/APIBackend/Configuration/appsettings.json` + `appsettings.{Environment}.json`,
then environment variables (`ConnectionStrings__DatasetStudio`, `Cors__AllowedOrigins__0`,
`ADMIN_*`, `Storage__S3StorageProfiles__0__*`), then command line. Keys:

- `ConnectionStrings:DatasetStudio` — PostgreSQL. Required outside Development; startup fails without it.
- Listen address — `launchSettings.json` (`http://localhost:5000`) for `dotnet run`; `ASPNETCORE_URLS` in Docker.
- `Cors:AllowedOrigins` — must list the client origin; empty is a startup error.
- `Storage:DatasetRootPath|BlobPath|ThumbnailPath|UploadPath|DataProtectionKeysPath`.
- `Storage:S3StorageProfiles[]` — `Name, ServiceUrl, AccessKey, SecretKey, Region, ForcePathStyle, AllowedBuckets[]`.
- `Extensions:BuiltInPath|CommunityPath|DisabledExtensions`.

Client: `src/ClientApp/wwwroot/appsettings.json` (`DatasetApi:BaseAddress`, blank = same
origin) with a Development overlay pointing at `http://localhost:5000`.

---

## 7. Testing

```bash
dotnet test                          # both projects; APIBackend.Tests needs Docker (Testcontainers)
dotnet test tests/ClientApp.Tests    # no Docker
```

Integration tests spin up a real Postgres container and, for the S3 tests only, an Adobe S3Mock
container (`adobe/s3mock`, pinned; MinIO is no longer pullable anonymously). Coverage is strongest on
endpoints, security checks (`ServeDatasetFileSecurityTests`, `S3KeySafetyTests`) and
streaming ingestion; the client has almost no tests (see the audit).

---

## 8. The plan

The code works end to end but has drifted: three page styles, two `IDatasetItemRepository`
interfaces, a dead `Core` library, config that only works in Development, and scale paths
(paging, item lookup, shard rewrites) that are O(dataset) where they should be O(page).
The refactor is sequenced in [docs/refactor-audit.md](docs/refactor-audit.md):

1. **Stabilise** — config, dependency vulnerabilities, repo hygiene, the handful of
   correctness bugs users hit today (HF item count, `/upload` dead links, default timestamps).
2. **Design system** — build the shared components in DESIGN.md and migrate every page;
   delete dead client code (translations, layout registry, unused components).
3. **Durable ingestion** — persist the queue, reconcile on startup, bound uploads.
4. **Storage scale** — row-group skipping, an item index, atomic shard rewrites, bounded
   caches.
5. **Security hardening** — rate limiting, permission grants, file-serving allow-lists,
   cookie revalidation.
6. **Feature completion** — server-side filtering, settings that persist, editing for
   streaming sources via a local overlay.

Each step ships as its own PR with tests and a browser verification; documentation changes
land with the behaviour they describe.

---

## License

MIT — see [LICENSE](LICENSE).
