# Dataset Studio — Refactor Audit

**Date:** 2026-11-06 · **Baseline:** `main` @ `9fa0db7` · **Method:** full read of every
source, config, script and doc in the repo, a forced `dotnet build` (48 client warnings), and
a live run (Docker Postgres, API on :5000, client on :5002) exercising register/login, a CSV
upload and a HuggingFace streaming import. Every item has a file reference. Nothing here has
been fixed yet; this document is the backlog and the sequencing for doing so.

Companion documents: [README.md](../README.md) (features, how/why),
[DESIGN.md](../DESIGN.md) (the UI target), [AGENTS.md](../AGENTS.md) (conventions).

**Severity:** *critical* = breaks the product or leaks data today · *high* = wrong result,
data loss, or unbounded cost on a normal path · *medium* = wrong on an edge or a scale/security
risk · *low* = hygiene, dead code, inconsistency.

**IDs** are stable and referenced from the README: A auth · D datasets/permissions · I
ingestion · P Parquet storage · G paging API · H HuggingFace · S S3 · F folder/file serving ·
T thumbnails · E editing · X extensions · C config/build · R repo/infra · K Core library ·
U client UI · Q tests · W docs.

---

## 0. The short version

Fifteen things that matter most, in the order they should be fixed:

| # | ID | Severity | One line |
|---|---|---|---|
| 1 | C-1 | critical | Base `appsettings.json` ships a placeholder DB password; any non-Development run dies at startup. |
| 2 | C-3 | high | `Microsoft.AspNetCore.Components.WebAssembly.Server 8.0.10` has a known high-severity CVE (NU1903) and is an 8.x package in a net10 host. |
| 3 | U-2 | high | Item editing is broken in the two-process dev setup: `ItemEditService` uses the raw origin-bound `HttpClient`, so every PATCH goes to :5002. |
| 4 | U-4 | high | ~20 MudBlazor v6 parameter names silently do nothing in v7: IndexedDB toggle, tag filters, theme toggle, view preferences, filter chips never fire. |
| 5 | U-3 | high | `MudStepper`/`MudSegmentedControl` don't exist in MudBlazor 7.8; the Create page renders as unknown HTML. |
| 6 | H-1 | high | HF streaming datasets show 0 items in the library: null config/split from the client, `Ordinal` null match, tokenless `/size`, no back-fill from `/rows`. |
| 7 | I-1 | high | Ingestion queue is in-memory; a restart loses jobs and strands datasets in Pending/Processing forever. |
| 8 | U-5 | high | Client prefetch cascade loads the whole dataset (≤100k items) into memory in the background regardless of scrolling. |
| 9 | P-1/P-2/P-3/P-4 | high | Paging is O(offset), item lookup is O(all items on server), edits rewrite whole 10M-row shards in memory, and the rewrite is not atomic. |
| 10 | R-1/R-2 | high | 53 `obj/`/`bin/` files are tracked under `src/Extensions` while `.gitignore` ignores that whole tree, hiding new source files from `git add`. |
| 11 | R-5/Q-2 | high | `minio/minio` (untagged, Docker Hub) can no longer be pulled; `start.sh` aborts and all 52 Docker-backed tests fail at fixture start. |
| 12 | U-7 | medium | Three "Upload" buttons link to `/upload`, which has no page. |
| 13 | U-9 | medium | With the API down the client throws out of the auth provider into the fatal "unhandled error" banner. |
| 14 | D-2 | medium | The permission-grant system has no write path; private datasets cannot be shared. |
| 15 | K-1/U-11 | medium | ~22 Core types and 11 client components are dead; four Core services are DI-registered for nothing. |

---

## 1. Config, build, packages (C)

| ID | Sev | Where | Problem |
|---|---|---|---|
| C-1 | critical | `src/APIBackend/Configuration/appsettings.json:11` | `Database=dataset_studio;Password=your_password_here`. `Program.cs:29-44` rebuilds the config chain so only `Configuration/appsettings.{Env}.json` and env vars can override; there is no `appsettings.Production.json`. Observed: `dotnet run --no-launch-profile` → `28P01` → exit. Fix: no credentials in the base file; require `ConnectionStrings__DatasetStudio` and fail with a clear message; keep dev values in the Development overlay. |
| C-2 | medium | `src/APIBackend/appsettings.json`, `appsettings.Development.json` | Second, stale config pair outside `Configuration/`. Not loaded at runtime, but `DatasetStudioDbContextFactory.cs:17-25` *does* load them (and always layers Development). Delete them; make the factory use the same chain as `Program.cs`. |
| C-3 | high | `src/APIBackend/APIBackend.csproj:13` | `WebAssembly.Server 8.0.10` → NU1903 (GHSA-qjpx-jqm5-h2c4). Move to the 10.0.x package matching the host. |
| C-4 | medium | `APIBackend.csproj:4,37-46` | net10.0 host with EF Core 8.0.11 / Npgsql 8.0.11 / Swashbuckle 6.5.0 and every referenced project on net8.0. Unsupported matrix; EF 8 LTS ends Nov 2026. Plan: everything to net10 + EF 10 in one PR, or everything to net8 LTS — not a mix. |
| C-5 | low | `Program.cs:76-83, 136` | Two throwaway service providers pre-build (ASP0000). Forced by extension `ConfigureServices`; revisit with X-1. |
| C-6 | medium | `Program.cs:46-58` | 5 GB `MaxRequestBodySize` applies to every endpoint; `MultipartHeadersLengthLimit`/`ValueLengthLimit = int.MaxValue` remove DoS guards. Scope the limit to the upload endpoint via `RequestSizeLimit`/`IHttpMaxRequestBodySizeFeature`. |
| C-7 | low | `Configuration/appsettings.json:9` | `Urls=http://localhost:5000` loopback default; only works in Docker because `ASPNETCORE_URLS` overrides. Document or drop. |
| C-8 | low | `Program.cs:286` | `MapFallbackToFile("index.html")` has no `/api` exclusion → unknown `/api/*` returns 200 + HTML. Use `MapFallbackToFile("{*path:nonfile}", …)` excluding `/api`. |
| C-9 | medium | all csproj | Floating versions (`8.0.*`, `33.*`, `7.8.*`, `4.5.*`) with no `packages.lock.json` → non-reproducible restores. Pin and commit lock files. |
| C-10 | low | `CoreViewer.Api.csproj:21`, `Creator.Api.csproj` | `Microsoft.AspNetCore.Http.Abstractions 2.2.0` (out of support, redundant with the shared framework). Remove. |
| C-11 | low | test csproj | `Microsoft.NET.Test.Sdk 17.8.0`, `xunit 2.4.2` are old; update with C-9. |
| C-12 | low | `src/ClientApp/ClientApp.csproj:29-30` | References `Extensions.SDK` "used by ClientExtensionRegistry" — that class was deleted in Phase 7. Remove the reference and the comment. |

## 2. Repository and infrastructure (R)

