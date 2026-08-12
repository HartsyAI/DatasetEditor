# Phase 7 verification — Hardening, cleanup & final production gate

Plan: `Phase 7 — Hardening, cleanup & final production gate` in the production-readiness plan.

## What shipped

1. **Test infrastructure fix (found before any Phase 7 feature work).** Neither test project
   was referenced by `DatasetStudio.sln`, so a bare `dotnet test` at the repo root silently ran
   nothing (`Determining projects to restore... All projects are up-to-date for restore.`, exit
   0) — every prior phase's "`dotnet test` green" claim in this session was really "the one test
   project I ran explicitly is green," never a true full-suite check. Separately,
   `ClientApp.Tests` (net8.0) has never been able to execute on this box at all: only the
   net10.0 shared runtime is installed, and the test host crashed with *"You must install or
   update .NET to run this application."* Fixed both: added `RollForward>LatestMajor` to
   `ClientApp.Tests.csproj` and added both test projects to the `.sln`. `dotnet test` now
   genuinely runs **109/109** (4 `ClientApp.Tests` + 105 `APIBackend.Tests`) from a single
   command.
2. **AI Tools honesty.** Nav entry and dashboard card both get a "Coming soon" chip; the
   `/ai-tools` page itself now states plainly what's planned and why it isn't built yet (needs
   its own model-serving decision), instead of reading as a broken feature.
3. **Extension system follow-through** — the biggest finding this phase, see its own section
   below.
4. **Logging standardization** — see its own section below.
5. **Dead-code sweep**: confirmed zero references anywhere to Phase 0/1's deleted
   `NoOpDatasetIngestionService`, the phantom `Services.DatasetManagement.IDatasetRepository`,
   `ParquetDataService`/`IParquetDataService`, `ParquetRepositoryExample`, or `ItemRepository`'s
   deleted dead methods. `ClientExtensionRegistry.cs` (see extensions section) is this phase's
   own dead-code removal.
6. **`docs/architecture.md` corrections**: the "O(1)-per-page" claim (3 occurrences) was never
   accurate — `ReadPageAsync` reads Parquet row groups sequentially from the cursor position
   until the page fills, so cost is bounded by a row group's size (the ingestion writer's batch
   size, 10,000 rows), not O(1) and not the full dataset. Corrected all three, and added a note
   on how the Phase 1 append fix affects row-group sizing on re-ingested shards. Also replaced
   the stale "ingestion memory" roadmap bullet (described a problem Phase 2 already fixed) and
   added the real extension-system state (below) in place of the old aspirational one-liner.

## Extension system follow-through (largest finding this phase)

The plan's own text called this "verify all 5 built-in extensions (now loading since Phase 0)
still initialize cleanly" — the real state was substantially worse than that framing assumed,
and Phase 0's own verification doc had already flagged it as deferred, not assumed-working:

- **Of 5 manifests, only `CoreViewer` has an actual project.** `Creator` has empty stub
  `.Api`/`.Client` projects (build, but contain no `IExtension` implementation).
  `Editor`/`AdvancedTools`/`AITools` are manifest-only, no project at all.
- **`AdvancedTools`' manifest didn't even parse.** Its fields were flat at the JSON root instead
  of nested under `metadata`, and `deploymentTarget` was missing entirely — both `required`
  C# properties, so `JsonSerializer.Deserialize` threw and `ApiExtensionRegistry` silently
  dropped it (logged as a warning, not a crash) — this was Phase 0's own deferred finding,
  confirmed still present, now fixed by rewriting the manifest to the real schema. Its
  `entryPoint` field named a `AdvancedToolsExtension` class that has never existed anywhere in
  the repo, and isn't even part of the manifest schema `ExtensionManifest.cs` defines (unknown
  JSON properties are silently ignored) — removed, replaced with an honest `_status` field.
- **Not one built-in extension had ever actually been built or loaded successfully, in any
  environment, before this phase** — `CoreViewer.Api`/`CoreViewer.Client` were referenced by
  nothing (not `.sln`, not any `ProjectReference`), so they'd never even compiled since whatever
  API surface they were written against. Adding them to `DatasetStudio.sln` surfaced two real
  compile errors on the first build: a missing `using Microsoft.Extensions.Logging;` (`ILogger`
  extension methods) and a missing `using Microsoft.AspNetCore.Http;` (`Results`) — both fixed.
- **A real SDK lifecycle bug, found by actually running the pipeline for the first time.**
  `BaseApiExtension.Logger` (`Context.Logger`, and `Context` throws until `InitializeAsync` sets
  it) is called from `ConfigureServices`/`ConfigureApp`'s default bodies — but Program.cs's real
  startup order calls `ConfigureServices` pre-`Build()` and `ConfigureApp` right after, both
  *before* `InitializeAsync` (which needs the fully-built `app.Services`). Confirmed live: a
  real local run against a published `CoreViewer.Api.dll` crashed with `InvalidOperationException:
  Extension not initialized. Call InitializeAsync first.` on the very first extension it tried
  to configure. Fixed by making `Logger` fall back to `NullLogger.Instance` before init instead
  of throwing (matches the existing `Logger?.LogDebug(...)` call sites elsewhere in the same
  file, which were already written assuming `Logger` could be null-safe). Re-verified clean
  after the fix — see the real-run narrative below.
