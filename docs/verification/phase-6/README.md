# Phase 6 verification — Deployment (Docker Compose, production config, publish pipeline)

Plan: `Phase 6 — Deployment: Docker Compose, production config, publish pipeline` in the
production-readiness plan.

## What shipped

- `Dockerfile` — multi-stage build. SDK stage publishes `src/APIBackend` (Release, `-r
  linux-x64`, framework-dependent) which, via its `ProjectReference` to `ClientApp.csproj`, also
  builds and trims the Blazor WASM client into the API's `wwwroot`. Runtime stage is
  `aspnet:10.0` + `gosu`, non-root by default via `docker-entrypoint.sh`. One container serves
  both API and client — no separate frontend container, per the plan's hosted-deployment
  decision.
- `docker-entrypoint.sh` — fixes named-volume ownership at container *start* (not build) time,
  then drops from root to the image's built-in non-root `app` user via `gosu` before exec'ing
  `dotnet APIBackend.dll`.
- `docker-compose.yml` — added an `api` service (builds from the new `Dockerfile`, depends on
  `postgres`'s healthcheck, three named volumes for `data`/`blobs`/`uploads`); `minio` moved
  behind a `profiles: ["dev"]` gate so a bare `up -d` doesn't start it.
- `docker-compose.prod.yml` — overlay only. Removes host port publishing from `postgres`/`api`,
  sources all secrets from `.env` (`POSTGRES_PASSWORD`, `ADMIN_*`, `DOMAIN`), adds a `caddy`
  service as the sole published entry point (80/443).
- `Caddyfile` — `{$DOMAIN:localhost} { reverse_proxy api:8080 }`. Automatic Let's Encrypt for a
  real domain, automatic locally-trusted self-signed cert for `localhost`/non-public addresses —
  no branching needed in the file itself.
- `.env.example` — documents every secret the prod overlay needs, plus commented-out
  `S3_PROFILE_*` vars for wiring a Phase 5 storage profile into production.
- `docs/DEPLOYMENT.md` — the self-host runbook: quick start, first-boot behavior (migrations,
  admin bootstrap re-sync on every boot, TLS), backup guidance per volume, S3 opt-in wiring, and
  the Docker/build landmines found this phase.
- Dependency fix: `Parquet.Net` 5.3.0 → 5.6.1, resolving the transitive `Snappier` CVE
  (GHSA-pggp-6c3x-2xmx, fixed past 1.1.6). Verified via NuGet nuspec inspection that 5.6.1 pulls
  a fixed Snappier, then re-ran the full suite (including the Phase 1 append-path tests) to
  confirm no behavior change from the bump.
- WASM trimming verified genuinely safe, not assumed: a Release publish trims by default via the
  Blazor WebAssembly SDK. Ran the full accumulated Playwright regression (routing, MudBlazor
  components, `Blazored.LocalStorage`-backed auth-token persistence, full auth flow) against the
  trimmed output directly — see "Real-run narrative" below. No trimming-related breakage found.
- Data-protection keys now persisted (`AddDataProtection().PersistKeysToFileSystem(...)`,
  configurable via `Storage:DataProtectionKeysPath`) so cookie-auth sessions survive container
  restarts instead of silently invalidating on every redeploy.

## Automated verification

- `dotnet build` — solution-wide, clean.
- Production `Dockerfile` — builds successfully end-to-end (see "bugs found and fixed" for the
  detour this took to land on the minimal working form).
- `dotnet test` — **105/105 passing** (up from 104: one new regression test this phase,
  `ConcurrentWritesAndReads_SameDataset_NeverThrowFileShareException` in
  `ParquetItemRepositoryTests.cs`).
- `docker compose -f docker-compose.yml config --quiet` and
  `-f docker-compose.yml -f docker-compose.prod.yml config --quiet` — both clean.

## Real-run narrative

Two full real-run passes were done: one against the trimmed Release build directly (isolating
WASM-trimming risk from the rest of the stack), one against the complete containerized
production stack (Postgres + API + Caddy) on fresh volumes.

**Trimmed build, standalone** (`ASPNETCORE_ENVIRONMENT=Production`, no reverse proxy):
1. First attempt failed — dashboard showed an unhandled-error banner
   (`01-trimmed-build-broken-baseaddress.png`). Root-caused to two separate config bugs (below),
   not a trimming issue.
2. After fixing both: clean dashboard load, no console errors
   (`02-trimmed-build-dashboard-fixed.png`), and a full register → login flow working
   (`03-trimmed-build-register-login.png`) — confirming MudBlazor and
   `Blazored.LocalStorage`'s reflection-dependent paths survive trimming.

**Full production stack**, `docker compose -f docker-compose.yml -f docker-compose.prod.yml up
-d --build` against **freshly created volumes** (`down -v` first):
1. Migrations applied automatically; admin bootstrap seeded from `.env`; Caddy issued a
   locally-trusted self-signed certificate for `localhost` and served HTTPS with HTTP→HTTPS
   redirect. Dashboard loads clean over HTTPS through the reverse proxy
   (`04-prod-stack-https-dashboard.png`).
2. Register/login through the proxy: working.
3. Local ZIP upload through the proxy: **first attempt failed with a 500** — this is the phase's
   most significant finding, a real Parquet read/write race condition (below). After the fix,
   rebuilt the image, tore down and recreated fresh volumes again, and re-ran the same script:
   upload succeeds cleanly, 8 items, success toast (`05-prod-stack-local-upload-after-race-fix.png`).
4. HuggingFace-streaming import (`mnist`, 200 items) through the proxy: dataset viewer renders
   real streamed thumbnails (`06-prod-stack-hf-streaming-viewer.png`). Some blank cards and one
   `[ERROR] Failed to load image for item` console message appeared alongside the pre-existing
   `#blazor-error-ui` banner — both are carried over from Phase 5's own verification (see
   "Explicitly out of scope" below), not new to this phase.