| ID | Sev | Where | Problem |
|---|---|---|---|
| R-1 | high | `git ls-files src/Extensions` | 53 tracked files under `obj/` and `bin/` (SDK, SDK.Api, CoreViewer.*, Creator.*); 28 show as modified after every build. `git rm -r --cached` them. |
| R-2 | high | `.gitignore:84,86` | `/src/Extensions` ignored wholesale (twice) — new extension source is invisible to `git add`. Replace the per-project list with generic `**/bin/`, `**/obj/`, `TestResults/`, `*.user`, `.vscode/`; drop the `HartsysDatasetEditor.*` entries (57-66). |
| R-3 | medium | `.gitignore:82` | `/uploads` is root-anchored; `src/APIBackend/uploads` (the real `Storage:UploadPath`) is not ignored. |
| R-4 | low | `Directory.Build.props:9`, `app.css:1`, `interop.js:1,223`, `en.json:3`, `indexeddb-cache.js` DB name, `StorageKeys` `hartsy_*`, `ApiKeySettingsPanel.razor:9,23` | Product still named "Hartsy's Dataset Editor" / "Hartsy" in eight places; UI says both "DatasetStudio" and "Dataset Studio". Settle on "Dataset Studio" (DESIGN.md §7). |
| R-5 | high | `docker-compose.yml:46` | `image: minio/minio` untagged. Verified: Docker Hub `minio/minio` is no longer anonymously pullable (manifest inspect → denied; tags API → 404). Pin a verified registry+tag (e.g. `quay.io/minio/minio:RELEASE.…` after confirming with `docker pull`) and document it. |
| R-6 | medium | `start.sh:101` | `compose up -d postgres minio` under `set -e` — a MinIO pull failure aborts before the API starts, so the whole dev loop breaks for a dev-only S3 fixture. Start Postgres first, MinIO best-effort. `--stop-db` and `stop.sh` also miss the `dev` profile, leaving MinIO running. |
| R-7 | low | `README.md` (old) `docker compose up -d` | Started the `api` container too, racing `dotnet run` on migrations. README now says `up -d postgres`. |
| R-8 | medium | `docker-compose.prod.yml:23` | Production connection string still `Database=dataset_studio_dev;Include Error Detail=true`. |
| R-9 | low | `.env.example:24-28`, `DEPLOYMENT.md:68-69` | `S3_PROFILE_*` variables are consumed by nothing; operators must write `Storage__S3StorageProfiles__0__*` by hand. Either wire them in the overlay or remove them. |
| R-10 | low | `Dockerfile:74` | CoreViewer publish copies duplicate `Core.dll`/`DTO.dll`/`Extensions.SDK*.dll` beside the extension; harmless only because the loader has no dependency resolver (X-2). |

## 3. Client UI (U) — see DESIGN.md for the target

### 3.1 Broken behaviour

| ID | Sev | Where | Problem |
|---|---|---|---|
| U-2 | high | `Features/Datasets/Services/ItemEditService.cs`, `Configuration/Program.cs:31` | Uses the raw `HttpClient` (base = client origin, no cookie handler). In dev, `PATCH /api/items/{id}` hits :5002 → every edit fails with "Failed to update title". Use `DatasetApiClient`. `datasetState` ctor param unread (CS9113); state update commented out (:55-57) so even a successful edit doesn't refresh the card. |
| U-3 | high | `Pages/CreateDataset.razor:9,10,76`; `Settings/Components/ViewPreferences.razor:7,13`; `Components/ViewerContainer.razor:44` | `MudStepper`, `MudStep`, `MudSegmentedControl`, `MudSegmentedControlItem`, `TextGrid` don't exist (RZ10012). Stepper renders both steps stacked as raw HTML; `ActiveIndex`/`Disabled` do nothing. Replace with `MudTabs`/`MudToggleGroup`; delete the `TextGrid` branch. |
| U-4 | high | 37 MUD0001/MUD0002 sites (full table in the client survey; summary): `DatasetViewer.razor:131-132` (IndexedDB switch), `FilterPanel.razor:35,46-47` (tag checkboxes), `ThemeSelector.razor:4-8`, `ViewPreferences.razor:26,30-31`, `FilterChips.razor:2,17`, `ErrorBoundary.razor:12,23`, `DatasetLibrary.razor:41,49,57`, `ImageCard.razor:65`, `ImageLightbox.razor:6`, `Index.razor:7,57,84,161`, 11× `MudIconButton Title=` | v6 parameter names on v7 components are silently ignored. Consequences today: the IndexedDB toggle, tag filters, theme toggle and view-preference controls never fire; `FilterChips` renders as a column; `GutterSize` does nothing; no icon button has a tooltip. Fix per DESIGN.md §1.1 and turn MUD0001/MUD0002 into errors. |
| U-5 | high | `Pages/DatasetViewer.razor.cs:96-110`, `Features/Datasets/Services/DatasetCacheService.cs` | `HandleDatasetStateChanged` fires `EnsureBufferedAsync(Items.Count + 120)` on every append; each page raises `OnChange` again → the cascade stops only at `HasMorePages == false` or `MaxBufferedItems = 100_000`. For HF datasets this is an unbounded request chain against HuggingFace. Prefetch only from `VirtualizedItemView.OnNearEnd`. |
| U-6 | high | `DatasetViewer.razor.cs:174-176` (`ApplyFiltersQuiet`) | `_filteredItems = _datasetState.Items` — every filter is a visual no-op; `ViewerContainer` binds `DatasetState.Items` directly. `_onlyReady` in the library never re-filters (`DatasetLibrary.razor.cs:57`). |
| U-7 | medium | `DatasetLibrary.razor:35,150`, `Shared/Components/DatasetSwitcher.razor:120`, `Index.razor.cs` `NavigateToUpload` | Link to `/upload`; no page has that route → "Page not found". Target `/datasets/create`. `NavMenu.razor.cs:66` links recent datasets with `?name=` but the viewer reads only `id`. |
| U-8 | medium | `DatasetLibrary.razor.cs:115` | Dataset delete has no confirmation; `ConfirmDialog` exists unused. |
| U-9 | medium | `Features/Auth/Services/CookieAuthenticationStateProvider.cs:26-31`, `Configuration/App.razor` | `GET /api/auth/me` with no try/catch and the default 100 s timeout. API down → `HttpRequestException` escapes → fatal `#blazor-error-ui` (observed live: "Checking access…" then "An unhandled error has occurred"). Catch → anonymous + "API unreachable" banner; set `HttpClient.Timeout`; wrap `Router` in `ErrorBoundary` (TODO at `App.razor:31`). |
| U-10 | medium | `DatasetCacheService.LoadFirstPageAsync`, `DatasetViewer.razor.cs:97-101,251-255`, `DatasetLibrary.razor:138` | Same failure reported twice (inline alert **and** snackbar); library shows "No datasets yet / Upload your first dataset" when the API call *fails*. |
| U-14 | medium | `Components/DatasetUploader.razor.cs:379` | ZIP buffered into `new MemoryStream((int)Math.Min(file.Size, int.MaxValue))` → >2 GB throws despite `MaxFileSize` 5 GB. Stream the browser file to the API. |
| U-15 | medium | `DatasetUploader.razor.cs:307,546,781,855,867,891,990,1004,1092` | Fixed `Task.Delay(500…5000)` between create → import → load → navigate instead of polling status. `GetFriendlyErrorMessage` (:333) hard-codes `https://localhost:7221`. |
| U-22 | medium | `Services/Caching/IndexedDbCache.cs:16-17,71-75`, `DatasetCacheService.cs:74-77,322-334`, `indexeddb-cache.js:88-91,118,276` | Cursor→page map is in-memory, so after a reload only page 0 is a hit; enabling the cache *clears* it; cached pages fabricate `NextCursor`; pages stored twice; no size cap. Together with U-4 the feature is unreachable. Decide: finish it or remove it. |
| U-24 | low | `Shared/Layout/NavMenu.razor.cs:35-59` | "Recent datasets" loads once at mount; `OnChange` only re-renders. Persist to LocalStorage (its own TODO) or drop. |
| U-28 | medium | `Features/Auth/Pages/Login.razor.cs:31`, `Register.razor.cs:30`, `NavMenu.razor.cs:17-22` | Non-401 failures throw unhandled; `_isSubmitting` stays true. |
| U-30 | low | `DatasetCacheService.MapItems:364-381` | Silently drops items lacking both URLs → count label drifts from server totals. Render a placeholder card instead. |
| A-8 | medium | `Shared/Components/RedirectToLogin.razor:9`, `Login.razor.cs:40` | `returnUrl` is the absolute URI and is navigated to verbatim → open redirect. Accept relative paths only. |

