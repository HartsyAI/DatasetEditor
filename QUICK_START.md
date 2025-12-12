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

✅ **Scaffolded (Ready for Implementation):**
- PostgreSQL + Parquet storage (Phase 2 - Complete scaffold)
- Extension system (Phase 3 - Complete scaffold)

📝 **TODO (Future Phases):**
- Extension implementation (Phase 3.1)
- Installation wizard (Phase 4)
- Multi-user auth (Phase 5)
- AI tools (Phase 6)

## Key Files

- **[REFACTOR_PLAN.md](REFACTOR_PLAN.md)** - Complete roadmap
- **[REFACTOR_COMPLETE_SUMMARY.md](REFACTOR_COMPLETE_SUMMARY.md)** - Phase 1 summary
- **[PHASE2_COMPLETE_SUMMARY.md](PHASE2_COMPLETE_SUMMARY.md)** - Phase 2 summary
- **[PHASE3_COMPLETE_SUMMARY.md](PHASE3_COMPLETE_SUMMARY.md)** - Phase 3 summary
- **[FILE_MIGRATION_MAP.md](FILE_MIGRATION_MAP.md)** - File locations

## Build Status

| Project | Status |
|---------|--------|
| Core | ✅ Builds |
| DTO | ✅ Builds |
| APIBackend | ✅ Builds |
| ClientApp | ⚠️ Warnings (non-critical) |

## Phase Progress

| Phase | Status | Description |
|-------|--------|-------------|
| Phase 1 | ✅ Complete | Architecture restructure |
| Phase 2 | ✅ Scaffold | PostgreSQL + Parquet infrastructure |
| Phase 3 | ✅ Scaffold | Extension system architecture |
| Phase 3.1 | 📝 Next | Extension implementation |
| Phase 4 | 📝 TODO | Installation wizard |
| Phase 5 | 📝 TODO | Authentication & multi-user |
| Phase 6-8 | 📝 TODO | AI Tools, Advanced Tools, Polish |

## Next Steps

**Phase 3.1: Extension Implementation**
- Implement extension loading logic
- Create CoreViewer extension
- Create Creator extension
- Migrate existing code to extensions

See [PHASE3_COMPLETE_SUMMARY.md](PHASE3_COMPLETE_SUMMARY.md) for details.
