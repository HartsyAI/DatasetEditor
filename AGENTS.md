# AGENTS.md — working in Dataset Studio

Instructions for AI coding agents (and humans) contributing to this repository. Read this
before changing anything. The three other documents you need:

- [README.md](README.md) — what the product is, every feature, how each is implemented and why.
- [DESIGN.md](DESIGN.md) — the UI design system. Every client change must comply.
- [docs/refactor-audit.md](docs/refactor-audit.md) — the current list of known defects and the
  refactor plan. Check it before fixing something; it may already be scoped.

## 1. What this is

A self-hosted web app for browsing, curating and editing large AI image datasets.
ASP.NET Core API (`src/APIBackend`) + Blazor WebAssembly client (`src/ClientApp`), both on
net10.0 with EF Core 10 and MudBlazor 7.8. PostgreSQL holds users/datasets/permissions; dataset items live in
Apache Parquet segment files (one manifest per dataset) on disk; HuggingFace and S3 datasets are streamed, not copied.
Details: README §"How it works".

## 2. Repository map

```
src/Core/          Domain models, parsers, shared business logic (net10.0; referenced by API and client)
src/DTO/           Wire contracts shared by API and client (net10.0)
src/APIBackend/    ASP.NET Core minimal API: Endpoints/, Services/, DataAccess/{PostgreSQL,Parquet}, Configuration/
src/ClientApp/     Blazor WASM: Features/<Feature>/{Pages,Components,Services}, Shared/{Layout,Components,Services}, Services/{ApiClients,Caching,Interop,StateManagement}, wwwroot/
src/Extensions/    SDK (manifest model, IApiExtension) and BuiltIn/CoreViewer (reference extension; see README §4.14)
tests/             APIBackend.Tests (xUnit + Testcontainers), ClientApp.Tests
docs/              architecture.md, DEPLOYMENT.md, refactor-audit.md, verification/phase-N (historical run logs)
docker-compose.yml, docker-compose.prod.yml, Dockerfile, Caddyfile, start.sh, stop.sh
```

Non-obvious locations:

- API settings are in `src/APIBackend/Configuration/appsettings*.json`, **not** the project root.
  `Program.cs` rebuilds the configuration chain by hand (see its header comment).
- Client API base URL: `src/ClientApp/wwwroot/appsettings.Development.json` (dev, cross-origin
  to :5000) vs blank in `appsettings.json` (production, same origin).
- EF migrations: `src/APIBackend/DataAccess/PostgreSQL/Migrations`, applied automatically at
  API startup.

## 3. Build, run, test

Prerequisites: .NET 10 SDK, Docker (for PostgreSQL and the integration tests).

```bash
# database (postgres only; add `minio` for the S3 dev profile)
docker compose up -d postgres

# API on http://localhost:5000 (Swagger at /swagger) — MUST run as Development
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/APIBackend

# client on http://localhost:5002
dotnet run --project src/ClientApp

# or all of the above
./start.sh

# tests (APIBackend.Tests needs a running Docker daemon for Testcontainers)
dotnet test
dotnet test tests/ClientApp.Tests        # no Docker needed
```

Gotchas:

- **The database connection string comes from the environment outside Development.** The base
  `Configuration/appsettings.json` has none, so a non-Development run without
  `ConnectionStrings__DatasetStudio` fails at startup with a message saying so. Development gets
  the local database from its overlay. Note that `dotnet run` applies `launchSettings.json`'s
  `ASPNETCORE_ENVIRONMENT=Development` on top of your shell, so use `--no-launch-profile` to test
  any other environment.
- `Cors:AllowedOrigins` must contain the client origin; the API refuses to start with an empty
  list because cookie auth requires `AllowCredentials()`.
- Register/login set an `HttpOnly` cookie. The client sends it cross-origin only because
  `CookieCredentialsHandler` is attached to the typed `HttpClient`s — never create a raw
  `HttpClient` for API calls.
- Admin account: set `ADMIN_USERNAME/ADMIN_EMAIL/ADMIN_PASSWORD` env vars; without them the
  seeded admin row is unusable by design.
- Verify in a browser before claiming a UI change works; the WASM client swallows many
  exceptions into the `#blazor-error-ui` banner. The browser devtools console shows
  `Logs.*` output.

## 4. Conventions

### C#

- Nullable and implicit usings are on solution-wide (`Directory.Build.props`). Treat nullable
  warnings as errors to fix, not suppress.
- File-scoped namespaces. One public type per file, file named after the type.
- Explicit types for locals (`string name = …`, `List<DatasetDto> rows = …`); `var` only where
  the type is literally on the right-hand side (`new`, casts, `Guid.NewGuid()`).