### 3.2 Design drift and duplication (DESIGN.md gaps)

| ID | Sev | Where | Problem |
|---|---|---|---|
| U-1 | medium | `<style>` blocks in `DatasetLibrary.razor:158-169`, `DatasetViewer.razor:173-189`, `DatasetUploader.razor:399-419`, `ImageCard.razor:142-317`, `ImageDetailPanel.razor:238-273`, `ImageLightbox.razor:113-179`, `ImageListRow.razor:47-94`, `VirtualizedItemView.razor:31-50` | Component-local `<style>` is emitted once per instance (hundreds of copies in a grid) and leaks generic class names (`.selected`, `.description`, `.list-item`) app-wide. Move to `.razor.css`. |
| U-13 | medium | `Settings.razor:9,46`, `ThemeSelector.razor:10`, `DatasetInfo.razor:6`, `DatasetStats.razor:6` | Literal "TODO:" sentences rendered to users. Theme switching is non-functional end to end (`MainLayout.ToggleTheme` has no caller, `ViewState.Load/SaveToStorageAsync` never called, `ThemeMode.Auto` unhandled). |
| U-16 | low | `NavMenu.razor:29` "Saved Datasets" vs page "My Datasets" vs dashboard "Library"/"Open library"; "Upload New Dataset"/"Open Creator"/"Use Upload" | Naming drift; fix to DESIGN.md §7. |
| U-17 | low | `DatasetLibrary.razor.cs:145-171`, `DatasetViewer.razor.cs:354-361`, `DatasetLibrary.razor:50-54` | Status→colour, status→severity and source→label are each implemented separately. One `StatusChip`/`SourceBadge`. |
| U-19 | medium | `wwwroot/index.html:5`; `ImageCard.razor:5-13`, `ImageListRow.razor:6`, `ImageDetailPanel.razor:10` (click-only divs); icon buttons without accessible names | `user-scalable=no`; cards and lightbox unreachable by keyboard; no `aria-label`s; `FocusOnNavigate Selector="h1"` never matches. |
| U-20 | medium | `DatasetViewer.razor:158` (`Xs=12 Md=3`, no `sm`) inside `100vh; overflow:hidden`; `DatasetLibrary.razor:22-60` unwrapped rows; `VirtualizedItemView` fixed column count | Below 960 px the detail panel is clipped and unreachable; library filter rows overflow; grid doesn't adapt columns to width. |
| U-21 | medium | `app.css:15` body `#020617`; `app.css:136` light-primary focus ring; `ImageCard.razor:15,124-134` `Color.Surface` icons on black; `ImageDetailPanel.razor:271`; `css/themes/*.css` (not linked); dark palette mixing gray `#1F2937/#111827` with slate | Hard-coded colours that ignore the theme; dead theme CSS. |
| U-23 | low | `index.html:48` | Dexie loaded from `unpkg.com` at runtime; no local copy → offline/CSP failure. Also an unused Material Icons webfont link (:14). |
| U-25 | low | `Index.razor:166-181` | "Getting started" step 3 tells users to use AI tools that don't exist. |
| U-26 | medium | `DatasetUploader.razor` 419 + `.razor.cs` 1,117 lines | One component for four sources with four copies of the progress panel, five copies of the form-reset block, four copies of the create→import→poll→navigate flow, a never-called `ProcessFileAsync` (:244-326), a no-op `HandleDrop` (:101-108), and dead multi-part/enrichment blocks (:419-479, :517-534). Split per DESIGN.md §6. |
| U-27 | low | `DatasetViewer.razor.cs:25-39`, `MainLayout.razor.cs:18-20`, `Index.razor.cs:16-18`, `DatasetUploader.razor.cs:32-70`, `FilterPanel.razor.cs:18-27` | `public` mutable fields and `[Inject] public` properties with `_underscore` names. Make them private. |
| U-29 | low | `VirtualizedItemView.razor.cs:143-152`, `:102-118`, `VirtualizedItemView.razor:26` | `OnContainerResized` re-renders on every ResizeObserver tick (no debounce); `MaybePrefetch` runs inside the render template. |

### 3.3 Dead client code