- **Docker image never packaged extensions at all.** The production `Dockerfile` only publishes
  `APIBackend.csproj`, which doesn't reference any extension project, and `.dockerignore` strips
  `**/bin/` anyway — so the container's `Extensions:BuiltInPath` scan found nothing, logging
  `Total API extensions to load: 0` even before this phase's other fixes could matter in
  production. Fixed: the build stage now also publishes `CoreViewer.Api` to a flat output
  directory, and the runtime stage copies its DLLs plus all 5 manifests (not the other 4's
  source — no project exists for them) into `/Extensions/BuiltIn/<id>/`, matching
  `ApiExtensionRegistry`'s scan path and `FindApiAssembly`'s flat-file convention. Verified
  against the actual built image (`docker run` standalone, then again inside the full compose
  stack): `Found 5 built-in extension(s)` → `Successfully loaded 1 API extension(s)` (CoreViewer;
  the other 4 correctly log "API assembly not found," not a crash).
- **`CoreViewerApiExtension`'s two endpoints stayed explicitly reference/example code**, not
  wired to real data. They return hardcoded placeholder JSON and carry no permission checks —
  turning them into a second, less-tested implementation of pagination/stats that
  `DatasetEndpoints.cs` already does correctly (with real `IDatasetPermissionService`
  enforcement) would have opened a new unauthenticated-read surface in a hardening phase for no
  real benefit. Relabeled with a clear class-level comment instead; verified live that the
  endpoint still responds (200, placeholder JSON) through the real reverse proxy — see the
  real-run narrative.
- **`ClientExtensionRegistry.cs` deleted.** It scanned the real filesystem
  (`Directory.Exists`/`File.Exists`) to discover client-side extensions — which cannot work in a
  Blazor WebAssembly browser sandbox (no real filesystem) — and had zero call sites anywhere in
  `ClientApp`, so it was already fully dead code, not a working-but-broken feature. A real
  client-extension loader needs a different design (e.g. an HTTP-served manifest + dynamic WASM
  assembly loading), not built here. `CoreViewer.Client`/`Creator.Client` are kept building (in
  the `.sln`, for compile-checking) but are honestly unreachable at runtime — documented in both
  files' header comments and in `docs/architecture.md` §6.

## Logging standardization

288 `Logs.*` (the old `Console.WriteLine`-backed static class) call sites across 41 files —
converting every one to constructor-injected `ILogger<T>` was considered and rejected as
disproportionate for a hardening pass (many are static utility classes in `Core.BusinessLogic`
with no DI-constructed instance to inject into, and the majority live in ClientApp Blazor
components/state-management singletons). The plan's own text explicitly sanctions
"remove/**wrap**" as an alternative, so:

- **~7 APIBackend service classes got real `ILogger<T>` constructor injection**:
  `DatasetIngestionService`, `AdminBootstrapper`, `HuggingFaceDiscoveryService`, `AuthService`,
  `BCryptPasswordHasher`, and the two `ItemEditEndpoints` minimal-API handlers (via parameter
  injection — a `static` class can't be a generic type argument, so these use `ILogger<Program>`
  matching Program.cs's own existing convention). `ServiceCollectionExtensions.AddDatasetServices`
  runs pre-`Build()` (no DI container yet), so it now takes an explicit `ILogger` parameter
  fed by a small bootstrap `LoggerFactory` built just for that one call.
- **`Core.Utilities.Logging.Logs` itself was rewritten to wrap a real `ILogger`**, set once via
  `Logs.Initialize(ILoggerFactory)` called from both `Program.cs` entry points (API, category
  `"DatasetStudio"`) — covers the remaining ~34 files/~200 call sites (all in
  `Core.BusinessLogic` and `ClientApp`) without an 280-site mechanical rewrite. In WASM this
  routes through the browser's built-in console logging provider (Console output alone doesn't
  reach devtools in a WASM app); on the API it routes through the normal ASP.NET Core logging
  pipeline. A `ConsoleFallbackLogger` (not `NullLogger`) covers the narrow window before
  `Initialize()` runs, so nothing logs silently into the void.
- Added `Microsoft.Extensions.Logging.Abstractions` 8.0.x to `Core.csproj` (pinned to match the
  repo's existing EF/WASM 8.x pin, since Core is in the WASM client's reference graph).
- **Discriminating check** (the concrete thing that would break if the wrap regressed): the
  container's startup log still contains `PostgreSQL configured with connection: …` and
  `Storage directories created: …` (both now routed through the bootstrap `ILogger`, not
  `Logs.Info`) — confirmed present in every real run this phase, including the final one below.

## Automated verification

- `dotnet build` — solution-wide (now including `CoreViewer.Api`/`.Client`,
  `Creator.Api`/`.Client`, both test projects), clean.
