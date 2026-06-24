# Dataset Studio — Documentation

Reference documentation for Dataset Studio. For setup and a quick start, see the
[main README](../README.md) (requirements, `./start.sh`, configuration, and testing).

## Contents

- **[architecture.md](architecture.md)** — system architecture, the data layer (PostgreSQL + Parquet), the streaming vs. local data paths, the virtualized client, and ingestion.
- **[EXTENSION_ARCHITECTURE.md](EXTENSION_ARCHITECTURE.md)** — design of the extension system (discovery, loading, the SDK contract split across `Extensions.SDK` / `Extensions.SDK.Api`).
- **[EXTENSION_QUICK_START.md](EXTENSION_QUICK_START.md)** — build your first extension, step by step.
- **[EXTENSION_SYSTEM_IMPLEMENTATION_PLAN.md](EXTENSION_SYSTEM_IMPLEMENTATION_PLAN.md)** — roadmap for the extension system (partly implemented, partly planned).

> The extension system is partially implemented: discovery, manifest loading, and the
> SDK contracts work; endpoint auto-registration and permission enforcement are still
> on the roadmap.