| ID | Sev | Where | Problem |
|---|---|---|---|
| U-11 | medium | `Shared/Components/{EmptyState,LoadingIndicator,ConfirmDialog,ErrorBoundary,DatasetSwitcher,LayoutSwitcher}`, `Features/Datasets/Components/{SearchBar,FilterChips,DatasetInfo,DatasetStats}`, `Settings/Components/LanguageSelector` | Eleven components rendered nowhere. Adopt the five shared ones (DESIGN.md §4), delete the rest. |
| U-12 | low | `Services/Interop/{FileReaderInterop,LocalStorageInterop,ImageLazyLoadInterop}.cs`; `Extensions/ServiceCollectionExtensions.cs` (`AddClientServices` never called); `wwwroot/js/interop.js` (only `clickElementById` used; `imageLazyLoad.*`/`localStorageInterop.*` JS objects don't exist); `IndexedDbInterop` 8 of 11 methods; `NavigationService` 4 methods; `NotificationService` 2 methods; `ViewState`/`FilterState`/`DatasetState` ~15 methods; `AppState` entirely; `DatasetCacheService.LoadPreviousPageAsync`; `_Imports.razor` duplicated in `Configuration/`; duplicate `@using` in `DatasetViewer.razor:3-4` | Delete. |
| U-18 | low | `wwwroot/translations/en.json` (155 lines), `es.json` (12 lines, a `"TODO"` key) | Never fetched or referenced; keys don't match UI copy. Delete; add i18n later with a real localizer if wanted. |
| K-2 | low | `Configuration/Program.cs:77-84` | `ParserRegistry`, `ModalityProviderRegistry`, `LayoutRegistry`, `FormatDetector`, `FilterService`, `SearchService` registered; none drives any UI (`FilterService` injected into the viewer and never called). Remove with K-1. |

## 4. Authentication and accounts (A)

| ID | Sev | Where | Problem |
|---|---|---|---|
| A-1 | high | `Endpoints/AuthEndpoints.cs:16-19`, `Services/Auth/AuthService.cs:22-59` | Open registration, no email verification, instant sign-in. Every "any authenticated user can…" capability (5 GB uploads, HF downloads of unbounded size, S3 probing) is internet-reachable. Add an invite/allow-registration setting (default off in production) and rate limiting. |
| A-2 | medium | `AuthEndpoints.cs:50-61`, `BCryptPasswordHasher.cs:10` | No throttling on `/login`/`/register`; each attempt costs a BCrypt-11 hash → CPU DoS and online guessing. ASP.NET rate limiting middleware. |
| A-3 | medium | `Program.cs:95-122` | Role/`IsActive` frozen in the cookie for 7 days; no `OnValidatePrincipal`/security stamp. Deactivation and demotion don't take effect. |
| A-4 | medium | `UserRepository.cs:54-72` vs `DatasetStudioDbContext.cs:102-103` | Uniqueness checked with `ToLower()` but unique indexes are case-sensitive → TOCTOU duplicates and a non-sargable lookup. Use `citext` or a lower-cased computed column with a unique index. |
| A-5 | low | `AuthService.cs:24-31` | No username/email format or max-length validation → Postgres `22001` → 500. |
| A-6 | low | `AdminBootstrapper.cs:68-77` | Overwrites the admin's email and hash on every boot; if `ADMIN_USERNAME` changes, the seeded `admin` row stays as an unusable account holding the name. |
| A-7 | low | `DTO/Datasets/DatasetDetailDto.cs:27` | `IsPublic` defaults `true` while `DatasetDto`, `DatasetEntity` and `CreateDatasetRequest` default `false`. |

## 5. Datasets, permissions, PostgreSQL (D)

| ID | Sev | Where | Problem |
|---|---|---|---|
| D-1 | medium | `DatasetEndpoints.cs:167-189`, `DatasetRepository.cs:21-62` | `POST /api/datasets` returns `createdAt/updatedAt = 0001-01-01` (observed live): `CreateAsync` stamps the entity but returns only the id; the handler serialises its own un-stamped DTO. Return the persisted DTO. |
| D-2 | medium | `DatasetPermissionService.cs:50-64`, `PermissionRepository.cs` | The grant table (`AccessLevel`, `CanDelete`, `ExpiresAt`, `CanShare`) has no insert/update/delete path anywhere. Sharing a private dataset is impossible from the API. Either build grant endpoints or remove the half-feature. |
| D-3 | medium | `DatasetEndpoints.cs:119-139, 295` | `pageSize` unbounded on datasets and items (`?pageSize=1000000`). HF rejects >100 with 422, surfaced as an empty 200 (H-6). Clamp. |
| D-4 | medium | `DatasetRepository.cs:101-151` + callers `DatasetEndpoints.cs:235-267, 289-340, 747-787`, `DatasetIngestionService.cs:285-295` | `UpdateAsync` is a full-row overwrite from a DTO snapshot held across long awaits (a 5 GB copy, an HF `/size` call); concurrent background status/count writes are clobbered. Add a concurrency token or patch-style updates. |
| D-5 | low | `DatasetRepository.cs:178-194` `SearchAsync`, `UserRepository.GetByIdAsync`, `DatasetEntity.ParquetPath/Metadata/TotalSizeBytes`; `Format` always "Unknown", `Modality` always "Image"; `CreateDatasetRequest.SourceType=WebUrl/LocalFolder` accepted with no processing path | Dead or inert fields and methods. |
| D-6 | low | `DatasetStudioDbContext.cs:56-61` | Hot query `WHERE is_public OR created_by_user_id=$1 ORDER BY created_at DESC OFFSET` has no composite index; offset paging degrades linearly. |
| D-7 | low | `DatasetItemEntity`, `CaptionEntity`, migrations | `dataset_items` and `captions` tables (and 11 indexes) exist and migrate; no code reads or writes them. Drop or use. |

## 6. Ingestion pipeline (I)

| ID | Sev | Where | Problem |
|---|---|---|---|
| I-1 | high | `Services/DatasetManagement/IngestionQueue.cs:32-37`, `IngestionBackgroundService.cs:29-35` | Unbounded in-memory `Channel`, single consumer, no persistence, no startup reconciliation. Crash/restart loses every queued job; datasets stay `Pending` (temp file orphaned in `%TEMP%`) or `Processing` forever. Persist jobs (a `jobs` table), re-enqueue or fail them on startup. |
| I-2 | medium | `IngestionBackgroundService.cs:60-63`; `DatasetIngestionService.cs:181,235,319` | On shutdown the `OperationCanceledException` is only logged; worse, the `Failed`-status write uses the same cancelled token, so it never lands. Use a non-cancellable token for the status write. |
| I-3 | medium | `IngestionBackgroundService.cs:31-34` | Strictly serial; one slow HF download blocks every user's upload; no timeout, no progress beyond logs. Bounded parallelism per dataset/user; progress counter on the dataset. |
| I-4 | medium | `IngestionQueue.cs:40`, `DatasetEndpoints.cs:271,793,838` | No backpressure; `Enqueue`'s return ignored. Uploads to temp can outrun consumption → disk full. |
| I-5 | medium | `DatasetEndpoints.cs:257-259` vs `ServiceCollectionExtensions.cs:88,112` | Uploads are written to `Path.GetTempPath()`, not `Storage:UploadPath`; in Docker that's ephemeral `/tmp`, not the mounted volume. |
| I-6 | medium | `DatasetIngestionService.cs:138-141` vs `:195-203` | `.parquet` has no parser; HF download auto-picks `.parquet` first → `NotSupportedException` after a full download. A ZIP of CSVs ingests 0 items. Add a Parquet reader (Parquet.Net is already a dependency) and sniff ZIP members. |
| I-7 | medium | `Services/Integration/HuggingFaceClient.cs:96` | Hub `tree/{revision}` is non-recursive; repos with `data/` folders report "no suitable data file". |
| I-8 | low | `DatasetIngestionService.cs:163-174` | HF download file deleted only on success; parse failures leak `uploads/hf_*`. |
| I-9 | low | `DatasetIngestionService.cs:28` vs `ParquetSchemaDefinition.cs:17` | `ChunkSize=2000` < `DefaultBatchSize=10_000` so every row group is 2,000 rows (README said 10K). Choose one number and document it; see P-5. |
| I-10 | low | `DatasetIngestionService.cs:329-372` | Folder import sets `Width=Height=0` (no probing), writes absolute server paths into item metadata (`original_path`), leaves `SourceType=LocalUpload` (F-3). |

## 7. Parquet storage and paging (P, G)

| ID | Sev | Where | Problem |
|---|---|---|---|
| P-1 | high | `DataAccess/Parquet/ParquetItemReader.cs:251-278` | `ReadFromShardAsync` decodes **all columns of every row group** before the skip loop and skips rows one at a time; never skips whole groups via `RowCount`; reopens the footer per request. Page N costs O(offset) under the dataset lock. Skip by group row counts; project columns. |
| P-2 | high | `ParquetItemRepository.cs:157-199, 471-486`; `ParquetItemReader.cs:109-121` | `GetItemAsync(itemId)` has no dataset hint → scans every dataset directory, every shard, every row group's `id` column. One `PATCH` is O(all items on the server); bulk does it per item. Route edits by `datasetId` (it's in the DTO) and keep an id→(shard,group) index. |
| P-3 | high | `ParquetItemRepository.cs:240-262` | Any edit reads an entire shard (≤10M rows) into memory and rewrites it as one row group, holding the lock that also gates reads. Rewrite only the affected row group, or keep an overlay/delta store for edits. |
| P-4 | high | `ParquetItemRepository.cs:256-260`, `ParquetItemWriter.cs:309-324` | `DeleteShard` then `WriteBatchAsync` — not atomic. A crash between them loses the shard; a swallowed `IOException` in delete leads to the whole shard being appended again (duplicates). Write to temp + rename. |
| P-5 | medium | `ParquetItemWriter.cs:115-135` | Append rewrites the footer and adds a row group per call; with 2,000-row chunks and no compaction row-group count grows unboundedly (hurts P-1, `CountAsync`, footer size). Larger groups and/or periodic compaction. |
| P-6 | medium | `ParquetSchemaDefinition.cs:19-20,129-137`; `DataAccess/Parquet/README.md` | Snappy constant declared but compression option commented out ("TODO: Update to new Parquet.NET API"); the README claims Snappy, column projection, parallel shard reads, "O(1) seek", DuckDB/LiteDB migration — none implemented. Fix the option; rewrite the README to the truth (W-3). |
| P-7 | medium | `ParquetItemRepository.cs:84,462-466` | In-memory `_datasetItemCounts` is the source of `startIndex`; set only after the whole loop, so a mid-call failure leaves it stale-low → next ingestion overlaps indices (the exact bug the `InitializeItemCounts` comment describes). Single-process only. |
| P-8 | low | `ParquetItemWriter.cs:15,275-282,326-368` (`_activeWriters`, `ShardWriter`, `FlushAsync`); `ParquetItemRepository.GetStatisticsAsync`; `ParquetItemReader.ReadAllAsync`, `MatchesFilter` | Dead or unreachable (see G-2). |
| P-9 | low | `ParquetItemWriter.cs:239`, `ParquetItemReader.cs:390` | Nullable `Description` round-trips as `""`. |
| P-10 | low | `ParquetItemReader.cs:85-97` | Exact-fill last page emits a cursor past the end → one extra empty page. |
| G-1 | medium | `DatasetEndpoints.cs:411-416` | Local datasets return `TotalCount: null` although the repository has a cached count. |
| G-2 | medium | `DatasetEndpoints.cs:404-409`, `ParquetItemReader.cs:406-483`, `DTO/Common/FilterRequest.cs` | Items endpoint accepts no filter parameters; `FilterRequest` and `MatchesFilter` are unreachable. Server-side filtering is the prerequisite for U-6. |
| G-3 | low | `ParquetItemReader.cs:50-60` | Malformed cursor silently restarts at `0:0`; cursors aren't bound to a dataset. Return 400. |

