# Dataset Studio

A desktop-grade web app for browsing, curating, and editing **billion-scale AI image datasets** — built with an ASP.NET Core API and a Blazor WebAssembly client.

Dataset Studio has three data paths so you can work the way you need to:

- **Stream from HuggingFace** — paste a repo and browse millions of images in seconds; rows are fetched on demand and **nothing is downloaded**.
- **Import locally** — upload CSV/TSV/Parquet/ZIP (or point at a folder already on the server) and the items are ingested into **Apache Parquet** (sharded) with metadata in **PostgreSQL**, so they're fully searchable and editable.
- **Connect an S3-compatible bucket** — AWS S3, MinIO, R2, Backblaze; images are proxied through the API on demand, nothing is copied.

The client never holds the whole dataset in memory: a virtualized viewer keeps only a few screenfuls of cards in the DOM at any scroll depth, so memory stays flat even on million-image datasets.

---

## ✨ Features

- **Virtualized grid & list** — row-virtualized rendering (`<Virtualize>`); flat memory, no freeze on huge datasets.
- **Perceived-instant images** — dominant-color (LQIP) placeholders show immediately, real width-bounded thumbnails (SkiaSharp, disk-cached) fade in over them, lazy + async decoding, zero layout shift.
- **Three ingestion sources** — stream from **HuggingFace** (zero-download, live via the datasets-server API), **upload** CSV/TSV/Parquet/JSON/JSONL/ZIP/image folders (parsed into Parquet in the background), or connect an **S3-compatible bucket** (AWS S3, MinIO, R2, Backblaze — proxied through the API, nothing copied).
- **Cursor pagination** — sharded-Parquet `shard:row` cursors and HF offsets, cost bounded by a row group's size, not the full dataset (see `docs/architecture.md` §4).
- **Editing & curation** — titles, descriptions, tags, favorites; single and bulk edits (shard-scoped writes).
- **Real multi-user accounts** — cookie-session auth, ownership + permission enforcement (owner/admin/granted-access, public vs. private datasets).
- **Self-host production deployment** — one Docker container (API + published client), Postgres, and a Caddy reverse proxy with automatic TLS; see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).
- **Extension system** — API-side plugins discovered from manifests; see [docs/architecture.md](docs/architecture.md) §6 for what's actually built today (one working example, the rest are stubs).

---

## 🧱 Architecture

```
┌────────────────────────┐      HTTP       ┌──────────────────────────┐
│        ClientApp        │ <────────────>  │        APIBackend         │
│  (Blazor WebAssembly)   │                 │   (ASP.NET Core, net10)   │
│  virtualized viewer,    │                 │  dataset/item endpoints,  │
│  sliding-window cache,   │                │  background ingestion     │
│  IndexedDB cache         │                │                           │
└────────────────────────┘                 └─────────────┬─────────────┘
                                                          │
                            ┌─────────────────────────────┼───────────────────────────┐
                            │ PostgreSQL (EF Core/Npgsql)  │   Apache Parquet (sharded)  │
                            │   dataset metadata, users     │  dataset items, 10M/shard   │
                            └───────────────────────────────────────────────────────────┘
                                                          │
                                          HuggingFace datasets-server (streaming)
```

**Projects** (`DatasetStudio.sln`):

| Project | Target | Role |
|---|---|---|
| `src/Core` | net8.0 | Domain models, parsers, business logic, abstractions |
| `src/DTO` | net8.0 | Shared API contracts (`DatasetItemDto`, `PageResponse<T>`, …) |
| `src/APIBackend` | net10.0 | ASP.NET Core API, EF Core/Postgres, Parquet storage, ingestion |
| `src/ClientApp` | net8.0 | Blazor WASM UI (MudBlazor), caching, viewer |
| `src/Extensions/SDK` | net8.0 | WASM-safe extension contracts (`IExtension`, manifest, context) |
| `src/Extensions/SDK.Api` | net8.0 | API-only extension hooks (`IApiExtension`, `BaseApiExtension`) |
| `src/Extensions/BuiltIn/*` | net8.0 | Built-in extensions — only **CoreViewer** is functional; `Creator`/`Editor`/`AdvancedTools`/`AITools` are stubs or manifest-only (see `docs/architecture.md` §6) |

---

## 🚀 Quick start

### The easy way

```bash
./start.sh
```