- `dotnet test` — **109/109** (was 105, and that 105 was actually the *only* thing prior phases'
  "green" claims ever verified — see the test-infrastructure fix above).
- Production `Dockerfile` builds cleanly with the new extension-publishing stage.
- `docker compose ... config --quiet` clean, including the temporary (uncommitted) S3-test
  overlay used for this phase's verification run (see below).

## Real-run narrative

Fresh volumes (`docker compose down -v` first), full production stack:
`docker-compose.yml` + `docker-compose.prod.yml` + `--profile dev` (for MinIO) + a **temporary,
uncommitted** compose overlay wiring a real `test-minio` S3 storage profile
(`Storage__S3StorageProfiles__0__*` env vars pointed at `http://minio:9000`,
`ForcePathStyle=true`) — `docker-compose.prod.yml` itself deliberately still ships this
commented-out/opt-in (see its own header comment: real deployments bring their own S3-compatible
endpoint, not MinIO). A MinIO bucket (`phase7-test`) was created and seeded with 4 small test
images via `mc`.

1. Clean boot: migrations applied, admin bootstrap synced, `S3 storage profiles configured:
   test-minio`, extension discovery `Found 5 built-in extension(s)` → `Successfully loaded 1
   API extension(s)`, `Now listening on: http://[::]:8080` — no exceptions.
2. Dashboard loads clean over HTTPS, no `#blazor-error-ui` banner
   (`01-dashboard-ai-tools-coming-soon.png`, showing the "AI tools · Coming soon" card).
3. AI Tools page shows the "Coming soon" chip and honest copy (`02-ai-tools-page.png`).
4. Registered a second user (`phase7user2`); logged in as `admin`
   (`03-admin-logged-in.png`).
5. **HF-streaming dataset**: `mnist` (the plain name) turned out to have been renamed upstream
   on HuggingFace to `ylecun/mnist` — confirmed directly against
   `datasets-server.huggingface.co`, which now returns `{"error":"The dataset has been renamed.
   Please use the current dataset name."}` for the old name. Not a Phase 7 regression, just an
   upstream rename discovered while building this run's test data; used the current name.
   Discovery found both `train`/`test` splits, picker shown (`04-hf-discovery-options.png`),
   confirmed import, real MNIST digit thumbnails render (`05-hf-mnist-real-thumbnails.png`).
6. **Local ZIP dataset**: 4 small PNGs uploaded and ingested, 4/4 items
   (`06-local-zip-viewer.png`).
7. **S3 dataset**: connected to the `test-minio` profile/`phase7-test` bucket, all 4 seeded
   images proxied through the API and rendered (`07-s3-minio-viewer.png`).
   `08-my-datasets-all-three-sources.png` shows all three side by side.
9. CoreViewer extension endpoint (`GET /api/extensions/coreviewer/datasets/{id}/items`) responds
   200 with its documented placeholder JSON, through the real reverse proxy, matching its
   relabeled reference-code status (no auth gate by design).
10. Logged out, logged in as `phase7user2` (`09-second-user-logged-in.png`). **Permission
    boundary checked across all three source types**: `DELETE /api/datasets/{id}` as the
    non-owner returned **403** for the HF-streamed, local-ZIP, and S3 datasets alike (all three
    default `IsPublic = true`, so the read side succeeds and only the write/delete side is
    gated — consistent with Phase 3's own design and verification).

Two pre-existing, harmless console 404s (`/favicon.ico`, `/manifest.json`) appear on every page
load — confirmed via direct `curl`, unrelated to any Phase 7 change, not chased further.

## Design deviations from the plan's literal text

- The plan's Phase 7 checklist says "confirm the AI Tools nav decision was actually applied, not
  left as a placeholder" — applied to both the nav entry *and* the dashboard card, plus the page
  itself, not just the nav (the dashboard card's old copy and button ("Explore AI tools") were
  just as misleading as the nav entry).
- CoreViewer's stub endpoints were **relabeled**, not wired to real data — the plan explicitly
  offers "fix or clearly relabel" as alternatives; relabeling was the safer choice given it
  avoids opening a new permission-check surface for marginal benefit (see extensions section).
- Logging used the plan's explicitly-sanctioned "wrap" alternative for Core/ClientApp rather than
  full `ILogger<T>` injection at all 280 call sites — see logging section for the reasoning.

## Result

Phase 7 goals met: AI Tools honesty applied and verified, extension system's real state found
and substantially repaired (manifest schema bug, SDK lifecycle bug, Docker packaging gap — all
found by actually running the pipeline end-to-end for the first time, not assumed from reading
the code), logging standardized via real injection where proportionate and a real-`ILogger`
wrapper elsewhere, dead code confirmed clean, architecture docs corrected. Along the way, found
and fixed a test-infrastructure gap that had been silently making "dotnet test green" a weaker
claim than it looked all session (neither test project was in the `.sln`; `ClientApp.Tests`
could never execute on this box at all). 109/109 tests passing. Full production stack verified
on fresh volumes across all three dataset source types, permission boundaries, and the extension
endpoint, through the real Caddy HTTPS reverse proxy.