## 8. HuggingFace (H)

| ID | Sev | Where | Problem |
|---|---|---|---|
| H-1 | high | `ClientApp/…/DatasetUploader.razor.cs:666-671`; `DatasetIngestionService.cs:94-109`; `DatasetEndpoints.cs:320-346, 387-400`; `HuggingFaceDatasetServerClient.cs:105-108` | Library shows 0 items for HF streaming datasets (observed live: record `totalItems: 0`, `/items` `totalCount: 70000`). (1) The client sends `Config=null, Split=null` whenever discovery finds one option; the server matches with `string.Equals(…, Ordinal)` so null never matches and `UpdateItemCountAsync` is skipped. (2) The page handler's auto-discovery calls `/size` without the user's token. (3) The page handler never back-fills `TotalItems` from `num_rows_total`. (4) When `/size` works it uses the sum over all configs/splits. Fix all four. |
| H-2 | medium | `DatasetEndpoints.cs:29-32,1241-1256` | `/huggingface/discover` is anonymous and forwards any client-supplied token upstream; 3+ upstream calls per request, 100 s timeout, no rate limit. Require auth. |
| H-3 | low | `HuggingFaceClient.cs:40,96,148` | `repository`/`revision`/`fileName` interpolated unescaped into URL paths. `Uri.EscapeDataString`. |
| H-4 | high | `DatasetIngestionService.cs:149-160`, `HuggingFaceClient.cs:137-211` | Download mode fetches any file of any size to disk for any registered user. Size cap + quota. |
| H-5 | medium | `DatasetEndpoints.cs:1211-1228`, `ThumbnailService` | Thumbnails fetched with one user's token are cached keyed only by repo/split/offset/width and served to anyone who can read the dataset → gated images become world-readable via a public dataset. Include an auth-scope in the key or don't cache token-fetched images. |
| H-6 | medium | `DatasetEndpoints.cs:369-377`, `HuggingFaceDatasetServerClient.cs:277-281,320-324` | Every upstream failure (401/404/422/5xx/timeout) becomes `200` with empty items. Return 502/4xx with a reason. |
| H-7 | medium | `HuggingFaceRowPageCache.cs:33,47-53` | Unbounded dictionary, expired entries never removed; one caller's `CancellationToken` is captured in the shared task. Bounded LRU; detach the token. |
| H-8 | low | `DatasetEndpoints.cs:1172-1175,567` | hf-thumbnail accepts any `IsStreaming` dataset (S3 datasets qualify); `MapStreamingRowToDatasetItem` never sets `DatasetId`. |
| H-9 | low | `Services/Integration/HuggingFaceStreamingStrategy.cs` (no callers); `ImportHuggingFaceDatasetRequest.ConfirmedDownloadFallback` (never read); `HuggingFaceDatasetInfo.Sha/LastModified/Tags`; `HuggingFaceDatasetProfile.HasImageFiles/ImageFiles` | Dead. |