This starts PostgreSQL (Docker), waits for it, runs the API (auto-applying EF migrations) and the client, then prints the URLs. **Ctrl+C** stops the apps; the database keeps running for fast restarts.

- App:     http://localhost:5002
- API:     http://localhost:5000  (Swagger at http://localhost:5000/swagger)

Stop everything with `./stop.sh` (add `--wipe` to also delete the database volume).

### Manual

```bash
# 1. Start PostgreSQL
docker compose up -d

# 2. Run the API (auto-applies migrations on startup)
dotnet run --project src/APIBackend

# 3. In another terminal, run the client
dotnet run --project src/ClientApp
```

### Requirements

- **.NET 10 SDK** (builds the net10 API and the net8 projects)
- **Docker + Docker Compose** (for PostgreSQL) — or your own Postgres reachable at the connection string below
- A modern browser (Chrome, Firefox, Edge, Safari)

### Production deployment

The above is the dev workflow (two `dotnet run` processes + a dev Postgres container). For a
real self-hosted deployment — one container serving both the API and the published client,
behind a Caddy reverse proxy with automatic TLS — see **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)**.

---

## 🗂️ Project structure

```
DatasetStudio/
├── src/
│   ├── Core/                 # Domain models, parsers, business logic
│   ├── DTO/                  # Shared contracts
│   ├── APIBackend/           # ASP.NET Core API + data layer
│   │   ├── DataAccess/
│   │   │   ├── PostgreSQL/    # EF Core DbContext, repositories, migrations
│   │   │   └── Parquet/       # Sharded Parquet reader/writer/repository
│   │   ├── Endpoints/        # Minimal API endpoints
│   │   ├── Services/         # Ingestion, HuggingFace integration, extensions
│   │   └── Configuration/    # appsettings + Program.cs
│   ├── ClientApp/            # Blazor WebAssembly UI
│   │   ├── Features/Datasets/ # Viewer, cards, virtualization, pages
│   │   └── Services/         # Caching, API clients, state, interop
│   └── Extensions/           # SDK, SDK.Api, and built-in extensions
├── tests/                    # APIBackend.Tests, ClientApp.Tests
├── docs/                     # Architecture, deployment, and per-phase verification records
├── docker-compose.yml        # Local PostgreSQL (+ MinIO, prod overlay in docker-compose.prod.yml)
├── Dockerfile                # Production image (API + published client, one container)
├── start.sh / stop.sh        # Dev launchers
└── DatasetStudio.sln
```

---

## ⚙️ Configuration

API settings live in `src/APIBackend/Configuration/appsettings*.json`:

- **`ConnectionStrings:DatasetStudio`** — PostgreSQL connection. The Development value matches `docker-compose.yml` (`Host=localhost;Port=5432;Database=dataset_studio_dev;Username=postgres;Password=postgres`).
- **`Urls`** — API bind address (`http://localhost:5000`).
- **`Cors:AllowedOrigins`** — must include the client origin (`http://localhost:5002`); required (not optional) since cookie auth needs `AllowCredentials()`, which is incompatible with a wildcard origin.
- **`Storage:*`** — local paths for Parquet shards, blobs, thumbnails, uploads, DataProtection keys, and optional `S3StorageProfiles` (see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for wiring one up).
- **`ADMIN_USERNAME` / `ADMIN_EMAIL` / `ADMIN_PASSWORD`** (env vars) — bootstraps/repairs the admin account on every startup; unset skips bootstrap and leaves the seeded placeholder admin row permanently unusable, by design.

The client reads its API base address from `src/ClientApp/wwwroot/appsettings.json` (`DatasetApi:BaseAddress`) — left blank in the base file (same-origin, used by production) with a dev-only override in `appsettings.Development.json`.

---

## 🧪 Testing

```bash
dotnet test                                   # both test projects — 109 tests
dotnet test tests/ClientApp.Tests             # client unit tests only
```

`tests/APIBackend.Tests` includes real integration tests against Testcontainers-managed
Postgres/MinIO instances — requires a working Docker daemon, not a manually-started database.

---

## 📚 Documentation

- [docs/architecture.md](docs/architecture.md) — system architecture, data flows, and the
  extension system's real state (§6)
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) — self-host production deployment (Docker Compose,
  Caddy/TLS, backups, S3 opt-in)

---

## 📄 License

MIT — see [LICENSE](LICENSE).
