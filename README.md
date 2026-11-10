# Dataset Studio

A self-hosted web app for **browsing, curating and editing very large AI image datasets** —
the kind that don't fit in memory, often don't fit on one disk, and frequently live somewhere
else (HuggingFace, an S3 bucket) that you'd rather not copy.

ASP.NET Core API + Blazor WebAssembly client. PostgreSQL for users/datasets/permissions,
Apache Parquet segments on disk for dataset items, and on-demand proxying for remote sources.

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
                    │  ingestion_jobs → IngestionBackgroundService     │
                    │  ThumbnailService (SkiaSharp, disk cache)        │
                    └───┬──────────────┬───────────────┬───────────────┘
                        │              │               │
             ┌──────────▼───┐  ┌───────▼────────┐  ┌───▼─────────────────────┐
  Storage    │ PostgreSQL   │  │ Parquet segs.  │  │ Remote, proxied on demand│
             │ users        │  │ dataset items  │  │ HuggingFace datasets-   │
             │ datasets     │  │ + manifest     │  │   server (/rows, /size)  │
             │ permissions  │  │ + served files │  │ S3-compatible buckets    │
             └──────────────┘  └────────────────┘  └──────────────────────────┘
```

**A dataset is a row in Postgres** (name, status, source type, counts, owner, visibility) plus
one of three item backends chosen by `SourceType`:

| Source type | Where items live | Where images live |
|---|---|---|
| `LocalUpload` (CSV/TSV/JSON/JSONL/Parquet/ZIP) | Parquet segments under `Storage:DatasetRootPath` | URLs in the file, or extracted ZIP images served by the API |
| `LocalFolder` (admin only) | Parquet segments | The original folder, served by the API |
| `HuggingFaceStreaming` | Not stored; each page is a `/rows` call | HuggingFace CDN, thumbnails proxied + cached by the API |
| `ExternalS3Streaming` | Not stored; each page is a `ListObjectsV2` call | The bucket, proxied + thumbnailed by the API |

**Every page request is bounded.** Local: an `r{offset}` cursor and one row group at a time.
HuggingFace: an integer offset against `/rows`. S3: the continuation token. The client asks for
the next page when the virtualised grid nears its buffer edge, and evicts old pages.

**Ingestion is asynchronous.** Uploads return `202` as soon as the file is on disk and a job row
is in `ingestion_jobs`; background workers claim jobs, parse each as a stream and write Parquet in
batches; the dataset's `Status` moves Pending → Processing → Completed/Failed, its item count
rises as batches land, and the client polls it. Queued jobs survive a restart.

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
| 4.12 | Settings, theme, language | Partial (theme, viewer preferences, HuggingFace token; no i18n) |
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

**What.** `/datasets` (`/my-datasets` still works) lists what you may see: your own plus public
datasets (anonymous visitors see public only). Cards (`DatasetCard`) show status, source,
counts and when the dataset was last updated, with Open and a confirmed Delete; `LibraryFilterBar`
searches and filters the loaded page by status, source and "only ready". New datasets are
private by default; `IsPublic` makes them visible to everyone.

**How.** `DatasetEndpoints.GetAllDatasets/GetDataset/CreateDataset/DeleteDataset`,
`DataAccess/PostgreSQL/Repositories/DatasetRepository.cs`, `Services/Auth/DatasetPermissionService.cs`.
A `DatasetVisibilityFilter` is built from the principal and applied as SQL `WHERE`, so pages
are never under-filled by post-filtering. `CanRead/CanWrite/CanDelete` consult owner, admin
role, and the `permissions` table. Updates are patches: `UpdateAsync(original, updated)` writes only the fields the caller
changed, status and item-count writes are single `UPDATE` statements, and a field someone else
changed to a different value in the meantime is refused with `409` (the row's `xmin` is the
concurrency token). The source selection (type, HuggingFace repo/config/split, S3
profile/bucket/prefix, folder) is one unit: changing any of it while another request changed
any of it is refused as a whole, so no request can store a mix nobody chose. A full-row overwrite from a stale snapshot used to revert background
ingestion's status and count (D-4).

**Why.** 404-not-403 for private datasets mirrors GitHub's handling of private repos. The
filter lives in SQL so counts and pages agree.

**Gaps.** The `permissions` grant table has no write path — sharing a private dataset with a
specific user is impossible from the API (D-2).

### 4.3 Upload ingestion — Working

**What.** Create a dataset, upload a `.csv`, `.tsv`, `.json` (array), `.jsonl`/`.ndjson`,
`.parquet`, or `.zip` (up to 5 GB). Column names are matched loosely (`image_url|url|image`,
`title|name|caption|text`, `description`, `id`, `width`, `height`, `tags`); every column is
kept in item metadata. ZIP members that are images (JPEG, PNG, GIF, BMP, WebP) are extracted
into the dataset's storage folder and served by the API, with their dimensions read from the
header; data files inside a ZIP are parsed as well, and a row whose `file_name` (HuggingFace
imagefolder's `metadata.csv`/`metadata.jsonl`) or image column names an extracted image is
linked to it. Parquet flat columns become fields; an `image: {bytes, path}` struct
(HuggingFace's `Image` feature) is written out as an image file and served.

**How.** `DatasetEndpoints.UploadDatasetFile` → staged file under `Storage:UploadPath` → a row in
`ingestion_jobs` (`IIngestionJobQueue`) → `IngestionBackgroundService` →
`DatasetIngestionService.IngestAsync`. Each parser is an `IAsyncEnumerable<DatasetItemDto>`;
`WriteInChunksAsync` drains it in 10,000-row chunks into `ParquetItemRepository.AddRangeAsync`,
which writes each chunk as a new immutable segment committed by the dataset's manifest (see
`DataAccess/Parquet/README.md`), and updates the dataset's item count after each chunk so the viewer shows progress. On
failure the dataset is marked Failed with a message and already-written rows are kept.

The queue (`IngestionJobStore`) is a Postgres table. `Ingestion:MaxConcurrentJobs` workers (default
2) claim the oldest queued job with one `UPDATE … FOR UPDATE SKIP LOCKED … RETURNING`; a partial
unique index allows one running job per dataset, because ingestion appends to its shards. An
enqueue wakes a worker at once; idle workers also poll every `Ingestion:PollInterval` as a safety net. At startup
the worker host recovers jobs the previous process left running: a job that wrote nothing is
requeued (up to `Ingestion:MaxAttempts`), one that wrote items is failed with the count it kept,
and a dataset left Processing with no job is failed. When `Ingestion:MaxQueuedJobs` jobs are
staging, queued or running (default 50), imports get `503` with `Retry-After`. Admission takes
a Postgres advisory lock, so concurrent requests can't all take the last slot, and an upload
reserves its slot (state Staging, never claimed) before writing a byte, then commits it once the
file is staged or releases it and deletes the file if staging fails. A HuggingFace access token
waits in the job encrypted with Data Protection and is removed when the job finishes. The staged
file is deleted when the job completes or fails for good, when its dataset is deleted while the
job is still queued, or at startup if the upload was cut off mid-write.

**Why.** Streaming parsers so a multi-GB file never materialises. A queue instead of
`Task.Run` so background work owns its own DI scope; a table instead of an in-memory channel so a
restart doesn't lose queued work or strand datasets in Pending/Processing (I-1). Recovery
assumes one API instance per database, which is how Dataset Studio is deployed. Per-entry ZIP containment checks instead
of trusting `ExtractToDirectory`. Partial writes are kept because re-ingesting into an
existing dataset is supported and rolling back would destroy earlier data.

**Gaps.** Parquet list and nested columns other than an image struct are skipped. The queue is
in-memory: a restart loses queued jobs and strands datasets in Pending/Processing (I-1).
Strictly serial (I-3).

**Client.** `/datasets/create` has one form per source. Upload takes one CSV, TSV, JSON, JSONL,
Parquet or ZIP file (drop or choose), streams it to `POST /api/datasets/{id}/upload` without an in-memory
copy, and opens the viewer as soon as the server accepts it; the viewer's status band follows
ingestion. HuggingFace streaming waits for the real status (not a fixed delay) to offer an
import when the repository can't be streamed. The browser still buffers the request body once
(fetch request streaming is not used), so very large uploads depend on browser memory.

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
TTL, at most 512 pages, least recently used evicted; each page is fetched on its own 30 s timeout
so one caller cancelling never fails the others), fetches the image server-side, resizes via `ThumbnailService`, and caches on disk.

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
`HuggingFaceClient.GetDatasetInfoAsync` lists the repository tree recursively (following the
Hub's `Link` pagination), then `DataFilePath` or the first parseable file is chosen (Parquet,
then JSONL, CSV, TSV, JSON; a `train` file first). A file larger than `Ingestion:MaxDownloadBytes`
(default 5 GB, the upload limit) is refused from its listed size before anything is fetched, and
the download stops if it passes the limit anyway. The file is streamed to `Storage:UploadPath`,
parsed with `IngestAsync`, and deleted whether or not parsing succeeds.

**Gaps.** One file per import, so a dataset split across many shards imports only the first;
no per-user quota beyond the size limit (H-4).

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
Containment compares real paths with every symlink in the path resolved (`RealPath`), so a
symlinked directory inside the folder can't reach outside it, while a folder that is itself
reached through a symlink still works. `ServeDatasetFile` (and `GetS3File`) serve only image files (`.jpg`, `.jpeg`, `.png`, `.webp`,
`.gif`, `.bmp`, see `ServableImageTypes`); any other file is answered 404. Refusals (wrong type,
path or symlink outside the folder, S3 key outside the prefix) are logged as warnings with the
dataset id and requested path.

**Why.** Admin-only because it reads arbitrary server paths; copying was rejected because
"point at a folder" is the whole point for huge local collections.

The dataset's `SourceType` becomes `LocalFolder`; each item's width and height are read from
the image header; item metadata holds only the path relative to the folder. TIFF files are
skipped: nothing else in the app serves or thumbnails them as images.

**Gaps.** No path allow-list, so an admin can point a dataset at any directory (F-1).

### 4.8 Thumbnails — Working

**How.** `ThumbnailService` (SkiaSharp): width clamped 32–1024, never upscales, always JPEG
q80, disk cache keyed by SHA-256 of the source key with two-level fan-out, per-key dedupe
gate plus a global `cores/2` generation semaphore, atomic write-then-rename. Used by local
files (`?w=`), S3 objects and HF rows. The served width (after clamping) is returned in
`X-Thumbnail-Width`. The local-file key includes the file's size and write time, so a replaced
file gets a new thumbnail. Originals are read with a byte limit (`Thumbnails:MaxSourceBytes`,
64 MB) and refused from the image header past `Thumbnails:MaxSourcePixels` (100 MP) before
any pixels are decoded; large JPEGs decode at reduced scale. Over a limit, local and S3 files
are served as the original and HF rows 404. The cache is capped at `Thumbnails:MaxCacheBytes`
(2 GB): a sweep at startup and every `SweepEveryWrites` (500) writes deletes the oldest files
(cache hits refresh a file's age hourly).

**Why.** A cold virtualised grid issues a burst of resize requests; bounding concurrency keeps
the API responsive. JPEG-only keeps one content type.

**Gaps.** The size cap is enforced by periodic sweeps, so the cache can briefly exceed it
between sweeps.

### 4.9 Virtualised viewer — Working

**What.** Grid, list and gallery modes; a right-hand detail panel; a lightbox; favourite
toggle; inline title edit; an ingestion status band that polls while processing; optional
IndexedDB page cache across reloads.

**How.** `/datasets/{id}` (`/dataset-viewer?id=` still works) is
`Features/Datasets/Pages/DatasetViewer.razor(.cs)`: `IngestionStatusBanner`, `ViewerToolbar`,
optional `FilterPanel`, then `ViewerContainer` → `VirtualizedItemView` (Blazor `<Virtualize>`
over *rows* of N cards; N from the container width via `GridLayout.ColumnsFor`, min card 180 px,
capped by the "Grid columns" setting; width changes arrive debounced from a `ResizeObserver`)
→ `ImageCard` / `ImageListRow`, and `DetailSidebar` (a column at `md`+, a bottom sheet below).
The next page is fetched only when an end-of-list sentinel comes within 800 px of the list's
visible area (IntersectionObserver). `DatasetCacheService` keeps a sliding window of pages from
`DatasetApiClient`, evicting at the front; `IndexedDbCache` (Dexie) optionally persists pages.
Cards and rows are keyboard reachable (Enter or Space opens the item).

**Why.** Row virtualisation keeps a responsive N-column grid inside a single `<Virtualize>`.
Placeholder colour + lazy `<img>` give instant layout with zero shift.

**Gaps.** Client-side filters are not applied (U-6, needs server-side filtering, PR 4.4).
Gallery mode renders like the grid.

### 4.10 Item editing — Partial

**What.** Edit title, description, tags, favourite and metadata on one item; add/remove tags,
set favourite, add metadata on many.

**How.** `PATCH /api/items/{id}` and `/api/items/bulk` (the client sends the dataset id) →
`ParquetItemRepository`: the item is found through its dataset's segment id indexes, then
`UpdateItemsAsync` rewrites only the segments holding an edited item, one row group at a time,
into a temp file renamed over the segment. Writes for one dataset are serialised; other datasets
are unaffected.

**Why.** Parquet is immutable, so updates are rewrites; limiting the rewrite to touched segments
(10,000 rows each from ingestion, up to about 2M after merging) is the cheapest correct option
without a delta store.

**Gaps.** Only local datasets are editable — streaming items get a fresh `Id` per page so a
PATCH always 404s (E-3). The dataset lock is held for the rewrite, so reads of that dataset wait.

### 4.11 Filtering and search — Placeholder

**What.** The viewer's filter panel offers text search, tags, dimensions and date range; the
library filters by status/source and search text.

**How.** `FilterState` collects criteria, but `DatasetViewer.ApplyFiltersQuiet` assigns the
unfiltered window straight through, so the grid never changes — only the count label does
(U-6). Tag checkboxes use v6 parameter names and never fire. The API's items endpoint accepts
no filter parameters, although `ParquetItemReader.MatchesFilter` exists unreachably. The
library's "Only ready datasets" switch doesn't re-filter.

**Gaps.** Real filtering needs server-side support (G-2) and a client that applies it.

### 4.12 Settings, theme, language — Partial

**What.** A Settings page with theme (Light, Dark or System), view preferences (view mode,
grid columns, hover details) and the HuggingFace access token, each in a `SectionCard`. The
token is kept in browser local storage and sent to the API only with HuggingFace item requests
(`X-HF-Access-Token`), which forward it upstream. The drawer also has a one-click light/dark switch.

**How.** `Shared/Theme/AppTheme.cs` is the one `MudTheme` (DESIGN.md §2.2); app-level tokens
are `--ds-*` custom properties in `wwwroot/css/app.css`. `MainLayout` loads `ViewState` from
LocalStorage on start, resolves the stored `ThemeMode` with `ThemeModeResolver` (System reads
and then watches `prefers-color-scheme` through `MudThemeProvider`), and every change is saved
back immediately. A small inline script in `index.html` reads the same saved value so the boot
screen matches the theme before Blazor loads.

**Why.** Dark is the default (`ViewSettings.Theme`); light must be complete. Persisting in the
browser keeps preferences per device without an account round-trip.

**Gaps.** No localisation (the unused translation files were removed, U-18). The high-contrast `ThemeMode` member is unimplemented and falls back to dark.

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
- `Ingestion:MaxDownloadBytes` — largest file HuggingFace download mode fetches (default 5 GB).
- `Thumbnails:MaxCacheBytes|MaxSourceBytes|MaxSourcePixels|SweepEveryWrites` — thumbnail cache cap and original limits (defaults 2 GB, 64 MB, 100,000,000 px, 500).
- `Ingestion:MaxConcurrentJobs|MaxQueuedJobs|MaxAttempts|PollInterval` — background import limits (defaults 2, 50, 3, `00:01:00`).
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