## 9. S3 (S)

| ID | Sev | Where | Problem |
|---|---|---|---|
| S-1 | medium | `DatasetEndpoints.cs:112-115,1053-1135` | A public S3 dataset is an anonymous, unmetered egress proxy for the whole prefix (or bucket), paid with the server's credentials. Size cap, range support, rate limit, or signed short-lived URLs. |
| S-2 | medium | `DatasetEndpoints.cs:1107-1114` | Thumbnail path buffers the whole object into memory before decode. Stream with a byte limit. |
| S-3 | low | `Services/Storage/S3KeySafety.cs:15` | `Contains("..")` rejects legitimate keys (`photo..final.jpg`); listing with a non-slash prefix matches siblings that the key check then rejects. |
| S-4 | low | `DatasetEndpoints.cs:443-457`, `S3StorageService.cs:25-27` | List failures swallowed with no log; `GetObjectResponse` never disposed; `ForcePathStyle=true` default breaks virtual-hosted-only regions. |

## 10. Folder import and file serving (F)

| ID | Sev | Where | Problem |
|---|---|---|---|
| F-1 | high | `DatasetEndpoints.cs:815-846, 1006-1016`; `DatasetIngestionService.cs:272-372` | An admin can point a dataset at any directory (`/etc`, `data/keys`); `ServeDatasetFile` has **no extension allow-list** (falls through to `application/octet-stream`), so with `IsPublic` the DataProtection key ring is world-readable. Allow-list image extensions on serve; add a configurable root allow-list for folder import. |
| F-2 | medium | `DatasetEndpoints.cs:992-1004` | Symlink check resolves only the final file; a symlinked directory component escapes containment. Resolve every path segment or compare `realpath`. |
| F-3 | low | `DatasetIngestionService.IngestFromFolderAsync` | `SourceType` stays `LocalUpload`; `.tiff/.tif` catalogued but served as octet-stream and not recognised as images elsewhere. |
| F-4 | low | `ServeDatasetFile`, `GetS3File` | Containment/symlink violations return 404 with no log — security events are invisible (also see "Logging" below). |

## 11. Thumbnails (T)

| ID | Sev | Where | Problem |
|---|---|---|---|
| T-1 | medium | `Services/Storage/ThumbnailService.cs` | No eviction, size cap, TTL or startup sweep; streaming datasets create one file per offset per width. Cap by bytes with LRU or age sweep. |
| T-2 | medium | `DatasetEndpoints.cs:1020-1024`; `ThumbnailService.cs:48` | Local cache key is the absolute path only (no mtime/size) → stale thumbnails forever after a file changes; `w` clamped silently. |
| T-3 | low | `DatasetEndpoints.cs:1023,1226` | Originals fully buffered before decode; no byte limit on HF fetch; no pixel-count guard before `SKBitmap.Decode` (decompression bombs). |
| T-4 | low | `ThumbnailService.cs:141-146`, `DatasetEndpoints.cs:1230-1237` | Always JPEG (drops alpha/animation); `Undecodable` falls back to the original for local/S3 but 404s for HF. |

## 12. Item editing (E)

| ID | Sev | Where | Problem |
|---|---|---|---|
| E-1 | medium | `Endpoints/ItemEditEndpoints.cs:112-207` | Bulk silently skips unwritable items and returns a lower `updatedCount` with no ids; client can't distinguish denial from not-found. |
| E-2 | medium | `ItemEditEndpoints.cs:32-40,112-119` | Neither handler takes a `CancellationToken`. |
| E-3 | medium | `DatasetEndpoints.cs:472,567` | Streaming items get a fresh `Guid` per page → every PATCH 404s; nothing tells the client. Either derive stable ids (`repo|split|offset`, `bucket|key`) with a local overlay store for edits, or hide edit controls for streaming datasets. `UpdateItemRequest.ItemId` is redundant with the route. |
| E-4 | low | `ItemEditEndpoints.cs:41-105` | Read-modify-write with no version/ETag; last writer wins at whole-item granularity. |
| E-5 | low | `ItemEditEndpoints.cs:107`, `AuthService.cs:56,95` | Logs user content and usernames at Information; never logs *who* edited. |

## 13. Extensions (X)

| ID | Sev | Where | Problem |
|---|---|---|---|
| X-1 | medium | `Services/Extensions/ApiExtensionRegistry.cs:293-302` | Probe paths hard-code `net8.0` and none matches the project build layout (`CoreViewer/CoreViewer.Api/bin/Debug/net8.0/…`); only the Dockerfile's flat copy matches, so under `dotnet run` nothing loads (observed live: "API assembly not found" ×5). |
| X-2 | medium | `ApiExtensionRegistry.cs:245-248` | Bare collectible `AssemblyLoadContext` with no `AssemblyDependencyResolver`: extension-private dependencies fail at first use; `UnloadExtensionAsync` has no callers. |
| X-3 | medium | `Program.cs:146-160,236-279` | Any DLL under `Extensions/Community/*` runs with full privileges and `IServiceCollection` access; no signing. Acceptable self-hosted, must be documented; `requiredPermissions` in manifests is never enforced. |
| X-4 | low | `ApiExtensionRegistry.cs:122,159,175-188,227,332` | Four `async` methods without `await` (CS1998); Kahn ordering lists dependents before dependencies (reverse of intent). |
| X-5 | medium | `src/Extensions/SDK/*` (`ExtensionManifest.LoadFromDirectory` throws `NotImplementedException`; `ExtensionCapabilityDescriptor`, `ManifestValidator`, `ExtensionVersion`, `ExtensionPublisher`, `BaseExtension` empty stubs; `BaseApiExtension.RegisterEndpoints` log-only; `BaseClientExtension` unreachable; `ExtensionApiClient` zero users); `BuiltIn/Creator` has no `.cs` files; `Editor`/`AdvancedTools`/`AITools` manifest-only; `CoreViewer` routes are unauthenticated placeholders | The extension system is scaffolding. Decide: cut it to the three things that exist (manifest, `IApiExtension`, loader) and delete the stubs, or invest in it properly. Either way stop documenting the stubs as features (W-4). |
| X-6 | low | `ApprovedExtensions.json` | A "marketplace" catalog with ratings and GitHub URLs that nothing reads; names and permission vocabularies disagree with the manifests. Delete. |

## 14. Core library (K)

