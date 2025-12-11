# 🚀 Dataset Studio - Quick Start

## Build & Run

```bash
# Build the solution
dotnet build DatasetStudio.sln

# Run the application
dotnet run --project src/APIBackend/APIBackend.csproj

# Open browser to:
# https://localhost:5001
```

## Project Structure

```
DatasetStudio/
├── src/
│   ├── Core/          → Domain logic & business rules
│   ├── DTO/           → API contracts
│   ├── APIBackend/    → ASP.NET Core API
│   ├── ClientApp/     → Blazor WebAssembly UI
│   └── Extensions/    → Extension system (Phase 3)
├── Docs/              → Documentation
└── DatasetStudio.sln  → Solution file
```

## Current Status

✅ **Working:**
- Dataset viewing (grid/list)
- Dataset upload (local, ZIP, HuggingFace)
- Filtering and search
- Image detail viewing
- Metadata editing
- Settings and preferences

📝 **TODO (Future Phases):**
- PostgreSQL + Parquet storage (Phase 2)
- Extension system (Phase 3)
- Installation wizard (Phase 4)
- Multi-user auth (Phase 5)
- AI tools (Phase 6)

## Key Files

- **[REFACTOR_PLAN.md](REFACTOR_PLAN.md)** - Complete roadmap
- **[REFACTOR_COMPLETE_SUMMARY.md](REFACTOR_COMPLETE_SUMMARY.md)** - What we built
- **[FILE_MIGRATION_MAP.md](FILE_MIGRATION_MAP.md)** - File locations

## Build Status

| Project | Status |
|---------|--------|
| Core | ✅ Builds |
| DTO | ✅ Builds |
| APIBackend | ✅ Builds |
| ClientApp | ⚠️ Warnings (non-critical) |

## Next Phase

**Phase 2: Database Migration**
- Switch from LiteDB to PostgreSQL + Parquet
- Support billions of dataset items
- Add user management foundation

See [REFACTOR_PLAN.md](REFACTOR_PLAN.md) for details.
