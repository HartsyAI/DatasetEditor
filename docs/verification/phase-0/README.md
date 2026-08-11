# Phase 0 verification — Foundation: boot fix, repository unification, test harness

Plan: `~/.claude/plans/fuzzy-soaring-garden.md`, Phase 0.

## Automated tests

`dotnet test tests/APIBackend.Tests/APIBackend.Tests.csproj`

```
Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18, Duration: 9 s
```

18/18 green, including:
- `DatasetEndpointsTests` (new) — create/get/list/delete a dataset through the real, mapped `/api/datasets` endpoints via `WebApplicationFactory`.
- `RouteEnumerationRegressionTests` (new, **standing** — keep permanently) — proves the whole route table builds without throwing; every mapped dataset route returns <500 against an empty/unknown dataset.
- `PostgreSqlMigrationsTests` — now runs against a real Testcontainers-provisioned Postgres by default instead of silently reporting a skip-as-failure when `DATASETSTUDIO_TEST_POSTGRES_CONNECTION` is unset; confirms the schema (`users`, `datasets`, `dataset_items`, `captions`, `permissions`) and the seeded admin row.
- `DatasetRepositoryTests` — rewritten against the real `DatasetDto`-based `DatasetRepository` API; the previous version tested a `DatasetEntity`-based shape (`GetAsync`/`ListAsync`/`CreateAsync(DatasetEntity)`) that never matched the real class and could not have compiled — meaning this suite had never actually run before Phase 0.

Test harness added: `Microsoft.AspNetCore.Mvc.Testing` (10.0.11, resolves cleanly against net10.0) + `Testcontainers.PostgreSql` (4.13.0); `DatasetStudioApiFactory` (one throwaway Postgres database per test, inside one shared Testcontainers Postgres for the whole run) + `PostgresContainerFixture`/`PostgresCollection`.

## Real run

`docker compose up -d` (Postgres) → `dotnet run --project src/APIBackend` → `dotnet run --project src/ClientApp`, driven with headless Playwright.

**Before Phase 0** (see prior audit): `GET /api/datasets` → 500, surfaced in-browser as a CORS error; the client showed a red "Failed to load datasets" toast plus a Blazor "An unhandled error has occurred. Reload." banner, simultaneously with a misleading "No datasets yet" empty state.

**After Phase 0:**

```
$ curl http://localhost:5000/api/datasets
{"datasets":[],"totalCount":0,"page":0,"pageSize":50}   -- 200, not 500
```

- `01-dashboard.png` / `02-my-datasets-empty.png` — genuine empty state, no error toast, no error banner, zero console/network errors (checked programmatically, see below).
- `GET /swagger/v1/swagger.json` → 200 (proves the whole route table, not just `/api/datasets`).
- `PATCH /api/items/{unknown-guid}` → 404 with a real error body (item-edit endpoints were already correctly wired — confirmed unaffected).
- Zero browser console errors or failed network requests during the dashboard → library flow.

## Notable things found and fixed along the way (beyond the plan's literal checklist)

- **`CreateDataset` would have 500'd on every call even after the interface fix** — it unconditionally called `StartIngestionAsync(id, uploadLocation: null, ...)`, which throws `FileNotFoundException` when there's no upload location (always true at creation time, before a follow-up upload/import call). Removed; ingestion now only starts from `UploadDatasetFile`/`ImportFromHuggingFace` as originally intended elsewhere in the same file.
- **Config-loading bug independent of the DI issue**: `Program.cs` unconditionally reloaded `Configuration/appsettings.Development.json` by hardcoded name (so a Production deployment would still load dev settings) and did so in a way that always outranked environment-variable overrides — meaning Docker/production secrets injection (`ConnectionStrings__DatasetStudio`, needed by Phase 6) would not have worked, and it also happened to be exactly why `WebApplicationFactory`-based tests couldn't point at a test database. Fixed to rebuild the standard `appsettings.json → appsettings.{Environment}.json → env vars → command line` chain from the correct `Configuration/` subfolder.
- **`src/APIBackend` had no `launchSettings.json`** (unlike `src/ClientApp`, which has one) — `ASPNETCORE_ENVIRONMENT` was never actually being set to `Development` for local `dotnet run`, masked until now by the bug above. Added, matching the client project's convention. Confirms Swagger UI (`if (app.Environment.IsDevelopment())`) was likely never actually reachable in local dev before this — worth a mention in the README's "Quick start" section in a later phase.
- **Extension discovery path bug confirmed fixed**: manifest discovery went from finding 0 of 5 built-in extensions to finding 4 (the 5th, `AdvancedTools`, has a manifest JSON missing required `Metadata`/`DeploymentTarget` fields — a separate, pre-existing bug, logged not fixed). All 4 discovered extensions still report "API assembly not found" and 0 load — because none of the built-in extension projects (`CoreViewer`, `Creator`; `Editor`/`AITools` have manifests but no project at all) are referenced by `DatasetStudio.sln` or built anywhere. Getting them to actually **load** is correctly Phase 7 scope ("Extension follow-through"), not Phase 0 — Phase 0's job was fixing discovery, which is done and verified.

## Environment note (not a Phase 0 finding)

`tests/ClientApp.Tests` (net8.0) could not be executed in this sandbox — only the net10.0 runtime is installed here (`dotnet --list-runtimes` shows only `10.0.10`), and .NET's test host doesn't roll forward across major versions. `dotnet build` succeeds for it (SDK-only, no runtime needed), and `dotnet run --project src/ClientApp` works fine (Blazor WASM's actual payload runs in-browser, not on the host's shared runtime). This is a sandbox gap, not a code regression from Phase 0 — a normal dev machine with the net8.0 runtime installed (as the README's prerequisites imply) won't hit this.

## What's still known-broken (expected, later phases)

- Ingestion durability (Parquet append path, HF-import DI-scope capture) — Phase 1.
- No real thumbnails, no S3, no auth, no local-folder wiring — later phases, as planned.