| ID | Sev | Where | Problem |
|---|---|---|---|
| K-1 | medium | `src/Core/BusinessLogic/{DatasetLoader,EnrichmentMergerService,FilterService,FormatDetector,SearchService}`, `Layouts/*`, `ModalityProviders/*`, `Parsers/*`; `Abstractions/{IDatasetItem,ILayoutProvider,IModalityProvider,IDatasetParser,IFormatDetector}`; `DomainModels/{DatasetItem,ImageItem,Metadata,PagedResult}`; `Constants/{DatasetFormats,Modalities}`; `Utilities/Helpers/{ImageHelper,TsvHelper,ZipHelpers}` | ~22 types form a client-era parser/filter/layout chain that nothing runs (`ParserRegistry` holds one `UnsplashTsvParser`; `DatasetFormat` lists nine formats all "TODO"). Only 13 Core types are live. Delete the chain; keep `MultiFileDetectorService` (used by the uploader) only if the uploader keeps it. |
| K-3 | low | `Core/Core.csproj:17`, `Abstractions/Repositories/*.cs` | Core depends on DTO because repository interfaces take DTOs — inverted layering. Move the repository abstractions into APIBackend (they're only used there) and let Core be truly shared, or merge Core into DTO. |
| K-4 | medium | `Core/Abstractions/Repositories/IDatasetItemRepository.cs` vs `APIBackend/Services/DatasetManagement/IDatasetItemRepository.cs`; `DataAccess/PostgreSQL/Repositories/ItemRepository.cs` | Two interfaces with the same name (2 vs 6 methods), both resolving to the same singleton; `ItemRepository` is a pass-through living in the PostgreSQL folder doing no Postgres I/O. Collapse to one interface. |
| K-5 | low | `DTO/Datasets/{DatasetDto,DatasetSummaryDto,DatasetDetailDto}`, `Services/DatasetManagement/Dtos/DatasetMappings.cs`; four HF file/split shapes | Near-identical records with hand mappers and divergent defaults (A-7). One `DatasetDto` + a summary projection. |
| K-6 | low | `using DatasetStudio.Core.Utilities;` in `ItemEditEndpoints.cs:5`, `HuggingFaceDiscoveryService.cs:8` and ~30 client files | The namespace contains no types. Remove. |
| K-7 | low | `Core/Utilities/Logging/Logs.cs:6,12` | Comment says ~40 / ~280 call sites; actual 258 across 38 files. |
| K-8 | low | `DatasetUploader.razor.cs:157-195` | Reads every selected non-ZIP file fully into a string in the browser only to label it Primary/Enrichment; the server re-parses from scratch. |

## 15. Tests (Q)

| ID | Sev | Where | Problem |
|---|---|---|---|
| Q-1 | low | README/phase-7 "109 tests" | Static count is 114 (110 API incl. 12 data rows, 4 client). |
| Q-2 | high | `tests/APIBackend.Tests/Fixtures/MinioContainerFixture.cs:19-24`, `PostgresContainerFixture.cs:55-59` | `new MinioBuilder()` (obsolete ctor, Docker Hub default image) is a fixture of the single collection shared by **every** Docker-backed class → with `minio/minio` unpullable all 52 Docker tests fail, not just the 11 S3 ones. Pin the image; make MinIO its own collection. |
| Q-3 | medium | — | Untested: item editing (`PATCH` single/bulk, shard rewrite), TSV/JSON/JSONL/ZIP ingestion, the multipart upload route, HF discovery, HF `/rows` mapping, HF download mode, dataset delete removing shards, list-visibility filtering, extension loading, and the entire client layer (cache, virtualisation, state; `ClientApp.Tests` has 4 cases). |
| Q-4 | low | `tests/INTEGRATION_TESTS.md` | Stale guide for a previous codebase (`HartsysDatasetEditor.*`, SQLite, ports 7085/7221). Delete. |

## 16. Documentation (W)

| ID | Where | Problem |
|---|---|---|
| W-1 | `docs/architecture.md:11,93,165-166` | "Two data paths" (there are three); upload supports Parquet (it doesn't); client filtering "applies to the buffered window" (it's bypassed). |
| W-2 | `README.md` (old) | Claimed Parquet upload, "109 tests", `docker compose up -d` as "start Postgres". Rewritten. |
| W-3 | `src/APIBackend/DataAccess/Parquet/README.md` | 450 lines of aspirational claims (Snappy, column projection, parallel shard reads, O(1) seek, 50-100K items/s, DuckDB/Spark/LiteDB migration). Replace with a 40-line description of what the reader/writer do. |
| W-4 | `src/Extensions/README.md`, `PHASE3_IMPLEMENTATION_SUMMARY.md`, `SCAFFOLD_SUMMARY.md`, `SDK/DEVELOPMENT_GUIDE.md`, `SDK/DevelopmentGuide.md`, `SDK/APPSETTINGS_EXAMPLES.md`, `BuiltIn/README.md`, `UserExtensions/README.md` | Describe files, classes, config keys and features that don't exist (`ApiExtensionLoader`, `ClientExtensionRegistry`, `PROGRAM_INTEGRATION.md`, `Extensions:Enabled/AutoLoad`, a flat manifest schema that the loader rejects). Delete; keep `docs/architecture.md` §6 as the single truth. |
| W-5 | `docs/DEPLOYMENT.md:54,68-69` | "four volumes" (six); `S3_PROFILE_*` variables that nothing reads. |
| W-6 | `Core/Utilities/Logging/Logs.cs` header, `ClientApp.csproj:29` | Stale counts and a reference to a deleted class. |

## Cross-cutting: logging and cancellation

- No request logging; containment/symlink violations, S3 list failures and HF null pages are
  invisible or Warning-only; failed logins log nothing; `Enqueue == false` is never logged.
  Conversely user content and usernames are logged at Information, and
  `EnableSensitiveDataLogging` is on in Development.
- Cancellation: `ItemEditEndpoints` ignores tokens (E-2); ingestion uses the host stopping
  token for the `Failed` write (I-2); `HuggingFaceRowPageCache` leaks a caller's token (H-7).
  No remaining `.Result`/`.Wait()`.

---

## 17. The plan

Each item is one PR with tests and a browser check; docs change in the same PR. Phases are
ordered by dependency and by how much they unblock. Nothing in a later phase should be started
on top of code a previous phase deletes.

### Phase 1 — Stabilise (small PRs, no behaviour redesign)

| PR | Scope | IDs |
|---|---|---|
| 1.1 Repo hygiene | Untrack `obj/bin`; rewrite `.gitignore`; delete stale docs (`tests/INTEGRATION_TESTS.md`, Extensions docs, `ApprovedExtensions.json`, Parquet README rewrite); fix names (Hartsy → Dataset Studio). | R-1 R-2 R-3 R-4 Q-4 W-3 W-4 W-6 X-6 |
| 1.2 Config | Remove placeholder credentials from base config; fail fast with a message; delete duplicate root `appsettings*`; align `DbContextFactory`; fix prod compose DB name; scope the 5 GB limit; `/api` fallback exclusion. | C-1 C-2 C-6 C-7 C-8 R-8 R-9 |
| 1.3 Packages | Fix NU1903; pick one framework matrix (recommend net10 + EF 10 + MudBlazor 8); pin versions + lock files; drop `Http.Abstractions 2.2.0`; update test SDKs. | C-3 C-4 C-9 C-10 C-11 C-12 |
| 1.4 Dev loop | Pin a pullable MinIO image (or swap fixture); make `start.sh` tolerant; pin the test fixture; separate the MinIO test collection. | R-5 R-6 Q-2 Q-1 |
| 1.5 Client correctness | Typed client in `ItemEditService`; fix all MUD0001/MUD0002/RZ10012 (make them errors); `/upload` links; auth-provider try/catch + timeout + `ErrorBoundary`; login/register exception handling; open redirect; delete confirmation. | U-2 U-3 U-4 U-7 U-8 U-9 U-28 A-8 |
| 1.6 HF item count | Default config/split server-side from the streaming plan; pass the token to `/size`; back-fill from `/rows`; per-split count; return upstream errors as errors. | H-1 H-6 |
| 1.7 Small API fixes | Return the persisted DTO on create; clamp `pageSize`; `TotalCount` for local datasets; cursor validation; cancellation in edit endpoints; `Failed` write with a non-cancelled token; upload to `Storage:UploadPath`. | D-1 D-3 G-1 G-3 E-2 I-2 I-5 |

### Phase 2 — Design system and client cleanup

| PR | Scope | IDs |
|---|---|---|
| 2.1 Theme + tokens | `AppTheme.cs`; `--ds-*` tokens; delete `themes/*.css`, body colour, icon font, CDN Dexie (vendor it); working theme toggle with persistence; `ThemeMode.Auto`. | U-13 U-21 U-23 |
| 2.2 Shared components | `PageHeader`, `Toolbar`, `StatusChip`, `SourceBadge`, `EmptyState`/`LoadingState`/`ErrorState` adoption, `ConfirmDialog` adoption, `NotificationService` as the only snackbar path. | U-10 U-11 U-16 U-17 |
| 2.3 Page migration | Dashboard, Datasets (`DatasetCard`, `LibraryFilterBar`), Create (`SourceOptionCard`, real stepper/tabs), Settings (wired, no TODO text), Auth pages — one PR each, each to DESIGN.md §9. | U-13 U-16 U-25 |
| 2.4 Viewer decomposition | `IngestionStatusBanner`, `ViewerToolbar`, `DetailSidebar`; styles to `.razor.css`; keyboard/aria on cards; responsive detail panel; column count from width; resize debounce; prefetch only from `OnNearEnd`. | U-1 U-5 U-19 U-20 U-29 |
| 2.5 Uploader split | Per-source panels + `IngestionProgressPanel` + `IngestionWorkflowService` with real status polling; stream ZIPs; remove dead `ProcessFileAsync`/multi-part blocks and the browser-side full-file reads. | U-14 U-15 U-26 K-8 |
| 2.6 Dead code | Delete unused components, interop, state methods, translations, duplicate `_Imports`, Core registrations; private fields. | U-12 U-18 U-24 U-27 K-2 |

### Phase 3 — Durable ingestion

| PR | Scope | IDs |
|---|---|---|
| 3.1 Persistent queue | `ingestion_jobs` table; enqueue = insert; worker claims with `FOR UPDATE SKIP LOCKED`; startup reconciliation (requeue or Fail stale `Processing`); progress counter. | I-1 I-3 I-4 |
| 3.2 Formats | Parquet upload reader; ZIP member sniffing; recursive HF tree; download size cap; cleanup on failure; folder import sets `SourceType`, probes dimensions, no absolute paths in metadata. | I-6 I-7 I-8 I-10 H-4 F-3 |
| 3.3 Concurrency | Concurrency token on `datasets`; patch-style status/count updates instead of snapshot overwrites. | D-4 |

### Phase 4 — Storage scale

| PR | Scope | IDs |
|---|---|---|
| 4.1 Reader | Skip whole row groups by `RowCount`; project columns; cache footers; emit no cursor on exact fill; bigger row groups + compaction; turn Snappy on. | P-1 P-5 P-6 P-10 I-9 |
| 4.2 Item index | Route all item ops by `datasetId`; sidecar index id→(shard, group, row); atomic temp+rename shard rewrites; rewrite only the touched group or add an edit-overlay store. | P-2 P-3 P-4 P-7 |
| 4.3 Caches | Bounded LRU for HF row pages; thumbnail cache cap/sweep; mtime in local keys; streaming decode with byte/pixel limits. | H-7 T-1 T-2 T-3 S-2 |
| 4.4 Server filtering | Filter parameters on `/items` backed by `MatchesFilter` (and for HF/S3 the subset that's possible); client applies results. | G-2 U-6 |

### Phase 5 — Security hardening

| PR | Scope | IDs |
|---|---|---|
| 5.1 Accounts | Registration policy setting + rate limiting on auth; cookie `OnValidatePrincipal` with a security stamp; case-insensitive unique indexes; input validation; admin bootstrap cleanup. | A-1 A-2 A-3 A-4 A-5 A-6 A-7 |
| 5.2 Sharing | Grant endpoints (create/list/revoke) and UI, or remove the grant columns. | D-2 D-7 |
| 5.3 File serving | Extension allow-list on serve; folder-import root allow-list; full-path symlink resolution; log security events; S3 egress limits; token-scoped HF thumbnail cache; authenticated discovery; escape HF URL parts. | F-1 F-2 F-4 S-1 S-3 S-4 H-2 H-3 H-5 H-8 |
| 5.4 Logging | Request logging; audit who-edited; stop logging user content; `Enqueue` failures. | E-5 cross-cutting |

### Phase 6 — Feature completion and structure

| PR | Scope | IDs |
|---|---|---|
| 6.1 Streaming edits | Stable ids for HF/S3 items + a local overlay for title/tags/favourite; bulk endpoint reports skipped ids; ETag on edits. | E-1 E-3 E-4 |
| 6.2 Core/DTO layering | Delete the dead Core chain; move repository abstractions to the API; one `IDatasetItemRepository`; one `DatasetDto`; remove dead usings. | K-1 K-3 K-4 K-5 K-6 K-7 D-5 D-6 H-9 P-8 P-9 |
| 6.3 Extensions decision | Either cut to manifest + `IApiExtension` + loader (fix probe paths, add a dependency resolver, document the trust model) and delete stubs, or build the real thing. | X-1 X-2 X-3 X-4 X-5 C-5 |
| 6.4 Tests | Cover editing, all upload formats, the upload route, HF discovery/rows mapping, list visibility, extension loading; bUnit tests for the viewer/cache/state. | Q-3 |

### Done means

- `dotnet build` with zero warnings in `src/` (analyzer warnings promoted to errors).
- `dotnet test` green with Docker, and the standalone subset green without it.
- Every page passes the DESIGN.md §9 checklist in both themes.
- README feature table has no "Placeholder" rows except AI tools, and no "broken in dev" notes.
- This document is empty except for the plan history.