5. Permission-boundary check (unauthorized delete via direct API call → 403): confirmed working
   through the proxy, same as Phase 3's own gate.

## Bugs found and fixed

1. **`ASPNETCORE_URLS` silently ignored.** `Program.cs`'s custom configuration-source rebuild
   (`builder.Configuration.Sources.Clear()` + manual re-add) dropped the default
   `ASPNETCORE_`-prefixed environment-variable provider, so the container's
   `ENV ASPNETCORE_URLS=http://+:8080` never took effect — the app kept binding to the
   hardcoded dev URL from `appsettings.json` and crashed with `AddressInUseException` under the
   real port mapping. Fixed with `.AddEnvironmentVariables(prefix: "ASPNETCORE_")` ahead of the
   existing plain `.AddEnvironmentVariables()` call.
2. **Client `BaseAddress` hardcoded to a dev-only absolute URL.** `wwwroot/appsettings.json`
   shipped `http://localhost:5000` baked into every build, including Release — this is what
   caused the CORS/`Failed to fetch` banner in `01-trimmed-build-broken-baseaddress.png`. Fixed
   by blanking the base file, adding a `appsettings.Development.json` overlay with the dev
   value, and having both the HttpClient factory setup (`ClientApp/Configuration/Program.cs`)
   and `ImageUrlHelper` fall back to `IWebAssemblyHostEnvironment.BaseAddress` (same-origin) when
   the configured value is blank — both needed the same fallback since both independently
   compare against "our own API's" origin.
3. **Container ran as root** and **DataProtection keys not persisted** — both fixed as described
   in "What shipped" above; confirmed via `docker exec ... whoami` before/after.
4. **Docker named volumes mount root-owned at container *start*, not build, time** — a
   build-time `chown` baked into the image never applies to the real volume mount points once
   docker-compose attaches actual named volumes, causing a crash-loop
   (`Access to the path '/app/data/parquet' is denied`) on first boot against fresh volumes.
   Fixed via `docker-entrypoint.sh` (root-starts, `chown`s the real mount paths, `gosu`-drops to
   `app`). See `docs/DEPLOYMENT.md`'s landmines section for the full detail.
5. **Real Parquet read/write race condition (most significant finding this phase).** The first
   fresh-volume production run's local-ZIP upload failed with a genuine 500:
   ```
   System.IO.IOException: The process cannot access the file
   '/app/data/parquet/dataset_..._shard_000000.parquet' because it is being used by another process.
   ```
   traced through `ParquetItemReader.ReadFromShardAsync` → `ParquetItemRepository.GetPageAsync`
   → `DatasetEndpoints.GetDatasetItems`. Root cause: `ParquetItemWriter` opens shard files with
   `FileMode.Open, FileAccess.ReadWrite` (exclusive `FileShare.None`, required since Phase 1's
   footer-rewrite-on-append fix), but `ParquetItemRepository`'s read methods (`GetPageAsync`,
   `GetItemAsync`, `GetCountAsync`, `GetStatisticsAsync`) never took the same per-dataset
   `SemaphoreSlim` lock the write methods already use (`GetDatasetLock(datasetId)`) — so a
   client's item-list fetch landing right after a background ingestion write, before that
   write's file handle was released, threw. This is a **general correctness bug, not
   Docker-specific** — it could hit any deployment under real concurrent read/write timing; the
   containerized run just had realistic-enough timing to actually surface it (a fast local
   `dotnet run` dev loop apparently never lined up the race). Fixed by adding the same lock
   acquire/release to all four read methods. Added a genuinely-reproducing regression test
   (`ConcurrentWritesAndReads_SameDataset_NeverThrowFileShareException`, 20 rounds of
   interleaved concurrent reads/writes via `Task.WhenAll`) instead of relying on timing luck to
   catch a regression.

## Design deviations from the plan's literal text

- The plan says "Postgres volume + `blobs`/`data` bind-mount backup guidance" — implemented as
  three separate named volumes (`data`, `blobs`, `uploads`) rather than bind mounts, consistent
  with how the rest of `docker-compose.yml` already manages the Postgres volume. Documented per
  volume in `docs/DEPLOYMENT.md`'s backup table.
- Runbook landed as `docs/DEPLOYMENT.md` (repo-root-level doc) rather than folded entirely into
  this verification README, since it's operator-facing reference material meant to outlive any
  single phase, not a one-time verification record.

## Explicitly out of scope (carried over from Phase 5, not re-investigated)

- The pre-existing `#blazor-error-ui` banner that appears alongside otherwise-correctly-handled
  import failures — reproduced again in `06-prod-stack-hf-streaming-viewer.png`, already
  root-caused and documented as out-of-scope in Phase 5's own verification doc.
- The `[ERROR] Failed to load image for item` console message seen during the same HF-streaming
  check — not yet confirmed as a real bug vs. live HF network latency during the check; not
  chased further this phase.

## Result

Phase 6 goals met: single-container deployment, production Compose overlay with TLS via Caddy,
WASM trimming verified safe (not assumed), Snappier CVE resolved, self-host runbook documented.
Found and fixed one genuine pre-existing correctness bug (the Parquet read/write race) along the
way, with a regression test. 105/105 tests passing.
