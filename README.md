# Dataset Studio

A desktop-grade web app for browsing, curating, and editing **billion-scale AI image datasets** — built with an ASP.NET Core API and a Blazor WebAssembly client.

Dataset Studio has two data paths so you can work the way you need to:

- **Stream from HuggingFace** — paste a repo and browse millions of images in seconds; rows are fetched on demand and **nothing is downloaded**.
- **Import locally** — upload CSV/TSV/Parquet/ZIP (or drop files in the data folder) and the items are ingested into **Apache Parquet** (sharded) with metadata in **PostgreSQL**, so they're fully searchable and editable.

The client never holds the whole dataset in memory: a virtualized viewer keeps only a few screenfuls of cards in the DOM at any scroll depth, so memory stays flat even on million-image datasets.

---

## ✨ Features

- **Virtualized grid & list** — row-virtualized rendering (`<Virtualize>`); flat memory, no freeze on huge datasets.
- **Perceived-instant images** — dominant-color (LQIP) placeholders show immediately, thumbnails fade in over them (zero layout shift), lazy + async decoding, width-bounded thumbnails.
- **HuggingFace streaming** — live, zero-download browsing via the HF datasets-server, with API-key/token support for gated datasets.
- **Local ingestion** — CSV/TSV/Parquet/JSON/JSONL/ZIP and image folders, parsed into Parquet; runs in the **background** so uploads return immediately and report status.
- **Cursor pagination** — O(1)/page over sharded Parquet (`shard:row` cursors) and over HF offsets.
- **Editing & curation** — titles, descriptions, tags, favorites; single and bulk edits (shard-scoped writes).
- **Extension system** — pluggable API + client extensions discovered from manifests (see `docs/`).

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
| `src/Extensions/BuiltIn/*` | net8.0 | Built-in extensions (CoreViewer, Creator) |

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
├── docs/                     # Architecture & extension docs
├── docker-compose.yml        # Local PostgreSQL
├── start.sh / stop.sh        # Dev launchers
└── DatasetStudio.sln
```

---

## ⚙️ Configuration

API settings live in `src/APIBackend/Configuration/appsettings*.json`:

- **`ConnectionStrings:DatasetStudio`** — PostgreSQL connection. The Development value matches `docker-compose.yml` (`Host=localhost;Port=5432;Database=dataset_studio_dev;Username=postgres;Password=postgres`).
- **`Urls`** — API bind address (`http://localhost:5000`).
- **`Cors:AllowedOrigins`** — must include the client origin (`http://localhost:5002`).
- **`Storage:*`** — local paths for Parquet shards, blobs, thumbnails, uploads.

The client reads its API base address from `src/ClientApp/wwwroot/appsettings.json` (`DatasetApi:BaseAddress`).

---

## 🧪 Testing

```bash
dotnet test                                   # all test projects
dotnet test tests/ClientApp.Tests             # client unit tests
```

`tests/APIBackend.Tests` exercises the Postgres/Parquet data layer and may require a database.

---

## 📚 Documentation

- [docs/architecture.md](docs/architecture.md) — system architecture & data flows
- [docs/EXTENSION_ARCHITECTURE.md](docs/EXTENSION_ARCHITECTURE.md) — extension system design
- [docs/EXTENSION_QUICK_START.md](docs/EXTENSION_QUICK_START.md) — build your first extension
- [docs/EXTENSION_SYSTEM_IMPLEMENTATION_PLAN.md](docs/EXTENSION_SYSTEM_IMPLEMENTATION_PLAN.md) — extension roadmap (partly implemented)

---

## 🔌 Extensions (in progress)

Extensions are self-describing plugins (a manifest + an assembly) discovered at startup on both the API and client. The contract is split so the Blazor client stays free of ASP.NET hosting types:

- **`IExtension`** (in `Extensions.SDK`, WASM-safe) — lifecycle, `ConfigureServices`, health.
- **`IApiExtension : IExtension`** (in `Extensions.SDK.Api`) — adds the API-only `ConfigureApp(IApplicationBuilder)` hook.

Author API extensions by deriving from `BaseApiExtension`, and client extensions from `BaseClientExtension`. See the extension docs above. The loading infrastructure works; endpoint auto-registration and permission enforcement are still on the roadmap.

---

## 📄 License

MIT — see [LICENSE](LICENSE).