- `///` XML doc comment on every public type and member. Comments explain *why*, including
  the bug or constraint that motivated the code; don't narrate *what* the code does.
- Minimal APIs: endpoints are `internal static class XxxEndpoints` with `MapXxxEndpoints`
  extension methods; handlers are `public static async Task<IResult>`; every mutating or
  data-returning endpoint resolves `IDatasetPermissionService` or a visibility filter. A new
  endpoint without an authorization decision is a bug.
- Services: interface in the same folder as the implementation; registered in
  `src/APIBackend/Extensions/ServiceCollectionExtensions.cs` (API) or
  `src/ClientApp/Configuration/Program.cs` (client).
- Logging: API services take `ILogger<T>` by constructor. `Core` and `ClientApp` may use the
  static `Logs.*` wrapper (it forwards to the real logging pipeline). Don't add new
  `Console.WriteLine`.
- Async all the way; pass `CancellationToken` through to I/O. No `.Result` / `.Wait()`.
- Enums that cross the wire live in `src/DTO` and are serialised as integers today; don't
  rename members.

### Blazor client

- Follow [DESIGN.md](DESIGN.md). In particular: no `<style>` blocks in `.razor` files, no hex
  colours in markup, shared components for page header / toolbar / empty / loading / error /
  status chip, one `Typo.h4` per page, `aria-label` on icon buttons.
- Markup in `.razor`, logic in `.razor.cs` (partial class), styles in `.razor.css`.
- Components that subscribe to a state `OnChange` implement `IDisposable` and unsubscribe.
- Go through `DatasetApiClient` / `AuthApiClient`; never `HttpClient` directly.
- Routes are listed in DESIGN.md §7; add a route there when you add a page.

### Data and storage

- Items are written to Parquet in batches (`ParquetSchemaDefinition.DefaultBatchSize`); never
  read a whole segment into memory to serve a page or apply an edit.
- Parquet files are immutable once published: write new files, then commit by replacing the
  dataset's manifest (`ParquetManifestStore.SaveAsync`) or renaming a complete file over a
  segment. Never modify a live file in place.
- Edits are segment-scoped and serialised per dataset (keyed semaphore in the item repository).
  Keep it that way.
- Schema changes are EF migrations (`dotnet ef migrations add … --project src/APIBackend`),
  checked in with the designer file and snapshot.
- Anything that serves a file from disk or a bucket goes through `S3KeySafety` /
  `ServeDatasetFile`'s path containment check. Add a regression test to
  `ServeDatasetFileSecurityTests` / `S3KeySafetyTests` when touching them.

### Tests

- API integration tests use `DatasetStudioApiFactory` + Testcontainers Postgres (and an S3-compatible
  container, Adobe S3Mock, for S3 tests only). Prefer an integration test over mocking the repository.
- Every bug fix gets a test that fails before the fix.
- `dotnet test` must be green before a PR; say explicitly if a suite could not run (e.g. no
  Docker) rather than implying it passed.

### Git

- Small, single-purpose commits with an imperative summary line, e.g. `Resolve real row count
  for HF-streaming datasets instead of showing 0`. Body explains why and what was verified.
- Don't commit `bin/`, `obj/`, `data/`, `blobs/`, `uploads/`, `.env`, or DataProtection keys.
  `.gitignore` covers these; if you add a runtime output folder, add it there.
- Docs change in the same commit as the behaviour they describe. README feature table,
  DESIGN.md, and `docs/architecture.md` are living documents, not release notes.

## 5. How to approach a task here

1. Read the relevant README feature section and the audit entry (if any) first.
2. Reproduce before fixing: run the API + client, or write the failing test.
3. Change the smallest surface that fixes the root cause; don't widen scope silently.
4. Verify: build with zero new warnings, `dotnet test`, and for UI a real browser check in
   both themes.
5. Update docs that the change invalidates. Don't add new `TODO:` comments or TODO text in
   UI; file it in the audit instead.
6. In the PR/commit body, state what was verified and how, and anything not verified.

## 6. Things that look like features but aren't

Say so rather than building on them:

- Extensions: the API loads `IApiExtension` plugins, but the only one (`CoreViewer`) is a
  reference that registers and maps nothing. Extension routes require a signed-in user but get
  no dataset permission check, so don't serve dataset data from one. No client-side extension
  loading exists. See README §4.14.
- AI Tools: nav entry and page are "Coming soon" placeholders.
- i18n: there is none (the unused `wwwroot/translations` files were deleted in PR 2.6).
- `Core.BusinessLogic` (parsers, layout/modality registries, `FilterService`, `SearchService`)
  is no longer registered in the client; the types remain in Core until PR 6.2 deletes them.
